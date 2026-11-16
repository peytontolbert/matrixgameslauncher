using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Win32;

public sealed class GameLocations {
    public string InstallDirectory="",DocumentsDirectory="",ContentDirectory="";
}
public sealed class LauncherSettings {
    public string AssettoClutchAssist="automatic";
    public string SteamDirectory="",DriverName="Driver",LibraryDirectory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"MatrixGames");
    public Dictionary<string,GameLocations> Games=new Dictionary<string,GameLocations>();
    public string[] PreviousLibraries=new string[0];
    public void SelectLibrary(string path){
        string next=Path.GetFullPath(path);new AssetVault(next);
        var old=PreviousLibraries??new string[0];
        if(!string.IsNullOrWhiteSpace(LibraryDirectory)&&!string.Equals(next,Path.GetFullPath(LibraryDirectory),StringComparison.OrdinalIgnoreCase))old=old.Concat(new[]{Path.GetFullPath(LibraryDirectory)}).ToArray();
        PreviousLibraries=old.Where(p=>!string.Equals(p,next,StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();LibraryDirectory=next;
    }
    static readonly JavaScriptSerializer Json=new JavaScriptSerializer{MaxJsonLength=131072};
    public static string SettingsPath { get {return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MatrixGames","Launcher","settings.json");} }
    public static LauncherSettings Load(string path){
        if(File.Exists(path)){
            if(new FileInfo(path).Length>131072)throw new IOException("Launcher settings file is too large");
            var result=Json.Deserialize<LauncherSettings>(File.ReadAllText(path));if(result==null||result.Games==null)throw new IOException("Invalid launcher settings");return result;
        }
        var settings=new LauncherSettings{SteamDirectory=Convert.ToString(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam","SteamPath","")),DriverName=Convert.ToString(Registry.GetValue(@"HKEY_CURRENT_USER\Software\MatrixGames\Launcher","DriverName","Driver"))};
        settings.Games["assetto-corsa"]=new GameLocations{InstallDirectory=Convert.ToString(Registry.GetValue(@"HKEY_CURRENT_USER\Software\MatrixGames\Launcher","GameDirectory","")),DocumentsDirectory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"Assetto Corsa")};return settings;
    }
    public void Save(string path){
        ContentInstaller.NoLinks(path);Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));string pending=path+"."+Guid.NewGuid().ToString("N")+".new";
        try{File.WriteAllText(pending,Json.Serialize(this),new UTF8Encoding(false));if(File.Exists(path))File.Replace(pending,path,path+".previous");else File.Move(pending,path);}finally{if(File.Exists(pending))File.Delete(pending);}
    }
}
