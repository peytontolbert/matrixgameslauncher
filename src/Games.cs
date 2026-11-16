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
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;

// Game adapters own installation detection, requirements and launch behavior.
// Server manifests never supply executables, scripts or command-line arguments.
public interface IGameAdapter {
    string Id {get;} string Name {get;} string DefaultDocuments {get;}
    string FindInstallation(string steam);
    void ResolveLocations(LauncherSettings settings,GameLocations locations);
    void ValidateLocations(GameLocations locations);
    string AccountId();
    string SelectModel(IWin32Window owner,ServerEntry server,ContentPlan plan);
    Task Launch(Dictionary<string,object> grant,ServerEntry server,string model,string account,LauncherSettings settings,Action<string> report,CancellationToken cancel);
}
public static class Games {
    public static readonly IGameAdapter[] All={new AssettoCorsa(),new GtaV()};
    public static IGameAdapter Resolve(string id){var game=All.FirstOrDefault(g=>g.Id==id);if(game==null)throw new IOException("This game does not have an installed launcher integration yet");return game;}
}
public static class JoinLink {
    static Match Parse(string link){return Regex.Match(link??"",@"\Amatrixgames://join/?\?game=([a-z0-9-]+)&server=([A-Za-z0-9_-]{1,80})\z");}
    public static string Server(string link){
        var match=Parse(link);
        if(!match.Success)throw new IOException("Invalid join link. Open matrixgames.app/servers and choose a server.");Games.Resolve(match.Groups[1].Value);return match.Groups[2].Value;
    }
}
public sealed class AssettoCorsa : IGameAdapter {
    public string Id {get{return "assetto-corsa";}} public string Name {get{return "Assetto Corsa";}}
    public string DefaultDocuments {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"Assetto Corsa");}}
    public string SelectModel(IWin32Window owner,ServerEntry server,ContentPlan plan){using(var dialog=new AssettoCarWindow(server,plan))return dialog.ShowDialog(owner)==DialogResult.OK?dialog.SelectedModel:null;}
    public string FindInstallation(string steam){
        return ContentDiscovery.SteamInstallations(steam).FirstOrDefault()??"";
    }
    public void ResolveLocations(LauncherSettings settings,GameLocations locations){
        if(string.IsNullOrWhiteSpace(locations.InstallDirectory)||!File.Exists(Path.Combine(locations.InstallDirectory,"acs.exe"))){string found=FindInstallation(settings.SteamDirectory);if(found.Length>0)locations.InstallDirectory=found;}
        if(string.IsNullOrWhiteSpace(locations.DocumentsDirectory))locations.DocumentsDirectory=DefaultDocuments;
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
    public static string InstalledSkin(string folder){return Directory.Exists(folder)?Directory.GetDirectories(folder).Select(Path.GetFileName).Where(Data.ContentId).OrderBy(n=>n).FirstOrDefault():null;}
    public static void ValidateGrant(Dictionary<string,object> d,ServerEntry server,string model){
        int schema=Convert.ToInt32(d["schema"]);string host=Data.Text(d,"host");
        if(!(schema==2||schema==3)||Data.Text(d,"game")!=server.Game||Data.Text(d,"server")!=server.Id||Data.Text(d,"track")!=server.Track||Data.Text(d,"model")!=model||!server.Models.Contains(model)||!Data.Id(model)||!Data.ContentId(server.Track)||Data.Text(d,"layout")!=server.Layout)throw new IOException("The server join response does not match your selection");
        if(host.Length>253||!host.Contains('.')||!host.Split('.').All(p=>Regex.IsMatch(p,"^[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?$")))throw new IOException("Invalid server address");
        Port(d["gamePort"]);Port(d["httpPort"]);double remaining=Convert.ToDouble(d["expiresAt"])-DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string password=Data.Text(d,"password");if(remaining<=0||remaining>600000||schema==2&&!Regex.IsMatch(password,@"\Amg1\.[A-Za-z0-9_-]{1,180}\.[A-Za-z0-9_-]{43}\z")||schema==3&&password.Length!=0)throw new IOException("Join permission expired or invalid. Join again.");
    }
    public static string RaceConfig(Dictionary<string,object> d,string driver,string account,string skin,string features){
        string publicName=Data.Text(d,"displayName");
        if(string.IsNullOrEmpty(publicName)||!Regex.IsMatch(publicName,@"\A[A-Za-z0-9][A-Za-z0-9 ._-]{0,22}[A-Za-z0-9]\z"))throw new IOException("The server did not provide your Matrix Games display name. Update the launcher and join again.");
        driver=publicName;
        Plain(driver);if(driver.Length>32||!Regex.IsMatch(account,@"\A7656119[0-9]{10}\z")||!Data.Id(skin)||!Data.ContentId(Data.Text(d,"track"))||Data.Text(d,"layout").Length>0&&!Data.ContentId(Data.Text(d,"layout"))||!Data.Id(Data.Text(d,"model")))throw new IOException("Invalid driver, account or content name");
        string password=Data.Text(d,"password");if(password.Length>0)Plain(password);Plain(Data.Text(d,"host"));
        if(features.Length>4096||features.Split(',').Any(f=>f.Length>0&&!Regex.IsMatch(f,@"\A[A-Z0-9_]{1,64}\z")))throw new IOException("Invalid server features");
        return "[HEADER]\r\nVERSION=2\r\n__CM_FEATURE_SET=2\r\n[RACE]\r\nTRACK="+d["track"]+"\r\nCONFIG_TRACK="+Data.Text(d,"layout")+"\r\nMODEL="+d["model"]+"\r\nMODEL_CONFIG=\r\nCARS=1\r\nAI_LEVEL=98\r\nFIXED_SETUP=0\r\nPENALTIES=0\r\n[REMOTE]\r\nACTIVE=1\r\n__CM_EXTENDED=1\r\n__FEATURES="+features+"\r\nSERVER_IP="+d["host"]+"\r\nSERVER_PORT="+Port(d["gamePort"])+"\r\nSERVER_HTTP_PORT="+Port(d["httpPort"])+"\r\nREQUESTED_CAR="+d["model"]+"\r\nNAME="+driver+"\r\nGUID="+account+"\r\nTEAM=\r\nPASSWORD="+password+"\r\n[CAR_0]\r\nMODEL=-\r\nSKIN="+skin+"\r\nDRIVER_NAME="+driver+"\r\nNATIONALITY=USA\r\n[SESSION_0]\r\nNAME=Practice\r\nTYPE=1\r\nDURATION_MINUTES=0\r\nSPAWN_SET=PIT\r\n[REPLAY]\r\nACTIVE=0\r\n[GHOST_CAR]\r\nRECORDING=0\r\nPLAYING=0\r\n[WEATHER]\r\nNAME=3_clear\r\n[TEMPERATURE]\r\nAMBIENT=26\r\nROAD=32\r\n[LIGHTING]\r\nSUN_ANGLE=6\r\nTIME_MULT=1\r\n";
    }
    public async Task Launch(Dictionary<string,object> d,ServerEntry server,string model,string account,LauncherSettings settings,Action<string> report,CancellationToken cancel){
        ValidateGrant(d,server,model);var locations=settings.Games[Id];ValidateLocations(locations);report("Checking the native server...");
        if(Convert.ToInt32(d["schema"])==2&&(!File.Exists(Path.Combine(locations.InstallDirectory,"dwrite.dll"))||!Directory.Exists(Path.Combine(locations.InstallDirectory,"extension"))))throw new IOException("This server requires Custom Shaders Patch. Install CSP in your Assetto Corsa folder before joining.");
        var origin="http://"+d["host"]+":"+Port(d["httpPort"]);
        var info=await ApiClient.Request(new Uri(origin+"/INFO"),null,null,cancel);string advertised=Data.Text(info,"track");var csp=Regex.Match(advertised,@"\Acsp/[0-9]+/\.\./(.+)\z");if(csp.Success)advertised=csp.Groups[1].Value;
        if(advertised!=server.Track+(server.Layout.Length>0?"-"+server.Layout:""))throw new IOException("Server track changed. Refresh the server list.");
        string features="";if(Convert.ToInt32(d["schema"])==2){var details=await ApiClient.Request(new Uri(origin+"/JSON%7C0"),null,null,cancel);object raw;if(details.TryGetValue("Features",out raw))features=string.Join(",",Data.Array(raw).Select(Convert.ToString));}
        string skins=Path.Combine(locations.InstallDirectory,"content","cars",model,"skins");string skin=InstalledSkin(skins);if(skin==null)throw new IOException("This car needs an installed skin");
        ValidateGrant(d,server,model);cancel.ThrowIfCancellationRequested();string config=RaceConfig(d,settings.DriverName,account,skin,features),cfg=Path.Combine(locations.DocumentsDirectory,"cfg"),path=Path.Combine(cfg,"race.ini");ContentInstaller.NoLinks(path);Directory.CreateDirectory(cfg);
        string backup=path+".matrixgames-"+Guid.NewGuid().ToString("N")+".bak";bool existed=File.Exists(path);if(existed)File.Copy(path,backup,false);
        try{
            File.WriteAllText(path,config,new UTF8Encoding(false));var start=new ProcessStartInfo{FileName=Path.Combine(locations.InstallDirectory,"acs.exe"),WorkingDirectory=locations.InstallDirectory,UseShellExecute=false};start.EnvironmentVariables["SteamAppId"]="244210";start.EnvironmentVariables["SteamGameId"]="244210";
            using(var process=Process.Start(start)){report("Assetto Corsa is running. Close the game to return to the launcher.");await Task.Run(()=>process.WaitForExit());}
        }finally{if(File.Exists(path)&&File.ReadAllText(path)==config){if(existed)File.Copy(backup,path,true);else File.Delete(path);}}
        report("Session ended. Previous race settings restored when unchanged; backup retained.");
    }
}
public sealed class AssettoCarWindow : Form {
    sealed class Choice {public string Id,Name;public override string ToString(){return Name;}}
    readonly ComboBox choices=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=640};
    public string SelectedModel {get{var selected=choices.SelectedItem as Choice;return selected==null?null:selected.Id;}}
    public static string Requirements(ContentPlan plan){
        if(plan==null)return "Required assets will be checked before joining.";
        var lines=new List<string>{"Required assets: "+plan.Manifest.Assets.Length+" | Download: "+(plan.DownloadBytes/1024.0/1024/1024).ToString("0.00")+" GB", "All server cars are needed to see other players, plus the track and traffic assets.", ""};
        foreach(var p in plan.Manifest.Assets){var choice=plan.Choices.FirstOrDefault(c=>c.Package==p);string state=choice==null?"Installed":choice.Folder!=null?"Reuse existing folder":choice.Archive!=null?"Install existing archive":p.InstalledOnly?"Install through Steam or track owner":"Download "+(p.Bytes/1024/1024)+" MB";lines.Add(state+" | "+p.Kind+" | "+p.Name);}
        return string.Join("\r\n",lines);
    }
    public AssettoCarWindow(ServerEntry server,ContentPlan plan=null){
        Text="Join "+server.Name;ClientSize=new Size(700,530);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;StartPosition=FormStartPosition.CenterParent;Font=new Font("Segoe UI",10);
        var panel=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(22)};
        panel.Controls.Add(new Label{Text="Choose your Assetto Corsa car",AutoSize=true,Font=new Font("Segoe UI",15)});
        panel.Controls.Add(new Label{Text=server.Name+" | "+(server.Online?"Online":"Offline")+" | "+server.Players+"/"+server.Capacity+" players\nTrack: "+server.Track,AutoSize=true,MaximumSize=new Size(640,65),Margin=new Padding(3,12,3,12)});
        foreach(string id in server.Models)choices.Items.Add(new Choice{Id=id,Name=server.ModelName(id)});if(choices.Items.Count>0)choices.SelectedIndex=0;panel.Controls.Add(choices);
        panel.Controls.Add(new TextBox{Text=Requirements(plan),Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Width=640,Height=270,Margin=new Padding(3,14,3,3)});
        var buttons=new FlowLayoutPanel{Width=640,Height=45,Margin=new Padding(0,12,0,0)};var next=new Button{Text=plan!=null&&plan.Choices.Length==0?"Join server":"Review required assets",AutoSize=true,DialogResult=DialogResult.OK,Enabled=choices.Items.Count>0};var back=new Button{Text="Cancel",AutoSize=true,DialogResult=DialogResult.Cancel};buttons.Controls.Add(next);buttons.Controls.Add(back);panel.Controls.Add(buttons);Controls.Add(panel);AcceptButton=next;CancelButton=back;
    }
}
