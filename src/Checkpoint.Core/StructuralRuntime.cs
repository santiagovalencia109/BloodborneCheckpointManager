using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Checkpoint.Core;

/// <summary>Version-independent fallback for runtimes without a reviewed contract.
/// Instead of pinning the bytes of the port's private file/save implementation, it checks only what
/// the native bridge consumes: named state and helpers, the public export tables, the file mutex
/// (taken from the code that locks it) and the file-record stride (taken from the code that indexes it).
/// Anything ambiguous or missing fails closed. Results are marked as not reviewed.</summary>
public static class StructuralRuntime
{
    // Exactly the names both native bridges look up and hook (native/*/hooks.cpp).
    public static readonly string[] FileExports=["sceKernelOpen","open","sceKernelClose","close","sceKernelRead","read",
        "sceKernelPread","pread","sceKernelWrite","write","sceKernelPwrite","pwrite","sceKernelLseek","lseek","sceKernelFstat","fstat",
        "sceKernelStat","stat","sceKernelGetdirentries","getdirentries","sceKernelRename","rename","sceKernelFtruncate","ftruncate",
        "sceKernelTruncate","truncate","sceKernelFsync","fsync","access","sceKernelCheckReachability","sceKernelMkdir","mkdir",
        "sceKernelRmdir","rmdir","sceKernelUnlink","unlink"];
    public static readonly string[] SaveExports=["sceSaveDataInitialize","sceSaveDataInitialize2","sceSaveDataInitialize3",
        "sceSaveDataTerminate","sceSaveDataMount","sceSaveDataMount2","sceSaveDataUmount","sceSaveDataSetParam","sceSaveDataSaveIcon",
        "sceSaveDataDelete","sceSaveDataDirNameSearch","sceSaveDataSetupSaveDataMemory","sceSaveDataGetSaveDataMemory","sceSaveDataSetSaveDataMemory"];
    public static readonly string[] PadExports=["scePadReadState","scePadRead"];
    private static readonly string[] SupermedoHelpers=["open","close","read","pread","write","lseek","stat","fstat","getdents"];
    private static readonly string[] AwakenHelpers=["open","close","read"]; // LTO removed the other six host helpers.
    private const int MaxFiles=1024;
    /// <summary>Reviewed file-record layouts (stride → path offset) per port; each must also be accepted by that port's native bridge.
    /// 0x218/24: Supermedo legacy and Awaken 0.9.33. 0x230/32: Supermedo atomic save.
    /// 0x228/36: Awaken after 0.9.33 (adds a request timestamp at 0x18 and a flag at 0x20 before the path).</summary>
    public static IReadOnlyDictionary<uint,uint> FileRecords(string port)=>port==Ports.Awaken
        ? new Dictionary<uint,uint>{[0x218]=24,[0x228]=36} : new Dictionary<uint,uint>{[0x218]=24,[0x230]=32};

    public static RuntimeResolution Resolve(string path,string port)=>ResolveBytes(File.ReadAllBytes(path),port);

    public static RuntimeResolution ResolveBytes(byte[] bytes,string port)
    {
        Ports.Validate(port);
        try { return ResolveCore(bytes,port); }
        catch(Exception ex) when(ex is ArgumentException or OverflowException or IndexOutOfRangeException or InvalidOperationException)
        { throw new IOException("Malformed runtime image.",ex); }
    }

    private static RuntimeResolution ResolveCore(byte[] bytes,string port)
    {
        var awaken=port==Ports.Awaken;
        var pe=new PeImage(bytes);

        // ---- Named symbols (LTO may append ".llvm.<hash>") ----
        static bool Matches(PeImage.Symbol s,string name)=>s.Name==name || s.Name.StartsWith(name+".llvm.",StringComparison.Ordinal);
        PeImage.Symbol[] Named(string name,bool function)=>pe.Symbols.Where(s=>s.Function==function && Matches(s,name))
            .GroupBy(s=>s.Rva).Select(g=>g.First()).ToArray();
        uint Function(string name)
        {
            var found=Named(name,true);
            if(found.Length!=1) throw new IOException(found.Length==0 ? "Missing runtime function: "+name : "Ambiguous runtime function: "+name);
            if(!pe.Region(found[0].Rva,1).Executable) throw new IOException("Invalid runtime function: "+name);
            return found[0].Rva;
        }
        uint Any(string name)
        {
            var found=pe.Symbols.Where(s=>Matches(s,name)).Select(s=>s.Rva).Distinct().ToArray();
            if(found.Length!=1) throw new IOException(found.Length==0 ? "Missing runtime symbol: "+name : "Ambiguous runtime symbol: "+name);
            return found[0];
        }
        uint Writable(uint rva,int size,string name)
        {
            if(!pe.Region(rva,size).Writable) throw new IOException("Runtime state is not writable: "+name);
            return rva;
        }

        // Function bodies are scanned once; oversized/unbounded ones are skipped.
        var bodies=new List<(uint Rva,byte[] Body)>();
        for(int i=0;i+1<pe.FunctionStarts.Length;i++)
        {
            var rva=pe.FunctionStarts[i]; var length=pe.FunctionStarts[i+1]-rva;
            if(length==0 || length>65536) continue;
            try { if(pe.Region(rva,(int)length).Executable) bodies.Add(pe.Function(rva)); } catch(IOException) { }
        }
        // When a data name is repeated (static globals in several translation units), keep the
        // candidate that is used by the same code as an already-resolved anchor.
        uint Data(string name,int size,params uint[] anchors)
        {
            var found=Named(name,false).Where(s=>pe.Region(s.Rva,1).Writable).Select(s=>s.Rva).ToArray();
            if(found.Length>1 && anchors.Length>0)
                found=found.Where(c=>bodies.Any(b=>PeImage.References(b.Rva,b.Body,c) && anchors.Any(a=>PeImage.References(b.Rva,b.Body,a)))).ToArray();
            if(found.Length!=1) throw new IOException(found.Length==0 ? "Missing runtime state: "+name : "Ambiguous runtime state: "+name);
            return Writable(found[0],size,name);
        }

        // ---- File table and the code that owns it ----
        var filesCandidates=Named("files",false);
        if(filesCandidates.Length!=1) throw new IOException(filesCandidates.Length==0 ? "Missing runtime state: files" : "Ambiguous runtime state: files");
        var files=filesCandidates[0].Rva;
        var owners=bodies.Where(b=>PeImage.References(b.Rva,b.Body,files)).ToList();
        if(owners.Count==0) throw new IOException("No runtime code uses the file table.");

        // Record stride: REX.W IMUL r64,r/m64,imm32 (register form) indexing the table. Only reviewed pairs exist in the bridges.
        var records=FileRecords(port); var strides=new HashSet<uint>();
        foreach(var (_,body) in owners)
            for(int j=0;j+7<=body.Length;j++)
                if(body[j] is >=0x48 and <=0x4f && body[j+1]==0x69 && body[j+2]>>6==3)
                {
                    var value=BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(j+3,4));
                    if(records.ContainsKey(value)) strides.Add(value);
                }
        if(strides.Count!=1) throw new IOException(strides.Count==0 ? "File record layout not recognised." : "Ambiguous file record layout.");
        var stride=strides.Single(); var pathOffset=records[stride];
        Writable(files,MaxFiles*(int)stride,"files");

        // File mutex: argument of the lock call (LEA RCX,[RIP+lock]; CALL acquire) in that same code.
        var lockName=awaken ? "AcquireSRWLockExclusive" : "pthread_mutex_lock";
        var unlockName=awaken ? "ReleaseSRWLockExclusive" : "pthread_mutex_unlock";
        var acquire=awaken ? Any(lockName) : Function(lockName);
        var release=awaken ? Any(unlockName) : Function(unlockName);
        var importSlots=pe.Symbols.Where(s=>s.Name=="__imp_"+lockName).Select(s=>s.Rva).ToHashSet();
        var lockScan=owners.ToList();
        if(Named("runtime_file_mount",true).Length==1) lockScan.Add(pe.Function(Function("runtime_file_mount")));
        // The file mutex is the one taken by every such function; other locks (e.g. a prefetch lock taken
        // only in do_open) drop out of the intersection.
        HashSet<uint>? locks=null;
        foreach(var (rva,body) in lockScan)
        {
            var taken=new HashSet<uint>();
            for(int j=0;j+12<=body.Length;j++)
            {
                if(body[j]!=0x48 || body[j+1]!=0x8d || body[j+2]!=0x0d) continue;
                var argument=(uint)(rva+j+7+BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(j+3,4)));
                bool direct=body[j+7]==0xe8 && rva+j+12+BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(j+8,4))==acquire;
                bool indirect=j+13<=body.Length && body[j+7]==0xff && body[j+8]==0x15 &&
                    importSlots.Contains((uint)(rva+j+13+BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(j+9,4))));
                if(direct || indirect) taken.Add(argument);
            }
            if(taken.Count==0) continue;
            if(locks==null) locks=taken; else locks.IntersectWith(taken);
        }
        locks??=[];
        uint fileLock;
        if(locks.Count==1) fileLock=locks.Single();
        else if(locks.Count==0 && awaken)
        {
            // Same reviewed rule as tools/inspect-awaken-runtime.py: the lock from the file table's LTO module.
            var suffix=filesCandidates[0].Name["files".Length..];
            fileLock=suffix.Length>0 ? Any("lock"+suffix) : throw new IOException("File lock not found.");
        }
        else throw new IOException(locks.Count==0 ? "File lock not found." : "Ambiguous file lock.");
        if(!pe.Symbols.Any(s=>!s.Function && s.Rva==fileLock)) throw new IOException("File lock is not a named runtime global.");
        Writable(fileLock,8,"file_lock");

        // ---- Remaining state ----
        var gpu=Data("gpu_enabled",4); var entered=Data("entered_game",4);
        var image=Data("image",8,gpu,entered);
        var root=Data("user_root",512,files);

        // ---- Public export tables ----
        var exports=Named("exports",false).Select(s=>s.Rva).Order().ToArray();
        var functionStarts=pe.FunctionStarts.ToHashSet();
        var tables=new Dictionary<string,(uint Rva,List<string> Names,List<uint> Targets)>();
        foreach(var table in exports)
        {
            var names=new List<string>(); var targets=new List<uint>();
            for(uint n=0;n<256;n++)
            {
                var at=table+n*16;
                if(n>0 && exports.Contains(at)) break;
                int offset; try { offset=pe.FileOffset(at,16); } catch(IOException) { break; }
                ulong namePointer=pe.U64(offset), target=pe.U64(offset+8);
                if(namePointer<=pe.ImageBase || target<=pe.ImageBase || namePointer-pe.ImageBase>=pe.ImageSize || target-pe.ImageBase>=pe.ImageSize) break;
                string text; try { text=pe.CString(pe.FileOffset((uint)(namePointer-pe.ImageBase),1),128); } catch(IOException) { break; }
                var function=(uint)(target-pe.ImageBase);
                if(text.Length==0 || !functionStarts.Contains(function) || !pe.Region(function,1).Executable) break;
                names.Add(text); targets.Add(function);
            }
            var kind=names.Count==0 ? null : names[0] switch { "sceKernelOpen"=>"file","sceSaveDataInitialize"=>"save","scePadInit"=>"pad",_=>null };
            if(kind==null) continue;
            if(tables.ContainsKey(kind)) throw new IOException("Ambiguous runtime exports: "+kind);
            tables[kind]=(table,names,targets);
        }
        foreach(var (kind,required) in new[]{("file",FileExports),("save",SaveExports),("pad",PadExports)})
        {
            if(!tables.TryGetValue(kind,out var t)) throw new IOException("Missing runtime exports: "+kind);
            var missing=required.FirstOrDefault(n=>!t.Names.Contains(n,StringComparer.Ordinal));
            if(missing!=null) throw new IOException("Missing runtime export: "+missing);
        }

        // ---- Layout in native/*/layout.h order ----
        var helperNames=awaken ? AwakenHelpers : SupermedoHelpers;
        var helpers=SupermedoHelpers.Select(n=>helperNames.Contains(n) ? Function("runtime_file_"+n) : 0u).ToArray();
        var attach=Function("runtime_thread_attach_main");
        var layout=new List<uint>{image,gpu,entered,root,fileLock,files,acquire,release};
        layout.AddRange(helpers); layout.Add(attach);
        foreach(var kind in new[]{"file","save","pad"}) { layout.Add(tables[kind].Rva); layout.Add((uint)tables[kind].Names.Count); }
        layout.Add(stride); layout.Add(pathOffset);
        if(layout.Count!=26) throw new IOException("Invalid runtime layout.");

        // ---- Loaded-code checks for every function the bridge will hook ----
        // The disk image is the reference; absolute (DIR64) relocations are masked for ASLR.
        IEnumerable<uint> Hooked(string kind,string[] wanted)=>wanted.Select(n=>tables[kind].Targets[tables[kind].Names.IndexOf(n)]);
        var hooked=Hooked("file",FileExports).Concat(Hooked("save",SaveExports)).Concat(Hooked("pad",PadExports))
            .Concat(helpers.Where(h=>h!=0)).Append(attach).Distinct().Order();
        var checks=new List<RuntimeCodeCheck>();
        foreach(var rva in hooked)
        {
            var (_,body)=pe.Function(rva); var masks=new List<int>();
            int first=Array.BinarySearch(pe.Relocations,rva); if(first<0) first=~first;
            for(int i=first;i<pe.Relocations.Length && pe.Relocations[i]<rva+body.Length;i++)
            {
                var at=(int)(pe.Relocations[i]-rva);
                if(at+8>body.Length) throw new IOException("Relocation crosses a function boundary.");
                for(int k=0;k<8;k++) { masks.Add(at+k); body[at+k]=0; }
            }
            checks.Add(new(rva,body.Length,masks.ToArray(),Convert.ToHexString(SHA256.HashData(body))));
        }
        return new((awaken ? "awaken" : "supermedo")+"-structural-v1",Convert.ToHexString(SHA256.HashData(bytes)),layout.ToArray(),checks,Reviewed:false);
    }
}
