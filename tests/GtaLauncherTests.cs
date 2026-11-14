using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
class GtaLauncherTests {
    static void Check(bool value,string text){if(!value)throw new Exception(text);}
    static void Reject(Action action){try{action();}catch(IOException){return;}throw new Exception("Invalid grant accepted");}
    public static int Main(string[] args){try{
        string root=Path.Combine(Path.GetFullPath(args[0]),Guid.NewGuid().ToString("N")),game=Path.Combine(root,"game"),library=Path.Combine(root,"library"),cache=Path.Combine(library,"runtime","gta-v");Directory.CreateDirectory(game);Directory.CreateDirectory(cache);
        var grant=new Dictionary<string,object>{{"schema",1},{"game","gta-v"},{"server","managed-gta-legion"},{"room","gta-legion"},{"revision","los-santos-legion-v1"},{"client","native"},{"ticket",new string('a',43)},{"endpoint","wss://api.matrixgames.app/v1/gta/native-socket"},{"expiresAt",DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()+299000}};
        GtaV.ValidateGrant(grant,"managed-gta-legion");
        foreach(var pair in new[]{Tuple.Create("game",(object)"assetto-corsa"),Tuple.Create("server",(object)"other"),Tuple.Create("client",(object)"browser"),Tuple.Create("endpoint",(object)"wss://attacker.example/socket"),Tuple.Create("expiresAt",(object)0L)}){var bad=new Dictionary<string,object>(grant);bad[pair.Item1]=pair.Item2;Reject(()=>GtaV.ValidateGrant(bad,"managed-gta-legion"));}
        Check(JoinLink.Server("matrixgames://join?game=gta-v&server=managed-gta-legion")=="managed-gta-legion","GTA protocol handler");
        Check(GtaV.LaunchArguments=="-applaunch 271590 -nobattleye -noBE","Direct GTA launch permits Rockstar ownership activation");
        File.Copy(Path.Combine(args[1],"ScriptHookV_3889.0_1158.13.zip"),Path.Combine(cache,"b64c97c3353906f14621e7e9511e4aec2a7d436ecc21ed124d3816585e2e6188.zip"));
        File.Copy(Path.Combine(args[1],"ScriptHookVDotNet-v3.7.0-nightly.204.zip"),Path.Combine(cache,"bb832165f6b546e63706fee696c5789f3160d7d5f6da67b6c261538c0d6963d5.zip"));
        Check(GtaRuntime.Missing(game).Length==8,"Detect missing native runtime");File.WriteAllText(Path.Combine(game,"ScriptHookV.dll"),"existing-loader");
        GtaRuntime.Install(game,library,CancellationToken.None,_=>{throw new Exception("Unexpected network download");});
        Check(GtaRuntime.Missing(game).Length==0,"Runtime files installed and verified");
        Check(Directory.GetFiles(Path.Combine(game,"MatrixGamesBackups"),"ScriptHookV.dll",SearchOption.AllDirectories).Any(f=>File.ReadAllText(f)=="existing-loader"),"Replaced runtime backed up");
        string plugin=Path.Combine(game,"scripts","MatrixGtaClient.dll");var time=File.GetLastWriteTimeUtc(plugin);
        GtaRuntime.Install(game,library,CancellationToken.None,_=>{throw new Exception("Unexpected download");});Check(File.GetLastWriteTimeUtc(plugin)==time,"Matching client reused");
        Console.WriteLine("PASS: GTA grants, direct launch arguments, verified cached installation, backups and reuse. Fixture: "+root);return 0;
    }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
