using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

public sealed class AssetVault {
    public readonly string Root;
    public AssetVault(string root){Root=Path.GetFullPath(root);ContentInstaller.NoLinks(Root);if(Root.StartsWith(@"\\"))throw new IOException("Choose a local disk for your Matrix Games library");}
    static string Hash(string value){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-","").ToLowerInvariant();}
    public string Payload(ContentPackage p){return ContentInstaller.Child(Root,"objects/"+p.Sha256+"/"+Hash(p.ArchiveRoot).Substring(0,12)+"/content");}
    public static string Destination(string game,ContentPackage p){return Path.GetFullPath(Path.Combine(game,p.Destination));}
    public ContentPackage[] Missing(string game,AssetManifest manifest){return manifest.Assets.Where(p=>!p.Matches(Destination(game,p))).ToArray();}
    public bool Cached(ContentPackage p){return p.Matches(Payload(p));}
    public string Review(string game,AssetManifest manifest){
        var missing=Missing(game,manifest);long bytes=missing.Where(p=>!Cached(p)).Sum(p=>p.Bytes);var text=new StringBuilder();
        text.AppendLine(missing.Length+" assets need installation or linking. Download: "+(bytes/1024.0/1024/1024).ToString("0.00")+" GB.");text.AppendLine("Library: "+Root);text.AppendLine("Matching game files stay in place. Existing library assets are reused. Replaced game folders are backed up.");text.AppendLine();
        foreach(var p in missing)text.AppendLine(p.Name+" — "+(Cached(p)?"reuse library":p.Url.Host+" · "+(p.Bytes/1024/1024)+" MB"));
        text.AppendLine();text.AppendLine("These assets are supplied by the server owner. Install mods only from sources you trust.");return text.ToString();
    }
    public void Apply(string server,string game,AssetManifest manifest,CancellationToken cancel,IProgress<InstallProgress> progress){
        if(!Data.Id(server))throw new IOException("Invalid server identity");ContentInstaller.NoLinks(game);Directory.CreateDirectory(Root);
        // The caller displays Review and obtains consent for this exact immutable manifest.
        string serverRoot=ContentInstaller.Child(Root,"servers/"+server);Directory.CreateDirectory(serverRoot);
        string approval=ContentInstaller.Child(serverRoot,"approval-"+manifest.Revision+".json");File.WriteAllText(approval,Data.Json.Serialize(new{server=server,revision=manifest.Revision,approvedAt=DateTime.UtcNow.ToString("o"),sources=manifest.Assets.Select(p=>new{id=p.Id,url=p.Url.AbsoluteUri,sha256=p.Sha256}).ToArray()}));
        var references=new List<object>();
        foreach(var p in manifest.Assets){
            cancel.ThrowIfCancellationRequested();string destination=Destination(game,p);
            if(p.Matches(destination)){references.Add(new{id=p.Id,path=destination,reusedExisting=true});continue;}
            string payload=Payload(p);
            if(!p.Matches(payload)){
                // A changed cached object is preserved for inspection; never repair through its game links.
                if(Directory.Exists(payload))throw new IOException("A cached asset changed. Remove its game links and repair it before joining: "+payload);
                long needed=checked(p.Bytes+p.ExpandedBytes+256L*1024*1024);if(new DriveInfo(Path.GetPathRoot(Root)).AvailableFreeSpace<needed)throw new IOException("Not enough library space for "+p.Name);
                string cache=ContentInstaller.Child(Root,"downloads"),stage=ContentInstaller.Child(Root,"staging/"+Guid.NewGuid().ToString("N"));
                string archive=ContentInstaller.Download(p,cache,cancel,progress);
                try{
                    string candidate=ContentInstaller.StageArchive(archive,p,stage,cancel,progress);if(!p.Matches(candidate))throw new IOException("Downloaded files do not match the server's installed-file checks");cancel.ThrowIfCancellationRequested();
                    Directory.CreateDirectory(Path.GetDirectoryName(payload));Directory.Move(candidate,payload);
                }finally{if(Directory.Exists(stage)){ContentInstaller.CheckTree(stage,p.ExpandedBytes);Directory.Delete(stage,true);}}
            }
            Link(game,destination,payload,cancel);references.Add(new{id=p.Id,path=destination,library=payload,reusedExisting=false});
        }
        File.WriteAllText(ContentInstaller.Child(serverRoot,"assets.json"),Data.Json.Serialize(new{server=server,revision=manifest.Revision,assets=references}));
        // Completed plans retain extracted assets once; compressed partials survive cancellation.
        foreach(var p in manifest.Assets){string archive=ContentInstaller.Child(Root,"downloads/"+p.Sha256+".7z");if(File.Exists(archive))File.Delete(archive);}
    }
    public void Link(string game,string destination,string payload,CancellationToken cancel){
        ContentInstaller.NoLinks(game);ContentInstaller.NoLinks(Path.GetDirectoryName(destination));ContentInstaller.NoLinks(payload);cancel.ThrowIfCancellationRequested();
        if(payload.StartsWith(Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Choose a library outside the content folder being installed");
        string current=Junction.Target(destination);
        if(current!=null&&!current.StartsWith(ContentInstaller.Child(Root,"objects")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Existing external content link was left unchanged: "+destination);
        Directory.CreateDirectory(Path.GetDirectoryName(destination));string backup=null;
        if(Directory.Exists(destination)){
            string backups=ContentInstaller.Child(game,".matrixgames-backups");Directory.CreateDirectory(backups);backup=ContentInstaller.Child(backups,Path.GetFileName(destination)+"-"+Guid.NewGuid().ToString("N"));Directory.Move(destination,backup);
        }
        try{Junction.Create(destination,payload);}catch{if(backup!=null&&!Directory.Exists(destination))Directory.Move(backup,destination);throw;}
    }
}
