using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

public sealed class LauncherWindow : Form {
    readonly LauncherSettings settings;readonly ApiClient api=new ApiClient();
    readonly ListBox servers=new ListBox{Dock=DockStyle.Fill};
    readonly Label status=new Label{AutoSize=false,Dock=DockStyle.Bottom,Height=64,Padding=new Padding(12)};
    readonly Label account=new Label{AutoSize=true,Text="Browse servers, then sign in to join."};
    readonly Button join=new Button{Text="Join server",AutoSize=true};
    readonly Button cancel=new Button{Text="Cancel",AutoSize=true,Enabled=false};
    readonly Button signIn=new Button{Text="Sign in",AutoSize=true},signOut=new Button{Text="Sign out",AutoSize=true};
    readonly TextBox details=new TextBox{Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical};
    readonly Label libraryStatus=new Label{AutoSize=true,MaximumSize=new Size(780,50)};
    readonly FlowLayoutPanel actions=new FlowLayoutPanel{Dock=DockStyle.Top,Height=46};
    CancellationTokenSource operation;bool busy;string pending;
    public LauncherWindow(string input){
        settings=LauncherSettings.Load(LauncherSettings.SettingsPath);api.Token=TokenStore.Load();pending=input==null?null:JoinLink.Server(input);
        Text="Matrix Games Launcher";Size=new Size(880,640);MinimumSize=new Size(720,520);StartPosition=FormStartPosition.CenterScreen;Font=new Font("Segoe UI",10);
        var header=new Label{Text="Matrix Games\nGame mods and online servers",Dock=DockStyle.Top,Height=100,Padding=new Padding(12),Font=new Font("Segoe UI",17)};
        var panel=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(12),ColumnCount=1,RowCount=6};panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));panel.RowStyles.Add(new RowStyle(SizeType.Absolute,30));panel.RowStyles.Add(new RowStyle(SizeType.Absolute,46));panel.RowStyles.Add(new RowStyle(SizeType.Percent,100));panel.RowStyles.Add(new RowStyle(SizeType.Absolute,110));panel.RowStyles.Add(new RowStyle(SizeType.Absolute,48));panel.RowStyles.Add(new RowStyle(SizeType.Absolute,50));Controls.Add(panel);Controls.Add(status);Controls.Add(header);
        var bottom=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=80,FlowDirection=FlowDirection.LeftToRight};bottom.Controls.Add(join);bottom.Controls.Add(cancel);
        panel.Controls.Add(account,0,0);panel.Controls.Add(actions,0,1);panel.Controls.Add(servers,0,2);panel.Controls.Add(details,0,3);panel.Controls.Add(libraryStatus,0,4);panel.Controls.Add(bottom,0,5);
        Button refresh=Button("Refresh servers",()=>Run(RefreshServers));signIn.Click+=(s,e)=>Run(SignIn);signOut.Click+=(s,e)=>Run(async()=>{try{if(api.Token!=null)await api.Call("/v1/launcher/auth/logout",new{},operation.Token);}finally{SignedOut("Signed out on this device.");}});
        Button options=Button("Settings / library",()=>{using(var dialog=new SettingsWindow(settings))dialog.ShowDialog(this);UpdateLibrary();});
        Button host=Button("Host a server",()=>Open("https://matrixgames.app/servers#host"));
        actions.Controls.AddRange(new Control[]{refresh,signIn,signOut,options,host});
        UpdateAccountButtons();UpdateLibrary();servers.SelectedIndexChanged+=(s,e)=>{var selected=servers.SelectedItem as ServerEntry;details.Text=selected==null?"Select a server to see its game and required content.":selected.Summary;};
        join.Click+=(s,e)=>Run(Join);cancel.Click+=(s,e)=>{if(operation!=null)operation.Cancel();};Shown+=(s,e)=>Run(RefreshServers);
        FormClosing+=(s,e)=>{if(busy){e.Cancel=true;status.Text="Cancel the current operation or close the game before closing the launcher.";}};
    }
    void UpdateLibrary(){libraryStatus.Text="Asset library: "+settings.LibraryDirectory+"  (change in Settings / library)";}
    void UpdateAccountButtons(){signIn.Visible=api.Token==null;signOut.Visible=api.Token!=null;}
    void SignedOut(string message){api.Token=null;TokenStore.Clear();account.Text=message;UpdateAccountButtons();}
    static Button Button(string title,Action action){var b=new Button{Text=title,AutoSize=true};b.Click+=(s,e)=>action();return b;}
    public static void Open(string url){Process.Start(new ProcessStartInfo{FileName=url,UseShellExecute=true});}
    async void Run(Func<Task> task){if(busy)return;busy=true;actions.Enabled=false;servers.Enabled=false;join.Enabled=false;cancel.Enabled=true;operation=new CancellationTokenSource();
        try{await task();}catch(OperationCanceledException){status.Text="Cancelled. Completed assets and resumable downloads are retained.";}catch(ApiException e){if(e.Status==401)SignedOut("Session expired. Sign in to join.");status.Text=e.Message;}catch(Exception e){status.Text=e.Message;}finally{operation.Dispose();operation=null;busy=false;actions.Enabled=true;servers.Enabled=true;join.Enabled=true;cancel.Enabled=false;UpdateAccountButtons();}}
    async Task RefreshServers(){
        status.Text="Loading servers...";string selection=pending??(servers.SelectedItem as ServerEntry??new ServerEntry()).Id;
        var result=await api.Call("/v1/servers",null,operation.Token);var rows=Data.Array(result["servers"]).Select(ServerEntry.Parse).Where(s=>Games.All.Any(g=>g.Id==s.Game)).ToArray();servers.Items.Clear();servers.Items.AddRange(rows);
        var selected=rows.FirstOrDefault(s=>s.Id==selection);if(selected!=null)servers.SelectedItem=selected;else if(rows.Length>0)servers.SelectedIndex=0;
        status.Text=rows.Length+" servers. Select a server and click Join; game options follow.";bool signedIn=false;
        if(api.Token!=null){try{var me=await api.Call("/v1/launcher/me",null,operation.Token);account.Text="Signed in as "+Data.Text(Data.Object(me["user"]),"name");signedIn=true;}catch(ApiException e){if(e.Status==401)SignedOut("Session expired. Sign in to join.");else account.Text="Could not check account. Refresh to retry.";}}UpdateAccountButtons();
        // A website Join is already a join request. Continue into this game's options
        // after sign-in, without requiring a second click in the server browser.
        if(pending!=null&&selected==null){pending=null;status.Text="The requested server is no longer listed. Choose another server.";}
        if(pending!=null&&selected!=null&&selected.Online&&signedIn){pending=null;BeginInvoke(new Action(()=>Run(Join)));}
    }
    async Task SignIn(){
        var start=await api.Call("/v1/launcher/auth/start",new{},operation.Token);string code=Data.Text(start,"userCode"),device=Data.Text(start,"deviceCode"),url=Data.Text(start,"verificationUrl");
        if(!System.Text.RegularExpressions.Regex.IsMatch(code,@"\A[A-F0-9]{10}\z")||!System.Text.RegularExpressions.Regex.IsMatch(device,@"\A[A-Za-z0-9_-]{43}\z")||url!="https://matrixgames.app/launcher.html#code="+code)throw new IOException("Unexpected sign-in response");
        status.Text="In your browser, confirm launcher code "+code+". Waiting for approval...";Open(url);
        for(int i=0;i<120;i++){await Task.Delay(5000,operation.Token);var reply=await api.Call("/v1/launcher/auth/poll",new{deviceCode=device},operation.Token);string token=Data.Text(reply,"token");if(token.Length==0)continue;if(!System.Text.RegularExpressions.Regex.IsMatch(token,@"\A[A-Za-z0-9_-]{43}\z"))throw new IOException("Invalid sign-in token");TokenStore.Save(token);api.Token=token;await RefreshServers();status.Text="Signed in. Select a server and join.";return;}throw new IOException("Sign-in expired. Try again.");
    }
    async Task Join(){
        var server=servers.SelectedItem as ServerEntry;if(server==null)throw new IOException("Select a server first");if(!server.Online)throw new IOException("This server is offline. Refresh to check again.");if(api.Token==null)throw new IOException("Sign in before joining a server.");
        var game=Games.Resolve(server.Game);GameLocations locations;if(!settings.Games.TryGetValue(game.Id,out locations)){locations=new GameLocations();settings.Games[game.Id]=locations;}game.ResolveLocations(settings,locations);game.ValidateLocations(locations);settings.Save(LauncherSettings.SettingsPath);string steamId=game.AccountId();
        // Keep one installation / game session active across launcher processes.
        using(var mutex=new Mutex(false,@"Local\MatrixGames.GameSession")){
            bool held=false;try{try{held=mutex.WaitOne(0);}catch(AbandonedMutexException){held=true;}if(!held)throw new IOException("Another launcher window is installing content or running a game.");
                status.Text="Checking server assets...";var response=await api.Call("/v1/servers/"+server.Id+"/manifest",null,operation.Token);if(Data.Text(response,"server")!=server.Id)throw new IOException("Wrong server manifest");var manifest=AssetManifest.Parse(response["manifest"]);if(manifest.Game!=server.Game||!manifest.Assets.Any(p=>p.Kind=="track"&&p.Id==server.Track)||!server.Models.All(id=>manifest.Assets.Any(p=>p.Kind=="car"&&p.Id==id)))throw new IOException("Server manifest is incomplete");
                var vault=new AssetVault(settings.LibraryDirectory);var plan=await Task.Run(()=>vault.Plan(locations.InstallDirectory,manifest,ContentDiscovery.SearchRoots(settings,locations)));operation.Token.ThrowIfCancellationRequested();
                string model=game.SelectModel(this,server,plan);if(model==null){status.Text="Join cancelled.";return;}
                if(plan.Choices.Length>0){string review=vault.Review(plan);using(var dialog=new ReviewWindow(server.Name,review,plan.Choices.Select(c=>c.Package).ToArray(),locations.InstallDirectory)){if(dialog.ShowDialog(this)!=DialogResult.OK){status.Text="No assets approved. Nothing downloaded.";return;}}
                    var progress=new Progress<InstallProgress>(p=>status.Text=p.Message+(p.Total>0?" ("+(p.Done*100/p.Total)+"%)":""));await Task.Run(()=>vault.Apply(server.Id,locations.InstallDirectory,manifest,operation.Token,progress,plan));
                }
                if((await Task.Run(()=>vault.Missing(locations.InstallDirectory,manifest))).Length>0)throw new IOException("Required content is missing or changed");status.Text="Authorizing your join...";
                var grant=await api.Call("/v1/servers/"+server.Id+"/join",new{model=model,steamId=steamId,manifestRevision=manifest.Revision},operation.Token);
                await game.Launch(grant,server,model,steamId,settings,message=>status.Text=message,operation.Token);
            }finally{if(held)mutex.ReleaseMutex();}
        }
    }
}
public sealed class ReviewWindow : Form {
    public ReviewWindow(string server,string review,ContentPackage[] packages,string game){Text="Approve server assets - "+server;Size=new Size(760,570);StartPosition=FormStartPosition.CenterParent;Font=new Font("Segoe UI",10);
        var text=new TextBox{Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Both,WordWrap=false,Dock=DockStyle.Fill,Text=review+"\r\n\r\n"+string.Join("\r\n\r\n",packages.Select(p=>p.Name+"\r\nServer download URL (only used when listed as a download above): "+p.Url.AbsoluteUri+"\r\nArchive SHA-256: "+p.Sha256+"\r\nGame link: "+AssetVault.Destination(game,p)))};var buttons=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=48,FlowDirection=FlowDirection.RightToLeft};var yes=new Button{Text="Approve and install",AutoSize=true,DialogResult=DialogResult.OK};var no=new Button{Text="Cancel",DialogResult=DialogResult.Cancel};buttons.Controls.Add(yes);buttons.Controls.Add(no);Controls.Add(text);Controls.Add(buttons);CancelButton=no;}
}
public sealed class SettingsWindow : Form {
    public SettingsWindow(LauncherSettings settings){Text="Matrix Games Settings";Size=new Size(720,650);StartPosition=FormStartPosition.CenterParent;Font=new Font("Segoe UI",10);var panel=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoScroll=true,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(16)};Controls.Add(panel);
        var steam=Folder(panel,"Steam folder (shared)",settings.SteamDirectory);var library=Folder(panel,"Matrix Games library folder or drive (for example K:\\)",settings.LibraryDirectory);panel.Controls.Add(new Label{Text="Change where new assets are stored. Existing linked assets stay in place and can be reused.",AutoSize=true,MaximumSize=new Size(640,40)});panel.Controls.Add(new Label{Text="Driver name",AutoSize=true});var driver=new TextBox{Text=settings.DriverName,Width=500,MaxLength=32};panel.Controls.Add(driver);
        var fields=new Dictionary<string,TextBox[]>();foreach(var game in Games.All){GameLocations location;if(!settings.Games.TryGetValue(game.Id,out location))location=new GameLocations();var install=Folder(panel,game.Name+" installation",string.IsNullOrWhiteSpace(location.InstallDirectory)?game.FindInstallation(settings.SteamDirectory):location.InstallDirectory);var documents=Folder(panel,game.Name+" documents / settings",string.IsNullOrWhiteSpace(location.DocumentsDirectory)?game.DefaultDocuments:location.DocumentsDirectory);var content=Folder(panel,game.Name+" existing mods folder (optional)",location.ContentDirectory);fields.Add(game.Id,new[]{install,documents,content});var detect=new Button{Text="Find "+game.Name+" from Steam",AutoSize=true};detect.Click+=(sender,eventArgs)=>{try{string found=game.FindInstallation(steam.Text);if(found.Length==0)MessageBox.Show(this,"No installation found in this Steam folder or its libraries.");else install.Text=found;}catch(Exception error){MessageBox.Show(this,error.Message);}};panel.Controls.Add(detect);steam.TextChanged+=(sender,eventArgs)=>{try{if(string.IsNullOrWhiteSpace(install.Text)||!File.Exists(Path.Combine(install.Text,"acs.exe"))){string found=game.FindInstallation(steam.Text);if(found.Length>0)install.Text=found;}}catch(IOException){}catch(ArgumentException){}};}
        var save=new Button{Text="Save settings",AutoSize=true};save.Click+=(s,e)=>{try{new AssetVault(library.Text);if(string.IsNullOrWhiteSpace(driver.Text)||driver.Text.Any(char.IsControl)||driver.Text.IndexOfAny(new[]{'=','[',']'})>=0)throw new IOException("Choose a plain driver name");settings.SteamDirectory=steam.Text;settings.SelectLibrary(library.Text);settings.DriverName=driver.Text.Trim();foreach(var f in fields)settings.Games[f.Key]=new GameLocations{InstallDirectory=f.Value[0].Text,DocumentsDirectory=f.Value[1].Text,ContentDirectory=f.Value[2].Text};foreach(var adapter in Games.All)adapter.ResolveLocations(settings,settings.Games[adapter.Id]);settings.Save(LauncherSettings.SettingsPath);DialogResult=DialogResult.OK;}catch(Exception error){MessageBox.Show(this,error.Message,"Settings");}};panel.Controls.Add(save);
        var enable=new Button{Text="Enable browser joins",AutoSize=true};enable.Click+=(s,e)=>{try{Program.InstallProtocol();MessageBox.Show(this,"Browser joins are enabled for your Windows account.");}catch(Exception error){MessageBox.Show(this,error.Message);}};panel.Controls.Add(enable);
        var openSteam=new Button{Text="Open Steam",AutoSize=true};openSteam.Click+=(s,e)=>{try{string exe=Path.Combine(steam.Text,"steam.exe");if(!File.Exists(exe))throw new IOException("Choose the folder containing steam.exe");Process.Start(new ProcessStartInfo{FileName=exe,UseShellExecute=true});}catch(Exception error){MessageBox.Show(this,error.Message);}};panel.Controls.Add(openSteam);
    }
    static TextBox Folder(FlowLayoutPanel panel,string label,string value){panel.Controls.Add(new Label{Text=label,AutoSize=true});var row=new FlowLayoutPanel{Width=650,Height=38};var text=new TextBox{Text=value,Width=530};var browse=new Button{Text="Browse",AutoSize=true};browse.Click+=(s,e)=>{using(var dialog=new FolderBrowserDialog{SelectedPath=text.Text})if(dialog.ShowDialog()==DialogResult.OK)text.Text=dialog.SelectedPath;};row.Controls.Add(text);row.Controls.Add(browse);panel.Controls.Add(row);return text;}
}
public static class Program {
    public static string ProtocolCommand(string exe){if(!Path.IsPathRooted(exe)||exe.Contains("\"")||exe.Any(char.IsControl))throw new IOException("Invalid launcher path");return "\""+exe+"\" \"%1\"";}
    public static void InstallProtocol(){
        string folder=Path.GetDirectoryName(LauncherSettings.SettingsPath),target=Path.Combine(folder,"MatrixGamesLauncher.exe");ContentInstaller.NoLinks(target);string command=ProtocolCommand(target);
        using(var existing=Registry.CurrentUser.OpenSubKey(@"Software\Classes\matrixgames\shell\open\command")){if(existing!=null&&Convert.ToString(existing.GetValue(""))!=command)throw new IOException("Another application owns matrixgames links. Its registration was left unchanged.");}
        Directory.CreateDirectory(folder);if(!string.Equals(Application.ExecutablePath,target,StringComparison.OrdinalIgnoreCase))File.Copy(Application.ExecutablePath,target,true);
        using(var key=Registry.CurrentUser.CreateSubKey(@"Software\Classes\matrixgames")){key.SetValue("","URL:Matrix Games Join");key.SetValue("URL Protocol","");using(var open=key.CreateSubKey(@"shell\open\command"))open.SetValue("",command);}
    }
    [STAThread] public static int Main(string[] args){try{System.Net.ServicePointManager.SecurityProtocol=System.Net.SecurityProtocolType.Tls12;if(args.Length>1)throw new IOException("Expected one Matrix Games join link");Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new LauncherWindow(args.Length==1?args[0]:null));return 0;}catch(Exception e){MessageBox.Show(e.Message,"Matrix Games Launcher");return 1;}}
}
