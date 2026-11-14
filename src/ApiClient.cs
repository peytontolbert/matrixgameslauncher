using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public sealed class ApiClient {
    public const string Origin="https://api.matrixgames.app";
    public string Token;
    public async Task<Dictionary<string,object>> Call(string path,object body,CancellationToken cancel){
        if(!path.StartsWith("/v1/"))throw new IOException("Invalid API path");
        return await Request(new Uri(Origin+path),body,Token,cancel);
    }
    public static async Task<Dictionary<string,object>> Request(Uri url,object body,string token,CancellationToken cancel){
        var r=(HttpWebRequest)WebRequest.Create(url);r.AllowAutoRedirect=false;r.Timeout=15000;r.ReadWriteTimeout=15000;r.UserAgent="MatrixGamesLauncher/3.0";
        if(!string.IsNullOrEmpty(token))r.Headers[HttpRequestHeader.Authorization]="Bearer "+token;
        using(cancel.Register(()=>r.Abort()))try{
            if(body!=null){r.Method="POST";r.ContentType="application/json";byte[] data=Encoding.UTF8.GetBytes(Data.Json.Serialize(body));r.ContentLength=data.Length;using(var output=await r.GetRequestStreamAsync())await output.WriteAsync(data,0,data.Length,cancel);}
            using(var response=(HttpWebResponse)await r.GetResponseAsync()){
                if(response.StatusCode!=HttpStatusCode.OK&&response.StatusCode!=HttpStatusCode.Created)throw new IOException("Unexpected service response");
                using(var stream=response.GetResponseStream())using(var bytes=new MemoryStream()){
                    byte[] buffer=new byte[8192];int count;while((count=await stream.ReadAsync(buffer,0,buffer.Length,cancel))>0){if(bytes.Length+count>1024*1024)throw new IOException("Service response is too large");bytes.Write(buffer,0,count);}
                    return Data.Object(Data.Json.DeserializeObject(Encoding.UTF8.GetString(bytes.ToArray())));
                }
            }
        }catch(WebException e){cancel.ThrowIfCancellationRequested();var response=e.Response as HttpWebResponse;var status=response==null?0:(int)response.StatusCode;if(response!=null)response.Dispose();throw new IOException(status==401?"Sign in to Matrix Games again.":status==409?"Server status or content changed. Refresh and join again.":"Could not contact the service"+(status>0?" (HTTP "+status+")":"")+". Try again.",e);}
    }
}
public static class TokenStore {
    static string PathName {get{return Path.Combine(Path.GetDirectoryName(LauncherSettings.SettingsPath),"session.bin");}}
    public static string Load(){if(!File.Exists(PathName))return null;try{return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(PathName),null,DataProtectionScope.CurrentUser));}catch(CryptographicException){return null;}}
    public static void Save(string token){ContentInstaller.NoLinks(PathName);Directory.CreateDirectory(Path.GetDirectoryName(PathName));File.WriteAllBytes(PathName,ProtectedData.Protect(Encoding.UTF8.GetBytes(token),null,DataProtectionScope.CurrentUser));}
    public static void Clear(){ContentInstaller.NoLinks(PathName);if(File.Exists(PathName))File.Delete(PathName);}
}
