using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

public static class LauncherTests {
    static int count;
    static void Assert(bool condition,string message){if(!condition)throw new Exception(message);count++;}
    static void Reject(Action run,string message){bool rejected=false;try{run();}catch{rejected=true;}Assert(rejected,message);}
    static ContentPackage Package(string id,string source){return new ContentPackage{Id=id,Name=id,Kind="car",ArchiveRoot=id,Url=new Uri("https://assets.example.test/car.7z"),Sha256=new string('a',64),Bytes=1,ExpandedBytes=3,Checks=new[]{new AssetCheck{Path="data.acd",Sha256=ContentInstaller.HashFile(Path.Combine(source,"data.acd")),Bytes=3}}};}
    public static int Main(string[] args){try{
        if(args.Length!=1||!Path.IsPathRooted(args[0]))throw new Exception("Pass an absolute scratch directory");string root=Path.Combine(args[0],Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        foreach(string bad in new[]{"../outside","root/../../outside","C:/outside","root/file:ads","root/CON.txt","root/file.","root/sub\\file","root/a\nb"})Reject(()=>ContentInstaller.ArchiveName(bad,"root"),"Unsafe archive path accepted");
        ContentInstaller.ArchiveName("root/skins/blue skin/body.dds","root");count++;
        foreach(string bad in new[]{"matrixgames://join?game=assetto-corsa&server=a&host=evil","matrixgames://join?game=halo&server=a","matrixgames://join?game=assetto-corsa&server=a\n","https://join?game=assetto-corsa&server=a"})Reject(()=>JoinLink.Server(bad),"Unsafe protocol accepted");
        Assert(JoinLink.Server("matrixgames://join?game=assetto-corsa&server=managed-110")=="managed-110","Join link failed");
        Assert(JoinLink.Model("matrixgames://join?game=assetto-corsa&server=managed-110&model=car_one")=="car_one","Selected car retained");
        Assert(Program.ProtocolCommand(@"C:\Test User\Launcher.exe")=="\"C:\\Test User\\Launcher.exe\" \"%1\"","Protocol quoting");
        string source=Path.Combine(root,"source","fixture");Directory.CreateDirectory(source);File.WriteAllText(Path.Combine(source,"data.acd"),"abc",new UTF8Encoding(false));var package=Package("fixture",source);
        var manifest=new AssetManifest{Game="assetto-corsa",Revision=new string('b',64),Assets=new[]{package}};string game=Path.Combine(root,"game"),library=Path.Combine(root,"library");Directory.CreateDirectory(game);var vault=new AssetVault(library);
        string archive=Path.Combine(root,"fixture.tar");using(var p=Process.Start(new ProcessStartInfo{FileName=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"tar.exe"),Arguments="-cf \""+archive+"\" -C \""+Path.GetDirectoryName(source)+"\" fixture",UseShellExecute=false,CreateNoWindow=true})){p.WaitForExit();Assert(p.ExitCode==0,"Fixture archive creation");}
        package.Sha256=ContentInstaller.HashFile(archive);package.Bytes=new FileInfo(archive).Length;Directory.CreateDirectory(Path.Combine(library,"downloads"));File.Copy(archive,Path.Combine(library,"downloads",package.Sha256+".7z"));
        Assert(vault.Missing(game,manifest).Length==1,"Missing asset detection");vault.Apply("one",game,manifest,CancellationToken.None,null);
        string destination=AssetVault.Destination(game,package),payload=vault.Payload(package);Assert(Junction.Target(destination)==payload,"Game points at shared vault");Assert(File.ReadAllText(Path.Combine(destination,"data.acd"))=="abc","Linked data readable");Assert(!File.Exists(Path.Combine(library,"downloads",package.Sha256+".7z")),"Completed archive removed");
        vault.Apply("two",game,manifest,CancellationToken.None,null);Assert(File.Exists(Path.Combine(library,"servers","two","assets.json")),"Second server records reused asset");Assert(Directory.GetDirectories(Path.Combine(library,"objects")).Length==1,"No duplicated payload");
        string game2=Path.Combine(root,"game2");Directory.CreateDirectory(game2);vault.Apply("three",game2,manifest,CancellationToken.None,null);Assert(Junction.Target(AssetVault.Destination(game2,package))==payload,"Cached asset reused without network");
        string game3=Path.Combine(root,"game3"),existing=AssetVault.Destination(game3,package);Directory.CreateDirectory(existing);File.WriteAllText(Path.Combine(existing,"data.acd"),"old");vault.Apply("four",game3,manifest,CancellationToken.None,null);string backup=Directory.GetDirectories(Path.Combine(game3,".matrixgames-backups")).Single();Assert(File.ReadAllText(Path.Combine(backup,"data.acd"))=="old","Original files backed up");
        string game4=Path.Combine(root,"game4"),installed=AssetVault.Destination(game4,package);Directory.CreateDirectory(installed);File.Copy(Path.Combine(source,"data.acd"),Path.Combine(installed,"data.acd"));vault.Apply("five",game4,manifest,CancellationToken.None,null);Assert(Junction.Target(installed)==null,"Existing matching game data stays in place");
        string outside=Path.Combine(root,"unrelated");Directory.CreateDirectory(outside);string game5=Path.Combine(root,"game5"),external=AssetVault.Destination(game5,package);Directory.CreateDirectory(Path.GetDirectoryName(external));Junction.Create(external,outside);Reject(()=>vault.Apply("six",game5,manifest,CancellationToken.None,null),"External links must be preserved");Assert(Junction.Target(external)==outside,"External link unchanged");
        var cancelled=new CancellationTokenSource();cancelled.Cancel();Reject(()=>vault.Apply("seven",game,manifest,cancelled.Token,null),"Cancellation honored");Assert(File.ReadAllText(Path.Combine(payload,"data.acd"))=="abc","Cancel preserves shared files");
        string game6=Path.Combine(root,"game6"),rollback=AssetVault.Destination(game6,package);Directory.CreateDirectory(rollback);File.WriteAllText(Path.Combine(rollback,"original.txt"),"preserve");Reject(()=>vault.Link(game6,rollback,Path.Combine(root,"missing-payload"),CancellationToken.None),"Invalid link fails");Assert(File.ReadAllText(Path.Combine(rollback,"original.txt"))=="preserve","Failed link restores original directory");
        Directory.CreateDirectory(Path.Combine(installed,"data"));Assert(!package.Matches(installed),"Unpacked physics override is not accepted as matching");
        Reject(()=>ContentInstaller.ValidateListing("fixture/data.acd\n","lrwxrwxrwx 1 0 0 3 Jan 1 00:00 fixture/data.acd -> outside\n",package),"Archive symlink rejected");
        Reject(()=>ContentInstaller.ValidateListing("fixture/data.acd\nfixture/DATA.acd\n","-rw-r--r-- 1 0 0 1 Jan 1 00:00 fixture/data.acd\n-rw-r--r-- 1 0 0 2 Jan 1 00:00 fixture/DATA.acd\n",package),"Duplicate Windows paths rejected");
        var settings=new LauncherSettings{LibraryDirectory=library,SteamDirectory=Path.Combine(root,"Steam"),DriverName="Fixture"};settings.Games["assetto-corsa"]=new GameLocations{InstallDirectory=game,DocumentsDirectory=Path.Combine(root,"Documents")};string settingsFile=Path.Combine(root,"settings.json");settings.Save(settingsFile);settings.DriverName="Updated";settings.Save(settingsFile);Assert(LauncherSettings.Load(settingsFile).DriverName=="Updated"&&File.Exists(settingsFile+".previous"),"Settings persist atomically");
        var grant=new Dictionary<string,object>{{"schema",3},{"game","assetto-corsa"},{"server","one"},{"track","track"},{"model","fixture"},{"host","203.0.113.8"},{"gamePort",9600},{"httpPort",8081},{"password",""},{"expiresAt",DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()+90000}};var server=new ServerEntry{Id="one",Game="assetto-corsa",Track="track",Models=new[]{"fixture"}};AssettoCorsa.ValidateGrant(grant,server,"fixture");Assert(AssettoCorsa.RaceConfig(grant,"Driver","76561198000000000","skin","").Contains("SERVER_PORT=9600"),"Race config");Reject(()=>AssettoCorsa.RaceConfig(grant,"bad\n[REMOTE]","76561198000000000","skin",""),"INI injection rejected");grant["track"]="other";Reject(()=>AssettoCorsa.ValidateGrant(grant,server,"fixture"),"Changed track rejected");
        // Scratch is retained for inspection. Never recursively delete junction-bearing fixtures.
        Console.WriteLine(count+" launcher checks passed. Fixture: "+root);return 0;
    }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
