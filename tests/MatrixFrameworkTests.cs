using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
class MatrixFrameworkTests {
    static int count;
    static void Check(bool value){if(!value)throw new Exception("Matrix framework check failed");count++;}
    static void Reject(Action action){try{action();}catch{count++;return;}throw new Exception("Invalid runtime input accepted");}
    public static int Main(string[] args){
        string grantFile=null;
        try{
            string root=Path.Combine(Path.GetFullPath(args[0]),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            var entries=new List<object>();foreach(var name in new[]{"MatrixGtaRuntime.exe","CoreRT.dll","CitizenGame.dll","components.json"}){string path=Path.Combine(root,name);File.WriteAllText(path,"hash-verification fixture only");entries.Add(new{path=name,sha256=ContentInstaller.HashFile(path),bytes=new FileInfo(path).Length});}
            string manifest=Path.Combine(root,"matrix-runtime.json");File.WriteAllText(manifest,Data.Json.Serialize(new{schema=1,runtime=MatrixNativeRuntime.Id,game="gta-v",entrypoint="MatrixGtaRuntime.exe",builds=new[]{3889},files=entries}));
            Check(MatrixNativeRuntime.Inspect(root).Builds.Single()==3889);
            File.AppendAllText(Path.Combine(root,"CoreRT.dll"),"changed");Reject(()=>MatrixNativeRuntime.Inspect(root));
            var server=new ServerEntry{Game="gta-v",Id="matrix-test"};
            var grant=new Dictionary<string,object>{{"schema",4},{"runtime",MatrixNativeRuntime.Id},{"game","gta-v"},{"server",server.Id},{"endpoint","https://native.matrixgames.app/"},{"gameBuild",3889},{"ticket",new string('a',43)},{"expiresAt",DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()+240000}};
            MatrixNativeRuntime.ValidateGrant(grant,server);count++;
            var zeroPort=new Dictionary<string,object>(grant);zeroPort["endpoint"]="https://native.matrixgames.app:0/";Reject(()=>MatrixNativeRuntime.ValidateGrant(zeroPort,server));
            foreach(var bad in new[]{Tuple.Create("schema",(object)1),Tuple.Create("runtime",(object)"other"),Tuple.Create("endpoint",(object)"http://native.matrixgames.app/"),Tuple.Create("endpoint",(object)"https://a@native.matrixgames.app/"),Tuple.Create("endpoint",(object)"https://native.matrixgames.app/?ticket=x"),Tuple.Create("endpoint",(object)"https://native.matrixgames.app:99999/"),Tuple.Create("server",(object)"other"),Tuple.Create("ticket",(object)"short"),Tuple.Create("expiresAt",(object)0L)}){var value=new Dictionary<string,object>(grant);value[bad.Item1]=bad.Item2;Reject(()=>MatrixNativeRuntime.ValidateGrant(value,server));}
            string session=Guid.NewGuid().ToString("N"),folder=Path.Combine(Path.GetDirectoryName(LauncherSettings.SettingsPath),"sessions");Directory.CreateDirectory(folder);grantFile=Path.Combine(folder,session+".bin");
            File.WriteAllBytes(grantFile,MatrixNativeRuntime.ProtectSession(grant,session,Path.Combine(root,"game"),root));
            using(var native=Process.Start(new ProcessStartInfo{FileName=Path.GetFullPath(args[1]),Arguments=session+" \""+root+"\"",UseShellExecute=false,CreateNoWindow=true})){native.WaitForExit();Check(native.ExitCode==0);}
            Check(!File.Exists(grantFile));Console.WriteLine("PASS: "+count+" Matrix framework package, grant and native handoff checks.");return 0;
        }catch(Exception error){Console.Error.WriteLine(error);return 1;}finally{if(grantFile!=null&&File.Exists(grantFile))File.Delete(grantFile);}
    }
}
