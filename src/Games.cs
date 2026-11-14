using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

// Game adapters own installation detection, requirements and launch behavior.
// Server manifests never supply executables, scripts or command-line arguments.
public interface IGameAdapter {
    string Id {get;} string Name {get;} string DefaultDocuments {get;}
    string FindInstallation(string steam);
    void ValidateLocations(GameLocations locations);
    string AccountId();
    Task Launch(Dictionary<string,object> grant,ServerEntry server,string model,string account,LauncherSettings settings,Action<string> report,CancellationToken cancel);
}
public static class Games {
    public static readonly IGameAdapter[] All={new AssettoCorsa()};
    public static IGameAdapter Resolve(string id){var game=All.FirstOrDefault(g=>g.Id==id);if(game==null)throw new IOException("This game does not have an installed launcher integration yet");return game;}
}
public static class JoinLink {
    static Match Parse(string link){return Regex.Match(link??"",@"\Amatrixgames://join\?game=([a-z0-9-]+)&server=([A-Za-z0-9_-]{1,80})(?:&model=([A-Za-z0-9_-]{1,80}))?\z");}
    public static string Model(string link){Server(link);return Parse(link).Groups[3].Value;}
    public static string Server(string link){
        var match=Parse(link);
        if(!match.Success)throw new IOException("Invalid join link. Open matrixgames.app/servers and choose a server.");Games.Resolve(match.Groups[1].Value);return match.Groups[2].Value;
    }
}
public sealed class AssettoCorsa : IGameAdapter {
    public string Id {get{return "assetto-corsa";}} public string Name {get{return "Assetto Corsa";}}
    public string DefaultDocuments {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"Assetto Corsa");}}
    public string FindInstallation(string steam){
        if(string.IsNullOrWhiteSpace(steam))return "";var candidates=new List<string>{Path.Combine(steam,"steamapps","common","assettocorsa")};
        string libraries=Path.Combine(steam,"steamapps","libraryfolders.vdf");if(File.Exists(libraries))foreach(Match m in Regex.Matches(File.ReadAllText(libraries),"\"path\"\\s*\"([^\"]+)\""))candidates.Add(Path.Combine(m.Groups[1].Value.Replace("\\\\","\\"),"steamapps","common","assettocorsa"));
        return candidates.FirstOrDefault(p=>File.Exists(Path.Combine(p,"acs.exe")))??"";
    }
    public void ValidateLocations(GameLocations locations){
        ContentInstaller.NoLinks(locations.InstallDirectory);ContentInstaller.NoLinks(locations.DocumentsDirectory);
        if(!File.Exists(Path.Combine(locations.InstallDirectory,"acs.exe")))throw new IOException("Set the Assetto Corsa folder containing acs.exe in Settings.");
        if(string.IsNullOrWhiteSpace(locations.DocumentsDirectory)||!Path.IsPathRooted(locations.DocumentsDirectory))throw new IOException("Set your Assetto Corsa documents folder in Settings.");
        if(Process.GetProcessesByName("acs").Any()||Process.GetProcessesByName("acs_x86").Any())throw new IOException("Exit Assetto Corsa before changing content or joining another server.");
    }
    public string AccountId(){uint id=unchecked((uint)Convert.ToInt32(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam\ActiveProcess","ActiveUser",0)));if(id==0||!Process.GetProcessesByName("steam").Any())throw new IOException("Open Steam and sign in to the account that owns Assetto Corsa.");return (76561197960265728UL+id).ToString();}
    static string Plain(string value){if(string.IsNullOrEmpty(value)||value.Length>180||value.Any(char.IsControl)||value.IndexOfAny(new[]{'=','[',']'})>=0)throw new IOException("Invalid launch setting");return value;}
    static int Port(object value){int p=Convert.ToInt32(value);if(p<1||p>65535)throw new IOException("Invalid server port");return p;}
    public static void ValidateGrant(Dictionary<string,object> d,ServerEntry server,string model){
        int schema=Convert.ToInt32(d["schema"]);string host=Data.Text(d,"host");
        if(!(schema==2||schema==3)||Data.Text(d,"game")!=server.Game||Data.Text(d,"server")!=server.Id||Data.Text(d,"track")!=server.Track||Data.Text(d,"model")!=model||!server.Models.Contains(model)||!Data.Id(model)||!Data.Id(server.Track))throw new IOException("The server join response does not match your selection");
        if(host.Length>253||!host.Contains('.')||!host.Split('.').All(p=>Regex.IsMatch(p,"^[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?$")))throw new IOException("Invalid server address");
        Port(d["gamePort"]);Port(d["httpPort"]);double remaining=Convert.ToDouble(d["expiresAt"])-DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string password=Data.Text(d,"password");if(remaining<=0||remaining>600000||schema==2&&!Regex.IsMatch(password,@"\Amg1\.[A-Za-z0-9_-]{1,180}\.[A-Za-z0-9_-]{43}\z")||schema==3&&password.Length!=0)throw new IOException("Join permission expired or invalid. Join again.");
    }
    public static string RaceConfig(Dictionary<string,object> d,string driver,string account,string skin,string features){
        Plain(driver);if(driver.Length>32||!Regex.IsMatch(account,@"\A7656119[0-9]{10}\z")||!Data.Id(skin)||!Data.Id(Data.Text(d,"track"))||!Data.Id(Data.Text(d,"model")))throw new IOException("Invalid driver, account or content name");
        string password=Data.Text(d,"password");if(password.Length>0)Plain(password);Plain(Data.Text(d,"host"));
        if(features.Length>4096||features.Split(',').Any(f=>f.Length>0&&!Regex.IsMatch(f,@"\A[A-Z0-9_]{1,64}\z")))throw new IOException("Invalid server features");
        return "[HEADER]\r\nVERSION=2\r\n__CM_FEATURE_SET=2\r\n[RACE]\r\nTRACK="+d["track"]+"\r\nCONFIG_TRACK=\r\nMODEL="+d["model"]+"\r\nMODEL_CONFIG=\r\nCARS=1\r\nAI_LEVEL=98\r\nFIXED_SETUP=0\r\nPENALTIES=0\r\n[REMOTE]\r\nACTIVE=1\r\n__CM_EXTENDED=1\r\n__FEATURES="+features+"\r\nSERVER_IP="+d["host"]+"\r\nSERVER_PORT="+Port(d["gamePort"])+"\r\nSERVER_HTTP_PORT="+Port(d["httpPort"])+"\r\nREQUESTED_CAR="+d["model"]+"\r\nNAME="+driver+"\r\nGUID="+account+"\r\nTEAM=\r\nPASSWORD="+password+"\r\n[CAR_0]\r\nMODEL=-\r\nSKIN="+skin+"\r\nDRIVER_NAME="+driver+"\r\nNATIONALITY=USA\r\n[SESSION_0]\r\nNAME=Practice\r\nTYPE=1\r\nDURATION_MINUTES=0\r\nSPAWN_SET=PIT\r\n[REPLAY]\r\nACTIVE=0\r\n[GHOST_CAR]\r\nRECORDING=0\r\nPLAYING=0\r\n[WEATHER]\r\nNAME=3_clear\r\n[TEMPERATURE]\r\nAMBIENT=26\r\nROAD=32\r\n[LIGHTING]\r\nSUN_ANGLE=6\r\nTIME_MULT=1\r\n";
    }
    public async Task Launch(Dictionary<string,object> d,ServerEntry server,string model,string account,LauncherSettings settings,Action<string> report,CancellationToken cancel){
        ValidateGrant(d,server,model);var locations=settings.Games[Id];ValidateLocations(locations);report("Checking the native server...");
        if(Convert.ToInt32(d["schema"])==2&&(!File.Exists(Path.Combine(locations.InstallDirectory,"dwrite.dll"))||!Directory.Exists(Path.Combine(locations.InstallDirectory,"extension"))))throw new IOException("This server requires Custom Shaders Patch. Install CSP in your Assetto Corsa folder before joining.");
        var origin="http://"+d["host"]+":"+Port(d["httpPort"]);
        var info=await ApiClient.Request(new Uri(origin+"/INFO"),null,null,cancel);string advertised=Data.Text(info,"track");var csp=Regex.Match(advertised,@"\Acsp/[0-9]+/\.\./([A-Za-z0-9_-]+)\z");if(csp.Success)advertised=csp.Groups[1].Value;
        if(advertised!=server.Track)throw new IOException("Server track changed. Refresh the server list.");
        string features="";if(Convert.ToInt32(d["schema"])==2){var details=await ApiClient.Request(new Uri(origin+"/JSON%7C0"),null,null,cancel);object raw;if(details.TryGetValue("Features",out raw))features=string.Join(",",Data.Array(raw).Select(Convert.ToString));}
        string skins=Path.Combine(locations.InstallDirectory,"content","cars",model,"skins");string skin=Directory.Exists(skins)?Directory.GetDirectories(skins).Select(Path.GetFileName).Where(Data.Id).OrderBy(n=>n).FirstOrDefault():null;if(skin==null)throw new IOException("This car needs an installed skin");
        ValidateGrant(d,server,model);cancel.ThrowIfCancellationRequested();string config=RaceConfig(d,settings.DriverName,account,skin,features),cfg=Path.Combine(locations.DocumentsDirectory,"cfg"),path=Path.Combine(cfg,"race.ini");ContentInstaller.NoLinks(path);Directory.CreateDirectory(cfg);
        string backup=path+".matrixgames-"+Guid.NewGuid().ToString("N")+".bak";bool existed=File.Exists(path);if(existed)File.Copy(path,backup,false);
        try{
            File.WriteAllText(path,config,new UTF8Encoding(false));var start=new ProcessStartInfo{FileName=Path.Combine(locations.InstallDirectory,"acs.exe"),WorkingDirectory=locations.InstallDirectory,UseShellExecute=false};start.EnvironmentVariables["SteamAppId"]="244210";start.EnvironmentVariables["SteamGameId"]="244210";
            using(var process=Process.Start(start)){report("Assetto Corsa is running. Close the game to return to the launcher.");await Task.Run(()=>process.WaitForExit());}
        }finally{if(File.Exists(path)&&File.ReadAllText(path)==config){if(existed)File.Copy(backup,path,true);else File.Delete(path);}}
        report("Session ended. Previous race settings restored when unchanged; backup retained.");
    }
}
