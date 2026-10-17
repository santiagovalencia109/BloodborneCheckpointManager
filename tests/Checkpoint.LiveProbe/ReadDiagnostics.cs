using System.Diagnostics;
using System.Runtime.InteropServices;
using Checkpoint.Core;
using Microsoft.Win32.SafeHandles;

internal static class ReadDiagnostics
{
    [DllImport("kernel32.dll",SetLastError=true)] private static extern SafeProcessHandle OpenProcess(uint rights,bool inherit,int pid);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool ReadProcessMemory(SafeProcessHandle handle,nint at,byte[] bytes,nuint size,out nuint read);
    public static void Inspect(string profileFile)
    {
        var p=JsonDisk.Read<Profile>(profileFile); var games=ExternalBridge.Discover(port:p.Port);
        if(games.Length!=1) { Console.WriteLine($"Read-only diagnosis needs one running {p.Port} game; found {games.Length}."); return; }
        var game=games[0];
        var r=ExternalBridge.ResolveRuntime(game.Executable,p.Port); using var process=Process.GetProcessById(game.Pid);
        using var handle=OpenProcess(0x10|0x1000,false,game.Pid); if(handle.IsInvalid) throw new IOException("Read-only process open failed");
        var host=process.MainModule!.BaseAddress;
        byte[] Read(nint at,int size) { var bytes=new byte[size]; if(!ReadProcessMemory(handle,at,bytes,(nuint)size,out var count)||count!=(nuint)size) throw new IOException("Process read failed"); return bytes; }
        var image=(nint)BitConverter.ToInt64(Read(host+(int)r.Layout[0],8));
        Console.WriteLine("MainAttach "+Convert.ToHexString(Read(host+(int)r.Layout[17],16)));
        Console.WriteLine("Entered "+Convert.ToHexString(Read(host+(int)r.Layout[2],4)));
        foreach(var offset in new[]{0x1336da0,0x2034770,0x14d9e80,0x17c13ce,0x17c13fe}) Console.WriteLine(offset.ToString("X")+" "+Convert.ToHexString(Read(image+offset,20)));
    }
}
