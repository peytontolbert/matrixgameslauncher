using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace MatrixGta {
    public sealed class JoinGrant {
        public int schema; public string game,server,room,revision,client,endpoint,ticket,gameExecutable; public long expiresAt;
        public void Validate(bool development=false) {
            Uri uri; if(!Uri.TryCreate(endpoint,UriKind.Absolute,out uri))throw new IOException("Invalid Matrix endpoint");
            bool production=endpoint=="wss://api.matrixgames.app/v1/gta/native-socket";
            bool local=development&&uri.Scheme=="ws"&&(uri.Host=="127.0.0.1"||uri.Host=="localhost")&&(uri.AbsolutePath=="/socket"||uri.AbsolutePath=="/v1/gta/native-socket")&&uri.UserInfo.Length==0&&uri.Query.Length==0&&uri.Fragment.Length==0;
            long remaining=expiresAt-DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if(schema!=1||game!="gta-v"||server!="managed-gta-legion"||room!="gta-legion"||revision!="los-santos-legion-v1"||client!="native"||!Regex.IsMatch(ticket??"",@"\A[A-Za-z0-9_-]{43}\z")||remaining<=0||remaining>300000||!(production||local))throw new IOException("Invalid or expired Matrix join permission");
        }
    }
    public sealed class VehicleState { public string model; public double[] position; public double heading,speed; }
    public sealed class ActorState { public double[] position,velocity; public double heading; public string model; public VehicleState vehicle; }
    public sealed class Actor { public string id,name; public ActorState state; }
    public sealed class Chat { public long id; public string name,text; }
    public sealed class Frame { public string type,id,room,revision,epoch; public int schema; public bool ready; public long serverTime; public Actor[] players; public Chat[] chats; }
    public static class Codec {
        public static readonly byte[] Entropy=Encoding.UTF8.GetBytes("MatrixGames.Gta.Join.v1");
        public static string DirectoryPath {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MatrixGames","Launcher");}}
        public static string GrantPath {get{return Path.Combine(DirectoryPath,"gta-join.bin");}}
        public static string StatusPath {get{return Path.Combine(DirectoryPath,"gta-status.json");}}
        public static T Read<T>(string text){return new JavaScriptSerializer{MaxJsonLength=131072,RecursionLimit=16}.Deserialize<T>(text);}
        public static string Write(object value){return new JavaScriptSerializer{MaxJsonLength=131072,RecursionLimit=16}.Serialize(value);}
        public static byte[] Protect(JoinGrant grant){grant.Validate();return ProtectedData.Protect(Encoding.UTF8.GetBytes(Write(grant)),Entropy,DataProtectionScope.CurrentUser);}
        public static JoinGrant Unprotect(byte[] bytes,bool development=false){if(bytes.Length>16384)throw new IOException("Join file too large");var g=Read<JoinGrant>(Encoding.UTF8.GetString(ProtectedData.Unprotect(bytes,Entropy,DataProtectionScope.CurrentUser)));g.Validate(development);return g;}
        static bool Vec(double[] v,double max){return v!=null&&v.Length==3&&v.All(n=>!double.IsNaN(n)&&!double.IsInfinity(n)&&Math.Abs(n)<=max);}
        public static void ValidateState(ActorState s){
            if(s==null||!Vec(s.position,12000)||!Vec(s.velocity,150)||double.IsNaN(s.heading)||double.IsInfinity(s.heading)||Math.Abs(s.heading)>Math.PI*2||!(s.model=="mp_m_freemode_01"||s.model=="mp_f_freemode_01"))throw new IOException("Invalid actor state");
            if(s.vehicle!=null&&(!(s.vehicle.model=="sultan"||s.vehicle.model=="blista")||!Vec(s.vehicle.position,12000)||double.IsNaN(s.vehicle.heading)||double.IsInfinity(s.vehicle.heading)||Math.Abs(s.vehicle.heading)>Math.PI*2||double.IsNaN(s.vehicle.speed)||double.IsInfinity(s.vehicle.speed)||Math.Abs(s.vehicle.speed)>150))throw new IOException("Invalid vehicle state");
        }
        public static double MatrixHeading(float degrees){return Math.Atan2(Math.Sin((degrees+90)*Math.PI/180),Math.Cos((degrees+90)*Math.PI/180));}
        public static float GtaHeading(double radians){return (float)((radians*180/Math.PI-90+720)%360);}
    }
    public sealed class Connection : IDisposable {
        readonly ClientWebSocket socket=new ClientWebSocket(); readonly CancellationTokenSource stop=new CancellationTokenSource();
        readonly object gate=new object(); Frame latest; string id; string error; long sequence,lastReceived;
        ActorState outgoing; readonly Timer stateTimer;
        public Connection(){stateTimer=new Timer(_=>{ActorState state;lock(gate)state=outgoing;if(state!=null)Send(state);},null,100,100);}
        public void SetState(ActorState state){Codec.ValidateState(state);lock(gate)outgoing=state;}
        int sending; public string Id {get{lock(gate)return id;}} public string Error {get{lock(gate)return error;}}
        public bool Online {get{return socket.State==WebSocketState.Open&&Id!=null&&Monotonic.Now()-Interlocked.Read(ref lastReceived)<5000;}}
        public Frame Latest {get{lock(gate)return latest;}}
        public async Task Start(JoinGrant grant,bool development=false){
            try{
                grant.Validate(development);socket.Options.AddSubProtocol("json");socket.Options.AddSubProtocol("mg-ticket."+grant.ticket);socket.Options.AddSubProtocol("matrix-native-v1");socket.Options.KeepAliveInterval=TimeSpan.FromSeconds(15);
                using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(stop.Token)){timeout.CancelAfter(8000);await socket.ConnectAsync(new Uri(grant.endpoint),timeout.Token).ConfigureAwait(false);}
                Interlocked.Exchange(ref lastReceived,Monotonic.Now());
                byte[] buffer=new byte[8192];
                while(!stop.IsCancellationRequested&&socket.State==WebSocketState.Open){
                    using(var bytes=new MemoryStream()){
                        WebSocketReceiveResult part;
                        do{using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(stop.Token)){timeout.CancelAfter(6000);part=await socket.ReceiveAsync(new ArraySegment<byte>(buffer),timeout.Token).ConfigureAwait(false);}if(part.MessageType!=WebSocketMessageType.Text)throw new IOException("Matrix session ended");if(bytes.Length+part.Count>131072)throw new IOException("Snapshot too large");bytes.Write(buffer,0,part.Count);}while(!part.EndOfMessage);
                        var frame=Codec.Read<Frame>(Encoding.UTF8.GetString(bytes.ToArray()));
                        if(frame==null||frame.schema!=1||frame.room!=grant.room||frame.revision!=grant.revision)throw new IOException("World mismatch");
                        if(frame.type=="welcome"){
                            if(!Regex.IsMatch(frame.id??"",@"\Anative:[a-f0-9-]{36}\z"))throw new IOException("Invalid native identity");
                            lock(gate)id=frame.id;
                        }else if(frame.type=="snapshot"){
                            if(frame.players==null||frame.players.Length>48||frame.players.Select(p=>p.id).Distinct().Count()!=frame.players.Length)throw new IOException("Invalid actor list");
                            foreach(var p in frame.players){if(!Regex.IsMatch(p.id??"",@"\A(?:native|browser):[a-zA-Z0-9_-]{1,80}\z"))throw new IOException("Invalid actor identity");Codec.ValidateState(p.state);}
                            if(frame.chats!=null&&(frame.chats.Length>100||frame.chats.Any(c=>c==null||c.text==null||c.text.Length>240||c.name==null||c.name.Length>32)))throw new IOException("Invalid chat snapshot");
                            lock(gate)latest=frame;
                        }else throw new IOException("Invalid Matrix message");
                        Interlocked.Exchange(ref lastReceived,Monotonic.Now());
                    }
                }
            }catch(Exception e){if(!stop.IsCancellationRequested){lock(gate)error=e is WebSocketException?"Could not connect to Matrix Games":e.Message;}}
            finally{stateTimer.Dispose();socket.Abort();}
        }
        public async void Send(ActorState state){
            if(!Online||Interlocked.CompareExchange(ref sending,1,0)!=0)return;
            try{Codec.ValidateState(state);byte[] bytes=Encoding.UTF8.GetBytes(Codec.Write(new{type="state",sequence=Interlocked.Increment(ref sequence),state=state}));await socket.SendAsync(new ArraySegment<byte>(bytes),WebSocketMessageType.Text,true,stop.Token).ConfigureAwait(false);}
            catch(Exception){lock(gate)error="Matrix connection interrupted";socket.Abort();}
            finally{Interlocked.Exchange(ref sending,0);}
        }
        public async void SendChat(string text){
            if(!Online||string.IsNullOrWhiteSpace(text)||text.Length>240||text.Any(char.IsControl))return;
            bool held=false;
            try{while(Interlocked.CompareExchange(ref sending,1,0)!=0){await Task.Delay(25,stop.Token).ConfigureAwait(false);if(!Online)return;}held=true;byte[] bytes=Encoding.UTF8.GetBytes(Codec.Write(new{type="chat",text=text}));await socket.SendAsync(new ArraySegment<byte>(bytes),WebSocketMessageType.Text,true,stop.Token).ConfigureAwait(false);}
            catch(Exception){lock(gate)error="Matrix connection interrupted";socket.Abort();}
            finally{if(held)Interlocked.Exchange(ref sending,0);}
        }
        public void Dispose(){stateTimer.Dispose();stop.Cancel();socket.Abort();}
    }
    // Stopwatch is monotonic and available on .NET Framework 4.8.
    static class Monotonic { public static long Now(){return System.Diagnostics.Stopwatch.GetTimestamp()*1000/System.Diagnostics.Stopwatch.Frequency;} }
}
