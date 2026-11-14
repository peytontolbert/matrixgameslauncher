using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

public sealed class InstallProgress {
    public string Message;public long Done,Total;
    public InstallProgress(string message,long done=0,long total=0){Message=message;Done=done;Total=total;}
}
public static class ContentInstaller {
    public static string HashFile(string path){using(var sha=SHA256.Create())using(var f=File.OpenRead(path))return BitConverter.ToString(sha.ComputeHash(f)).Replace("-","").ToLowerInvariant();}
    // Never follow an existing junction/symlink into another game or user folder.
    public static void NoLinks(string path){
        for(string p=Path.GetFullPath(path);!string.IsNullOrEmpty(p);p=Path.GetDirectoryName(p))
            if((Directory.Exists(p)||File.Exists(p))&&(File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new IOException("Choose the real game folder; linked folders cannot be installed into: "+p);
    }
    public static string Child(string root,string relative){
        string parent=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,result=Path.GetFullPath(Path.Combine(parent,relative));
        if(!result.StartsWith(parent,StringComparison.OrdinalIgnoreCase))throw new IOException("Content path escaped its destination");NoLinks(result);return result;
    }
    public static void ArchiveName(string name,string id){
        if(string.IsNullOrEmpty(name)||name.Length>220||name.Any(char.IsControl)||name.IndexOfAny(new[]{'\\',':','<','>','"','|','?','*'})>=0)throw new IOException("Unsupported archive path");
        string normalized=name.TrimEnd('/');var parts=normalized.Split('/');
        if(parts[0]!=id||parts.Any(p=>p.Length==0||p=="."||p==".."||p!=p.TrimEnd(' ','.')||Regex.IsMatch(p,"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\\.|$)",RegexOptions.IgnoreCase)))throw new IOException("Unsafe archive path: "+name);
    }
    public static void ValidateListing(string names,string details,ContentPackage package){
        var paths=names.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries);var rows=details.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries);
        if(paths.Length==0||paths.Length>100000||paths.Length!=rows.Length)throw new IOException("Invalid archive listing");
        var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long total=0;
        for(int i=0;i<paths.Length;i++){
            ArchiveName(paths[i],paths[i].Split('/')[0]);if(!seen.Add(paths[i].TrimEnd('/')))throw new IOException("Duplicate archive destination");
            var m=Regex.Match(rows[i],@"^([d-])[rwxstST-]{9}\s+\d+\s+\S+\s+\S+\s+(\d+)\s+");
            if(!m.Success||rows[i].Contains(" -> ")||rows[i].Contains(" link to "))throw new IOException("Archive contains links or unsupported entries");
            total=checked(total+long.Parse(m.Groups[2].Value));if(total>package.ExpandedBytes)throw new IOException("Archive expands beyond its verified size");
        }
        if(!paths.Any(p=>p.StartsWith(package.ArchiveRoot+"/",StringComparison.Ordinal)))throw new IOException("Expected content folder is missing");
        if(total!=package.ExpandedBytes)throw new IOException("Archive size differs from the reviewed package");
    }
    static string Quote(string value){if(value.Contains("\"")||value.Any(char.IsControl))throw new IOException("Invalid file path");return "\""+value+"\"";}
    static async Task<string> ReadBounded(StreamReader reader){var result=new StringBuilder();char[] buffer=new char[8192];int count;while((count=await reader.ReadAsync(buffer,0,buffer.Length))>0){if(result.Length+count>16*1024*1024)throw new IOException("Archive listing is too large");result.Append(buffer,0,count);}return result.ToString();}
    static string Tar(string arguments,CancellationToken cancel){
        string tool=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"tar.exe");
        if(!File.Exists(tool))throw new IOException("Windows archive support is missing. Update Windows before installing content.");
        using(var p=new Process{StartInfo=new ProcessStartInfo{FileName=tool,Arguments=arguments,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true}}){
            p.Start();Task<string> stdout=ReadBounded(p.StandardOutput),stderr=ReadBounded(p.StandardError);var timer=Stopwatch.StartNew();
            try{while(!p.WaitForExit(100)){cancel.ThrowIfCancellationRequested();if(stdout.IsFaulted||stderr.IsFaulted)throw new IOException("Archive output exceeded its limit");if(timer.Elapsed.TotalMinutes>15)throw new IOException("Archive operation timed out");}cancel.ThrowIfCancellationRequested();}
            catch{if(!p.HasExited){p.Kill();p.WaitForExit();}throw;}
            string output=stdout.GetAwaiter().GetResult(),error=stderr.GetAwaiter().GetResult();
            if(p.ExitCode!=0)throw new IOException("Windows could not read this archive: "+error.Substring(0,Math.Min(error.Length,400)));
            if(output.Length>16*1024*1024)throw new IOException("Archive listing is too large");return output;
        }
    }
    static void Report(IProgress<InstallProgress> progress,string message,long done=0,long total=0){if(progress!=null)progress.Report(new InstallProgress(message,done,total));}
    public static bool VerifyArchive(string archive,ContentPackage package){return File.Exists(archive)&&new FileInfo(archive).Length==package.Bytes&&HashFile(archive)==package.Sha256;}
    public static string Download(ContentPackage package,string cache,CancellationToken cancel,IProgress<InstallProgress> progress){
        NoLinks(cache);Directory.CreateDirectory(cache);string archive=Child(cache,package.Sha256+".7z"),partial=Child(cache,package.Sha256+".part");
        Report(progress,"Verifying cached download: "+package.Id);
        if(VerifyArchive(archive,package))return archive;
        if(File.Exists(archive))File.Delete(archive);
        long offset=File.Exists(partial)?new FileInfo(partial).Length:0;
        if(offset>package.Bytes){File.Delete(partial);offset=0;}
        if(offset!=package.Bytes){
            cancel.ThrowIfCancellationRequested();var request=(HttpWebRequest)WebRequest.Create(package.Url);
            request.AllowAutoRedirect=false;request.UserAgent="MatrixGamesLauncher/3.0";request.Timeout=30000;request.ReadWriteTimeout=30000;
            if(offset>0)request.AddRange(offset);
            using(cancel.Register(()=>request.Abort()))try{
                using(var response=(HttpWebResponse)request.GetResponse()){
                    if(response.StatusCode==HttpStatusCode.OK)offset=0;
                    else if(response.StatusCode==HttpStatusCode.PartialContent){
                        var range=Regex.Match(response.Headers[HttpResponseHeader.ContentRange]??"",@"^bytes (\d+)-(\d+)/(\d+)$");
                        if(!range.Success||long.Parse(range.Groups[1].Value)!=offset||long.Parse(range.Groups[2].Value)!=package.Bytes-1||long.Parse(range.Groups[3].Value)!=package.Bytes)throw new IOException("Invalid download resume response");
                    }else throw new IOException("Publisher download unavailable ("+(int)response.StatusCode+")");
                    if(response.ContentLength>=0&&response.ContentLength!=package.Bytes-offset)throw new IOException("Publisher package changed. Update the launcher before installing.");
                    using(var input=response.GetResponseStream())using(var file=new FileStream(partial,FileMode.OpenOrCreate,FileAccess.Write,FileShare.None)){
                        file.SetLength(offset);file.Position=offset;byte[] buffer=new byte[256*1024];int count;long last=0;
                        while((count=input.Read(buffer,0,buffer.Length))>0){cancel.ThrowIfCancellationRequested();offset+=count;if(offset>package.Bytes)throw new IOException("Download exceeds expected size");file.Write(buffer,0,count);
                            if(DateTime.UtcNow.Ticks-last>150*TimeSpan.TicksPerMillisecond||offset==package.Bytes){Report(progress,"Downloading "+package.Id,offset,package.Bytes);last=DateTime.UtcNow.Ticks;}
                        }
                        file.Flush(true);
                    }
                }
            }catch(WebException){cancel.ThrowIfCancellationRequested();throw;}
        }
        cancel.ThrowIfCancellationRequested();Report(progress,"Verifying download: "+package.Id);
        if(!VerifyArchive(partial,package)){if(File.Exists(partial))File.Delete(partial);throw new IOException("Download failed its SHA-256 check. Retry, or update the launcher if the publisher changed the package.");}
        File.Move(partial,archive);return archive;
    }
    public static string StageArchive(string archive,ContentPackage package,string staging,CancellationToken cancel,IProgress<InstallProgress> progress){
        NoLinks(archive);NoLinks(staging);if(Directory.Exists(staging))throw new IOException("Installation staging folder already exists");
        if(!VerifyArchive(archive,package))throw new IOException("Archive integrity check failed");
        Report(progress,"Checking archive paths: "+package.Id);
        ValidateListing(Tar("-tf "+Quote(archive),cancel),Tar("-tvf "+Quote(archive),cancel),package);
        cancel.ThrowIfCancellationRequested();Directory.CreateDirectory(staging);Report(progress,"Extracting "+package.Id);
        Tar("-xf "+Quote(archive)+" -C "+Quote(staging)+" -- "+Quote(package.ArchiveRoot),cancel);
        string candidate=Child(staging,package.ArchiveRoot);CheckTree(candidate,package.ExpandedBytes);return candidate;
    }
    public static void CheckTree(string folder,long maximum){
        NoLinks(folder);long bytes=0;var queue=new Queue<string>();queue.Enqueue(folder);
        while(queue.Count>0)foreach(string path in Directory.GetFileSystemEntries(queue.Dequeue())){
            NoLinks(path);var attrs=File.GetAttributes(path);
            if((attrs&FileAttributes.Directory)!=0)queue.Enqueue(path);else{bytes=checked(bytes+new FileInfo(path).Length);if(bytes>maximum)throw new IOException("Extracted content exceeds its verified size");}
        }
    }
    public static string Commit(string candidate,string destination,string backups,Func<string,bool> validate,CancellationToken cancel){
        NoLinks(candidate);NoLinks(destination);NoLinks(backups);if(!validate(candidate))throw new IOException("Downloaded content does not match the server version. Existing files were left unchanged.");
        cancel.ThrowIfCancellationRequested();Directory.CreateDirectory(Path.GetDirectoryName(destination));Directory.CreateDirectory(backups);
        string backup=null;if(Directory.Exists(destination)){backup=Child(backups,Path.GetFileName(destination)+"-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N"));Directory.Move(destination,backup);}
        try{Directory.Move(candidate,destination);}
        catch{if(backup!=null&&!Directory.Exists(destination))Directory.Move(backup,destination);throw;}
        return backup;
    }
}
