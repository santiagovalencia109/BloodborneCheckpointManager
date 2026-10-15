using System.Diagnostics;
using System.Text.Json;

namespace Checkpoint.Core;

public sealed record Profile(string Executable, string Saves, string Library, string ExtraProcessNames = "",
    string LaunchPath = "", string LaunchArguments = "", string WorkingDirectory = "",
    bool RestartAfterRestore = false, bool RequestWindowClose = false, bool LiveCheckpoints = false,
    bool ExternalBridge = false)
{
    public Profile ForSupportedManagerMode()=>this with { ExternalBridge=LiveCheckpoints };
}
public sealed record Entry(string Path, long Size, string Sha256);
public sealed record Manifest(int Version, string Id, string Name, DateTimeOffset Created, string Source, bool Emergency, Entry[] Files);
public sealed record RestoreJournal(string Target, string Stage, string Original, string Displaced, string CheckpointId);

public static class JsonDisk
{
    public static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path)) ?? throw new IOException(TextCatalog.T("error.empty_json") + path);
    public static void Write<T>(string path, T value)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, value, new JsonSerializerOptions { WriteIndented = true });
            stream.Flush(true);
        }
        if (File.Exists(path)) File.Replace(temp, path, null);
        else File.Move(temp, path);
    }
}

public interface IGameGuard { void EnsureClosed(Profile profile); }
public sealed class GameRunningException(string message) : IOException(message);

public sealed class ProcessGuard : IGameGuard
{
    public void EnsureClosed(Profile profile)
        => EnsureNoOtherGames(profile, null);

    public void EnsureNoOtherGames(Profile profile, int? allowedProcessId)
    {
        if (profile.ExternalBridge)
            foreach (var game in ExternalBridge.Discover())
                if (game.Pid != allowedProcessId) throw new GameRunningException(TextCatalog.T("error.running", Path.GetFileNameWithoutExtension(game.Executable), game.Pid));
        if (!File.Exists(profile.Executable) || !string.Equals(Path.GetExtension(profile.Executable), ".exe", StringComparison.OrdinalIgnoreCase))
            throw new IOException(TextCatalog.T("error.executable"));
        var names = new[] { profile.Executable, "bbport", "bb-probe" }
            .Concat(profile.ExtraProcessNames.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(name => { var file = Path.GetFileName(name); return file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? file[..^4] : file; })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Name-based blocking deliberately includes other installations and inaccessible binaries.
        var processes = Process.GetProcesses();
        try
        {
            foreach (var p in processes)
            {
                string name;
                try { name = p.ProcessName; }
                catch (InvalidOperationException) { continue; } // Already exited.
                catch (Exception ex) { throw new IOException(TextCatalog.T("error.inspect_process"), ex); }
                if (names.Contains(name) && p.Id != allowedProcessId) throw new GameRunningException(TextCatalog.T("error.running",name,p.Id));
            }
        }
        finally { foreach (var p in processes) p.Dispose(); }
    }
}
