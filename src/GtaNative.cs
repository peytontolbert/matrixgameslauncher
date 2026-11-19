using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

// Matrix Games Launcher owns GTA joining through its separately installed engine
// runtime. Server manifests cannot name programs or arbitrary launch arguments.
public sealed class GtaV : IGameAdapter {
    public string Id {get{return "gta-v";}} public string Name {get{return "GTA V · Matrix client";}}
    public string DefaultDocuments {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"MatrixGames","GTA V");}}
    public string FindInstallation(string steam){
        if(string.IsNullOrWhiteSpace(steam))return "";
        var roots=new List<string>{Path.GetFullPath(steam)};string index=Path.Combine(steam,"steamapps","libraryfolders.vdf");
        if(File.Exists(index)&&new FileInfo(index).Length<1024*1024)foreach(Match m in Regex.Matches(File.ReadAllText(index),"\"path\"\\s*\"([^\"]+)\""))roots.Add(m.Groups[1].Value.Replace("\\\\","\\"));
        return roots.SelectMany(r=>new[]{r,Path.Combine(r,"steamapps","common","Grand Theft Auto V"),Path.Combine(r,"steamapps","common","Grand Theft Auto V Legacy")}).FirstOrDefault(r=>File.Exists(Path.Combine(r,"GTA5.exe")))??"";
    }
    public void ResolveLocations(LauncherSettings settings,GameLocations locations){if(string.IsNullOrWhiteSpace(locations.InstallDirectory))locations.InstallDirectory=FindInstallation(settings.SteamDirectory);if(string.IsNullOrWhiteSpace(locations.DocumentsDirectory))locations.DocumentsDirectory=DefaultDocuments;}
    public void ValidateLocations(GameLocations locations){
        ContentInstaller.NoLinks(locations.InstallDirectory);string exe=Path.Combine(locations.InstallDirectory,"GTA5.exe");
        if(!File.Exists(exe))throw new IOException("Select your GTA V Legacy folder containing GTA5.exe in Settings.");
        if(Process.GetProcessesByName("GTA5").Any(p=>!p.HasExited))throw new IOException("Exit GTA V before joining a Matrix server.");
    }
    public string AccountId(){return "";}
    public string SelectModel(IWin32Window owner,ServerEntry server,ContentPlan plan){return "default";}
#if MATRIX_STORY_EXPERIMENT
    public static void ValidateGrant(Dictionary<string,object> grant,string server){
        string endpoint=Data.Text(grant,"endpoint");long ttl=Convert.ToInt64(grant["expiresAt"])-DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if(Convert.ToInt32(grant["schema"])!=1||Data.Text(grant,"game")!="gta-v"||Data.Text(grant,"server")!=server||server!="managed-gta-legion"||Data.Text(grant,"room")!="gta-legion"||Data.Text(grant,"revision")!="los-santos-legion-v1"||Data.Text(grant,"client")!="native"||endpoint!="wss://api.matrixgames.app/v1/gta/native-socket"||!Regex.IsMatch(Data.Text(grant,"ticket"),@"\A[A-Za-z0-9_-]{43}\z")||ttl<=0||ttl>300000)throw new IOException("Invalid or expired GTA join permission");
    }
    // Let Rockstar perform its normal ownership check. Forcing offline mode
    // prevents first-run activation even when Steam and the user are online.
    public static string LaunchArguments {get{return "-applaunch 271590 -nobattleye -noBE";}}
#endif
    public async Task Launch(Dictionary<string,object> grant,ServerEntry server,string model,string account,LauncherSettings settings,Action<string> report,CancellationToken cancel){
        var location=settings.Games[Id];ValidateLocations(location);
        var runtime=await Task.Run(()=>RuntimeMapCapacity.Prepare(MatrixNativeRuntime.Location(settings)));
        await runtime.Launch(grant,server,location,report,cancel);
    }
}
#if MATRIX_STORY_EXPERIMENT
public static class GtaRuntime {
    sealed class Part {public string Archive,Target,Hash;public Part(string archive,string target,string hash){Archive=archive;Target=target;Hash=hash;}}
    sealed class Package {public string Url,Hash;public Part[] Parts;}
    static readonly Package[] Packages={
        new Package{Url="https://www.dev-c.com/files/ScriptHookV_3889.0_1158.13.zip",Hash="b64c97c3353906f14621e7e9511e4aec2a7d436ecc21ed124d3816585e2e6188",Parts=new[]{
            new Part("bin/ScriptHookV.dll","ScriptHookV.dll","126be57ca9dca00e471f9de611766009c900aad88f89ae6c2926ec62e5aaeb4f"),new Part("bin/dinput8.dll","dinput8.dll","9fd9e02353b7d39fe07b9667f7ea2697229a7f2d0e7d389eb79eb212b1bb181d")}},
        new Package{Url="https://github.com/scripthookvdotnet/scripthookvdotnet-nightly/releases/download/v3.7.0-nightly.204/ScriptHookVDotNet-v3.7.0-nightly.204.zip",Hash="bb832165f6b546e63706fee696c5789f3160d7d5f6da67b6c261538c0d6963d5",Parts=new[]{
            new Part("ScriptHookVDotNet-Loader.asi","ScriptHookVDotNet-Loader.asi","9dcaeb537cf451bb845d6a4758e67e5471753ffabc222a9ed9b0970178f614f9"),
            new Part("ScriptHookVDotNet.dll","ScriptHookVDotNet.dll","51335932f63b69fc48a9525f3e29fa2b1f004af6495eb67f418d3802cf7b1548"),
            new Part("ScriptHookVDotNet2.dll","ScriptHookVDotNet2.dll","7adad043debf15c249056cff8a1417ccbdb6f135f253a459462b7a4a3801bafb"),
            new Part("ScriptHookVDotNet3.dll","ScriptHookVDotNet3.dll","b08896c020bacf00425cbaf7680e6c77956a9e4a1d09f402684ebf0aeb77f9ca"),
            new Part("ScriptHookVDotNet.ini","ScriptHookVDotNet.ini","6991fceeb953a1fec2d6d60017b93350474a62b05a52121d4cf92217008f8d9a")}}
    };
    static byte[] Client(){using(var resource=Assembly.GetExecutingAssembly().GetManifestResourceStream("MatrixGtaClient.dll")){if(resource==null)throw new IOException("This launcher build does not include the Matrix GTA client.");using(var bytes=new MemoryStream()){resource.CopyTo(bytes);return bytes.ToArray();}}}
    static string Hash(byte[] bytes){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
    static bool Matches(string file,string hash){return File.Exists(file)&&ContentInstaller.HashFile(file)==hash;}
    public static string[] Missing(string game){return Packages.SelectMany(p=>p.Parts).Where(p=>!Matches(Path.Combine(game,p.Target),p.Hash)).Select(p=>p.Target).Concat(Matches(Path.Combine(game,"scripts","MatrixGtaClient.dll"),Hash(Client()))?new string[0]:new[]{"scripts/MatrixGtaClient.dll"}).ToArray();}
    public static string Review(string game){return "Install the Matrix networking client for GTA V Story Mode?\r\n\r\nFiles needed:\r\n"+string.Join("\r\n",Missing(game))+"\r\n\r\nScript Hook V: dev-c.com\r\nScriptHookVDotNet: official GitHub release v3.7.0-nightly.204\r\nMatrixGtaClient: included in this launcher\r\n\r\nExisting matching files are reused. Replaced files are backed up. Downloads are verified against pinned SHA-256 hashes. GTA will launch in Story Mode with BattlEye disabled for this mod session. This is a development movement preview; combat and shared vehicle control are not supported.";}
    public static void Install(string game,string library,CancellationToken cancel,Action<string> report){
        ContentInstaller.NoLinks(game);ContentInstaller.NoLinks(library);var replacements=new List<Tuple<string,string,bool>>();
        string backup=Path.Combine(game,"MatrixGamesBackups",Guid.NewGuid().ToString("N"));ContentInstaller.NoLinks(backup);
        try{
            foreach(var package in Packages){
                var needed=package.Parts.Where(p=>!Matches(Path.Combine(game,p.Target),p.Hash)).ToArray();if(needed.Length==0)continue;
                string cache=Path.Combine(library,"runtime","gta-v",package.Hash+".zip");ContentInstaller.NoLinks(cache);Directory.CreateDirectory(Path.GetDirectoryName(cache));
                if(!Matches(cache,package.Hash)){
                    report("Downloading GTA script runtime from "+new Uri(package.Url).Host);string pending=cache+"."+Guid.NewGuid().ToString("N")+".partial";
                    try{
                        var request=(HttpWebRequest)WebRequest.Create(package.Url);request.UserAgent="MatrixGamesLauncher";request.Referer="https://www.dev-c.com/gtav/scripthookv/";request.Timeout=30000;request.ReadWriteTimeout=30000;
                        using(cancel.Register(()=>request.Abort()))using(var response=request.GetResponse())using(var input=response.GetResponseStream())using(var output=File.Create(pending)){
                            byte[] buffer=new byte[65536];int count;long size=0;while((count=input.Read(buffer,0,buffer.Length))>0){cancel.ThrowIfCancellationRequested();size+=count;if(size>32*1024*1024)throw new IOException("Runtime archive exceeds size limit");output.Write(buffer,0,count);}
                        }
                        if(!Matches(pending,package.Hash))throw new IOException("GTA runtime download checksum differs; installation stopped");
                        if(File.Exists(cache))File.Replace(pending,cache,null);else File.Move(pending,cache);
                    }finally{if(File.Exists(pending))File.Delete(pending);}
                }
                using(var zip=ZipFile.OpenRead(cache))foreach(var part in needed){
                    cancel.ThrowIfCancellationRequested();var entry=zip.GetEntry(part.Archive);if(entry==null||entry.Length>32*1024*1024)throw new IOException("Invalid runtime archive");
                    byte[] bytes;using(var stream=entry.Open())using(var output=new MemoryStream()){stream.CopyTo(output);bytes=output.ToArray();}
                    if(Hash(bytes)!=part.Hash)throw new IOException("Runtime file checksum differs");Replace(game,part.Target,bytes,backup,replacements);
                }
            }
            byte[] client=Client();if(!Matches(Path.Combine(game,"scripts","MatrixGtaClient.dll"),Hash(client)))Replace(game,"scripts/MatrixGtaClient.dll",client,backup,replacements);
        }catch{
            foreach(var entry in replacements.AsEnumerable().Reverse()){if(entry.Item3)File.Copy(entry.Item2,entry.Item1,true);else if(File.Exists(entry.Item1))File.Delete(entry.Item1);}throw;
        }
    }
    static void Replace(string root,string relative,byte[] bytes,string backup,List<Tuple<string,string,bool>> changes){
        string path=ContentInstaller.Child(root,relative),saved=ContentInstaller.Child(backup,relative);ContentInstaller.NoLinks(path);ContentInstaller.NoLinks(saved);Directory.CreateDirectory(Path.GetDirectoryName(path));bool existed=File.Exists(path);
        if(existed){Directory.CreateDirectory(Path.GetDirectoryName(saved));File.Copy(path,saved,false);}changes.Add(Tuple.Create(path,saved,existed));File.WriteAllBytes(path,bytes);
    }
}
#endif
