using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using GTA;
using GTA.Math;
using GTA.Native;

namespace MatrixGta {
    // Loaded by ScriptHookVDotNet in GTA V Story Mode. No FiveM component,
    // executable injection, or replacement of Rockstar networking is involved.
    public sealed class MatrixGtaClient : Script {
        sealed class Remote {public Ped Ped;public Vehicle Vehicle;public string PedModel,CarModel;}
        readonly Dictionary<string,Remote> remotes=new Dictionary<string,Remote>();
        Connection connection; Task receive; bool attempted,placed,finished; int lastSend,lastStatus; Vector3 original;
        string chatText="",epoch;long lastChat;int chatUntil;
        static readonly Vector3 Spawn=new Vector3(186.94f,-850.84f,32.17f);
        public MatrixGtaClient(){Interval=0;Tick+=OnTick;Aborted+=(s,e)=>Cleanup();KeyDown+=OnKey;}
        static Vector3 V(double[] p){return new Vector3((float)p[0],(float)p[1],(float)p[2]);}
        static double[] A(Vector3 p){return new[]{(double)p.X,(double)p.Y,(double)p.Z};}
        static bool Alive(Entity e){return e!=null&&e.Exists();}
        void Status(string state,string detail){
            try{Directory.CreateDirectory(Codec.DirectoryPath);File.WriteAllText(Codec.StatusPath,Codec.Write(new{state=state,detail=detail,at=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}));}catch(IOException){}catch(UnauthorizedAccessException){}
        }
        void OnTick(object sender,EventArgs e){
            if(finished||!Alive(Game.Player.Character))return;
            // This client is exclusively a Story Mode mod. Never attach it to
            // GTA Online; the Matrix server is a separate transport/world.
            if(Function.Call<bool>(Hash.NETWORK_IS_SESSION_ACTIVE)){Cleanup();finished=true;return;}
            try{
                if(!attempted){
                    if(!File.Exists(Codec.GrantPath))return;
                    attempted=true;
                    bool development=false;
                    #if MATRIX_GTA_DEVELOPMENT
                    development=true;
                    #endif
                    var grant=Codec.Unprotect(File.ReadAllBytes(Codec.GrantPath),development);
                    string executable=Process.GetCurrentProcess().MainModule.FileName;
                    if(!string.Equals(Path.GetFullPath(grant.gameExecutable),Path.GetFullPath(executable),StringComparison.OrdinalIgnoreCase))throw new IOException("Join permission belongs to another GTA installation");
                    File.Delete(Codec.GrantPath);connection=new Connection();receive=connection.Start(grant,development);Status("connecting","Joining Matrix Games");
                }
                if(connection==null)return;
                if(receive.IsCompleted||connection.Error!=null){Status("disconnected",connection.Error??"Session ended");Cleanup();finished=true;return;}
                if(!connection.Online)return;
                var player=Game.Player.Character;
                if(!placed){
                    if(player.IsInVehicle()){Function.Call(Hash.TASK_LEAVE_ANY_VEHICLE,player.Handle,0,16);return;}
                    original=player.Position;player.Position=Spawn;placed=true;Status("connected","Matrix GTA development session");
                }
                Function.Call(Hash.SET_PED_DENSITY_MULTIPLIER_THIS_FRAME,0f);
                Function.Call(Hash.SET_VEHICLE_DENSITY_MULTIPLIER_THIS_FRAME,0f);
                Function.Call(Hash.SET_RANDOM_VEHICLE_DENSITY_MULTIPLIER_THIS_FRAME,0f);
                var frame=connection.Latest;if(frame!=null)Render(frame);
                if(Game.GameTime-lastSend>=100){connection.SetState(Capture(player));lastSend=Game.GameTime;}
                if(Game.GameTime-lastStatus>=1000){Status("connected","Native GTA connected; "+remotes.Values.Count(r=>Alive(r.Ped))+" remote peds and "+remotes.Values.Count(r=>Alive(r.Vehicle))+" remote vehicles rendered");lastStatus=Game.GameTime;}
                GTA.UI.Screen.ShowSubtitle(Game.GameTime<chatUntil?chatText:"Matrix Games development · F6: Chat · F7: Sultan · F8: Blista",100);
            }catch(Exception error){Status("error",error.Message);Cleanup();finished=true;}
        }
        ActorState Capture(Ped player){
            var p=player.Position;var vehicle=player.CurrentVehicle;VehicleState car=null;
            if(Alive(vehicle)){
                string model=vehicle.Model.Hash==new Model("sultan").Hash?"sultan":vehicle.Model.Hash==new Model("blista").Hash?"blista":null;
                if(model==null||vehicle.Driver!=player)throw new IOException("This preview supports driving a Sultan or Blista. Rejoin to continue.");
                car=new VehicleState{model=model,position=A(vehicle.Position),heading=Codec.MatrixHeading(vehicle.Heading),speed=vehicle.Speed};
            }
            return new ActorState{position=new[]{(double)p.X,(double)p.Y,(double)p.Z-1.0},velocity=A(player.Velocity),heading=Codec.MatrixHeading(player.Heading),model="mp_m_freemode_01",vehicle=car};
        }
        void Render(Frame frame){
            if(epoch!=frame.epoch){epoch=frame.epoch;lastChat=0;}
            foreach(var chat in frame.chats??new Chat[0])if(chat.id>lastChat){lastChat=chat.id;chatText=(chat.name+": "+chat.text).Replace("~","-");chatUntil=Game.GameTime+5000;}
            var seen=new HashSet<string>();
            foreach(var actor in frame.players){
                if(actor.id==connection.Id)continue;seen.Add(actor.id);var s=actor.state;
                Remote r;if(!remotes.TryGetValue(actor.id,out r)){r=new Remote();remotes.Add(actor.id,r);}
                if(r.PedModel!=s.model){Delete(r);r.PedModel=s.model;}
                if(!Alive(r.Ped)){
                    var model=new Model(s.model);model.Request();if(!model.IsLoaded)continue;
                    r.Ped=Ped.Create(model,V(s.position)+new Vector3(0,0,1),Codec.GtaHeading(s.heading));model.MarkAsNoLongerNeeded();
                    if(!Alive(r.Ped))continue;r.Ped.IsPersistent=true;r.Ped.IsInvincible=true;r.Ped.IsCollisionEnabled=false;r.Ped.BlockPermanentEvents=true;Function.Call(Hash.SET_PED_DEFAULT_COMPONENT_VARIATION,r.Ped.Handle);
                }
                string car=s.vehicle==null?null:s.vehicle.model;
                if(r.CarModel!=car){if(Alive(r.Vehicle))r.Vehicle.Delete();r.Vehicle=null;r.CarModel=car;}
                if(car!=null){
                    if(!Alive(r.Vehicle)){
                        var model=new Model(car);model.Request();if(!model.IsLoaded)continue;
                        r.Vehicle=Vehicle.Create(model,V(s.vehicle.position),Codec.GtaHeading(s.vehicle.heading));model.MarkAsNoLongerNeeded();
                        if(!Alive(r.Vehicle))continue;r.Vehicle.IsPersistent=true;r.Vehicle.IsInvincible=true;r.Vehicle.IsCollisionEnabled=false;
                        Function.Call(Hash.SET_VEHICLE_DOORS_LOCKED,r.Vehicle.Handle,2);
                        Function.Call(Hash.FREEZE_ENTITY_POSITION,r.Vehicle.Handle,true);
                    }
                    r.Vehicle.PositionNoOffset=V(s.vehicle.position);r.Vehicle.Heading=Codec.GtaHeading(s.vehicle.heading);
                    if(r.Ped.CurrentVehicle!=r.Vehicle)r.Ped.SetIntoVehicle(r.Vehicle,VehicleSeat.Driver);
                }else{
                    if(r.Ped.IsInVehicle())r.Ped.Task.ClearAllImmediately();
                    Function.Call(Hash.FREEZE_ENTITY_POSITION,r.Ped.Handle,true);
                    r.Ped.PositionNoOffset=V(s.position)+new Vector3(0,0,1);r.Ped.Heading=Codec.GtaHeading(s.heading);
                }
            }
            foreach(var id in remotes.Keys.Where(id=>!seen.Contains(id)).ToArray()){Delete(remotes[id]);remotes.Remove(id);}
        }
        void OnKey(object sender,KeyEventArgs e){
            if(connection!=null&&connection.Online&&placed&&e.KeyCode==Keys.F6){string text=Game.GetUserInput(WindowTitle.EnterMessage60,"",60);connection.SendChat(text);return;}
            if(connection==null||!connection.Online||!placed||!(e.KeyCode==Keys.F7||e.KeyCode==Keys.F8))return;
            var model=new Model(e.KeyCode==Keys.F7?"sultan":"blista");model.Request(500);if(!model.IsLoaded)return;
            var player=Game.Player.Character;var car=Vehicle.Create(model,player.Position,player.Heading);model.MarkAsNoLongerNeeded();
            if(Alive(car)){car.IsPersistent=false;player.SetIntoVehicle(car,VehicleSeat.Driver);}
        }
        static void Delete(Remote r){if(Alive(r.Ped))r.Ped.Delete();if(Alive(r.Vehicle))r.Vehicle.Delete();r.Ped=null;r.Vehicle=null;}
        void Cleanup(){
            if(connection!=null)connection.Dispose();
            foreach(var r in remotes.Values)Delete(r);remotes.Clear();
            if(placed&&Alive(Game.Player.Character)&&!Game.Player.Character.IsInVehicle())Game.Player.Character.Position=original;
            placed=false;
        }
    }
}
