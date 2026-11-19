using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

static class RuntimeMapCapacityTests {
    static int count;
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);count++;}
    static void Reject(Action action){try{action();}catch(IOException){count++;return;}throw new Exception("Invalid runtime update accepted");}
    static readonly byte[] Old=Encoding.UTF8.GetBytes("\uFEFF<root>\r\n<PoolName>MapDataStore</PoolName>\r\n\t<PoolSize value=\"12000\"/>\r\n<PoolName>Other</PoolName><PoolSize value=\"12000\"/></root>\r\n");
    static readonly byte[] New=RuntimeMapCapacity.Expand(Old);
    static readonly string OldHash=RuntimeMapCapacity.Digest(Old),NewHash=RuntimeMapCapacity.Digest(New);
    static string Fixture(string scratch){
        string root=Path.Combine(scratch,"map-capacity-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var entries=new List<object>();
        foreach(string name in new[]{"MatrixGtaRuntime.exe","CoreRT.dll","CitizenGame.dll","components.json",RuntimeMapCapacity.Config}){
            string file=Path.Combine(root,name);Directory.CreateDirectory(Path.GetDirectoryName(file));File.WriteAllBytes(file,name==RuntimeMapCapacity.Config?Old:Encoding.UTF8.GetBytes("unchanged fixture"));
            entries.Add(new{path=name,sha256=ContentInstaller.HashFile(file),bytes=new FileInfo(file).Length});
        }
        File.WriteAllText(Path.Combine(root,"matrix-runtime.json"),Data.Json.Serialize(new{schema=1,runtime=MatrixNativeRuntime.Id,game="gta-v",entrypoint="MatrixGtaRuntime.exe",builds=new[]{3889},files=entries}),new UTF8Encoding(false));return root;
    }
    static MatrixNativeRuntime Apply(string root){return RuntimeMapCapacity.Apply(root,OldHash,NewHash,()=>false,null);}
    public static void Run(string scratch){
        string root=Fixture(scratch),config=Path.GetFullPath(Path.Combine(root,RuntimeMapCapacity.Config)),manifest=Path.Combine(root,"matrix-runtime.json");
        var other=Directory.GetFiles(root,"*",SearchOption.AllDirectories).Where(p=>p!=config&&p!=manifest).ToDictionary(p=>p,ContentInstaller.HashFile);
        Apply(root);
        Check(File.ReadAllBytes(config).SequenceEqual(New),"Wrong map configuration");
        Check(Encoding.UTF8.GetString(New).Replace("<PoolSize value=\"24000\"/>","<PoolSize value=\"12000\"/>")==Encoding.UTF8.GetString(Old),"Unexpected pool/encoding changes");
        Check(other.All(p=>ContentInstaller.HashFile(p.Key)==p.Value),"Changed an unrelated runtime file");
        Check(MatrixNativeRuntime.Inspect(root).Builds.Single()==3889,"Updated package failed verification");
        byte[] committed=File.ReadAllBytes(manifest);DateTime timestamp=File.GetLastWriteTimeUtc(config);Apply(root);
        Check(File.ReadAllBytes(manifest).SequenceEqual(committed)&&File.GetLastWriteTimeUtc(config)==timestamp,"Migration was not idempotent");
        root=Fixture(scratch);config=Path.Combine(root,RuntimeMapCapacity.Config);manifest=Path.Combine(root,"matrix-runtime.json");committed=File.ReadAllBytes(manifest);
        Reject(()=>RuntimeMapCapacity.Apply(root,OldHash,NewHash,()=>true,null));Check(File.ReadAllBytes(config).SequenceEqual(Old),"Changed a running game's configuration");
        int busyChecks=0;Reject(()=>RuntimeMapCapacity.Apply(root,OldHash,NewHash,()=>++busyChecks==2,null));Check(File.ReadAllBytes(config).SequenceEqual(Old),"Ignored game startup during update");
        Reject(()=>RuntimeMapCapacity.Apply(root,OldHash,NewHash,()=>false,()=>{throw new IOException("Injected manifest failure");}));
        Check(File.ReadAllBytes(config).SequenceEqual(Old)&&File.ReadAllBytes(manifest).SequenceEqual(committed),"Failed update did not roll back");
        Check(!Directory.GetFiles(root,"*.matrix-map-*",SearchOption.AllDirectories).Any(),"Update leaked temporary files");
        // Simulate power loss after the configuration replacement.
        File.WriteAllBytes(config,New);Reject(()=>MatrixNativeRuntime.Inspect(root));Apply(root);
        Check(MatrixNativeRuntime.Inspect(root).Builds.Single()==3889,"Could not recover interrupted update");
        root=Fixture(scratch);File.WriteAllBytes(Path.Combine(root,RuntimeMapCapacity.Config),New);File.AppendAllText(Path.Combine(root,"CoreRT.dll"),"tampered");Reject(()=>Apply(root));
        root=Fixture(scratch);File.AppendAllText(Path.Combine(root,RuntimeMapCapacity.Config),"tampered");Reject(()=>Apply(root));
        root=Fixture(scratch);Reject(()=>RuntimeMapCapacity.Apply(root,new string('a',64),new string('b',64),()=>false,null));
        root=Fixture(scratch);Reject(()=>RuntimeMapCapacity.Apply(root,OldHash,new string('b',64),()=>false,null));Check(File.ReadAllBytes(Path.Combine(root,RuntimeMapCapacity.Config)).SequenceEqual(Old),"Wrong target checksum wrote configuration");
        Reject(()=>RuntimeMapCapacity.Expand(Encoding.UTF8.GetBytes("<PoolName>Other</PoolName>")));
        Console.WriteLine("PASS: "+count+" runtime map-capacity migration checks.");
    }
}
