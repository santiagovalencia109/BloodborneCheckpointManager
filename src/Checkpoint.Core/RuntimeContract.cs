using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Checkpoint.Core;

public sealed record RuntimeResolution(string Family, string Sha256, uint[] Layout,
    IReadOnlyList<RuntimeCodeCheck> Code);
public sealed record RuntimeCodeCheck(uint Rva, int Length, int[] Masks, string Sha256);

/// <summary>Resolve COFF names and validate relocation-independent runtime ABI shapes.
/// Unknown/stripped/changed contracts fail closed. No launcher or executable-name trust.</summary>
public static class RuntimeContract
{
    private sealed record Pattern(string Symbol,int Length,string Sha256,int[] Masks,Reference[] References);
    private sealed record Reference(int Offset,int Next,string Symbol);
    private sealed record Table(string Symbol,string[] Names,string[] Functions);
    private sealed record FileRecord(int Stride,int PathOffset);
    private sealed record Contract(int Schema,string Family,Dictionary<string,string> Layout,
        Dictionary<string,Table> Tables,Pattern[] Patterns,string[]? Scope,FileRecord? FileRecord);
    private sealed record Section(uint Rva,uint Size,uint Raw,uint RawSize,uint Flags);
    private sealed record Symbol(string Name,uint Rva,bool Function);
    private static readonly string[] Contracts=[ReadContract("BCM.runtime-contract.json"),ReadContract("BCM.runtime-contract.atomic.json")];
    private static string ReadContract(string resource)
    {
        using var stream=typeof(RuntimeContract).Assembly.GetManifestResourceStream(resource)!;
        using var reader=new StreamReader(stream); return reader.ReadToEnd();
    }
    public static bool HasSymbols(string path)
    {
        try
        {
            using var file=File.OpenRead(path); using var reader=new BinaryReader(file);
            if(file.Length<256 || file.Length>256*1024*1024 || reader.ReadUInt16()!=0x5a4d) return false;
            file.Position=60; var pe=reader.ReadInt32(); if(pe<64 || pe>file.Length-24) return false;
            file.Position=pe; if(reader.ReadUInt32()!=0x4550 || reader.ReadUInt16()!=0x8664) return false;
            file.Position=pe+12; var symbols=reader.ReadUInt32(); var count=reader.ReadUInt32();
            return symbols>0 && count>0 && count<1_000_000 && (ulong)symbols+count*18UL+4<=(ulong)file.Length;
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException) { return false; }
    }
    public static RuntimeResolution Resolve(string path)=>ResolveBytes(File.ReadAllBytes(path));
    public static RuntimeResolution ResolveBytes(byte[] bytes)
    {
        var errors=new List<string>();
        foreach(var contract in Contracts)
        {
            try { return ResolveBytes(bytes,contract); }
            catch(IOException error) { errors.Add(error.Message); }
        }
        throw new IOException("No compatible save I/O contract: "+string.Join(" / ",errors.Distinct()));
    }
    // Explicit contract input supports synthetic format/relocation tests. Attachment always uses the embedded contract.
    public static RuntimeResolution ResolveBytes(byte[] bytes,string contractJson)
    {
        try { return ResolveCore(bytes,contractJson); }
        catch(Exception ex) when(ex is ArgumentException or OverflowException or IndexOutOfRangeException or JsonException or KeyNotFoundException)
        { throw new IOException("Malformed runtime or compatibility contract.",ex); }
    }
    private static RuntimeResolution ResolveCore(byte[] bytes,string contractJson)
    {
        var contract=JsonSerializer.Deserialize<Contract>(contractJson,new JsonSerializerOptions { PropertyNameCaseInsensitive=true })
            ?? throw new IOException("Missing runtime contract.");
        if(contract.Schema!=1) throw new IOException("Unknown runtime contract schema.");
        ushort U16(int p)=>BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(p,2));
        uint U32(int p)=>BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(p,4));
        ulong U64(int p)=>BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(p,8));
        int pe=checked((int)U32(60));
        if(U16(0)!=0x5a4d || U32(pe)!=0x4550 || U16(pe+4)!=0x8664 || U16(pe+24)!=0x20b)
            throw new IOException("Runtime must be a Windows x64 PE image.");
        var count=U16(pe+6); var optionalSize=U16(pe+20); var imageSize=U32(pe+24+56);
        if(count==0 || count>96 || optionalSize<112) throw new IOException("Invalid PE sections.");
        var imageBase=U64(pe+24+24); var sections=new List<Section>();
        for(int n=0;n<count;n++)
        {
            int at=checked(pe+24+optionalSize+n*40);
            var section=new Section(U32(at+12),U32(at+8),U32(at+20),U32(at+16),U32(at+36));
            if((ulong)section.Rva+Math.Max(section.Size,section.RawSize)>imageSize ||
                (ulong)section.Raw+section.RawSize>(ulong)bytes.Length) throw new IOException("Invalid PE section bounds.");
            if(sections.Any(s=>section.Rva<s.Rva+Math.Max(s.Size,s.RawSize) && s.Rva<section.Rva+Math.Max(section.Size,section.RawSize)))
                throw new IOException("Overlapping PE sections.");
            sections.Add(section);
        }
        Section Region(uint rva,int length)=>sections.SingleOrDefault(s=>rva>=s.Rva &&
            (ulong)rva+(uint)length<=(ulong)s.Rva+Math.Max(s.Size,s.RawSize)) ?? throw new IOException("RVA outside the image.");
        int FileOffset(uint rva,int length)
        {
            var s=Region(rva,length);
            if((ulong)rva+(uint)length>(ulong)s.Rva+s.RawSize) throw new IOException("RVA has no file bytes.");
            return checked((int)(s.Raw+rva-s.Rva));
        }
        string Z(int offset,int limit)
        {
            if(offset<0 || offset>=bytes.Length) throw new IOException("Invalid string offset.");
            int end=Array.IndexOf(bytes,(byte)0,offset,Math.Min(limit,bytes.Length-offset));
            if(end<0) throw new IOException("Unterminated symbol.");
            return Encoding.ASCII.GetString(bytes,offset,end-offset);
        }
        int symStart=checked((int)U32(pe+12)); uint symCount=U32(pe+16);
        if(symStart<=0 || symCount==0 || symCount>1_000_000) throw new IOException("Runtime has no usable COFF symbols.");
        int strings=checked(symStart+(int)symCount*18); int stringSize=checked((int)U32(strings));
        if(stringSize<4 || (long)strings+stringSize>bytes.Length) throw new IOException("Invalid COFF strings.");
        var symbols=new List<Symbol>();
        for(uint n=0;n<symCount;)
        {
            int at=checked(symStart+(int)n*18); var section=unchecked((short)U16(at+12)); var aux=bytes[at+17];
            if(n+1+aux>symCount) throw new IOException("Invalid COFF auxiliary entries.");
            if(section>0 && section<=sections.Count && bytes[at+16] is 2 or 3)
            {
                string name;
                if(U32(at)==0)
                {
                    uint offset=U32(at+4); if(offset<4 || offset>=stringSize) throw new IOException("Invalid COFF name.");
                    name=Z(checked(strings+(int)offset),stringSize-(int)offset);
                }
                else
                {
                    var span=bytes.AsSpan(at,8); int end=span.IndexOf((byte)0); name=Encoding.ASCII.GetString(end<0 ? span : span[..end]);
                }
                if(name.Length>0 && !name.StartsWith('.'))
                {
                    var rva=checked(sections[section-1].Rva+U32(at+8)); Region(rva,1);
                    symbols.Add(new(name,rva,U16(at+14)==32));
                }
            }
            n+=1u+aux;
        }
        var keyed=new Dictionary<string,Symbol>(StringComparer.Ordinal);
        foreach(var group in symbols.OrderBy(s=>s.Rva).GroupBy(s=>s.Name))
        { int n=0; foreach(var s in group) keyed.Add(s.Name+"#"+n++,s); }
        Symbol Required(string key)=>keyed.TryGetValue(key,out var symbol) ? symbol : throw new IOException("Missing runtime symbol: "+key);
        uint Data(string name,int size)
        {
            var rva=Required(contract.Layout[name]).Rva;
            if((Region(rva,size).Flags&0x80000000)==0) throw new IOException("Runtime state is not writable: "+name);
            return rva;
        }
        uint Function(string name)
        {
            var sym=Required(contract.Layout[name]);
            if(!sym.Function || (Region(sym.Rva,1).Flags&0x20000000)==0) throw new IOException("Invalid runtime function: "+name);
            return sym.Rva;
        }
        if(contract.Scope!=null)
        {
            var actual=symbols.Where(s=>s.Function && (s.Name.StartsWith("runtime_file_",StringComparison.Ordinal) || s.Name.StartsWith("runtime_savedata_",StringComparison.Ordinal) || s.Name.StartsWith("runtime_pad_",StringComparison.Ordinal))).Select(s=>s.Name).Order(StringComparer.Ordinal);
            if(!actual.SequenceEqual(contract.Scope.Order(StringComparer.Ordinal))) throw new IOException("Runtime file/save/input interface changed.");
        }
        var checks=new List<RuntimeCodeCheck>();
        foreach(var pattern in contract.Patterns)
        {
            var s=Required(pattern.Symbol);
            if(!s.Function || pattern.Length<=0 || pattern.Length>65536 || (Region(s.Rva,pattern.Length).Flags&0x20000000)==0)
                throw new IOException("Invalid critical function: "+pattern.Symbol);
            var body=bytes.AsSpan(FileOffset(s.Rva,pattern.Length),pattern.Length).ToArray();
            foreach(var reference in pattern.References)
            {
                if(reference.Offset<0 || reference.Offset>body.Length-4 || reference.Next<0 || reference.Next>body.Length)
                    throw new IOException("Invalid function reference.");
                long target=(long)s.Rva+reference.Next+BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(reference.Offset,4));
                if(target!=Required(reference.Symbol).Rva) throw new IOException("Runtime reference changed: "+pattern.Symbol+" → "+reference.Symbol);
            }
            foreach(int mask in pattern.Masks) { if(mask<0 || mask>=body.Length) throw new IOException("Invalid relocation mask."); body[mask]=0; }
            if(Convert.ToHexString(SHA256.HashData(body))!=pattern.Sha256) throw new IOException("Runtime ABI changed: "+pattern.Symbol);
            checks.Add(new(s.Rva,pattern.Length,pattern.Masks,pattern.Sha256));
        }
        (uint rva,uint count) TableOf(string name)
        {
            var table=contract.Tables[name]; var rva=Required(table.Symbol).Rva;
            if(table.Names.Length==0 || table.Names.Length!=table.Functions.Length || table.Names.Length>256)
                throw new IOException("Invalid export contract.");
            int at=FileOffset(rva,table.Names.Length*16);
            for(int i=0;i<table.Names.Length;i++)
            {
                var nameRva=checked((uint)(U64(at+i*16)-imageBase)); var fnRva=checked((uint)(U64(at+i*16+8)-imageBase));
                if(Z(FileOffset(nameRva,1),128)!=table.Names[i] || fnRva!=Required(table.Functions[i]).Rva)
                    throw new IOException("Runtime exports changed: "+name);
            }
            return(rva,(uint)table.Names.Length);
        }
        var record=contract.FileRecord ?? new FileRecord(0x218,24);
        if(!((record.Stride==0x218 && record.PathOffset==24) || (record.Stride==0x230 && record.PathOffset==32)))
            throw new IOException("Unknown runtime file record layout.");
        var files=TableOf("file"); var saves=TableOf("save"); var pad=TableOf("pad");
        var helpers=new[]{"open","close","read","pread","write","lseek","stat","fstat","getdents"};
        var layout=new List<uint>{Data("image",8),Data("gpu_enabled",4),Data("entered_game",4),Data("user_root",512),
            Data("file_lock",8),Data("files",1024*record.Stride),Function("pthread_mutex_lock"),Function("pthread_mutex_unlock")};
        layout.AddRange(helpers.Select(n=>Function("runtime_file_"+n)));
        layout.AddRange([Function("runtime_thread_attach_main"),files.rva,files.count,saves.rva,saves.count,pad.rva,pad.count,(uint)record.Stride,(uint)record.PathOffset]);
        return new(contract.Family,Convert.ToHexString(SHA256.HashData(bytes)),layout.ToArray(),checks);
    }
}
