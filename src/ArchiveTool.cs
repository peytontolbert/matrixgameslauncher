using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

// The pinned, unmodified 7-Zip redistributable is embedded: no PATH dependency,
// installer, elevated privileges, or executable downloaded from a content server.
public static class ArchiveTool {
    const string Digest="dd3757f31c97e33776f2590317d4fb12f2da075db3dbb1e3f94d0f2a01d84ffa";
    public static string Materialize(string parent){
        string root=ContentInstaller.Child(parent,".tools/7zip-26.06");ContentInstaller.NoLinks(root);Directory.CreateDirectory(root);
        string tool=ContentInstaller.Child(root,"7za.exe");
        if(!File.Exists(tool)){
            string temporary=ContentInstaller.Child(root,Guid.NewGuid().ToString("N")+".new");
            try{
                using(var source=typeof(ArchiveTool).Assembly.GetManifestResourceStream("Matrix.7za.exe"))
                using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){
                    if(source==null)throw new IOException("Launcher archive component is missing. Download the current launcher.");source.CopyTo(output);output.Flush(true);
                }
                if(ContentInstaller.HashFile(temporary)!=Digest)throw new IOException("Launcher archive component failed verification");
                try{File.Move(temporary,tool);}catch(IOException){if(!File.Exists(tool))throw;}
            }finally{if(File.Exists(temporary))File.Delete(temporary);}
        }
        ContentInstaller.NoLinks(tool);
        if(ContentInstaller.HashFile(tool)!=Digest)throw new IOException("Launcher archive component was modified. Remove the indicated tool cache and retry: "+root);
        return tool;
    }
    public static string Quote(string value){if(value.Contains("\"")||value.Any(char.IsControl))throw new IOException("Invalid archive argument");return "\""+value+"\"";}
    static async Task<string> ReadBounded(StreamReader reader){var result=new StringBuilder();char[] buffer=new char[8192];int n;while((n=await reader.ReadAsync(buffer,0,buffer.Length))>0){if(result.Length+n>16*1024*1024)throw new IOException("Archive listing is too large");result.Append(buffer,0,n);}return result.ToString();}
    public static string Run(string parent,string arguments,CancellationToken cancel){
        cancel.ThrowIfCancellationRequested();string tool=Materialize(parent);
        // Hold a read-only handle so the verified executable cannot be replaced
        // between the digest check and process creation/execution.
        using(var locked=new FileStream(tool,FileMode.Open,FileAccess.Read,FileShare.Read)){
            using(var sha=System.Security.Cryptography.SHA256.Create())if(BitConverter.ToString(sha.ComputeHash(locked)).Replace("-","").ToLowerInvariant()!=Digest)throw new IOException("Archive component changed");
            using(var p=new Process{StartInfo=new ProcessStartInfo{FileName=tool,Arguments=arguments,UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8}}){
                p.Start();p.StandardInput.Close();var stdout=ReadBounded(p.StandardOutput);var stderr=ReadBounded(p.StandardError);var timer=Stopwatch.StartNew();
                try{while(!p.WaitForExit(100)){cancel.ThrowIfCancellationRequested();if(stdout.IsFaulted||stderr.IsFaulted)throw new IOException("Archive output exceeded its limit");if(timer.Elapsed.TotalMinutes>30)throw new IOException("Archive operation timed out");}cancel.ThrowIfCancellationRequested();}
                catch{if(!p.HasExited){p.Kill();p.WaitForExit();}throw;}
                string output=stdout.GetAwaiter().GetResult(),error=stderr.GetAwaiter().GetResult();
                if(p.ExitCode!=0)throw new IOException("Could not read the content archive: "+error.Substring(0,Math.Min(error.Length,400)));return output;
            }
        }
    }
    public static void ValidateListing(string listing,ContentPackage package){
        var records=Regex.Split(listing.Trim(),@"\r?\n\s*\r?\n");
        if(records.Length==0||records.Length>100000)throw new IOException("Invalid archive listing");
        var seen=new Dictionary<string,bool>(StringComparer.OrdinalIgnoreCase);long total=0;bool found=false;
        foreach(string record in records){
            var fields=new Dictionary<string,string>(StringComparer.Ordinal);
            foreach(string row in record.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries)){
                int separator=row.IndexOf(" = ",StringComparison.Ordinal);if(separator<1)throw new IOException("Malformed archive listing");
                string key=row.Substring(0,separator);if(fields.ContainsKey(key))throw new IOException("Duplicate archive metadata");fields.Add(key,row.Substring(separator+3));
            }
            string name,size,attributes,encrypted,folder,mode;
            if(!fields.TryGetValue("Path",out name)||!fields.TryGetValue("Size",out size))throw new IOException("Incomplete archive listing");
            name=name.Replace('\\','/');ContentInstaller.ArchiveName(name,name.Split('/')[0]);name=name.TrimEnd('/');
            fields.TryGetValue("Attributes",out attributes);fields.TryGetValue("Folder",out folder);fields.TryGetValue("Encrypted",out encrypted);fields.TryGetValue("Mode",out mode);
            if(encrypted!=null&&encrypted!="-"||fields.Any(k=>!string.IsNullOrEmpty(k.Value)&&(k.Key.IndexOf("Link",StringComparison.OrdinalIgnoreCase)>=0||k.Key=="Alternate Stream"))||
                (!string.IsNullOrEmpty(mode)&&mode[0]!='-'&&mode[0]!='d')||
                (attributes!=null&&Regex.IsMatch(attributes,@"(?:^|\s)[lcbps][rwxstST-]{9}(?:\s|$)")))throw new IOException("Archive contains links, encryption or unsupported entries");
            bool directory=folder=="+"||(attributes!=null&&(attributes.StartsWith("D",StringComparison.Ordinal)||Regex.IsMatch(attributes,@"(?:^|\s)d[rwxstST-]{9}(?:\s|$)")));
            if(seen.ContainsKey(name))throw new IOException("Duplicate archive destination");seen.Add(name,directory);
            long bytes;if(!long.TryParse(size,NumberStyles.None,CultureInfo.InvariantCulture,out bytes)||bytes<0||(directory&&bytes!=0))throw new IOException("Invalid archive size");
            total=checked(total+bytes);if(total>package.ExpandedBytes)throw new IOException("Archive expands beyond its verified size");
            if(!directory&&name.StartsWith(package.ArchiveRoot+"/",StringComparison.Ordinal))found=true;
        }
        foreach(string name in seen.Keys){string parent=name;while(parent.LastIndexOf('/')>=0){parent=parent.Substring(0,parent.LastIndexOf('/'));bool dir;if(seen.TryGetValue(parent,out dir)&&!dir)throw new IOException("Archive file conflicts with a directory");}}
        if(!found)throw new IOException("Expected content folder is missing");if(total!=package.ExpandedBytes)throw new IOException("Archive size differs from the reviewed package");
    }
}
