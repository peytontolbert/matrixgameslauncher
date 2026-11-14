using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

public static class Data {
    public static readonly JavaScriptSerializer Json=new JavaScriptSerializer{MaxJsonLength=1024*1024};
    public static string Text(Dictionary<string,object> row,string key){object value;return row.TryGetValue(key,out value)?Convert.ToString(value):"";}
    public static bool Id(string value){return Regex.IsMatch(value??"",@"\A[a-zA-Z0-9_-]{1,80}\z");}
    public static Dictionary<string,object> Object(object value){var result=value as Dictionary<string,object>;if(result==null)throw new IOException("Invalid server response");return result;}
    public static object[] Array(object value){var result=value as IEnumerable;if(result==null||value is string)throw new IOException("Invalid server list");return result.Cast<object>().ToArray();}
    public static string SafePath(string path){ContentInstaller.ArchiveName("root/"+path,"root");return path;}
}
public sealed class AssetCheck {
    public string Path,Sha256;public long Bytes;
    public string Problem(string folder){
        string file=System.IO.Path.Combine(folder,Path);
        try{if(!File.Exists(file))return Path+": missing";long size=new FileInfo(file).Length;if(size==0)return Path+": empty";if(Bytes>0&&size!=Bytes)return Path+": size differs (installed "+size+", required "+Bytes+" bytes)";if(!string.IsNullOrEmpty(Sha256)&&ContentInstaller.HashFile(file)!=Sha256)return Path+": checksum differs from server requirement";return null;}
        catch(IOException){return Path+": could not read file";}catch(UnauthorizedAccessException){return Path+": access denied";}
    }
    public bool Matches(string folder){return Problem(folder)==null;}
}
public sealed class ContentPackage {
    public string Id,Kind,Sha256,Name,ArchiveRoot;public long Bytes,ExpandedBytes;public Uri Url;public AssetCheck[] Checks;
    public string Destination { get {return "content/"+(Kind=="car"?"cars/":"tracks/")+Id;} }
    public bool Matches(string folder){return Directory.Exists(folder)&&!(Kind=="car"&&Directory.Exists(System.IO.Path.Combine(folder,"data")))&&Checks.All(c=>c.Matches(folder));}
    public string[] Problems(string folder){if(!Directory.Exists(folder))return new[]{"Content folder not found: "+folder};if(Kind=="car"&&Directory.Exists(System.IO.Path.Combine(folder,"data")))return new[]{"Unpacked data folder overrides the required car physics"};return Checks.Select(c=>c.Problem(folder)).Where(s=>s!=null).ToArray();}
    public static ContentPackage Parse(object value){
        var d=Data.Object(value);var p=new ContentPackage{Id=Data.Text(d,"id"),Kind=Data.Text(d,"kind"),Name=Data.Text(d,"name"),Sha256=Data.Text(d,"sha256"),ArchiveRoot=Data.Text(d,"archiveRoot"),Bytes=Convert.ToInt64(d["bytes"]),ExpandedBytes=Convert.ToInt64(d["expandedBytes"])};
        if(!Data.Id(p.Id)||!(p.Kind=="car"||p.Kind=="track")||!Regex.IsMatch(p.Sha256,"^[a-f0-9]{64}$")||p.Bytes<1||p.Bytes>20L*1024*1024*1024||p.ExpandedBytes<1||p.ExpandedBytes>50L*1024*1024*1024)throw new IOException("Invalid server asset proof");Data.SafePath(p.ArchiveRoot);
        p.Url=new Uri(Data.Text(d,"url"));if(p.Url.Scheme!="https"||p.Url.Port!=443||p.Url.UserInfo.Length>0||p.Url.Fragment.Length>0||!p.Url.Host.Contains(".")||p.Url.IsLoopback||p.Url.AbsoluteUri.Length>4096)throw new IOException("Server assets require an HTTPS CDN address");
        p.Checks=Data.Array(d["checks"]).Select(v=>{var c=Data.Object(v);string sha=Data.Text(c,"sha256");long bytes=c.ContainsKey("bytes")?Convert.ToInt64(c["bytes"]):0;if(sha.Length>0&&!Regex.IsMatch(sha,"^[a-f0-9]{64}$")||bytes<0||bytes==0&&sha.Length==0)throw new IOException("Invalid installed-file proof");return new AssetCheck{Path=Data.SafePath(Data.Text(c,"path")),Sha256=sha,Bytes=bytes};}).ToArray();
        if(p.Checks.Length<1||p.Checks.Length>2048||p.Checks.Select(c=>c.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=p.Checks.Length||!p.Checks.Any(c=>c.Path==(p.Kind=="car"?"data.acd":"data/surfaces.ini")&&c.Sha256.Length==64))throw new IOException("Server asset checks are incomplete");return p;
    }
}
public sealed class AssetManifest {
    public string Game,Revision;public ContentPackage[] Assets;
    public static AssetManifest Parse(object value){var d=Data.Object(value);var m=new AssetManifest{Game=Data.Text(d,"game"),Revision=Data.Text(d,"revision")};if(Convert.ToInt32(d["schema"])!=1||m.Game!="assetto-corsa"||!Regex.IsMatch(m.Revision,"^[a-f0-9]{64}$"))throw new IOException("Unsupported asset manifest");m.Assets=Data.Array(d["assets"]).Select(ContentPackage.Parse).ToArray();if(m.Assets.Length<1||m.Assets.Length>64||m.Assets.Select(a=>a.Kind+":"+a.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=m.Assets.Length)throw new IOException("Invalid asset list");return m;}
}
public sealed class ServerEntry {
    public Dictionary<string,string> ModelNames=new Dictionary<string,string>();
    public string GameName {get{var adapter=Games.All.FirstOrDefault(g=>g.Id==Game);return adapter==null?Game:adapter.Name;}}
    public string ModelName(string id){string name;return ModelNames.TryGetValue(id,out name)?name:id.Replace('_',' ');}
    public string Summary {get{return GameName+" | "+(Online?"Online":"Offline")+" | "+Players+"/"+Capacity+" players\r\nRequired track: "+Track+"\r\nServer cars ("+Models.Length+"): "+string.Join(", ",Models.Select(ModelName))+"\r\nJoin to choose a car and check every required asset against your saved folders.";}}
    public string Id,Game,Name,Track;public bool Online;public string[] Models;public int Players,Capacity;
    public static ServerEntry Parse(object value){var d=Data.Object(value);var s=new ServerEntry{Id=Data.Text(d,"id"),Game=Data.Text(d,"game"),Name=Data.Text(d,"name"),Track=Data.Text(d,"track"),Online=Convert.ToBoolean(d["online"]),Players=Convert.ToInt32(d["players"]),Capacity=Convert.ToInt32(d["capacity"]),Models=Data.Array(d["models"]).Select(m=>Data.Text(Data.Object(m),"id")).ToArray()};if(!Data.Id(s.Id)||!Data.Id(s.Track)||s.Models.Length==0||!s.Models.All(Data.Id))throw new IOException("Invalid server entry");foreach(var raw in Data.Array(d["models"])){var row=Data.Object(raw);s.ModelNames[Data.Text(row,"id")]=Data.Text(row,"name");}return s;}
    public override string ToString(){return Name+"  |  "+GameName+"  |  "+Players+"/"+Capacity+"  |  "+(Online?"Online":"Offline");}
}
