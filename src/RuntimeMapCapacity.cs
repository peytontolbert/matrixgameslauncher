using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

// NYC registers more maps than the stock runtime's 12,000-entry registry can hold.
// Migrate only the pinned configuration, preserving all executables and caches.
internal static class RuntimeMapCapacity {
    public const int Capacity=24000;
    internal const string Config="citizen/common/data/gameconfig.xml";
    const string Original="19d263277a8a3b125a37e095f0472680e9c3c02d0f7f200b458df0bfff9c0797";
    const string Updated="47c372717971958bad792ceb1eb3e4292ab295b9986ad011f7e193e4b146f8ac";
    internal static string Digest(byte[] bytes){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
    internal static byte[] Expand(byte[] bytes){
        var utf8=new UTF8Encoding(false,true);string text=utf8.GetString(bytes);
        const string pattern="(<PoolName>MapDataStore</PoolName>\\s*<PoolSize value=\")12000(\"\\s*/>)";
        if(Regex.Matches(text,pattern).Count!=1)throw new IOException("Unsupported Matrix GTA map configuration. Reinstall the current runtime before joining.");
        return utf8.GetBytes(Regex.Replace(text,pattern,"${1}24000${2}"));
    }
    static bool GameRunning(){
        foreach(var process in Process.GetProcesses())using(process){
            try{string name=process.ProcessName;if(name.StartsWith("MatrixGtaRuntime",StringComparison.OrdinalIgnoreCase)||name.Equals("GTA5",StringComparison.OrdinalIgnoreCase)||name.StartsWith("FiveM",StringComparison.OrdinalIgnoreCase))return true;}
            catch(InvalidOperationException){} // Process exited during enumeration.
        }
        return false;
    }
    public static MatrixNativeRuntime Prepare(string root){
        using(var mutex=new Mutex(false,@"Local\MatrixGames.GtaRuntimeUpdate")){
            bool held=false;try{
                try{held=mutex.WaitOne(0);}catch(AbandonedMutexException){held=true;}
                if(!held)throw new IOException("Another launcher is updating the Matrix GTA runtime.");
                return Apply(root,Original,Updated,GameRunning,null);
            }finally{if(held)mutex.ReleaseMutex();}
        }
    }
    // Internal seams allow fault-injection tests against synthetic private fixtures.
    internal static MatrixNativeRuntime Apply(string root,string original,string updated,Func<bool> busy,Action afterConfigWrite){
        root=Path.GetFullPath(root);
        if(busy())throw new IOException("Exit GTA and the Matrix GTA runtime before joining again.");
        // A power loss can leave the new configuration beside the old manifest.
        // Accept only that exact, pinned intermediate state; verify every other file.
        MatrixNativeRuntime.Inspect(root,(path,expected,size,actual,actualSize)=>path==Config&&expected==original&&actual==updated&&size==actualSize);
        string config=ContentInstaller.Child(root,Config),manifest=Path.Combine(root,"matrix-runtime.json");
        ContentInstaller.NoLinks(config);
        if(!File.Exists(config))throw new IOException("The Matrix runtime is missing its map configuration. Reinstall the current runtime.");
        byte[] before=File.ReadAllBytes(config);string hash=Digest(before);
        if(hash!=original&&hash!=updated)throw new IOException("The Matrix GTA map configuration is not a supported version. Reinstall the current runtime before joining.");
        byte[] manifestBefore=File.ReadAllBytes(manifest);
        var json=new System.Web.Script.Serialization.JavaScriptSerializer{MaxJsonLength=4*1024*1024};
        var value=json.Deserialize<Dictionary<string,object>>(Encoding.UTF8.GetString(manifestBefore).TrimStart('\uFEFF'));
        var entry=Data.Array(value["files"]).Cast<Dictionary<string,object>>().SingleOrDefault(e=>Data.Text(e,"path")==Config);
        if(entry==null)throw new IOException("The Matrix runtime does not verify its map configuration. Reinstall the current runtime.");
        if(hash==updated&&Data.Text(entry,"sha256")==updated)return MatrixNativeRuntime.Inspect(root);
        byte[] after=hash==original?Expand(before):before;
        if(Digest(after)!=updated||after.Length!=before.Length)throw new IOException("Unexpected Matrix map configuration update.");
        entry["sha256"]=updated;entry["bytes"]=after.Length;
        byte[] manifestAfter=new UTF8Encoding(false).GetBytes(json.Serialize(value));
        string suffix=".matrix-map-"+Guid.NewGuid().ToString("N");
        string configTemp=config+suffix,manifestTemp=manifest+suffix;
        bool changed=false,committed=false;
        try{
            WriteNew(configTemp,after);WriteNew(manifestTemp,manifestAfter);
            if(busy())throw new IOException("Exit GTA before the Matrix runtime can be updated.");
            // Refuse concurrent edits between inspection and replacement.
            if(!File.ReadAllBytes(config).SequenceEqual(before)||!File.ReadAllBytes(manifest).SequenceEqual(manifestBefore))throw new IOException("The Matrix runtime changed during its update. Join again.");
            if(hash==original){File.Replace(configTemp,config,null);changed=true;}
            if(afterConfigWrite!=null)afterConfigWrite();
            File.Replace(manifestTemp,manifest,null);committed=true;
            return MatrixNativeRuntime.Inspect(root);
        }catch{
            // If the manifest commit fails, restore the old bytes atomically. If
            // Windows also prevents rollback, the pinned intermediate state above
            // safely completes on the next join, without relaxing other integrity checks.
            if(changed&&!committed){try{if(File.Exists(configTemp))File.Delete(configTemp);WriteNew(configTemp,before);File.Replace(configTemp,config,null);}catch(IOException){}catch(UnauthorizedAccessException){}}
            throw;
        }finally{foreach(string file in new[]{configTemp,manifestTemp}){try{if(File.Exists(file))File.Delete(file);}catch(IOException){}catch(UnauthorizedAccessException){}}}
    }
    static void WriteNew(string path,byte[] bytes){using(var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(bytes,0,bytes.Length);stream.Flush(true);}}
}
