using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

// A join-scoped override: keep the driver's normal assists and preserve edits
// made while the game is running. Car physics files are never modified.
public sealed class AssettoAssistLease : IDisposable {
    readonly string path,backup,written;
    readonly bool existed;
    public static bool? AutomaticClutch(string mode,string controls){
        if(mode=="game")return null;
        if(mode=="on")return true;
        if(mode=="off")return false;
        if(mode!="automatic")throw new IOException("Invalid Assetto clutch setting");
        var header=Regex.Match(controls??"",@"(?im)^\[HEADER\][^\r\n]*\r?\n(?<body>(?:(?!^\[)[\s\S])*)");
        var method=Regex.Match(header.Groups["body"].Value,@"(?im)^INPUT_METHOD\s*=\s*(KEYBOARD|X360)\s*$");
        return method.Success?(bool?)true:null;
    }
    public static string Override(string text,bool enabled){
        var section=Regex.Match(text,@"(?im)^\[ASSISTS\][^\r\n]*(?:\r?\n|$)(?<body>(?:(?!^\[)[\s\S])*)");
        string line="AUTO_CLUTCH="+(enabled?"1":"0");
        if(!section.Success)return text.TrimEnd()+"\r\n[ASSISTS]\r\n"+line+"\r\n";
        string body=section.Groups["body"].Value;
        if(Regex.IsMatch(body,@"(?im)^AUTO_CLUTCH\s*="))body=Regex.Replace(body,@"(?im)^AUTO_CLUTCH\s*=[^\r\n]*",line);
        else body=line+"\r\n"+body;
        return text.Substring(0,section.Groups["body"].Index)+body+text.Substring(section.Index+section.Length);
    }
    public AssettoAssistLease(string cfg,string mode){
        string controls=Path.Combine(cfg,"controls.ini");ContentInstaller.NoLinks(controls);
        bool? requested=AutomaticClutch(mode,File.Exists(controls)?File.ReadAllText(controls):"");
        if(!requested.HasValue)return;
        path=Path.Combine(cfg,"assists.ini");ContentInstaller.NoLinks(path);existed=File.Exists(path);
        string before=existed?File.ReadAllText(path):"";written=Override(before,requested.Value);
        backup=path+".matrixgames-"+Guid.NewGuid().ToString("N")+".bak";
        if(existed)File.Copy(path,backup,false);
        try{File.WriteAllText(path,written,new UTF8Encoding(false));}
        catch{if(existed)File.Copy(backup,path,true);else if(File.Exists(path))File.Delete(path);throw;}
    }
    public void Dispose(){
        if(path!=null&&File.Exists(path)&&File.ReadAllText(path)==written){if(existed)File.Copy(backup,path,true);else File.Delete(path);}
    }
}
