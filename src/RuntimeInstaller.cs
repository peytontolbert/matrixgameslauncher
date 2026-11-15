using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

// The framework is a reviewed first-party release, separate from server-owner mods.
// A server listing cannot change this URL, digest, executable or destination.
public static class RuntimeInstaller {
    public const string Sha256="81cc1419088fb7a1123a2dc83a3ecac2a99414dc26e33e7bcb6be5c3e3766da4";
    public const long DownloadBytes=334593112;
    public const long ExpandedLimit=1200L*1024*1024;
    public static async Task<MatrixNativeRuntime> Install(LauncherSettings settings,ApiClient api,IProgress<InstallProgress> progress,CancellationToken cancel){
        string target=MatrixNativeRuntime.Location(settings);ContentInstaller.NoLinks(target);
        if(Directory.Exists(target)||File.Exists(target))throw new IOException("The runtime folder already exists. Its files have been preserved; select another library or repair it before installing.");
        if(string.IsNullOrEmpty(api.Token))throw new IOException("Sign in before installing the Matrix runtime.");
        string staging=ContentInstaller.Child(settings.LibraryDirectory,"staging/runtime-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(staging);
        string archive=Path.Combine(staging,"runtime.zip"),payload=Path.Combine(staging,"payload");
        var request=(HttpWebRequest)WebRequest.Create(ApiClient.Origin+"/v1/gta/runtime/windows");
        request.AllowAutoRedirect=false;request.Timeout=15000;request.ReadWriteTimeout=30000;request.UserAgent="MatrixGamesLauncher/3.0";
        request.Headers[HttpRequestHeader.Authorization]="Bearer "+api.Token;
        using(cancel.Register(()=>request.Abort())){
            try{
                using(var response=(HttpWebResponse)await request.GetResponseAsync()){
                    if(response.StatusCode!=HttpStatusCode.OK||response.ContentLength!=DownloadBytes)throw new IOException("Unexpected runtime release. Update Matrix Games Launcher and try again.");
                    using(var input=response.GetResponseStream())using(var output=new FileStream(archive,FileMode.CreateNew,FileAccess.Write,FileShare.None)){
                        byte[] buffer=new byte[131072];int count;long done=0;
                        while((count=await input.ReadAsync(buffer,0,buffer.Length,cancel))>0){done+=count;if(done>DownloadBytes)throw new IOException("Runtime download exceeded its declared size");await output.WriteAsync(buffer,0,count,cancel);progress.Report(new InstallProgress("Downloading Matrix GTA runtime",done,DownloadBytes));}
                        if(done!=DownloadBytes)throw new IOException("Runtime download was interrupted; join again to retry.");
                    }
                }
            }catch(WebException e){cancel.ThrowIfCancellationRequested();if(e.Response!=null)e.Response.Dispose();throw new IOException("Could not download the Matrix runtime. Check your connection and sign in, then try again.",e);}
        }
        await Task.Run(()=>ExtractVerified(archive,payload,Sha256,DownloadBytes,ExpandedLimit,cancel),cancel);
        progress.Report(new InstallProgress("Verifying the installed Matrix runtime"));
        await Task.Run(()=>MatrixNativeRuntime.Inspect(payload),cancel);
        cancel.ThrowIfCancellationRequested();ContentInstaller.NoLinks(target);
        Directory.CreateDirectory(Path.GetDirectoryName(target));Directory.Move(payload,target);
        File.Delete(archive);Directory.Delete(staging);return MatrixNativeRuntime.Inspect(target);
    }
    public static void ExtractVerified(string archive,string destination,string digest,long expectedBytes,long expandedLimit,CancellationToken cancel){
        ContentInstaller.NoLinks(archive);ContentInstaller.NoLinks(destination);
        if(Directory.Exists(destination)||File.Exists(destination))throw new IOException("Runtime staging folder already exists");
        if(new FileInfo(archive).Length!=expectedBytes||ContentInstaller.HashFile(archive)!=digest)throw new IOException("The runtime download failed its integrity check. Nothing was installed.");
        using(var zip=ZipFile.OpenRead(archive)){
            if(zip.Entries.Count<1||zip.Entries.Count>10000)throw new IOException("Invalid runtime archive");
            var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long total=0;
            foreach(var entry in zip.Entries){
                ContentInstaller.ArchiveName("runtime/"+entry.FullName,"runtime");
                int mode=(entry.ExternalAttributes>>16)&0xf000;
                if(entry.FullName.EndsWith("/")||!seen.Add(entry.FullName)||(mode!=0&&mode!=0x8000)||(entry.ExternalAttributes&0x400)!=0)throw new IOException("Unsupported runtime archive entry");
                total=checked(total+entry.Length);if(total>expandedLimit)throw new IOException("Runtime archive exceeds its expanded size limit");
            }
            Directory.CreateDirectory(destination);
            foreach(var entry in zip.Entries){
                cancel.ThrowIfCancellationRequested();string file=ContentInstaller.Child(destination,entry.FullName);Directory.CreateDirectory(Path.GetDirectoryName(file));
                using(var input=entry.Open())using(var output=new FileStream(file,FileMode.CreateNew,FileAccess.Write,FileShare.None)){
                    byte[] buffer=new byte[131072];int count;long written=0;
                    while((count=input.Read(buffer,0,buffer.Length))>0){cancel.ThrowIfCancellationRequested();written+=count;if(written>entry.Length)throw new IOException("Runtime entry exceeds its declared size");output.Write(buffer,0,count);}
                    if(written!=entry.Length)throw new IOException("Incomplete runtime archive entry");
                }
            }
        }
    }
}
