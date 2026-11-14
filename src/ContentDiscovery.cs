using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

// Probe known game/content layouts inside user-selected roots. Never crawl disks
// or infer compatibility from a folder name alone.
public static class ContentDiscovery {
    public static string[] SteamInstallations(string selected){
        var candidates=new List<string>();if(string.IsNullOrWhiteSpace(selected))return candidates.ToArray();
        string root=Path.GetFullPath(selected);if(File.Exists(root))root=Path.GetDirectoryName(root);
        candidates.Add(root);
        if(string.Equals(Path.GetFileName(root),"steamapps",StringComparison.OrdinalIgnoreCase))root=Path.GetDirectoryName(root);
        var libraries=new List<string>{root};string index=Path.Combine(root,"steamapps","libraryfolders.vdf");
        try{if(File.Exists(index)&&new FileInfo(index).Length<1024*1024)foreach(Match m in Regex.Matches(File.ReadAllText(index),"\"path\"\\s*\"([^\"]+)\""))libraries.Add(m.Groups[1].Value.Replace("\\\\","\\"));}catch(IOException){}catch(UnauthorizedAccessException){}
        foreach(string library in libraries)if(Path.IsPathRooted(library))candidates.Add(Path.Combine(library,"steamapps","common","assettocorsa"));
        return candidates.Distinct(StringComparer.OrdinalIgnoreCase).Where(p=>File.Exists(Path.Combine(p,"acs.exe"))).ToArray();
    }
    public static string[] SearchRoots(LauncherSettings settings,GameLocations locations){
        return new[]{locations.InstallDirectory,locations.DocumentsDirectory,locations.ContentDirectory,settings.LibraryDirectory}.Concat(settings.PreviousLibraries??new string[0]).Concat(SteamInstallations(settings.SteamDirectory)).Where(p=>!string.IsNullOrWhiteSpace(p)&&Path.IsPathRooted(p)).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    static IEnumerable<string> Candidates(ContentPackage p,IEnumerable<string> roots){
        string type=p.Kind=="car"?"cars":"tracks";
        foreach(string root in roots){if(string.IsNullOrWhiteSpace(root)||!Path.IsPathRooted(root))continue;
            // The content-addressed object path lets an old library remain reusable.
            string cached=null;try{cached=new AssetVault(root).Payload(p);}catch(IOException){}if(cached!=null)yield return cached;
            yield return Path.Combine(root,"content",type,p.Id);yield return Path.Combine(root,type,p.Id);yield return Path.Combine(root,p.Id);
            if(string.Equals(Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)),p.Id,StringComparison.OrdinalIgnoreCase))yield return root;
        }
    }
    public static string FindFolder(ContentPackage p,IEnumerable<string> roots){
        foreach(string folder in Candidates(p,roots).Distinct(StringComparer.OrdinalIgnoreCase)){
            if(!p.Matches(folder))continue;
            try{ContentInstaller.NoLinks(folder);return Path.GetFullPath(folder);}catch(IOException){}
        }return null;
    }
    public static string FindArchive(ContentPackage p,IEnumerable<string> roots){
        // Only archives named for this asset, directly in selected content roots.
        // Integrity is checked before displaying them as an alternative to a download.
        foreach(string root in roots.Where(pth=>!string.IsNullOrWhiteSpace(pth)&&Path.IsPathRooted(pth))){
            foreach(string suffix in new[]{".7z",".zip",".tar"})foreach(string folder in new[]{root,Path.Combine(root,p.Kind=="car"?"cars":"tracks"),Path.Combine(root,"downloads")}){
                string path=Path.Combine(folder,p.Id+suffix);try{ContentInstaller.NoLinks(path);if(ContentInstaller.VerifyArchive(path,p))return path;}catch(IOException){}catch(UnauthorizedAccessException){}
            }
        }return null;
    }
}
