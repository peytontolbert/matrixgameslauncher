using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

// Matrix Games Launcher owns runtime installation and account UI. A server can
// select a supported runtime/profile but cannot supply executable arguments.
public sealed class MatrixNativeRuntime {
    public const string Id="matrix-gta-native-v1";
    public readonly string Root;
    public readonly int[] Builds;
    MatrixNativeRuntime(string root,int[] builds){Root=root;Builds=builds;}
    static readonly string[] Required={"MatrixGtaRuntime.exe","CoreRT.dll","CitizenGame.dll","components.json"};
    public static MatrixNativeRuntime Inspect(string root){return Inspect(root,null);}
    internal static MatrixNativeRuntime Inspect(string root,Func<string,string,long,string,long,bool> interruptedUpdate){
        root=Path.GetFullPath(root);ContentInstaller.NoLinks(root);string file=Path.Combine(root,"matrix-runtime.json");ContentInstaller.NoLinks(file);
        if(!File.Exists(file))throw new IOException("The Matrix GTA runtime is not installed in the selected Matrix library.");
        if(new FileInfo(file).Length>4*1024*1024)throw new IOException("Invalid runtime manifest");
        var manifest=new System.Web.Script.Serialization.JavaScriptSerializer{MaxJsonLength=4*1024*1024}.Deserialize<Dictionary<string,object>>(File.ReadAllText(file));
        if(Convert.ToInt32(manifest["schema"])!=1||Data.Text(manifest,"runtime")!=Id||Data.Text(manifest,"game")!="gta-v"||Data.Text(manifest,"entrypoint")!="MatrixGtaRuntime.exe")throw new IOException("Wrong Matrix runtime package");
        var builds=Data.Array(manifest["builds"]).Select(Convert.ToInt32).ToArray();if(builds.Length<1||builds.Length>32||builds.Any(b=>b<1604||b>9999)||builds.Distinct().Count()!=builds.Length)throw new IOException("Invalid runtime build list");
        var entries=Data.Array(manifest["files"]);if(entries.Length<Required.Length||entries.Length>10000)throw new IOException("Incomplete runtime package");
        var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var item in entries){var entry=(Dictionary<string,object>)item;string relative=Data.Text(entry,"path"),digest=Data.Text(entry,"sha256");
            if(!seen.Add(relative)||!Regex.IsMatch(digest,@"\A[a-f0-9]{64}\z")||relative.IndexOf('\\')>=0)throw new IOException("Invalid runtime file manifest");
            string target=ContentInstaller.Child(root,relative);ContentInstaller.NoLinks(target);
            if(!File.Exists(target))throw new IOException("Matrix runtime file is missing or changed: "+relative);
            long expectedSize=Convert.ToInt64(entry["bytes"]),actualSize=new FileInfo(target).Length;string actualHash=ContentInstaller.HashFile(target);
            if((actualSize!=expectedSize||actualHash!=digest)&&(interruptedUpdate==null||!interruptedUpdate(relative,digest,expectedSize,actualHash,actualSize)))throw new IOException("Matrix runtime file is missing or changed: "+relative);
        }
        if(Required.Any(name=>!seen.Contains(name)))throw new IOException("Matrix runtime is incomplete");
        return new MatrixNativeRuntime(root,builds);
    }
    public static string Location(LauncherSettings settings){return Path.Combine(settings.LibraryDirectory,"runtimes","gta-v");}
    public static void ValidateGrant(Dictionary<string,object> grant,ServerEntry server){
        long ttl=Convert.ToInt64(grant["expiresAt"])-DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Uri endpoint;string address=Data.Text(grant,"endpoint");
        if(Convert.ToInt32(grant["schema"])!=4||Data.Text(grant,"runtime")!=Id||Data.Text(grant,"game")!="gta-v"||server.Game!="gta-v"||Data.Text(grant,"server")!=server.Id||!Data.Id(server.Id)||ttl<=0||ttl>300000||!Regex.IsMatch(Data.Text(grant,"ticket"),@"\A[A-Za-z0-9_-]{43}\z")||!Uri.TryCreate(address,UriKind.Absolute,out endpoint)||endpoint.Scheme!="https"||endpoint.UserInfo.Length>0||endpoint.Query.Length>0||endpoint.Fragment.Length>0||!Regex.IsMatch(address,@"\Ahttps://[a-zA-Z0-9][a-zA-Z0-9.-]{0,252}(:[0-9]{1,5})?(/[a-zA-Z0-9_/-]*)?\z"))throw new IOException("Invalid Matrix runtime join permission");
        if(endpoint.Port<1||address.Length>512)throw new IOException("Invalid Matrix runtime endpoint");
        int build=Convert.ToInt32(grant["gameBuild"]);if(build<1604||build>9999)throw new IOException("Invalid GTA build");
    }
    public static byte[] ProtectSession(Dictionary<string,object> grant,string session,string game,string runtime){
        if(!Regex.IsMatch(session,@"\A[a-f0-9]{32}\z"))throw new IOException("Invalid runtime session ID");
        var value=new Dictionary<string,object>(grant);value["schema"]=1;value["session"]=session;value["gameDirectory"]=Path.GetFullPath(game);value["runtimeDirectory"]=Path.GetFullPath(runtime);
        return ProtectedData.Protect(Encoding.UTF8.GetBytes(Data.Json.Serialize(value)),Encoding.UTF8.GetBytes("MatrixGames.Runtime.Join.v1"),DataProtectionScope.CurrentUser);
    }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool WritePrivateProfileString(string section,string key,string value,string path);
    public async Task Launch(Dictionary<string,object> grant,ServerEntry server,GameLocations game,Action<string> report,CancellationToken cancel){
        ValidateGrant(grant,server);int build=Convert.ToInt32(grant["gameBuild"]);
        if(!Builds.Contains(build))throw new IOException("This Matrix runtime does not support the server's GTA build.");
        string installed=Path.GetFullPath(game.InstallDirectory);ContentInstaller.NoLinks(installed);
        string exe=Path.Combine(installed,"GTA5.exe");if(!File.Exists(exe))throw new IOException("Select your installed GTA V Legacy folder in Settings.");
        if(FileVersionInfo.GetVersionInfo(exe).FileVersion!="1.0."+build+".0")throw new IOException("The selected server and installed GTA build do not match.");
        if(installed.Any(char.IsControl)||game.DocumentsDirectory.Any(char.IsControl))throw new IOException("Invalid GTA location");
        string saves=Path.GetFullPath(Path.Combine(game.DocumentsDirectory,"MatrixGames"));ContentInstaller.NoLinks(saves);
        if(Encoding.UTF8.GetByteCount(saves)>180)throw new IOException("Select a shorter GTA documents path in Settings so the game can save its profile.");
        Directory.CreateDirectory(saves);
        string ini=Path.Combine(Root,"CitizenFX.ini");ContentInstaller.NoLinks(ini);
        if(!WritePrivateProfileString("Game","IVPath",installed,ini)||!WritePrivateProfileString("Game","SavedBuildNumber",build.ToString(),ini)||!WritePrivateProfileString("Game","DefaultBuild",build.ToString(),ini)||!WritePrivateProfileString("Matrix","SavePath",saves,ini))throw new IOException("Could not save the Matrix runtime game locations");
        string folder=Path.Combine(Path.GetDirectoryName(LauncherSettings.SettingsPath),"sessions");ContentInstaller.NoLinks(folder);Directory.CreateDirectory(folder);
        string session=Guid.NewGuid().ToString("N"),file=Path.Combine(folder,session+".bin"),status=file+".status.json";
        byte[] bytes=ProtectSession(grant,session,installed,Root);using(var output=new FileStream(file,FileMode.CreateNew,FileAccess.Write,FileShare.None))output.Write(bytes,0,bytes.Length);
        try{
            cancel.ThrowIfCancellationRequested();using(var host=Process.Start(new ProcessStartInfo{FileName=Path.Combine(Root,"MatrixGtaRuntime.exe"),Arguments="--matrix-session "+session+" -b"+build,WorkingDirectory=Root,UseShellExecute=false})){
                report("Starting the Matrix GTA runtime...");
                for(int i=0;i<180;i++){
                    await Task.Delay(1000,cancel);
                    if(host.HasExited&&host.ExitCode!=0)throw new IOException("The Matrix GTA runtime exited during startup (code "+host.ExitCode+"). Check its log, then join again.");
                    if(!File.Exists(status))continue;
                    ContentInstaller.NoLinks(status);if(new FileInfo(status).Length>4096)throw new IOException("Invalid native startup response");
                    Dictionary<string,object> response;try{response=Data.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(status));}catch(ArgumentException){continue;}catch(IOException){continue;}
                    if(Data.Text(response,"session")!=session||Data.Text(response,"runtime")!=Id||Data.Text(response,"state")!="connecting")throw new IOException("Invalid native startup response");
                    using(var native=Process.GetProcessById(Convert.ToInt32(response["pid"]))){if(!Path.GetFullPath(native.MainModule.FileName).StartsWith(Root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Unexpected native runtime process");report("Matrix GTA is connecting to "+server.Name+"...");await Task.Run(()=>native.WaitForExit());report("Matrix GTA session ended.");return;}
                }
                throw new IOException("The Matrix GTA runtime did not finish starting. Check its log and game ownership prompt, then join again.");
            }
        }finally{if(File.Exists(file)&&File.ReadAllBytes(file).SequenceEqual(bytes))File.Delete(file);if(File.Exists(status))File.Delete(status);}
    }
}
