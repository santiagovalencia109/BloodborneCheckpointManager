namespace Checkpoint.Core;

public static class Ports
{
    public const string Supermedo="supermedo", Awaken="awaken";
    public static readonly string[] All=[Supermedo,Awaken];
    public static void Validate(string port)
    { if(!All.Contains(port,StringComparer.Ordinal)) throw new IOException("Unknown game port: "+port); }
    public static string BridgeFile(string port)
    { Validate(port); return port==Awaken ? "bcm-awaken.dll" : "bcm-external.dll"; }
    public static string BridgeHash(string port)
    { Validate(port); return port==Awaken ? AwakenBridgeIdentity.Sha256 : ExternalBridgeIdentity.Sha256; }
}

public sealed class PortProfiles
{
    public Dictionary<string,Profile> Profiles { get; init; }=new(StringComparer.Ordinal);
    public static PortProfiles Load(string directory)
    {
        var path=Path.Combine(directory,"port-profiles.json");
        var result=File.Exists(path) ? JsonDisk.Read<PortProfiles>(path) : new PortProfiles();
        var legacy=Path.Combine(directory,"settings.json");
        if(File.Exists(legacy))
        {
            var active=JsonDisk.Read<Profile>(legacy); Ports.Validate(active.Port);
            result.Profiles[active.Port]=active.ForSupportedManagerMode();
        }
        foreach(var (port,profile) in result.Profiles)
        { Ports.Validate(port); if(profile.Port!=port) throw new IOException("Port profile identity mismatch."); }
        return result;
    }
    public Profile Get(string port,string directory)
    {
        Ports.Validate(port);
        return Profiles.TryGetValue(port,out var profile) ? profile.ForSupportedManagerMode() :
            new Profile("","",Path.Combine(directory,"Checkpoints",port),LiveCheckpoints:true,ExternalBridge:true,Port:port);
    }
    public void Save(string directory,Profile profile)
    {
        Ports.Validate(profile.Port); Profiles[profile.Port]=profile;
        JsonDisk.Write(Path.Combine(directory,"port-profiles.json"),this);
        JsonDisk.Write(Path.Combine(directory,"settings.json"),profile);
    }
}
