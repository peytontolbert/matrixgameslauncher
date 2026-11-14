using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

// Directory junctions let games use their normal content paths without a second
// copy of the user's approved library assets. No shell or administrator is needed.
public static class Junction {
    const uint MountPoint=0xA0000003;
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool DeviceIoControl(SafeFileHandle handle,uint code,byte[] input,int inputSize,byte[] output,int outputSize,out int returned,IntPtr overlapped);
    static SafeFileHandle Open(string path,uint access){var h=CreateFile(path,access,3,IntPtr.Zero,3,0x00200000|0x02000000,IntPtr.Zero);if(h.IsInvalid)throw new Win32Exception(Marshal.GetLastWin32Error());return h;}
    public static string Target(string path){
        if(!Directory.Exists(path)||(File.GetAttributes(path)&FileAttributes.ReparsePoint)==0)return null;
        byte[] data=new byte[16384];int count;using(var h=Open(path,0))if(!DeviceIoControl(h,0x900A8,null,0,data,data.Length,out count,IntPtr.Zero))throw new Win32Exception(Marshal.GetLastWin32Error());
        if(BitConverter.ToUInt32(data,0)!=MountPoint)throw new IOException("Only Matrix Games directory junctions can be replaced");
        string target=Encoding.Unicode.GetString(data,16+BitConverter.ToUInt16(data,8),BitConverter.ToUInt16(data,10));if(!target.StartsWith(@"\??\"))throw new IOException("Invalid junction target");return Path.GetFullPath(target.Substring(4));
    }
    public static void Create(string path,string target){
        ContentInstaller.NoLinks(Path.GetDirectoryName(path));ContentInstaller.NoLinks(target);if(Directory.Exists(path)||File.Exists(path))throw new IOException("Link destination is occupied");
        target=Path.GetFullPath(target);if(target.StartsWith(@"\\")||!Directory.Exists(target))throw new IOException("Choose an existing local content directory");
        byte[] substitute=Encoding.Unicode.GetBytes(@"\??\"+target),print=Encoding.Unicode.GetBytes(target);int length=8+substitute.Length+2+print.Length+2;byte[] buffer=new byte[length+8];
        Array.Copy(BitConverter.GetBytes(MountPoint),0,buffer,0,4);Array.Copy(BitConverter.GetBytes((ushort)length),0,buffer,4,2);
        Array.Copy(BitConverter.GetBytes((ushort)substitute.Length),0,buffer,10,2);Array.Copy(BitConverter.GetBytes((ushort)(substitute.Length+2)),0,buffer,12,2);Array.Copy(BitConverter.GetBytes((ushort)print.Length),0,buffer,14,2);
        Array.Copy(substitute,0,buffer,16,substitute.Length);Array.Copy(print,0,buffer,18+substitute.Length,print.Length);
        Directory.CreateDirectory(path);try{int returned;using(var h=Open(path,0x40000000))if(!DeviceIoControl(h,0x900A4,buffer,buffer.Length,null,0,out returned,IntPtr.Zero))throw new Win32Exception(Marshal.GetLastWin32Error());}catch{Directory.Delete(path,false);throw;}
    }
}
