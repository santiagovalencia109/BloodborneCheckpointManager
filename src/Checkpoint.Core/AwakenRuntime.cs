using System.Security.Cryptography;
using System.Text.Json;

namespace Checkpoint.Core;

/// <summary>Independent reviewed LTO/SRW adapter. Never falls back to a Supermedo contract.</summary>
public static class AwakenRuntime
{
    private sealed record Contract(int Schema,string Family,string Sha256,long Length,uint[] Layout,RuntimeCodeCheck[] Code);
    public static RuntimeResolution Resolve(string path)
    {
        using var stream=typeof(AwakenRuntime).Assembly.GetManifestResourceStream("BCM.awaken-contract.json")!;
        var c=JsonSerializer.Deserialize<Contract>(stream,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})
            ?? throw new IOException("Missing Awaken runtime contract.");
        using var file=File.OpenRead(path);
        if(c.Schema!=1 || file.Length!=c.Length || Convert.ToHexString(SHA256.HashData(file))!=c.Sha256)
            throw new IOException("This Awaken engine has not been verified. Select the native engine/out/bb-probe.exe from the supported 0.9.33 package.");
        if(c.Layout.Length!=26 || c.Layout[24]!=536 || c.Layout[25]!=24 || c.Code.Length==0)
            throw new IOException("Invalid Awaken runtime contract.");
        return new(c.Family,c.Sha256,c.Layout,c.Code);
    }
}
