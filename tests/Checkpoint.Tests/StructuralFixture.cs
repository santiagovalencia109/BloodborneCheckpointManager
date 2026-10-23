using System.Text;

/// <summary>Synthetic x64 PE shaped like a port runtime: the file table, its mutex and stride,
/// public exports and helper symbols. Contains no game code.</summary>
static class StructuralFixture
{
    public sealed record Options(bool Awaken=false,uint Stride=0x230,bool SecondLock=false,bool SecondImage=false,
        bool AnchorImage=true,bool DropExport=false,bool DropHelper=false,bool ReadOnlyFiles=false,byte Filler=0x90);
    public const uint Text=0x1000, Data=0x4000, Files=0x10000;
    public const uint Image=Data, Gpu=Data+8, Entered=Data+12, Image2=Data+16, Root=Data+0x100, Lock=Data+0x300, Lock2=Data+0x308;
    public const uint FileTable=Data+0x400, SaveTable=Data+0x700, PadTable=Data+0x800, Strings=Data+0x900;

    public static byte[] Create(Options? options=null)
    {
        var o=options ?? new Options();
        string Llvm(string name)=>o.Awaken ? name+".llvm.4242" : name;
        var bytes=new byte[0x8000];
        void U16(int at,ushort v)=>BitConverter.GetBytes(v).CopyTo(bytes,at);
        void U32(int at,uint v)=>BitConverter.GetBytes(v).CopyTo(bytes,at);
        void U64(int at,ulong v)=>BitConverter.GetBytes(v).CopyTo(bytes,at);
        U16(0,0x5a4d); U32(60,0x80); U32(0x80,0x4550); U16(0x84,0x8664); U16(0x86,2); U16(0x94,0xf0);
        U16(0x98,0x20b); U64(0xb0,0x140000000); U32(0xd0,Data+0x100000);
        void Section(int at,uint rva,uint vsize,uint rawSize,uint flags) { U32(at+8,vsize); U32(at+12,rva); U32(at+16,rawSize); U32(at+20,rva); U32(at+36,flags); }
        Section(0x188,Text,0x3000,0x3000,0x60000020);
        Section(0x1b0,Data,0x100000,0x4000,o.ReadOnlyFiles ? 0x40000040u : 0xc0000040u);

        var symbols=new List<(string Name,uint Rva,bool Function)>();
        uint cursor=Text;
        uint Fn(string name,params byte[][] parts)
        {
            var at=cursor; var code=parts.SelectMany(p=>p).ToArray();
            Array.Fill(bytes,o.Filler,(int)at,0x40); code.CopyTo(bytes,(int)at);
            symbols.Add((name,at,true)); cursor+=0x40; return at;
        }
        // Instruction encoders whose operands are relative to their own position (set when placed).
        byte[] Rip(byte[] opcode,uint at,uint target)
        { var b=opcode.Concat(new byte[4]).ToArray(); BitConverter.GetBytes((int)(target-(at+(uint)b.Length))).CopyTo(b,opcode.Length); return b; }
        byte[] Call(uint at,uint target) { var b=new byte[5]; b[0]=0xe8; BitConverter.GetBytes((int)(target-(at+5))).CopyTo(b,1); return b; }
        byte[] Ret=[0xc3];

        var acquire=Fn(o.Awaken ? "AcquireSRWLockExclusive" : "pthread_mutex_lock",Ret);
        Fn(o.Awaken ? "ReleaseSRWLockExclusive" : "pthread_mutex_unlock",Ret);
        // do_close: lea rax,[files]; imul rax,rax,stride; lea rcx,[lock]; call acquire; ret
        {
            var at=cursor; var lea=Rip([0x48,0x8d,0x05],at,Files);
            var imul=new byte[]{0x48,0x69,0xc0}.Concat(BitConverter.GetBytes(o.Stride)).ToArray();
            var lockAt=at+(uint)(lea.Length+imul.Length); var lockLea=Rip([0x48,0x8d,0x0d],lockAt,Lock);
            Fn("do_close",lea,imul,lockLea,Call(lockAt+7,acquire),Ret);
        }
        if(o.SecondLock)
        {
            var at=cursor; var lea=Rip([0x48,0x8d,0x05],at,Files); var lockAt=at+7;
            Fn("do_other",lea,Rip([0x48,0x8d,0x0d],lockAt,Lock2),Call(lockAt+7,acquire),Ret);
        }
        { var at=cursor; var a=Rip([0x48,0x8d,0x05],at,Root); Fn("runtime_file_configure",a,Rip([0x48,0x8d,0x0d],at+7,Files),Ret); }
        { var at=cursor; var a=Rip([0x48,0x8b,0x05],at,Image); Fn("main",a,o.AnchorImage ? Rip([0x8b,0x05],at+7,Gpu) : Ret,Ret); }
        if(o.SecondImage) { var at=cursor; Fn("other",Rip([0x48,0x8b,0x05],at,Image2),Ret); }
        var helpers=o.Awaken ? new[]{"open","close","read"} : new[]{"open","close","read","pread","write","lseek","stat","fstat","getdents"};
        foreach(var h in helpers) if(!(o.DropHelper && h=="read")) Fn("runtime_file_"+h,Ret);
        Fn("runtime_thread_attach_main",Ret);
        var io=Fn("sce_io",Ret); var save=Fn("save_io",Ret); var pad=Fn("pad_io",Ret);
        Fn("zz_end",Ret);

        uint strings=Strings;
        void Table(uint rva,IEnumerable<string> names,uint target)
        {
            uint n=0;
            foreach(var name in names)
            {
                Encoding.ASCII.GetBytes(name+"\0").CopyTo(bytes,(int)strings);
                U64((int)(rva+n*16),0x140000000UL+strings); U64((int)(rva+n*16+8),0x140000000UL+target);
                strings+=(uint)name.Length+1; n++;
            }
            symbols.Add((Llvm("exports"),rva,false));
        }
        var fileNames=Checkpoint.Core.StructuralRuntime.FileExports.Concat(["sceKernelRename2"]).Where(n=>!(o.DropExport && n=="unlink"));
        Table(FileTable,fileNames,io);
        Table(SaveTable,Checkpoint.Core.StructuralRuntime.SaveExports,save);
        Table(PadTable,new[]{"scePadInit"}.Concat(Checkpoint.Core.StructuralRuntime.PadExports),pad);
        symbols.AddRange([("image",Image,false),("gpu_enabled",Gpu,false),("entered_game",Entered,false),(Llvm("user_root"),Root,false),
            (Llvm("lock"),Lock,false),(Llvm("files"),Files,false)]);
        if(o.SecondLock) symbols.Add(("lock",Lock2,false));
        if(o.SecondImage) symbols.Add(("image",Image2,false));

        // COFF symbol table (all long names) and string table after the raw sections.
        int symbolStart=bytes.Length; var table=new List<byte>(); var names=new List<byte>{0,0,0,0};
        foreach(var s in symbols)
        {
            var entry=new byte[18]; BitConverter.GetBytes((uint)names.Count).CopyTo(entry,4);
            names.AddRange(Encoding.ASCII.GetBytes(s.Name+"\0"));
            ushort section=s.Rva<Data ? (ushort)1 : (ushort)2;
            BitConverter.GetBytes(s.Rva-(section==1 ? Text : Data)).CopyTo(entry,8);
            BitConverter.GetBytes(section).CopyTo(entry,12); BitConverter.GetBytes((ushort)(s.Function ? 32 : 0)).CopyTo(entry,14);
            entry[16]=3; table.AddRange(entry);
        }
        BitConverter.GetBytes(names.Count).CopyTo(names.ToArray(),0);
        var stringTable=names.ToArray(); BitConverter.GetBytes(stringTable.Length).CopyTo(stringTable,0);
        U32(0x8c,(uint)symbolStart); U32(0x90,(uint)symbols.Count);
        return bytes.Concat(table).Concat(stringTable).ToArray();
    }
}
