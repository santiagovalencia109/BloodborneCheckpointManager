using System.Buffers.Binary;
using System.Text;

namespace Checkpoint.Core;

/// <summary>Bounded read-only view of a Windows x64 PE image with its COFF symbol table.
/// Used by the structural runtime resolver; never executes or modifies the inspected file.</summary>
internal sealed class PeImage
{
    public sealed record Section(uint Rva,uint VirtualSize,uint Raw,uint RawSize,uint Flags)
    {
        public uint End=>Rva+Math.Max(VirtualSize,RawSize);
        public bool Writable=>(Flags&0x80000000)!=0;
        public bool Executable=>(Flags&0x20000000)!=0;
    }
    public sealed record Symbol(string Name,uint Rva,bool Function);

    public byte[] Bytes { get; }
    public ulong ImageBase { get; }
    public uint ImageSize { get; }
    public IReadOnlyList<Section> Sections { get; }
    public IReadOnlyList<Symbol> Symbols { get; }
    /// <summary>Sorted RVAs of every function symbol; a function's body ends at the next one.</summary>
    public uint[] FunctionStarts { get; }
    /// <summary>Sorted RVAs of 8-byte absolute (DIR64) base relocations.</summary>
    public uint[] Relocations { get; }

    public PeImage(byte[] bytes)
    {
        Bytes=bytes;
        if(bytes.Length<256) throw new IOException("Runtime must be a Windows x64 PE image.");
        int pe=checked((int)U32(60));
        if(U16(0)!=0x5a4d || pe<64 || pe>bytes.Length-264 || U32(pe)!=0x4550 || U16(pe+4)!=0x8664 || U16(pe+24)!=0x20b)
            throw new IOException("Runtime must be a Windows x64 PE image.");
        var count=U16(pe+6); var optionalSize=U16(pe+20); ImageSize=U32(pe+24+56); ImageBase=U64(pe+24+24);
        if(count==0 || count>96 || optionalSize<112+16*6) throw new IOException("Invalid PE sections.");
        var sections=new List<Section>();
        for(int n=0;n<count;n++)
        {
            int at=checked(pe+24+optionalSize+n*40);
            var s=new Section(U32(at+12),U32(at+8),U32(at+20),U32(at+16),U32(at+36));
            if((ulong)s.Rva+Math.Max(s.VirtualSize,s.RawSize)>ImageSize || (ulong)s.Raw+s.RawSize>(ulong)bytes.Length)
                throw new IOException("Invalid PE section bounds.");
            if(sections.Any(o=>s.Rva<o.End && o.Rva<s.End)) throw new IOException("Overlapping PE sections.");
            sections.Add(s);
        }
        Sections=sections;

        int symStart=checked((int)U32(pe+12)); uint symCount=U32(pe+16);
        if(symStart<=0 || symCount==0 || symCount>1_000_000) throw new IOException("Runtime has no usable COFF symbols.");
        int strings=checked(symStart+(int)symCount*18);
        if(strings+4>bytes.Length) throw new IOException("Invalid COFF strings.");
        int stringSize=checked((int)U32(strings));
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
                    name=CString(checked(strings+(int)offset),stringSize-(int)offset);
                }
                else
                {
                    var span=bytes.AsSpan(at,8); int end=span.IndexOf((byte)0); name=Encoding.ASCII.GetString(end<0 ? span : span[..end]);
                }
                if(name.Length>0 && !name.StartsWith('.'))
                {
                    var rva=checked(sections[section-1].Rva+U32(at+8));
                    if(rva<sections[section-1].End) symbols.Add(new(name,rva,U16(at+14)==32));
                }
            }
            n+=1u+aux;
        }
        Symbols=symbols;
        FunctionStarts=symbols.Where(s=>s.Function).Select(s=>s.Rva).Distinct().Order().ToArray();
        Relocations=ReadRelocations(pe+24);
    }

    private uint[] ReadRelocations(int optional)
    {
        uint rva=U32(optional+112+5*8), size=U32(optional+116+5*8);
        if(rva==0 || size==0) return [];
        int pos=FileOffset(rva,(int)size), end=pos+(int)size; var list=new List<uint>();
        while(pos+8<=end)
        {
            uint page=U32(pos), block=U32(pos+4);
            if(block<8 || pos+block>end) throw new IOException("Invalid PE relocations.");
            for(int at=pos+8;at+2<=pos+block;at+=2)
            {
                var entry=U16(at); var kind=entry>>12;
                if(kind==10) list.Add(page+(uint)(entry&0xfff));
                else if(kind!=0) throw new IOException("Unexpected PE relocation type.");
            }
            pos+=(int)block;
        }
        list.Sort(); return list.ToArray();
    }

    public ushort U16(int p)=>BinaryPrimitives.ReadUInt16LittleEndian(Bytes.AsSpan(p,2));
    public uint U32(int p)=>BinaryPrimitives.ReadUInt32LittleEndian(Bytes.AsSpan(p,4));
    public ulong U64(int p)=>BinaryPrimitives.ReadUInt64LittleEndian(Bytes.AsSpan(p,8));

    public Section Region(uint rva,int length)=>Sections.SingleOrDefault(s=>rva>=s.Rva && (ulong)rva+(uint)length<=s.End)
        ?? throw new IOException("RVA outside the image.");
    public int FileOffset(uint rva,int length)
    {
        var s=Region(rva,length);
        if((ulong)rva+(uint)length>(ulong)s.Rva+s.RawSize) throw new IOException("RVA has no file bytes.");
        return checked((int)(s.Raw+rva-s.Rva));
    }
    public string CString(int offset,int limit)
    {
        if(offset<0 || offset>=Bytes.Length) throw new IOException("Invalid string offset.");
        int end=Array.IndexOf(Bytes,(byte)0,offset,Math.Min(limit,Bytes.Length-offset));
        if(end<0) throw new IOException("Unterminated string.");
        return Encoding.ASCII.GetString(Bytes,offset,end-offset);
    }

    /// <summary>Bytes of a function, from its symbol to the next function symbol.</summary>
    public (uint Rva,byte[] Body) Function(uint rva,int limit=65536)
    {
        int index=Array.BinarySearch(FunctionStarts,rva);
        if(index<0 || index+1>=FunctionStarts.Length) throw new IOException("Function has no known end.");
        var length=FunctionStarts[index+1]-rva;
        if(length==0 || length>limit) throw new IOException("Unexpected function size.");
        var section=Region(rva,(int)length);
        if(!section.Executable) throw new IOException("Function is not executable.");
        return (rva,Bytes.AsSpan(FileOffset(rva,(int)length),(int)length).ToArray());
    }

    /// <summary>True if the body has a RIP-relative memory operand (ModRM mod=00 rm=101) resolving to target.
    /// Trailing immediates of 0/1/2/4 bytes are allowed. Matching an exact symbol address makes accidental hits negligible.</summary>
    public static bool References(uint rva,byte[] body,uint target)
    {
        for(int j=1;j+4<=body.Length;j++)
        {
            if((body[j-1]&0xC7)!=0x05) continue;
            long displacement=BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(j,4));
            foreach(var tail in stackalloc[]{0,1,2,4})
                if(rva+j+4+tail+displacement==target) return true;
        }
        return false;
    }
}
