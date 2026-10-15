using System.Diagnostics;

namespace Checkpoint.Core;

public interface IGameLifecycle
{
    void ValidateLaunch(Profile profile);
    Task StopAndWaitAsync(Profile profile, IProgress<string>? progress, CancellationToken cancellation);
    Task LaunchAndWaitAsync(Profile profile, IProgress<string>? progress, CancellationToken cancellation);
}

public sealed record RestoreResult(string Original, bool Restarted, string Message);

public sealed class RestoreWorkflow(IGameLifecycle lifecycle)
{
    public async Task<RestoreResult> RunAsync(Profile profile, Action preflight, Func<string> restore,
        IProgress<string>? progress, CancellationToken cancellation, Action<bool>? waitingChanged = null)
    {
        if (profile.RestartAfterRestore) lifecycle.ValidateLaunch(profile);
        progress?.Report(TextCatalog.T("progress.verify"));
        await Task.Run(preflight, cancellation);
        waitingChanged?.Invoke(true);
        try { await lifecycle.StopAndWaitAsync(profile, progress, cancellation); }
        finally { waitingChanged?.Invoke(false); }
        cancellation.ThrowIfCancellationRequested();
        progress?.Report(TextCatalog.T("progress.restore"));
        // Do not interrupt the directory transaction halfway through.
        var original = await Task.Run(restore);
        if (!profile.RestartAfterRestore || cancellation.IsCancellationRequested)
            return new(original, false, TextCatalog.T("status.restored_closed"));
        try
        {
            waitingChanged?.Invoke(true);
            try { await lifecycle.LaunchAndWaitAsync(profile, progress, cancellation); }
            finally { waitingChanged?.Invoke(false); }
            return new(original, true, TextCatalog.T("status.restored_started"));
        }
        catch (Exception ex)
        {
            return new(original, false, TextCatalog.T("status.launch_failed") + ex.Message);
        }
    }
}

public sealed class WindowsGameLifecycle : IGameLifecycle
{
    private static bool Closed(Profile p)
    {
        try { new ProcessGuard().EnsureClosed(p); return true; }
        catch (GameRunningException) { return false; }
    }

    public void ValidateLaunch(Profile p)
    {
        if (string.IsNullOrWhiteSpace(p.LaunchPath) || !Path.IsPathFullyQualified(p.LaunchPath) || !File.Exists(p.LaunchPath))
            throw new IOException(TextCatalog.T("error.launch_file"));
        if (string.IsNullOrWhiteSpace(p.WorkingDirectory) || !Path.IsPathFullyQualified(p.WorkingDirectory) || !Directory.Exists(p.WorkingDirectory))
            throw new IOException(TextCatalog.T("error.launch_directory"));
        var extension = Path.GetExtension(p.LaunchPath).ToLowerInvariant();
        if (extension is not (".exe" or ".bat" or ".cmd")) throw new IOException(TextCatalog.T("error.launch_extension"));
        if (extension != ".exe" && !string.IsNullOrWhiteSpace(p.LaunchArguments))
            throw new IOException(TextCatalog.T("error.script_arguments"));
    }

    private static Process[] ExactProcesses(Profile p)
    {
        var candidates = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(p.Executable));
        var matches = new List<Process>();
        try
        {
            foreach (var process in candidates)
            {
                if (process.HasExited) continue;
                string path;
                try { path = process.MainModule?.FileName ?? throw new IOException(TextCatalog.T("error.process_path")); }
                catch (InvalidOperationException) when (process.HasExited) { continue; }
                if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(p.Executable), StringComparison.OrdinalIgnoreCase)) matches.Add(process);
            }
        }
        catch { foreach (var process in candidates) process.Dispose(); throw; }
        foreach (var process in candidates.Except(matches)) process.Dispose();
        return matches.ToArray();
    }

    public async Task StopAndWaitAsync(Profile p, IProgress<string>? progress, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!Closed(p) && p.RequestWindowClose)
        {
            var matches = ExactProcesses(p);
            try
            {
                if (matches.Length != 1) throw new IOException(TextCatalog.T("error.exact_process"));
                progress?.Report(TextCatalog.T("progress.close_request"));
                if (!matches[0].CloseMainWindow()) throw new IOException(TextCatalog.T("error.close_rejected"));
            }
            finally { foreach (var process in matches) process.Dispose(); }
        }
        var timer = Stopwatch.StartNew();
        TimeSpan? closedSince = null;
        while (timer.Elapsed < TimeSpan.FromSeconds(120))
        {
            cancellation.ThrowIfCancellationRequested();
            if (Closed(p))
            {
                closedSince ??= timer.Elapsed;
                if (timer.Elapsed - closedSince.Value >= TimeSpan.FromSeconds(2)) return;
                progress?.Report(TextCatalog.T("progress.quiet"));
            }
            else
            {
                closedSince = null;
                progress?.Report(TextCatalog.T("progress.wait_exit",120-(int)timer.Elapsed.TotalSeconds));
            }
            await Task.Delay(250, cancellation);
        }
        throw new IOException(TextCatalog.T("error.still_running"));
    }

    public async Task LaunchAndWaitAsync(Profile p, IProgress<string>? progress, CancellationToken cancellation)
    {
        ValidateLaunch(p);
        cancellation.ThrowIfCancellationRequested();
        new ProcessGuard().EnsureClosed(p);
        if (File.Exists(Path.Combine(p.Library, "pending-restore.json"))) throw new IOException(TextCatalog.T("error.launch_pending"));
        progress?.Report(TextCatalog.T("progress.launch"));
        var script = !Path.GetExtension(p.LaunchPath).Equals(".exe", StringComparison.OrdinalIgnoreCase);
        var start = new ProcessStartInfo(p.LaunchPath)
        {
            WorkingDirectory = p.WorkingDirectory, Arguments = p.LaunchArguments ?? "", UseShellExecute = script
        };
        using var launched = Process.Start(start) ?? throw new IOException(TextCatalog.T("error.launch_unconfirmed"));
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(60))
        {
            cancellation.ThrowIfCancellationRequested();
            var matches = ExactProcesses(p);
            try
            {
                if (matches.Length > 1) throw new IOException(TextCatalog.T("error.multiple_games"));
                if (matches.Length == 1) return;
            }
            finally { foreach (var process in matches) process.Dispose(); }
            progress?.Report(TextCatalog.T("progress.wait_launch",60-(int)timer.Elapsed.TotalSeconds));
            await Task.Delay(500, cancellation);
        }
        throw new IOException(TextCatalog.T("error.launch_timeout"));
    }
}
