using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
public static class ArchiveCodecTests {
    static int count;
    static void Assert(bool condition,string why){if(!condition)throw new Exception(why);count++;}
    static void Reject(Action action,string why){try{action();}catch(IOException){count++;return;}throw new Exception(why);}
    public static void Run(string root){
        string folder=Path.Combine(root,"codecs"),source=Path.Combine(folder,"source","content","tracks","fixture");Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source,"data.acd"),"abc");Directory.CreateDirectory(Path.Combine(folder,"source","apps"));File.WriteAllText(Path.Combine(folder,"source","apps","unrelated.txt"),"outside");
        foreach(string codec in new[]{"LZMA","LZMA2"}){
            string archive=Path.Combine(folder,codec+".7z"),input=Path.Combine(folder,"source");
            ArchiveTool.Run(folder,"a -t7z -m0="+codec+" -bd -bb0 -- "+ArchiveTool.Quote(archive)+" "+ArchiveTool.Quote(input+"/*"),CancellationToken.None);
            var p=new ContentPackage{Id="fixture",ArchiveRoot="content/tracks/fixture",Bytes=new FileInfo(archive).Length,ExpandedBytes=10,Sha256=ContentInstaller.HashFile(archive)};
            string staging=Path.Combine(folder,"stage-"+codec),candidate=ContentInstaller.StageArchive(archive,p,staging,CancellationToken.None,null);
            Assert(File.ReadAllText(Path.Combine(candidate,"data.acd"))=="abc",codec+" extraction failed");Assert(!Directory.Exists(Path.Combine(staging,"apps")),"Archive extracted unrelated files");
        }
        var package=new ContentPackage{ArchiveRoot="fixture",ExpandedBytes=3};
        string valid="Path = fixture/data\nSize = 3\nFolder = -\nEncrypted = -\n";
        ArchiveTool.ValidateListing(valid,package);count++;
        foreach(string path in new[]{"../escape","fixture/../escape","C:/escape","fixture/CON","fixture/a:stream","fixture/a.","fixture/"})Reject(()=>ArchiveTool.ValidateListing(valid.Replace("fixture/data",path),package),"Unsafe path accepted");
        foreach(string extra in new[]{"Symbolic Link = outside\n","Hard Link = outside\n","Mode = lrwxrwxrwx\n","Attributes = A lrwxrwxrwx\n","Alternate Stream = +\n","Size = 3\n"})Reject(()=>ArchiveTool.ValidateListing(valid+extra,package),"Unsafe metadata accepted");
        Reject(()=>ArchiveTool.ValidateListing(valid.Replace("Encrypted = -","Encrypted = +"),package),"Encrypted archive accepted");
        Reject(()=>ArchiveTool.ValidateListing(valid+"\n"+valid.Replace("fixture/data","fixture/DATA"),new ContentPackage{ArchiveRoot="fixture",ExpandedBytes=6}),"Windows path collision accepted");
        Reject(()=>ArchiveTool.ValidateListing(valid+"\n"+valid.Replace("fixture/data","fixture/data/child"),new ContentPackage{ArchiveRoot="fixture",ExpandedBytes=6}),"File-directory collision accepted");
        Reject(()=>ArchiveTool.ValidateListing(valid.Replace("Size = 3","Size = 4"),package),"Expanded size mismatch accepted");
        var canceled=new CancellationTokenSource();canceled.Cancel();bool stopped=false;try{ArchiveTool.Run(folder,"l",canceled.Token);}catch(OperationCanceledException){stopped=true;}Assert(stopped,"Cancellation ignored");
        string tool=ArchiveTool.Materialize(folder);File.WriteAllText(tool,"modified");Reject(()=>ArchiveTool.Materialize(folder),"Modified executable accepted");
        Console.WriteLine(count+" archive codec and safety checks passed.");
    }
}
