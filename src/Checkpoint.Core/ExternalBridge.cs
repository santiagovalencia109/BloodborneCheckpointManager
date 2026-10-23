using System.ComponentModel;
using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;
using System.Collections.Concurrent;

namespace Checkpoint.Core;

public sealed record ExternalGame(int Pid, string Executable, string HostSha256);
public sealed class ExternalAttachException(string message, int code) : IOException(message)
{
    public int Code { get; } = code;
}

/// <summary>Opt-in attachment to validated runtime contracts. Never identifies a game by name alone.</summary>
public static class ExternalBridge
{
    public const string SupportedHostSha256 = "4AE2D15EAFCE351C61DC10B8498B2BAD61CDC3A5C15C92E274FD63A7A4FC312A";
    private const uint Rights = 0x0002 | 0x0008 | 0x0010 | 0x0020 | 0x0400 | 0x100000;
    private static readonly ConcurrentDictionary<(int Pid, long Started), byte> uncertainCalls = new();
    private static readonly ConcurrentDictionary<(string Port,string Path,long Length,long Modified),RuntimeResolution> resolvedFiles=new();
    private static readonly ConcurrentDictionary<string,byte> unreviewedHosts=new(StringComparer.OrdinalIgnoreCase);
    /// <summary>True when this runtime build was accepted by the structural fallback rather than a reviewed contract.</summary>
    public static bool IsUnreviewed(string hostSha256)=>unreviewedHosts.ContainsKey(hostSha256);
    [DllImport("kernel32.dll", SetLastError=true)] private static extern bool ReadProcessMemory(SafeProcessHandle process, IntPtr address, byte[] bytes, nuint count, out nuint read);
    [DllImport("kernel32.dll", SetLastError=true)] private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError=true)] private static extern bool IsWow64Process2(SafeProcessHandle process, out ushort machine, out ushort native);
    [DllImport("kernel32.dll", SetLastError=true)] private static extern IntPtr VirtualAllocEx(SafeProcessHandle process, IntPtr at, nuint size, uint allocation, uint protection);
    [DllImport("kernel32.dll", SetLastError=true)] private static extern bool VirtualFreeEx(SafeProcessHandle process, IntPtr at, nuint size, uint free);
    [DllImport("kernel32.dll", SetLastError=true)] private static extern bool WriteProcessMemory(SafeProcessHandle process, IntPtr at, byte[] bytes, nuint size, out nuint written);
    [DllImport("kernel32.dll", SetLastError=true)] private static extern SafeWaitHandle CreateRemoteThread(SafeProcessHandle process, IntPtr attributes, nuint stack, IntPtr start, IntPtr parameter, uint flags, IntPtr id);
    [DllImport("kernel32.dll", SetLastError=true)] private static extern uint WaitForSingleObject(SafeWaitHandle thread, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError=true)] private static extern bool GetExitCodeThread(SafeWaitHandle thread, out uint code);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
    [DllImport("kernel32.dll", CharSet=CharSet.Ansi, ExactSpelling=true)] private static extern IntPtr GetProcAddress(IntPtr module, string name);

    public static string Fingerprint(string executable)
    {
        using var file = new FileStream(executable, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(file));
    }

    public static ExternalGame[] Discover(string[]? names = null,string port=Ports.Supermedo)
    {
        Ports.Validate(port);
        if (!OperatingSystem.IsWindows()) return [];
        var matches = new List<ExternalGame>();
        var processes = Process.GetProcesses();
        try
        {
            foreach (var process in processes)
            {
                try
                {
                    if (names != null && !names.Contains(process.ProcessName, StringComparer.OrdinalIgnoreCase)) continue;
                    if (process.HasExited || process.MainModule?.FileName is not { } executable) continue;
                    var known = new FileInfo(executable);
                    if (!RuntimeContract.HasSymbols(executable)) continue;
                    var key=(port,executable,known.Length,known.LastWriteTimeUtc.Ticks);
                    if (!resolvedFiles.TryGetValue(key,out var resolution))
                    {
                        try { resolution=ResolveRuntime(executable,port); } catch(IOException) { resolution=new("incompatible","",[],[]); }
                        resolvedFiles[key]=resolution;
                    }
                    if (resolution.Sha256.Length>0) matches.Add(new(process.Id, executable, resolution.Sha256));
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException or NotSupportedException) { }
            }
        }
        finally { foreach (var process in processes) process.Dispose(); }
        return matches.ToArray();
    }

    // Verified from the installed executable; generated alongside its fingerprint.
    public const long SupportedHostLength = 15272960;

    public static async Task<ExternalGame> AttachAsync(Profile profile, string bridgePath, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess) throw new IOException(TextCatalog.T("external.x64"));
        Ports.Validate(profile.Port);
        var matches = Discover(port:profile.Port);
        if (matches.Length != 1)
        {
            if(matches.Length==0 && File.Exists(profile.Executable))
            {
                try { _=ResolveRuntime(profile.Executable,profile.Port); }
                catch(IOException ex) { throw new IOException(TextCatalog.T("external.contract_failed",Path.GetFileName(profile.Executable),ex.Message),ex); }
            }
            throw new IOException(TextCatalog.T("external.single", matches.Length));
        }
        var game = matches[0];
        new ProcessGuard().EnsureNoOtherGames(profile with { Executable = game.Executable }, game.Pid);
        var path = Path.GetFullPath(bridgePath);
        if (!File.Exists(path)) throw new IOException(TextCatalog.T("external.dll_missing"));
        var source = SafeTree.Canonical(profile.Saves);
        if (source.Length > 1022) throw new IOException(TextCatalog.T("external.path_long"));
        return await Task.Run(() =>
        {
            using var companion=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
            if (Convert.ToHexString(SHA256.HashData(companion)) != Ports.BridgeHash(profile.Port))
                throw new IOException(TextCatalog.T("external.dll_changed"));
            // Keep the runtime file locked against replacement throughout validation and activation.
            using var runtimeFile=new FileStream(game.Executable,FileMode.Open,FileAccess.Read,FileShare.Read);
            var resolution=ResolveRuntime(game.Executable,profile.Port);
            if(resolution.Sha256!=game.HostSha256) throw new IOException(TextCatalog.T("external.runtime_changed"));
            using var process = Process.GetProcessById(game.Pid);
            var identity=(game.Pid,process.StartTime.ToUniversalTime().Ticks);
            if (uncertainCalls.ContainsKey(identity)) throw new ExternalAttachException(TextCatalog.T("external.timeout"), -1);
            using var handle = OpenProcess(Rights, false, game.Pid);
            if (handle.IsInvalid) throw Error("external.attach_failed");
            if (!IsWow64Process2(handle, out var machine, out var native) || machine != 0 || native != 0x8664)
                throw new IOException(TextCatalog.T("external.x64"));
            var module = Module(process, path);
            var existing=process.Modules.Cast<ProcessModule>().Where(m => new[]{"bcm-external.dll","bcm-awaken.dll"}.Contains(Path.GetFileName(m.FileName),StringComparer.OrdinalIgnoreCase)).ToArray();
            if (existing.Any(m => Fingerprint(m.FileName)!=Ports.BridgeHash(profile.Port)))
                throw new ExternalAttachException(TextCatalog.T("external.restart_bridge"), -2);
            if (module==IntPtr.Zero && existing.Length==1) module=existing[0].BaseAddress;
            if (module == IntPtr.Zero)
            {
                ValidateLoadedCode(handle,process,resolution);
                // Resolve the local LoadLibraryW's owning module, including Windows forwarding to KernelBase.
                // Use its RVA in the target's corresponding module; do not assume equal module bases.
                var local = GetProcAddress(GetModuleHandle("kernel32.dll"), "LoadLibraryW");
                using var self = Process.GetCurrentProcess();
                var owner = self.Modules.Cast<ProcessModule>().Single(m => local.ToInt64() >= m.BaseAddress.ToInt64() && local.ToInt64() < m.BaseAddress.ToInt64() + m.ModuleMemorySize);
                var remoteOwner = process.Modules.Cast<ProcessModule>().Single(m => string.Equals(m.FileName, owner.FileName, StringComparison.OrdinalIgnoreCase));
                var load = new IntPtr(remoteOwner.BaseAddress.ToInt64() + local.ToInt64() - owner.BaseAddress.ToInt64());
                try { _ = Invoke(handle, load, Encoding.Unicode.GetBytes(path + "\0")); }
                catch (ExternalAttachException ex) when (ex.Code == -1) { uncertainCalls[identity]=0; throw; }
                process.Refresh(); module = Module(process, path);
                if (module == IntPtr.Zero) throw new IOException(TextCatalog.T("external.load_failed"));
            }
            var config = new byte[8 + 1024 * 2 + 26*4 + 68];
            BitConverter.GetBytes(config.Length).CopyTo(config, 0); BitConverter.GetBytes(3).CopyTo(config, 4);
            Encoding.Unicode.GetBytes(source).CopyTo(config, 8);
            for(int n=0;n<26;n++) BitConverter.GetBytes(resolution.Layout[n]).CopyTo(config,2056+n*4);
            Encoding.ASCII.GetBytes(resolution.Sha256).CopyTo(config,2160);
            var start = new IntPtr(module.ToInt64() + ExportRva(path, "BcmStart"));
            uint result;
            try { result = Invoke(handle, start, config); }
            catch (ExternalAttachException ex) when (ex.Code == -1) { uncertainCalls[identity]=0; throw; }
            if (result != 0) throw new ExternalAttachException(TextCatalog.T(result == 13 ? "external.source_mismatch" : "external.start_failed", result), (int)result);
            return game;
        }, cancellation).ConfigureAwait(false);
    }

    public static RuntimeResolution ResolveRuntime(string executable,string port=Ports.Supermedo)
    {
        Ports.Validate(port);
        // Reviewed contracts first; a port update that only changes private implementation details
        // falls back to the structural check of what the bridge actually uses.
        IOException reviewed;
        try { return ResolveReviewed(executable,port); }
        catch(IOException ex) { reviewed=ex; }
        try { var structural=StructuralRuntime.Resolve(executable,port); unreviewedHosts[structural.Sha256]=0; return structural; }
        catch(IOException structural) { throw new IOException(reviewed.Message+" / Structural check: "+structural.Message,structural); }
    }
    private static RuntimeResolution ResolveReviewed(string executable,string port)
    {
        if(port==Ports.Awaken) return AwakenRuntime.Resolve(executable);
        var hash=Fingerprint(executable);
        if(hash==SupportedHostSha256) return new("supermedo-legacy-verified",hash,
            [0xc0cc88,0xc0cc90,0xc0cc94,0xc05040,0xc05030,0xc50f40,0x4f74a0,0x4f74d0,
             0x8a90,0x8f50,0x9010,0x9140,0x9260,0x92e0,0x9370,0x94b0,0x9690,
             0x18280,0x515d40,40,0x5183d0,14,0x517680,11,0x218,24],[]);
        return RuntimeContract.Resolve(executable);
    }
    private static void ValidateLoadedCode(SafeProcessHandle handle,Process process,RuntimeResolution resolution)
    {
        var address=process.MainModule?.BaseAddress ?? throw new IOException("No loaded runtime module.");
        foreach(var check in resolution.Code)
        {
            var body=new byte[check.Length];
            if(!ReadProcessMemory(handle,new IntPtr(address.ToInt64()+check.Rva),body,(nuint)body.Length,out var read) || read!=(nuint)body.Length)
                throw new IOException(TextCatalog.T("external.loaded_code"));
            foreach(var mask in check.Masks) body[mask]=0;
            if(Convert.ToHexString(SHA256.HashData(body))!=check.Sha256) throw new IOException(TextCatalog.T("external.loaded_code"));
        }
    }

    private static IntPtr Module(Process process, string path)
        => process.Modules.Cast<ProcessModule>().FirstOrDefault(m => string.Equals(m.FileName, path, StringComparison.OrdinalIgnoreCase))?.BaseAddress ?? IntPtr.Zero;

    private static IOException Error(string key) => new(TextCatalog.T(key) + " " + new Win32Exception(Marshal.GetLastWin32Error()).Message);

    private static uint Invoke(SafeProcessHandle process, IntPtr entry, byte[] payload)
    {
        var address = VirtualAllocEx(process, IntPtr.Zero, (nuint)payload.Length, 0x3000, 0x04);
        if (address == IntPtr.Zero) throw Error("external.attach_failed");
        bool finished = true;
        try
        {
            if (!WriteProcessMemory(process, address, payload, (nuint)payload.Length, out var written) || written != (nuint)payload.Length)
                throw Error("external.attach_failed");
            using var thread = CreateRemoteThread(process, IntPtr.Zero, 0, entry, address, 0, IntPtr.Zero);
            if (thread.IsInvalid) throw Error("external.attach_failed");
            finished = false;
            var wait = WaitForSingleObject(thread, 15000);
            if (wait != 0) throw new ExternalAttachException(TextCatalog.T("external.timeout"), -1);
            finished = true;
            if (!GetExitCodeThread(thread, out var code)) throw Error("external.attach_failed");
            return code;
        }
        finally
        {
            // A timed-out thread can still reference its payload. Never free memory beneath it or terminate it.
            if (finished) _ = VirtualFreeEx(process, address, 0, 0x8000);
        }
    }

    public static int ExportRva(string path, string export)
    {
        using var file = File.OpenRead(path); using var pe = new PEReader(file);
        var directory = pe.PEHeaders.PEHeader?.ExportTableDirectory ?? throw new IOException("Invalid native bridge.");
        if (directory.Size < 40) throw new IOException("Native bridge has no export table.");
        var table = pe.GetSectionData(directory.RelativeVirtualAddress).GetContent();
        int U32(int offset) => BitConverter.ToInt32(table.AsSpan(offset, 4));
        var functionCount = U32(20); var nameCount = U32(24);
        var functions = pe.GetSectionData(U32(28)).GetContent();
        var names = pe.GetSectionData(U32(32)).GetContent();
        var ordinals = pe.GetSectionData(U32(36)).GetContent();
        for (int i = 0; i < nameCount; ++i)
        {
            var at = pe.GetSectionData(BitConverter.ToInt32(names.AsSpan(i * 4, 4))).GetContent();
            var bytes = at.AsSpan(); var end = bytes.IndexOf((byte)0);
            if (end < 0 || Encoding.ASCII.GetString(bytes[..end]) != export) continue;
            var ordinal = BitConverter.ToUInt16(ordinals.AsSpan(i * 2, 2));
            if (ordinal >= functionCount) throw new IOException("Invalid bridge export ordinal.");
            var rva = BitConverter.ToInt32(functions.AsSpan(ordinal * 4, 4));
            if (rva >= directory.RelativeVirtualAddress && rva < directory.RelativeVirtualAddress + directory.Size)
                throw new IOException("Forwarded bridge exports are not supported.");
            return rva;
        }
        throw new IOException("Native bridge entry point is missing.");
    }
}
