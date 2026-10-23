using Checkpoint.Core;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;

var root = Path.Combine(Path.GetTempPath(), "bcm-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var passed = 0;
var languages=Path.Combine(AppContext.BaseDirectory,"Languages");
void Test(string name, Action<string> test)
{
    var folder = Path.Combine(root, Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(folder);
    test(folder);
    Console.WriteLine("PASS " + name);
    passed++;
}
static void Assert(bool value) { if (!value) throw new Exception("Assertion failed"); }
static void Fails(Action action) { try { action(); } catch (IOException) { return; } throw new Exception("Expected IOException"); }
static Profile Setup(string dir)
{
    var profile = new Profile(Environment.ProcessPath!, Path.Combine(dir, "savedata"), Path.Combine(dir, "library"));
    var game = Path.Combine(profile.Saves, "1", "CUSA03173", "SPRJ0005");
    Directory.CreateDirectory(game);
    var bytes = new byte[0x40000]; bytes[0] = 1;
    File.WriteAllBytes(Path.Combine(game, "userdata0010"), bytes);
    Directory.CreateDirectory(game + ".sce_sys");
    File.WriteAllText(Path.Combine(game + ".sce_sys", "param.bin"), "metadata");
    return profile;
}
static string SystemSave(Profile p) => Path.Combine(p.Saves, "1", "CUSA03173", "SPRJ0005", "userdata0010");
Test("Adaptive contract accepts relocated sections, changed identity and renamed runtime", dir =>
{
    var a=AdaptiveFixture.Create(0x1000,0x3000); var b=AdaptiveFixture.Create(0x2000,0x6000);
    var first=RuntimeContract.ResolveBytes(a.Bytes,a.Contract); var second=RuntimeContract.ResolveBytes(b.Bytes,a.Contract);
    Assert(first.Sha256!=second.Sha256 && first.Layout[0]!=second.Layout[0] && second.Code.Count==1);
    Assert(first.Layout[8]!=second.Layout[8]);
});
Test("Adaptive contract rejects changed function code and incorrect reference targets", dir =>
{
    var a=AdaptiveFixture.Create(0x1000,0x3000); a.Bytes[0x217]=0x90;
    Fails(()=>RuntimeContract.ResolveBytes(a.Bytes,a.Contract));
    var b=AdaptiveFixture.Create(0x1000,0x3000); b.Bytes[0x213]++;
    Fails(()=>RuntimeContract.ResolveBytes(b.Bytes,b.Contract));
});
Test("Adaptive contract rejects missing symbols, export mismatches and truncated PE", dir =>
{
    var a=AdaptiveFixture.Create(0x1000,0x3000); a.Bytes[a.SymbolOffset]=(byte)'z';
    Fails(()=>RuntimeContract.ResolveBytes(a.Bytes,a.Contract));
    var b=AdaptiveFixture.Create(0x1000,0x3000); b.Bytes[0x1500+8]++;
    Fails(()=>RuntimeContract.ResolveBytes(b.Bytes,b.Contract));
    Fails(()=>RuntimeContract.ResolveBytes([1,2,3],a.Contract));
});
Test("Adaptive contract rejects overlapping sections and malformed COFF metadata", dir =>
{
    var a=AdaptiveFixture.Create(0x1000,0x3000); BitConverter.GetBytes(0x1000u).CopyTo(a.Bytes,0x188+40+12);
    Fails(()=>RuntimeContract.ResolveBytes(a.Bytes,a.Contract));
    var b=AdaptiveFixture.Create(0x1000,0x3000); b.Bytes[b.SymbolOffset+17]=255;
    Fails(()=>RuntimeContract.ResolveBytes(b.Bytes,b.Contract));
});

Test("Adaptive contract rejects a changed public I/O surface", dir =>
{
    var fixture=AdaptiveFixture.Create(0x1000,0x3000);
    var document=System.Text.Json.Nodes.JsonNode.Parse(fixture.Contract)!;
    document["scope"]=new System.Text.Json.Nodes.JsonArray("runtime_file_unexpected");
    Fails(()=>RuntimeContract.ResolveBytes(fixture.Bytes,document.ToJsonString()));
    document["scope"]=new System.Text.Json.Nodes.JsonArray();
    Assert(RuntimeContract.ResolveBytes(fixture.Bytes,document.ToJsonString()).Code.Count==1);
});
Test("COFF names longer than 512 bytes do not reject a compatible runtime", dir =>
{
    var fixture=AdaptiveFixture.Create(0x1000,0x3000);
    var bytes=fixture.Bytes; int count=BitConverter.ToInt32(bytes,0x90); int entry=fixture.SymbolOffset+count*18;
    Array.Clear(bytes,entry,18); BitConverter.GetBytes(4u).CopyTo(bytes,entry+4);
    BitConverter.GetBytes(0x18u).CopyTo(bytes,entry+8); BitConverter.GetBytes((ushort)1).CopyTo(bytes,entry+12); bytes[entry+16]=3;
    BitConverter.GetBytes(count+1).CopyTo(bytes,0x90);
    int strings=entry+18; BitConverter.GetBytes(905u).CopyTo(bytes,strings);
    Array.Fill(bytes,(byte)'x',strings+4,900); bytes[strings+904]=0;
    Assert(RuntimeContract.ResolveBytes(bytes,fixture.Contract).Code.Count==1);
    bytes[strings+904]=(byte)'x'; Fails(()=>RuntimeContract.ResolveBytes(bytes,fixture.Contract));
});
Test("Atomic-save record metadata is carried to the bridge; unknown layouts are rejected", dir =>
{
    var fixture=AdaptiveFixture.Create(0x1000,0x3000); var document=System.Text.Json.Nodes.JsonNode.Parse(fixture.Contract)!;
    document["fileRecord"]=new System.Text.Json.Nodes.JsonObject { ["stride"]=560,["pathOffset"]=32 };
    var resolved=RuntimeContract.ResolveBytes(fixture.Bytes,document.ToJsonString());
    Assert(resolved.Layout.Length==26 && resolved.Layout[24]==560 && resolved.Layout[25]==32);
    document["fileRecord"]!["stride"]=600;
    Fails(()=>RuntimeContract.ResolveBytes(fixture.Bytes,document.ToJsonString()));
});
Test("Legacy prepared profiles migrate to supported Supermedo mode without changing paths", dir =>
{
    var profile=Setup(dir) with { LiveCheckpoints=true,ExternalBridge=false };
    Assert(profile.ForSupportedManagerMode()==(profile with { ExternalBridge=true }));
    Assert(!(profile with { LiveCheckpoints=false }).ForSupportedManagerMode().ExternalBridge);
});
Test("Public input ABI allows keyboard implementation changes while pinning save code", dir =>
{
    var fixture=AdaptiveFixture.Create(0x1000,0x3000,publicPadAbi:true);
    var first=RuntimeContract.ResolveBytes(fixture.Bytes,fixture.Contract);
    fixture.Bytes[0x280]=0x90;
    Assert(RuntimeContract.ResolveBytes(fixture.Bytes,fixture.Contract).Code.Count==first.Code.Count);
    fixture.Bytes[0x217]=0x90;
    Fails(()=>RuntimeContract.ResolveBytes(fixture.Bytes,fixture.Contract));
});
Test("Public input ABI rejects missing exports, unknown layouts and uncovered save code", dir =>
{
    var fixture=AdaptiveFixture.Create(0x1000,0x3000,publicPadAbi:true);
    var document=System.Text.Json.Nodes.JsonNode.Parse(fixture.Contract)!;
    document["inputAbi"]="unknown";
    Fails(()=>RuntimeContract.ResolveBytes(fixture.Bytes,document.ToJsonString()));
    document["inputAbi"]="orbis-pad-120-v1";
    document["patterns"]=new System.Text.Json.Nodes.JsonArray();
    Fails(()=>RuntimeContract.ResolveBytes(fixture.Bytes,document.ToJsonString()));
    document=System.Text.Json.Nodes.JsonNode.Parse(fixture.Contract)!;
    document["tables"]!["pad"]!["names"]![0]="missing";
    Fails(()=>RuntimeContract.ResolveBytes(fixture.Bytes,document.ToJsonString()));
});
Test("Public input export targets must be executable functions", dir =>
{
    var fixture=AdaptiveFixture.Create(0x1000,0x3000,publicPadAbi:true);
    // Preserve the name and table agreement, but redirect the symbol to writable data.
    int entry=fixture.SymbolOffset+10*18;
    BitConverter.GetBytes(0x80u).CopyTo(fixture.Bytes,entry+8);
    BitConverter.GetBytes((ushort)2).CopyTo(fixture.Bytes,entry+12);
    BitConverter.GetBytes(0x140003080UL).CopyTo(fixture.Bytes,0x1548);
    BitConverter.GetBytes(0x140003080UL).CopyTo(fixture.Bytes,0x1558);
    Fails(()=>RuntimeContract.ResolveBytes(fixture.Bytes,fixture.Contract));
});
Test("Structural fallback resolves a port update from the code that owns the file table", dir =>
{
    var r=StructuralRuntime.ResolveBytes(StructuralFixture.Create(),Ports.Supermedo);
    Assert(!r.Reviewed && r.Family=="supermedo-structural-v1" && r.Layout.Length==26);
    Assert(r.Layout[0]==StructuralFixture.Image && r.Layout[3]==StructuralFixture.Root && r.Layout[4]==StructuralFixture.Lock && r.Layout[5]==StructuralFixture.Files);
    Assert(r.Layout[18]==StructuralFixture.FileTable && r.Layout[19]==37 && r.Layout[20]==StructuralFixture.SaveTable && r.Layout[21]==14);
    Assert(r.Layout[22]==StructuralFixture.PadTable && r.Layout[23]==3 && r.Layout[24]==0x230 && r.Layout[25]==32);
    Assert(r.Layout.Take(18).All(v=>v!=0) && r.Code.Count>0);
    // Private implementation bytes change between port builds; the bridge-facing structure does not.
    var changed=StructuralRuntime.ResolveBytes(StructuralFixture.Create(new(Filler:0xcc)),Ports.Supermedo);
    Assert(changed.Layout.SequenceEqual(r.Layout) && changed.Sha256!=r.Sha256);
    var legacy=StructuralRuntime.ResolveBytes(StructuralFixture.Create(new(Stride:0x218)),Ports.Supermedo);
    Assert(legacy.Layout[24]==0x218 && legacy.Layout[25]==24);
});
Test("Structural fallback separates repeated globals by the code that uses them", dir =>
{
    var r=StructuralRuntime.ResolveBytes(StructuralFixture.Create(new(SecondImage:true)),Ports.Supermedo);
    Assert(r.Layout[0]==StructuralFixture.Image);
    Fails(()=>StructuralRuntime.ResolveBytes(StructuralFixture.Create(new(SecondImage:true,AnchorImage:false)),Ports.Supermedo));
});
Test("Structural fallback fails closed on unknown records, ambiguous locks and missing interface", dir =>
{
    Fails(()=>StructuralRuntime.ResolveBytes(StructuralFixture.Create(new(Stride:0x240)),Ports.Supermedo));
    Fails(()=>StructuralRuntime.ResolveBytes(StructuralFixture.Create(new(SecondLock:true)),Ports.Supermedo));
    Fails(()=>StructuralRuntime.ResolveBytes(StructuralFixture.Create(new(DropExport:true)),Ports.Supermedo));
    Fails(()=>StructuralRuntime.ResolveBytes(StructuralFixture.Create(new(DropHelper:true)),Ports.Supermedo));
    Fails(()=>StructuralRuntime.ResolveBytes(StructuralFixture.Create(new(ReadOnlyFiles:true)),Ports.Supermedo));
    Fails(()=>StructuralRuntime.ResolveBytes([1,2,3],Ports.Supermedo));
    var stripped=StructuralFixture.Create(); BitConverter.GetBytes(0u).CopyTo(stripped,0x90);
    Fails(()=>StructuralRuntime.ResolveBytes(stripped,Ports.Supermedo));
});
Test("Structural Awaken fallback uses SRW locks, LTO names and only the remaining host helpers", dir =>
{
    var r=StructuralRuntime.ResolveBytes(StructuralFixture.Create(new(Awaken:true,Stride:0x218)),Ports.Awaken);
    Assert(!r.Reviewed && r.Family=="awaken-structural-v1" && r.Layout[4]==StructuralFixture.Lock && r.Layout[5]==StructuralFixture.Files);
    Assert(r.Layout[11..17].All(v=>v==0) && r.Layout[8]!=0 && r.Layout[9]!=0 && r.Layout[10]!=0 && r.Layout[24]==0x218 && r.Layout[25]==24);
    // Awaken after 0.9.33: 552-byte records with the path at 36. Supermedo's bridge does not accept that layout.
    var updated=StructuralRuntime.ResolveBytes(StructuralFixture.Create(new(Awaken:true,Stride:0x228)),Ports.Awaken);
    Assert(updated.Layout[24]==0x228 && updated.Layout[25]==36);
    Fails(()=>StructuralRuntime.ResolveBytes(StructuralFixture.Create(new(Stride:0x228)),Ports.Supermedo));
    Fails(()=>StructuralRuntime.ResolveBytes(StructuralFixture.Create(new(Awaken:true,Stride:0x230)),Ports.Awaken));
    // A Supermedo-shaped runtime is not an Awaken runtime and vice versa.
    Fails(()=>StructuralRuntime.ResolveBytes(StructuralFixture.Create(),Ports.Awaken));
    Fails(()=>StructuralRuntime.ResolveBytes(StructuralFixture.Create(new(Awaken:true,Stride:0x218)),Ports.Supermedo));
});
Test("Runtime resolution prefers reviewed contracts and reports both failures", dir =>
{
    var path=Path.Combine(dir,"bb-probe.exe"); File.WriteAllBytes(path,StructuralFixture.Create());
    Assert(!ExternalBridge.ResolveRuntime(path,Ports.Supermedo).Reviewed);
    File.WriteAllBytes(path,[1,2,3]);
    try { ExternalBridge.ResolveRuntime(path,Ports.Supermedo); throw new Exception("Expected failure"); }
    catch(IOException ex) { Assert(ex.Message.Contains("Structural check:")); }
});
Test("Updater picks the newest published Windows package and ignores drafts, older and malformed releases", dir =>
{
    string Asset(string v,bool signed=true)=>$$"""{"name":"BloodborneCheckpointManager-v{{v}}-windows.zip","size":1234,"browser_download_url":"https://github.com/santiagovalencia109/BloodborneCheckpointManager/releases/download/v{{v}}/BloodborneCheckpointManager-v{{v}}-windows.zip"{{(signed ? ",\"digest\":\"sha256:"+new string('a',64)+"\"" : "")}}}""";
    string Release(string v,bool draft=false,string? assets=null)=>$$"""{"tag_name":"v{{v}}","name":"BCM {{v}}","body":"notes {{v}}","html_url":"https://github.com/x","draft":{{(draft ? "true" : "false")}},"assets":[{{assets ?? Asset(v)}}]}""";
    var json="["+string.Join(",",Release("0.9.4-experimental"),Release("0.10.1-experimental",draft:true),Release("0.10.0-experimental"),
        Release("0.9.9-experimental",assets:""),Release("nightly"),Release("0.9.6-experimental"))+"]";
    var best=Updater.SelectNewest(json,"0.9.5-experimental")!;
    Assert(best.Version=="0.10.0-experimental" && best.Sha256==new string('A',64) && best.ZipSize==1234 && best.Notes=="notes 0.10.0-experimental");
    Assert(Updater.SelectNewest(json,"0.10.0-experimental")==null && Updater.SelectNewest("[]","0.9.5")==null);
    Assert(Updater.Compare("0.10.0-experimental","0.9.9")>0 && Updater.Compare("v1.0.0","1.0.0-experimental")==0);
    // A download URL outside this repository is never accepted.
    var foreign=Release("0.11.0-experimental").Replace("https://github.com/santiagovalencia109/","https://example.com/");
    Assert(Updater.SelectNewest("["+foreign+"]","0.9.5")==null);
    // Without GitHub's digest the release is listed, but installing requires a checksum.
    var unsigned=Updater.SelectNewest("["+Release("0.12.0-experimental",assets:Asset("0.12.0-experimental",signed:false))+"]","0.9.5")!;
    Assert(unsigned.Sha256==null);
    try { Updater.DownloadAsync(new HttpClient(),unsigned,dir,null,CancellationToken.None).GetAwaiter().GetResult(); throw new Exception("Expected failure"); }
    catch(UpdateException ex) { Assert(ex.Key=="update.no_checksum"); }
});
Test("Update packages extract without path traversal and must contain the manager and both bridges", dir =>
{
    var zip=Path.Combine(dir,"good.zip");
    using(var archive=System.IO.Compression.ZipFile.Open(zip,System.IO.Compression.ZipArchiveMode.Create))
        foreach(var file in Updater.RequiredFiles) using(var w=new StreamWriter(archive.CreateEntry(file).Open())) w.Write(file);
    var target=Path.Combine(dir,"good"); Updater.ExtractSafely(zip,target); Updater.ValidatePackage(target);
    File.Delete(Path.Combine(target,"Native","bcm-awaken.dll")); Fails(()=>Updater.ValidatePackage(target));
    var evil=Path.Combine(dir,"evil.zip");
    using(var archive=System.IO.Compression.ZipFile.Open(evil,System.IO.Compression.ZipArchiveMode.Create))
        using(var w=new StreamWriter(archive.CreateEntry("../outside.txt").Open())) w.Write("x");
    Fails(()=>Updater.ExtractSafely(evil,Path.Combine(dir,"evil")));
    Assert(!File.Exists(Path.Combine(dir,"outside.txt")) && Updater.IsWritable(dir));
});
if(args.Contains("--contract-only")) { Console.WriteLine($"{passed} contract checks passed."); return; }

Test("Older profiles keep external attachment disabled; explicit preference roundtrips", dir =>
{
    var p=Setup(dir); var path=Path.Combine(dir,"settings.json");
    File.WriteAllText(path,"{\"Executable\":\"native.exe\",\"Saves\":\"saves\",\"Library\":\"library\",\"LiveCheckpoints\":true}");
    Assert(!JsonDisk.Read<Profile>(path).ExternalBridge);
    JsonDisk.Write(path,p with { ExternalBridge=true }); Assert(JsonDisk.Read<Profile>(path).ExternalBridge);
});
Test("Port profiles migrate legacy Supermedo settings without rewriting them", dir =>
{
    var p=Setup(dir) with { LiveCheckpoints=true,ExternalBridge=true };
    var path=Path.Combine(dir,"settings.json"); JsonDisk.Write(path,p); var original=File.ReadAllBytes(path);
    var profiles=PortProfiles.Load(dir); Assert(profiles.Get(Ports.Supermedo,dir)==p);
    var awaken=profiles.Get(Ports.Awaken,dir);
    Assert(awaken.Port==Ports.Awaken && awaken.Executable=="" && awaken.Saves=="" && awaken.Library!=p.Library);
    Assert(File.ReadAllBytes(path).SequenceEqual(original));
});
Test("Port switching persists independent executables, saves and libraries", dir =>
{
    var super=Setup(dir); var profiles=new PortProfiles(); profiles.Save(dir,super);
    var awaken=profiles.Get(Ports.Awaken,dir) with { Executable="awaken.exe",Saves="awaken-saves" };
    profiles.Save(dir,awaken); var loaded=PortProfiles.Load(dir);
    Assert(loaded.Get(Ports.Supermedo,dir)==super && loaded.Get(Ports.Awaken,dir)==awaken);
    loaded.Save(dir,super); loaded=PortProfiles.Load(dir);
    Assert(loaded.Get(Ports.Awaken,dir)==awaken && JsonDisk.Read<Profile>(Path.Combine(dir,"settings.json"))==super);
});
Test("Invalid port identities cannot select a bridge or overwrite profiles", dir =>
{
    Fails(()=>Ports.BridgeFile("unknown")); Fails(()=>new PortProfiles().Save(dir,new Profile("a","b","c",Port:"../supermedo")));
    Assert(!File.Exists(Path.Combine(dir,"settings.json")));
    Assert(Ports.BridgeFile(Ports.Awaken)!=Ports.BridgeFile(Ports.Supermedo));
});
Test("Awaken adapter cannot accept a renamed arbitrary executable", dir =>
{
    var path=Path.Combine(dir,"bb-probe.exe"); File.WriteAllBytes(path,[1,2,3]);
    Fails(()=>ExternalBridge.ResolveRuntime(path,Ports.Awaken));
});
Test("Runtime fingerprint follows contents across rename and rejects altered content", dir =>
{
    var first=Path.Combine(dir,"bb-probe.exe"); File.WriteAllBytes(first,[1,2,3,4]);
    var hash=ExternalBridge.Fingerprint(first); var renamed=Path.Combine(dir,"renamed.exe"); File.Move(first,renamed);
    Assert(ExternalBridge.Fingerprint(renamed)==hash && hash!=ExternalBridge.SupportedHostSha256);
    File.AppendAllText(renamed,"changed"); Assert(ExternalBridge.Fingerprint(renamed)!=hash);
});

Test("English and Spanish catalogs have matching keys and valid placeholders", dir =>
{
    // Language files use camelCase; read with the catalog's public loader too.
    var es=System.Text.Json.JsonSerializer.Deserialize<LanguagePack>(File.ReadAllText(Path.Combine(languages,"es.json")),new System.Text.Json.JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
    var en=System.Text.Json.JsonSerializer.Deserialize<LanguagePack>(File.ReadAllText(Path.Combine(languages,"en.json")),new System.Text.Json.JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
    Assert(en.Strings.Keys.ToHashSet().SetEquals(es.Strings.Keys));
    var catalog=TextCatalog.Load(languages,"es"); Assert(catalog.LoadWarning==null);
    foreach(var key in en.Strings.Keys) _=catalog.Get(key,DateTimeOffset.Now,DateTimeOffset.Now,3,4,5);
    Assert(catalog.Get("settings.title")=="Configura tu juego");
});
Test("A new partial language is discovered without rebuilding; missing keys use English", dir =>
{
    JsonDisk.Write(Path.Combine(dir,"de.json"),new LanguagePack(1,"de","Deutsch",new(){["nav.settings"]="Einstellungen"}));
    var catalog=TextCatalog.Load(dir,"de"); Assert(catalog.LoadWarning==null);
    Assert(TextCatalog.Discover(dir).Any(p=>p.Code=="de") && catalog.Get("nav.settings")=="Einstellungen");
    Assert(catalog.Get("settings.title")=="Set up your game");
});
Test("Invalid translation placeholders fall back to English instead of crashing", dir =>
{
    JsonDisk.Write(Path.Combine(dir,"es.json"),new LanguagePack(1,"es","Español",new(){["error.running"]="{1} {1}"}));
    var catalog=TextCatalog.Load(dir,"es"); Assert(catalog.LoadWarning!=null && catalog.Language=="en");
    Assert(catalog.Get("error.running","bbport",123).Contains("123"));
});
Test("Malformed or unsupported language files fall back and are not offered", dir =>
{
    File.WriteAllText(Path.Combine(dir,"xx.json"),"{broken");
    JsonDisk.Write(Path.Combine(dir,"yy.json"),new LanguagePack(99,"yy","Future",new()));
    Assert(TextCatalog.Load(dir,"xx").LoadWarning!=null && TextCatalog.Load(dir,"yy").LoadWarning!=null);
    Assert(TextCatalog.Discover(dir).Single().Code=="en");
});
Test("Translation language codes cannot escape the language directory", dir =>
{
    Assert(TextCatalog.Load(dir,"../es").LoadWarning!=null);
});

Test("Roundtrip, rename, extra files removed, emergency backup retained", dir =>
{
    var p = Setup(dir); var store = new CheckpointStore(p, new FakeGuard());
    var original = SafeTree.Scan(p.Saves);
    var cp = store.Create("Antes del jefe");
    store.Rename(cp.Id, "Clérigo"); Assert(store.List()[0].Name == "Clérigo");
    File.WriteAllText(Path.Combine(p.Saves, "extra.txt"), "new");
    var old = store.Restore(cp.Id);
    SafeTree.Equal(original, SafeTree.Scan(p.Saves));
    Assert(File.Exists(Path.Combine(old, "extra.txt")));
    Assert(store.List().Any(m => m.Emergency));
});
Test("Running game blocks both operations before mutation", dir =>
{
    var p = Setup(dir); var guard = new FakeGuard(); var store = new CheckpointStore(p, guard);
    var cp = store.Create("test"); guard.Running = true;
    Fails(() => store.Create("blocked")); Fails(() => store.Restore(cp.Id)); Assert(store.List().Length == 1);
});
Test("Delete only selected checkpoint while game runs; preserve live saves and other copies", dir =>
{
    var p=Setup(dir); var guard=new FakeGuard(); var store=new CheckpointStore(p,guard);
    var first=store.Create("first"); var second=store.Create("second"); var original=SafeTree.Scan(p.Saves);
    guard.Running=true;
    Assert(store.Delete(second.Id)==null);
    Assert(!Directory.Exists(Path.Combine(p.Library,second.Id)));
    Assert(store.List().Single().Id==first.Id);
    SafeTree.Equal(original,SafeTree.Scan(p.Saves));
    Fails(() => store.Delete("../savedata"));
});
Test("Delete rejects checkpoints of another profile", dir =>
{
    var p=Setup(dir); var store=new CheckpointStore(p,new FakeGuard()); var cp=store.Create("first");
    var manifest=Path.Combine(p.Library,cp.Id,"manifest.json");
    JsonDisk.Write(manifest,cp with { Source=Path.Combine(dir,"another-save-root") });
    Fails(() => store.Delete(cp.Id)); Assert(Directory.Exists(Path.Combine(p.Library,cp.Id)));
});
Test("Cleanup failure after unpublishing does not break other checkpoints", dir =>
{
    var p=Setup(dir); var store=new CheckpointStore(p,new FakeGuard());
    var first=store.Create("first"); var second=store.Create("second"); var original=SafeTree.Scan(p.Saves);
    var interrupted=new CheckpointStore(p,new FakeGuard(),phase => { if (phase=="DeleteUnpublished") throw new IOException("locked cleanup"); });
    var residual=interrupted.Delete(second.Id);
    Assert(residual!=null && Directory.Exists(residual) && SafeTree.Within(residual,p.Library));
    Assert(store.List().Single().Id==first.Id); SafeTree.Equal(original,SafeTree.Scan(p.Saves));
});
Test("Pending restoration prevents deleting its checkpoint", dir =>
{
    var p=Setup(dir); var store=new CheckpointStore(p,new FakeGuard()); var cp=store.Create("first");
    var interrupted=new CheckpointStore(p,new FakeGuard(),phase => { if (phase=="Prepared") throw new IOException("test interruption"); });
    Fails(() => interrupted.Restore(cp.Id)); Fails(() => store.Delete(cp.Id));
    Assert(Directory.Exists(Path.Combine(p.Library,cp.Id))); store.Recover();
});
Test("Batch delete removes only selected copies, deduplicates IDs and preserves live saves", dir =>
{
    var p=Setup(dir); var guard=new FakeGuard(); var store=new CheckpointStore(p,guard);
    var a=store.Create("a"); var b=store.Create("b"); var keep=store.Create("keep"); var live=SafeTree.Scan(p.Saves);
    guard.Running=true;
    var result=store.DeleteMany([a.Id,b.Id,a.Id]);
    Assert(result.Deleted==2 && result.Error==null && result.Residuals.Length==0);
    Assert(store.List().Single().Id==keep.Id); SafeTree.Equal(live,SafeTree.Scan(p.Saves));
});
Test("Batch delete preflights every checkpoint before removing any copy", dir =>
{
    var p=Setup(dir); var store=new CheckpointStore(p,new FakeGuard()); var a=store.Create("a"); var b=store.Create("b");
    var path=Path.Combine(p.Library,b.Id,"manifest.json"); JsonDisk.Write(path,b with { Source=Path.Combine(dir,"another-save-root") });
    Fails(()=>store.DeleteMany([a.Id,b.Id])); Assert(Directory.Exists(Path.Combine(p.Library,a.Id)));
    Fails(()=>store.DeleteMany([a.Id,"../savedata"])); Assert(Directory.Exists(Path.Combine(p.Library,a.Id)));
    Fails(()=>store.DeleteMany([a.Id,Guid.NewGuid().ToString("N")])); Assert(Directory.Exists(Path.Combine(p.Library,a.Id)));
});
Test("Pending restoration blocks a whole batch deletion", dir =>
{
    var p=Setup(dir); var store=new CheckpointStore(p,new FakeGuard()); var a=store.Create("a"); var b=store.Create("b");
    var interrupted=new CheckpointStore(p,new FakeGuard(),phase=>{ if(phase=="Prepared") throw new IOException("test interruption"); });
    Fails(()=>interrupted.Restore(a.Id)); Fails(()=>store.DeleteMany([a.Id,b.Id]));
    Assert(Directory.Exists(Path.Combine(p.Library,a.Id)) && Directory.Exists(Path.Combine(p.Library,b.Id))); store.Recover();
});
Test("Batch delete reports locked cleanup and continues with the remaining selection", dir =>
{
    var p=Setup(dir); var store=new CheckpointStore(p,new FakeGuard()); var a=store.Create("a"); var b=store.Create("b"); var keep=store.Create("keep");
    int cleaned=0; var blocked=new CheckpointStore(p,new FakeGuard(),phase=>{ if(phase=="DeleteUnpublished" && ++cleaned==1) throw new IOException("locked"); });
    var result=blocked.DeleteMany([a.Id,b.Id]);
    Assert(result.Deleted==2 && result.Error==null && result.Residuals.Length==1 && Directory.Exists(result.Residuals[0]));
    Assert(store.List().Single().Id==keep.Id);
});
Test("Batch delete reports partial progress if a later directory cannot be unpublished", dir =>
{
    var p=Setup(dir); var store=new CheckpointStore(p,new FakeGuard()); var a=store.Create("a"); var b=store.Create("b"); var c=store.Create("c");
    int moved=0; var blocked=new CheckpointStore(p,new FakeGuard(),phase=>{ if(phase=="BeforeDeleteUnpublish" && ++moved==2) throw new IOException("locked"); });
    var result=blocked.DeleteMany([a.Id,b.Id,c.Id]);
    Assert(result.Deleted==1 && result.Error=="locked");
    Assert(store.List().Select(m=>m.Id).ToHashSet().SetEquals([b.Id,c.Id]));
});
Test("Empty batch deletion is a no-op", dir =>
{
    var p=Setup(dir); var store=new CheckpointStore(p,new FakeGuard()); var cp=store.Create("keep");
    var result=store.DeleteMany([]); Assert(result.Deleted==0 && result.Error==null && store.List().Single().Id==cp.Id);
});
Test("Explicit older checkpoint restores its bytes instead of newest checkpoint", dir =>
{
    var p=Setup(dir); var store=new CheckpointStore(p,new FakeGuard()); var older=store.Create("older");
    var changed=File.ReadAllBytes(SystemSave(p)); changed[1]=42; File.WriteAllBytes(SystemSave(p),changed);
    var newest=store.Create("newest");
    store.Restore(older.Id); Assert(File.ReadAllBytes(SystemSave(p))[1]==0);
    Assert(store.List().Any(cp => cp.Id==newest.Id));
    store.Restore(newest.Id); Assert(File.ReadAllBytes(SystemSave(p))[1]==42);
});
Test("Tampered payload blocked before emergency or restore", dir =>
{
    var p = Setup(dir); var store = new CheckpointStore(p, new FakeGuard()); var cp = store.Create("test");
    File.WriteAllText(Path.Combine(p.Library, cp.Id, "data", "rogue.txt"), "tampered");
    var before = SafeTree.Scan(p.Saves); Fails(() => store.Restore(cp.Id)); SafeTree.Equal(before, SafeTree.Scan(p.Saves)); Assert(store.List().Length == 1);
});
Test("Manifest traversal rejected", dir =>
{
    var p = Setup(dir); var store = new CheckpointStore(p, new FakeGuard()); var cp = store.Create("test");
    JsonDisk.Write(Path.Combine(p.Library, cp.Id, "manifest.json"), cp with { Files = [new Entry("../outside", 1, "bad")] });
    Fails(() => store.Restore(cp.Id));
});
Test("Overlapping directories blocked", dir =>
{
    var p = Setup(dir); Fails(() => new CheckpointStore(p with { Library = Path.Combine(p.Saves, "backups") }, new FakeGuard()));
});
Test("Wrong profile blocked", dir =>
{
    var p = Setup(dir); var cp = new CheckpointStore(p, new FakeGuard()).Create("test");
    var other = Setup(Path.Combine(dir, "other")) with { Library = p.Library };
    var store = new CheckpointStore(other, new FakeGuard()); Assert(store.List().Length == 0); Fails(() => store.Restore(cp.Id));
});
Test("Open writer blocks snapshot", dir =>
{
    var p = Setup(dir); using var writer = new FileStream(SystemSave(p), FileMode.Open, FileAccess.Write, FileShare.None);
    Fails(() => new CheckpointStore(p, new FakeGuard()).Create("locked"));
});
Test("Wrong size and temporary saves blocked", dir =>
{
    var p = Setup(dir); var store = new CheckpointStore(p, new FakeGuard());
    File.WriteAllText(Path.Combine(p.Saves, "inflight.bbtmp"), "temp"); Fails(() => store.Create("temp"));
    File.Delete(Path.Combine(p.Saves, "inflight.bbtmp")); File.WriteAllText(SystemSave(p), "truncated"); Fails(() => store.Create("bad"));
});
foreach (var stage in new[] { "Prepared", "OriginalMoved", "Installed" })
    Test("Interrupted restore recovery: " + stage, dir =>
    {
        var p = Setup(dir); var guard = new FakeGuard(); var store = new CheckpointStore(p, guard); var cp = store.Create("test");
        File.WriteAllText(Path.Combine(p.Saves, "progress.txt"), "must survive"); var before = SafeTree.Scan(p.Saves);
        var broken = new CheckpointStore(p, guard, point => { if (point == stage) throw new IOException("Simulated crash"); });
        Fails(() => broken.Restore(cp.Id)); Assert(store.HasPendingRestore);
        Fails(() => store.Create("blocked pending"));
        guard.Running = true; Fails(store.Recover); guard.Running = false;
        store.Recover(); SafeTree.Equal(before, SafeTree.Scan(p.Saves)); Assert(!store.HasPendingRestore);
    });
Test("Real process guard detects configured running executable", dir =>
{
    var p = Setup(dir); Fails(() => new ProcessGuard().EnsureClosed(p));
});
Test("Process starts between directory moves: preserve original and recover later", dir =>
{
    var p = Setup(dir); var guard = new FakeGuard(); var store = new CheckpointStore(p, guard); var cp = store.Create("test");
    var broken = new CheckpointStore(p, guard, stage => { if (stage == "OriginalMoved") guard.Running = true; });
    Fails(() => broken.Restore(cp.Id)); Assert(broken.HasPendingRestore); guard.Running = false; store.Recover(); Assert(File.Exists(SystemSave(p)));
});
Test("Same-length payload corruption rejected by hash", dir =>
{
    var p = Setup(dir); var store = new CheckpointStore(p, new FakeGuard()); var cp = store.Create("test");
    var path = Path.Combine(p.Library, cp.Id, "data", cp.Files.First(e => e.Path.EndsWith("userdata0010")).Path);
    var bytes = File.ReadAllBytes(path); bytes[1] = 42; File.WriteAllBytes(path, bytes);
    Fails(() => store.Restore(cp.Id)); Assert(store.List().Length == 1);
});
Test("Destination locked even across separate libraries", dir =>
{
    var p = Setup(dir); using var held = new FileStream(p.Saves + ".bcm.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    Fails(() => new CheckpointStore(p with { Library = Path.Combine(dir, "another-library") }, new FakeGuard()).Create("blocked"));
});
Test("All-zero system data rejected", dir =>
{
    var p = Setup(dir); File.WriteAllBytes(SystemSave(p), new byte[0x40000]);
    Fails(() => new CheckpointStore(p, new FakeGuard()).Create("invalid"));
});
Test("Failed snapshot never publishes partial data", dir =>
{
    var p = Setup(dir); using var held = new FileStream(SystemSave(p), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    var store = new CheckpointStore(p, new FakeGuard()); Fails(() => store.Create("locked")); Assert(store.List().Length == 0);
});
Test("Forged recovery paths rejected", dir =>
{
    var p = Setup(dir); var store = new CheckpointStore(p, new FakeGuard()); store.Create("test");
    JsonDisk.Write(Path.Combine(p.Library, "pending-restore.json"), new RestoreJournal(p.Saves, Path.Combine(dir, "other.stage"), Path.Combine(dir, "other.original"), Path.Combine(dir, "other.displaced"), Guid.NewGuid().ToString("N")));
    Fails(store.Recover); Assert(File.Exists(SystemSave(p)));
});
Test("Workflow closes before restoring and launches only after success", dir =>
{
    var p = Setup(dir) with { RestartAfterRestore = true }; var calls = new List<string>(); var life = new FakeLifecycle(calls);
    var result = new RestoreWorkflow(life).RunAsync(p, () => calls.Add("preflight"), () => { calls.Add("restore"); return "original"; }, null, default, waiting => calls.Add(waiting ? "waiting" : "not-waiting")).GetAwaiter().GetResult();
    Assert(string.Join(",", calls) == "validate,preflight,waiting,close,not-waiting,restore,waiting,launch,not-waiting"); Assert(result.Restarted);
});
Test("Invalid launcher does not close game or touch saves", dir =>
{
    var p = Setup(dir) with { RestartAfterRestore = true }; var calls = new List<string>(); var life = new FakeLifecycle(calls) { FailValidation = true };
    Fails(() => new RestoreWorkflow(life).RunAsync(p, () => calls.Add("preflight"), () => { calls.Add("restore"); return ""; }, null, default).GetAwaiter().GetResult());
    Assert(string.Join(",", calls) == "validate");
});
Test("Corrupt checkpoint rejected before requesting closure", dir =>
{
    var p = Setup(dir); var calls = new List<string>();
    Fails(() => new RestoreWorkflow(new FakeLifecycle(calls)).RunAsync(p, () => throw new IOException("corrupt"), () => { calls.Add("restore"); return ""; }, null, default).GetAwaiter().GetResult()); Assert(calls.Count == 0);
});
Test("Close timeout does not restore or relaunch", dir =>
{
    var p = Setup(dir); var calls = new List<string>();
    Fails(() => new RestoreWorkflow(new FakeLifecycle(calls) { FailClose = true }).RunAsync(p, () => { }, () => { calls.Add("restore"); return ""; }, null, default).GetAwaiter().GetResult()); Assert(string.Join(",", calls) == "close");
});
Test("Restore failure never relaunches", dir =>
{
    var p = Setup(dir) with { RestartAfterRestore = true }; var calls = new List<string>();
    Fails(() => new RestoreWorkflow(new FakeLifecycle(calls)).RunAsync(p, () => { }, () => throw new IOException("disk full"), null, default).GetAwaiter().GetResult()); Assert(!calls.Contains("launch"));
});
Test("Launch failure preserves successful restore result", dir =>
{
    var p = Setup(dir) with { RestartAfterRestore = true }; var calls = new List<string>();
    var result = new RestoreWorkflow(new FakeLifecycle(calls) { FailLaunch = true }).RunAsync(p, () => { }, () => "original", null, default).GetAwaiter().GetResult(); Assert(!result.Restarted && result.Original == "original");
});
Test("Cancellation after restore does not launch or undo restore", dir =>
{
    var p = Setup(dir) with { RestartAfterRestore = true }; var calls = new List<string>(); using var cancel = new CancellationTokenSource();
    var result = new RestoreWorkflow(new FakeLifecycle(calls)).RunAsync(p, () => { }, () => { cancel.Cancel(); return "original"; }, null, cancel.Token).GetAwaiter().GetResult(); Assert(!result.Restarted && result.Original == "original" && !calls.Contains("launch"));
});
Test("Cancellation while waiting never reaches restore", dir =>
{
    var p = Setup(dir); var calls = new List<string>(); using var cancel = new CancellationTokenSource();
    var waiting=false;
    try { new RestoreWorkflow(new FakeLifecycle(calls) { OnClose = cancel.Cancel }).RunAsync(p, () => { }, () => { calls.Add("restore"); return ""; }, null, cancel.Token, value => waiting=value).GetAwaiter().GetResult(); throw new Exception("Expected cancellation"); }
    catch (OperationCanceledException) { Assert(!calls.Contains("restore") && !waiting); }
});
Test("Old settings retain safe defaults", dir =>
{
    var path = Path.Combine(dir, "old.json"); File.WriteAllText(path, "{\"Executable\":\"game.exe\",\"Saves\":\"saves\",\"Library\":\"library\",\"ExtraProcessNames\":\"\"}");
    var p = JsonDisk.Read<Profile>(path); Assert(!p.RequestWindowClose && !p.RestartAfterRestore && p.LaunchPath == "");
});
Test("Script launch rejects inline shell arguments", dir =>
{
    var p = Setup(dir); var bat = Path.Combine(dir, "launch.bat"); File.WriteAllText(bat, "@echo off");
    var configured = p with { LaunchPath = bat, LaunchArguments = "anything & bad", WorkingDirectory = dir };
    Fails(() => new WindowsGameLifecycle().ValidateLaunch(configured)); new WindowsGameLifecycle().ValidateLaunch(configured with { LaunchArguments = "" });
});
if (!args.Contains("--skip-live-pipes") && OperatingSystem.IsWindows() && string.Equals(Path.GetFileName(Environment.ProcessPath), "Checkpoint.Tests.exe", StringComparison.OrdinalIgnoreCase))
{
    Test("Observer connection cannot acquire capture or restore despite a responsive pipe", dir =>
    {
        var p=Setup(dir); using var server=new FakeBridgeServer(p.Saves) { Capture=false,Restore=false };
        using var bridge=LiveBridgeClient.ConnectAsync(p,default,observationOnly:true).GetAwaiter().GetResult();
        Assert(!bridge.CanCapture && !bridge.CanRestore);
        Fails(()=>bridge.AcquireAsync(false,null,default).GetAwaiter().GetResult());
        Fails(()=>bridge.AcquireAsync(true,null,default).GetAwaiter().GetResult());
        Assert(!server.Calls.Contains("acquire"));
    });
    Test("Normal checkpoint connection rejects an observer without publishing a checkpoint", dir =>
    {
        var p=Setup(dir); using var server=new FakeBridgeServer(p.Saves) { Capture=false,Restore=false };
        Fails(()=>LiveBridgeClient.ConnectAsync(p,default).GetAwaiter().GetResult());
        Assert(new CheckpointStore(p,new FakeGuard()).List().Length==0 && !server.Calls.Contains("acquire"));
    });
    Test("Live capture requires and checks a native lease over a real pipe", dir =>
    {
        var p = Setup(dir); using var server = new FakeBridgeServer(p.Saves);
        using var bridge = LiveBridgeClient.ConnectAsync(p, default).GetAwaiter().GetResult();
        bridge.AcquireAsync(false, null, default).GetAwaiter().GetResult();
        var cp = new CheckpointStore(p, bridge).Create("Live capture");
        Assert(cp.Files.Length > 0 && server.Calls.Contains("check")); bridge.Complete();
        Assert(server.Calls.Contains("release"));
    });
    Test("Live bridge mismatched source rejected before copying", dir =>
    {
        var p = Setup(dir); using var server = new FakeBridgeServer(Path.Combine(dir, "wrong"));
        Fails(() => LiveBridgeClient.ConnectAsync(p, default).GetAwaiter().GetResult()); Assert(!Directory.Exists(p.Library));
    });
    Test("Live restore marks mutation before directory changes and releases on success", dir =>
    {
        var p = Setup(dir); var cp = new CheckpointStore(p, new FakeGuard()).Create("Live restore");
        File.WriteAllText(Path.Combine(p.Saves, "new.txt"), "later");
        using var server = new FakeBridgeServer(p.Saves); using var bridge = LiveBridgeClient.ConnectAsync(p, default).GetAwaiter().GetResult();
        bridge.AcquireAsync(true, null, default).GetAwaiter().GetResult();
        var store = new CheckpointStore(p, bridge, phase => { if (phase == "Prepared") bridge.BeginMutation(); });
        store.Restore(cp.Id); bridge.Complete();
        Assert(server.Calls.Contains("begin-mutation") && server.Calls.Last() == "release" && !File.Exists(Path.Combine(p.Saves,"new.txt")));
    });
    Test("Old bridge without input barrier cannot begin live restore", dir =>
    {
        var p=Setup(dir); using var server=new FakeBridgeServer(p.Saves) { InputBarrier=false };
        using var bridge=LiveBridgeClient.ConnectAsync(p,default).GetAwaiter().GetResult();
        Fails(() => bridge.AcquireAsync(true,null,default).GetAwaiter().GetResult());
        Assert(!server.Calls.Contains("acquire"));
    });
    Test("Restore lease without blocked input is rejected before mutation", dir =>
    {
        var p=Setup(dir); using var server=new FakeBridgeServer(p.Saves) { InputBlocked=false };
        using var bridge=LiveBridgeClient.ConnectAsync(p,default).GetAwaiter().GetResult();
        Fails(() => bridge.AcquireAsync(true,null,default).GetAwaiter().GetResult());
        Assert(!server.Calls.Contains("begin-mutation"));
    });
    Test("Interrupted live restore disconnects without releasing mutation barrier", dir =>
    {
        var p = Setup(dir); var cp = new CheckpointStore(p, new FakeGuard()).Create("test");
        using var server = new FakeBridgeServer(p.Saves);
        using (var bridge = LiveBridgeClient.ConnectAsync(p, default).GetAwaiter().GetResult())
        {
            bridge.AcquireAsync(true, null, default).GetAwaiter().GetResult();
            var store = new CheckpointStore(p, bridge, phase => { if (phase == "Prepared") bridge.BeginMutation(); if (phase == "OriginalMoved") throw new IOException("crash"); });
            Fails(() => store.Restore(cp.Id));
        }
        Assert(!server.Calls.Contains("release")); new CheckpointStore(p, new FakeGuard()).Recover();
    });
    Test("Lost live barrier blocks snapshot publication", dir =>
    {
        var p = Setup(dir); using var server = new FakeBridgeServer(p.Saves) { RejectChecks = true };
        using var bridge = LiveBridgeClient.ConnectAsync(p, default).GetAwaiter().GetResult(); bridge.AcquireAsync(false, null, default).GetAwaiter().GetResult();
        Fails(() => new CheckpointStore(p, bridge).Create("blocked"));
        Assert(new CheckpointStore(p, new FakeGuard()).List().Length == 0);
    });
}
else Console.WriteLine("SKIP real-pipe client tests: explicitly skipped or not running through the isolated Checkpoint.Tests.exe apphost.");
Console.WriteLine($"{passed} tests passed. Fixtures preserved at: {root}");

sealed class FakeGuard : IGameGuard
{
    public bool Running { get; set; }
    public void EnsureClosed(Profile profile) { if (Running) throw new IOException("Game running"); }
}

sealed class FakeLifecycle(List<string> calls) : IGameLifecycle
{
    public bool FailValidation, FailClose, FailLaunch;
    public Action? OnClose;
    public void ValidateLaunch(Profile p) { calls.Add("validate"); if (FailValidation) throw new IOException("bad launch"); }
    public Task StopAndWaitAsync(Profile p, IProgress<string>? progress, CancellationToken token)
    { calls.Add("close"); if (FailClose) throw new IOException("timeout"); OnClose?.Invoke(); return Task.CompletedTask; }
    public Task LaunchAndWaitAsync(Profile p, IProgress<string>? progress, CancellationToken token)
    { calls.Add("launch"); if (FailLaunch) throw new IOException("launch failed"); return Task.CompletedTask; }
}

sealed class FakeBridgeServer : IDisposable
{
    public bool InputBarrier=true, InputBlocked=true;
    public bool Capture=true, Restore=true;
    private readonly NamedPipeServerStream pipe;
    private readonly Task worker;
    private readonly CancellationTokenSource cancellation = new();
    public readonly List<string> Calls = [];
    public bool RejectChecks;
    public FakeBridgeServer(string source)
    {
        pipe = new NamedPipeServerStream("bcm-checkpoint-v1-" + Environment.ProcessId, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        worker = Task.Run(async () =>
        {
            try
            {
                await pipe.WaitForConnectionAsync(cancellation.Token);
                using var reader = new StreamReader(pipe, leaveOpen: true); using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                var session = Guid.NewGuid().ToString(); var lease = Guid.NewGuid().ToString();
                while (await reader.ReadLineAsync(cancellation.Token) is { } line)
                {
                    using var request = JsonDocument.Parse(line); var op = request.RootElement.GetProperty("op").GetString()!; Calls.Add(op);
                    object response = op switch
                    {
                        "hello" => new { ok=true, protocol=1, pid=Environment.ProcessId, source, session, capture=Capture, restore=Restore, inputBarrier=InputBarrier },
                        "acquire" => new { ok=true, lease, source, frozen=true, title=true, inputBlocked=InputBlocked },
                        "check" when RejectChecks => new { ok=false, error="lease lost" },
                        _ => new { ok=true, frozen=op != "release" }
                    };
                    await writer.WriteLineAsync(JsonSerializer.Serialize(response));
                }
            }
            catch (OperationCanceledException) { } catch (IOException) { }
        });
    }
    public void Dispose() { cancellation.Cancel(); pipe.Dispose(); try { worker.GetAwaiter().GetResult(); } catch (ObjectDisposedException) { } cancellation.Dispose(); }
}

static class AdaptiveFixture
{
    public sealed record Fixture(byte[] Bytes,string Contract,int SymbolOffset);
    public static Fixture Create(uint codeRva,uint dataRva,bool publicPadAbi=false)
    {
        var bytes=new byte[0x92000];
        void U16(int at,ushort value)=>BitConverter.GetBytes(value).CopyTo(bytes,at);
        void U32(int at,uint value)=>BitConverter.GetBytes(value).CopyTo(bytes,at);
        void U64(int at,ulong value)=>BitConverter.GetBytes(value).CopyTo(bytes,at);
        void Name(int at,string name)=>System.Text.Encoding.ASCII.GetBytes(name).CopyTo(bytes,at);
        U16(0,0x5a4d); U32(60,0x80); U32(0x80,0x4550); U16(0x84,0x8664); U16(0x86,2); U16(0x94,0xf0);
        U16(0x98,0x20b); U64(0xb0,0x140000000); U32(0xd0,dataRva+0x90000);
        void Section(int at,uint rva,uint size,uint raw,uint flags)
        { U32(at+8,size); U32(at+12,rva); U32(at+16,size); U32(at+20,raw); U32(at+36,flags); }
        Section(0x188,codeRva,0x1000,0x200,0x60000020); Section(0x1b0,dataRva,0x90000,0x1200,0xc0000040);
        bytes[0x210]=0x48; bytes[0x211]=0x8d; bytes[0x212]=5; bytes[0x217]=0xc3;
        U32(0x213,unchecked(dataRva-(codeRva+0x17)));
        var symbols=new List<(string Name,uint Value,short Section,ushort Type)>{
            ("image",0,2,0),("gpu",8,2,0),("entered",12,2,0),("root",16,2,0),
            ("mutex",0x210,2,0),("files",0x400,2,0),("entry",0x10,1,32),
            ("filetab",0x300,2,0),("savetab",0x320,2,0),("padtab",0x340,2,0)};
        if(publicPadAbi) { symbols.Add(("padimpl",0x80,1,32)); bytes[0x280]=0xc3; }
        const int symbolStart=0x91200; U32(0x8c,symbolStart); U32(0x90,(uint)symbols.Count);
        for(int n=0;n<symbols.Count;n++)
        { var s=symbols[n]; int at=symbolStart+n*18; Name(at,s.Name); U32(at+8,s.Value); U16(at+12,(ushort)s.Section); U16(at+14,s.Type); bytes[at+16]=s.Type==32 ? (byte)2 : (byte)3; }
        U32(symbolStart+symbols.Count*18,4);
        for(int n=0;n<3;n++) { U64(0x1500+n*32,0x140000000UL+dataRva+0x360u+(uint)n*16); U64(0x1508+n*32,0x140000000UL+codeRva+0x10); Name(0x1560+n*16,"export"+n); }
        var layout=new Dictionary<string,string>{["image"]="image#0",["gpu_enabled"]="gpu#0",["entered_game"]="entered#0",["user_root"]="root#0",["file_lock"]="mutex#0",["files"]="files#0"};
        foreach(var name in new[]{"pthread_mutex_lock","pthread_mutex_unlock","runtime_thread_attach_main","runtime_file_open","runtime_file_close","runtime_file_read","runtime_file_pread","runtime_file_write","runtime_file_lseek","runtime_file_stat","runtime_file_fstat","runtime_file_getdents"}) layout[name]="entry#0";
        var body=bytes.AsSpan(0x210,8).ToArray(); for(int n=3;n<7;n++) body[n]=0;
        var tables=new Dictionary<string,object>(); int idx=0;
        foreach(var kind in new[]{"file","save","pad"}) { tables[kind]=new { symbol=kind+"tab#0",names=new[]{"export"+idx++},functions=new[]{"entry#0"} }; }
        if(publicPadAbi)
        {
            var names=new[]{"scePadRead","scePadReadState"};
            for(int n=0;n<2;n++) { U64(0x1540+n*16,0x140000000UL+dataRva+0x500u+(uint)n*32); U64(0x1548+n*16,0x140000000UL+codeRva+0x80); Name(0x1700+n*32,names[n]); }
            tables["pad"]=new { symbol="padtab#0",names,functions=new[]{"padimpl#0","padimpl#0"} };
        }
        string contract=JsonSerializer.Serialize(new { schema=publicPadAbi ? 2 : 1,inputAbi=publicPadAbi ? "orbis-pad-120-v1" : null,family="synthetic",layout,tables,patterns=new[]{new { symbol="entry#0",length=8,sha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(body)),masks=new[]{3,4,5,6},references=new[]{new { offset=3,next=7,symbol="image#0" }} }} });
        return new(bytes,contract,symbolStart);
    }
}

