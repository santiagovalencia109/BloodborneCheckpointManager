using Checkpoint.Core;
if(args.Length==2 && args[0]=="read-diagnostics") { ReadDiagnostics.Inspect(args[1]); return; }
if(args.Length is 2 or 3 && args[0]=="inspect-runtime")
{
    try { var r=ExternalBridge.ResolveRuntime(args[1],args.Length==3 ? args[2] : Ports.Supermedo); Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(r)); }
    catch(IOException error) { Console.Error.WriteLine("Runtime compatibility: "+error.Message); Environment.ExitCode=2; }
    return;
}


if(args.Length==2 && args[0]=="test-runtime-contract")
{
    var data=File.ReadAllBytes(args[1]); var original=RuntimeContract.ResolveBytes(data);
    var unrelated=data.Concat(new byte[]{1,2,3,4,5}).ToArray();
    var changed=RuntimeContract.ResolveBytes(unrelated);
    if(changed.Sha256==original.Sha256 || !changed.Layout.SequenceEqual(original.Layout)) throw new Exception("Unrelated update failed.");
    var headers=new System.Reflection.PortableExecutable.PEReader(new MemoryStream(data)).PEHeaders;
    var function=original.Code.First(); var section=headers.SectionHeaders.Single(s=>function.Rva>=s.VirtualAddress && function.Rva<s.VirtualAddress+s.SizeOfRawData);
    int fileOffset=checked(section.PointerToRawData+(int)function.Rva-section.VirtualAddress);
    int instruction=Enumerable.Range(0,function.Length).First(i=>!function.Masks.Contains(i));
    var altered=data.ToArray(); altered[fileOffset+instruction]^=1;
    bool refused=false; try { RuntimeContract.ResolveBytes(altered); } catch(IOException) { refused=true; }
    if(!refused) throw new Exception("Changed ABI was accepted.");
    Console.WriteLine($"PASS actual runtime: {original.Code.Count} critical functions; unrelated whole-file update accepted, critical opcode change rejected."); return;
}
if (args.Length < 2) throw new ArgumentException("Usage: Checkpoint.LiveProbe profile.json hello|capture|restore-latest");
var profile = JsonDisk.Read<Profile>(Path.GetFullPath(args[0]));
if (args[1] == "watch")
{
    using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(60));
    while (ExternalBridge.Discover().Length == 0) await Task.Delay(50, wait.Token);
    using var watcher = await LiveBridgeClient.ConnectAsync(profile, wait.Token, observationOnly: true);
    Console.WriteLine(watcher.Greeting.GetRawText()); return;
}
if (args[1] == "prepare-from-checkpoint")
{
    if (args.Length != 4) throw new ArgumentException("Pass new fixture directory and native executable.");
    var output = SafeTree.Canonical(args[2]);
    if (Directory.Exists(output)) throw new IOException("Fixture already exists; use a new directory.");
    var library = SafeTree.Canonical(profile.Library);
    var checkpoint = Directory.EnumerateDirectories(library)
        .Where(path => Guid.TryParseExact(Path.GetFileName(path), "N", out _))
        .Select(path => JsonDisk.Read<Manifest>(SafeTree.Child(path, "manifest.json")))
        .Where(cp => cp.Version == 1 && !cp.Emergency && string.Equals(cp.Source, profile.Saves, StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(cp => cp.Created).FirstOrDefault() ?? throw new IOException("No existing manual checkpoint.");
    var data = SafeTree.Child(library, checkpoint.Id + "/data");
    SafeTree.ValidateLayout(checkpoint.Files); SafeTree.Equal(checkpoint.Files, SafeTree.Scan(data));
    var saves = Path.Combine(output, "user", "savedata");
    SafeTree.Copy(data, saves, checkpoint.Files);
    var isolated = profile with { Executable = args[3], Saves = saves, Library = Path.Combine(output, "checkpoints"),
        LaunchPath = "", LaunchArguments = "", WorkingDirectory = output, RestartAfterRestore = false, RequestWindowClose = false, LiveCheckpoints = true };
    JsonDisk.Write(Path.Combine(output, "settings.json"), isolated);
    File.WriteAllText(Path.Combine(output, "pad.txt"), "");
    File.WriteAllText(Path.Combine(output, "bbport.ini"), "upscaler=fsr3\npreset=0\noutput_res=1920x1080\nshow_fps=1\nskip_intro=1\n");
    Console.WriteLine("Fixture prepared from immutable checkpoint " + checkpoint.Id + ": " + output);
    return;
}
using var bridge = await LiveBridgeClient.ConnectAsync(profile, CancellationToken.None, observationOnly: args[1] == "hello");
Console.WriteLine(bridge.Greeting.GetRawText());
var progress = new Progress<string>(Console.WriteLine);
if (args[1] == "hello") return;
if (args[1] == "capture")
{
    await bridge.AcquireAsync(false, progress, CancellationToken.None);
    var checkpoint = new CheckpointStore(profile, bridge).Create("Live probe " + DateTime.Now.ToString("HH-mm-ss"));
    bridge.Complete(); Console.WriteLine("CAPTURED " + checkpoint.Id); return;
}
if (args[1] == "restore-latest" || args[1] == "restore-held-cross")
{
    var before = new CheckpointStore(profile, new ProcessGuard());
    var checkpoint = before.List().FirstOrDefault(cp => !cp.Emergency) ?? throw new IOException("No checkpoint.");
    before.ValidateRestore(checkpoint.Id);
    var acquire = bridge.AcquireAsync(true, progress, CancellationToken.None);
    if (args[1] == "restore-held-cross")
    {
        var pad=SafeTree.Child(SafeTree.Canonical(profile.WorkingDirectory),"pad.txt");
        if (!File.Exists(pad)) throw new IOException("Held-input test needs an existing isolated pad file.");
        await Task.Delay(1000);
        File.WriteAllText(pad,"cross");
        Console.WriteLine("TEST: Cross held during restoration; release pad.txt after verifying title.");
        try { await acquire; } catch { File.WriteAllText(pad,""); throw; }
    }
    else await acquire;
    var store = new CheckpointStore(profile, bridge, phase => { if (phase == "Prepared") bridge.BeginMutation(); });
    var original = store.Restore(checkpoint.Id);
    bridge.Complete(); Console.WriteLine("RESTORED " + checkpoint.Id + " ORIGINAL " + original); return;
}
throw new ArgumentException("Unknown operation.");
