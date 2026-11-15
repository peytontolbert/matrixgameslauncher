using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
static class RuntimeInstallTests {
    static int checks;
    static void Reject(Action action){try{action();}catch(IOException){checks++;return;}throw new Exception("Unsafe runtime archive accepted");}
    public static void Run(string root){
        string folder=Path.Combine(root,"runtime-install");Directory.CreateDirectory(folder);
        foreach(string name in new[]{"../escape","/absolute","a:b","CON.txt","a\\b","file."}){
            string zip=Path.Combine(folder,Guid.NewGuid().ToString("N")+".zip");
            using(var archive=ZipFile.Open(zip,ZipArchiveMode.Create))using(var writer=new StreamWriter(archive.CreateEntry(name).Open()))writer.Write("unsafe");
            string output=Path.Combine(folder,Guid.NewGuid().ToString("N"));
            Reject(()=>RuntimeInstaller.ExtractVerified(zip,output,ContentInstaller.HashFile(zip),new FileInfo(zip).Length,100,CancellationToken.None));
            if(Directory.Exists(output))throw new Exception("Unsafe ZIP created a destination");
        }
        string valid=Path.Combine(folder,"valid.zip");using(var archive=ZipFile.Open(valid,ZipArchiveMode.Create))using(var writer=new StreamWriter(archive.CreateEntry("bin/fixture.dll").Open()))writer.Write("fixture");
        string target=Path.Combine(folder,"valid"),hash=ContentInstaller.HashFile(valid);long bytes=new FileInfo(valid).Length;
        Reject(()=>RuntimeInstaller.ExtractVerified(valid,target,new string('0',64),bytes,100,CancellationToken.None));
        Reject(()=>RuntimeInstaller.ExtractVerified(valid,target,hash,bytes,2,CancellationToken.None));
        RuntimeInstaller.ExtractVerified(valid,target,hash,bytes,100,CancellationToken.None);
        if(File.ReadAllText(Path.Combine(target,"bin","fixture.dll"))!="fixture")throw new Exception("Runtime extraction failed");checks++;
        Reject(()=>RuntimeInstaller.ExtractVerified(valid,target,hash,bytes,100,CancellationToken.None));
        Console.WriteLine(checks+" runtime archive checks passed.");
    }
}
