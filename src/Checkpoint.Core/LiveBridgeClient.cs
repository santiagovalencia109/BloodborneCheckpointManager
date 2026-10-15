using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace Checkpoint.Core;

/// <summary>A lease from the opt-in native port bridge, never a bypass of process checks.</summary>
public sealed class LiveBridgeClient : IDisposable, IGameGuard
{
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint pid);
    private readonly NamedPipeClientStream pipe;
    private readonly Profile profile;
    private readonly int pid;
    private string session = "", lease = "";
    private bool mutation, released;
    public bool CanRestore { get; private set; }
    public bool CanCapture { get; private set; }
    private bool inputBarrier;
    public JsonElement Greeting { get; private set; }
    private LiveBridgeClient(Profile profile, int pid)
    {
        this.profile = profile; this.pid = pid;
        pipe = new NamedPipeClientStream(".", "bcm-checkpoint-v1-" + pid, PipeDirection.InOut, PipeOptions.Asynchronous);
    }

    public static async Task<LiveBridgeClient> ConnectAsync(Profile profile, CancellationToken cancellation, bool observationOnly = false)
    {
        if (!OperatingSystem.IsWindows()) throw new IOException(TextCatalog.T("bridge.windows"));
        if (profile.ExternalBridge)
        {
            var game = await ExternalBridge.AttachAsync(profile, Path.Combine(AppContext.BaseDirectory, "Native", "bcm-external.dll"), cancellation).ConfigureAwait(false);
            profile = profile with { Executable = game.Executable };
        }
        var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(profile.Executable));
        var matches = new List<int>();
        try
        {
            foreach (var p in processes)
            {
                if (p.HasExited) continue;
                if (string.Equals(p.MainModule?.FileName, Path.GetFullPath(profile.Executable), StringComparison.OrdinalIgnoreCase)) matches.Add(p.Id);
            }
        }
        finally { foreach (var p in processes) p.Dispose(); }
        if (matches.Count != 1) throw new IOException(TextCatalog.T("bridge.single_instance"));
        var client = new LiveBridgeClient(profile, matches[0]);
        try
        {
            new ProcessGuard().EnsureNoOtherGames(profile, client.pid);
            await client.pipe.ConnectAsync(profile.ExternalBridge ? 25000 : 2500, cancellation).ConfigureAwait(false);
            if (!GetNamedPipeServerProcessId(client.pipe.SafePipeHandle, out var serverPid) || serverPid != client.pid)
                throw new IOException(TextCatalog.T("bridge.pid"));
            var hello = await client.ExchangeAsync(new { op = "hello" }, cancellation).ConfigureAwait(false);
            client.Greeting = hello;
            if (hello.GetProperty("protocol").GetInt32() != 1 || hello.GetProperty("pid").GetInt32() != client.pid)
                throw new IOException(TextCatalog.T("bridge.protocol"));
            client.ValidateSource(hello.GetProperty("source").GetString());
            client.session = hello.GetProperty("session").GetString() ?? "";
            if (!Guid.TryParse(client.session, out _)) throw new IOException(TextCatalog.T("bridge.session"));
            client.CanRestore = hello.GetProperty("restore").GetBoolean();
            client.CanCapture = hello.GetProperty("capture").GetBoolean();
            client.inputBarrier = hello.TryGetProperty("inputBarrier", out var input) && input.GetBoolean();
            if (!observationOnly && !client.CanCapture) throw new IOException(TextCatalog.T("external.observer_only"));
            return client;
        }
        catch (TimeoutException ex) { client.Dispose(); throw new IOException(TextCatalog.T(profile.ExternalBridge ? "bridge.external_inactive" : "bridge.inactive"), ex); }
        catch { client.Dispose(); throw; }
    }

    private void ValidateSource(string? source)
    {
        if (source == null || !string.Equals(SafeTree.Canonical(source), SafeTree.Canonical(profile.Saves), StringComparison.OrdinalIgnoreCase))
            throw new IOException(TextCatalog.T("bridge.source"));
    }

    private async Task<JsonElement> ExchangeAsync(object value, CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(55));
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value) + "\n");
        await pipe.WriteAsync(bytes, timeout.Token).ConfigureAwait(false);
        await pipe.FlushAsync(timeout.Token).ConfigureAwait(false);
        var buffer = new List<byte>(); var one = new byte[1];
        for (;;)
        {
            if (await pipe.ReadAsync(one, timeout.Token).ConfigureAwait(false) != 1) throw new IOException(TextCatalog.T("bridge.disconnected"));
            if (one[0] == 10) break;
            if (buffer.Count >= 8192) throw new IOException(TextCatalog.T("bridge.large_response"));
            buffer.Add(one[0]);
        }
        using var document = JsonDocument.Parse(buffer.ToArray());
        var reply = document.RootElement.Clone();
        if (!reply.GetProperty("ok").GetBoolean()) throw new IOException(TextCatalog.T("bridge.error_prefix") + TextCatalog.ServerMessage(reply.GetProperty("error").GetString() ?? ""));
        return reply;
    }

    public async Task AcquireAsync(bool restoring, IProgress<string>? progress, CancellationToken cancellation)
    {
        if (!restoring && !CanCapture) throw new IOException(TextCatalog.T("external.observer_only"));
        if (restoring && !CanRestore) throw new IOException(TextCatalog.T("bridge.restore_disabled"));
        if (restoring && !inputBarrier) throw new IOException(TextCatalog.T("bridge.input_missing"));
        progress?.Report(restoring ? TextCatalog.T("bridge.returning") : TextCatalog.T("bridge.saving"));
        var reply = await ExchangeAsync(new { op = "acquire", session, mode = restoring ? "restore" : "capture" }, cancellation).ConfigureAwait(false);
        ValidateSource(reply.GetProperty("source").GetString());
        lease = reply.GetProperty("lease").GetString() ?? "";
        if (!Guid.TryParse(lease, out _) || !reply.GetProperty("frozen").GetBoolean() || (restoring && (!reply.GetProperty("title").GetBoolean() || !reply.TryGetProperty("inputBlocked", out var blocked) || !blocked.GetBoolean())))
            throw new IOException(TextCatalog.T("bridge.unsafe"));
    }

    public void EnsureClosed(Profile ignored)
    {
        if (lease.Length == 0 || released) throw new IOException(TextCatalog.T("bridge.no_lease"));
        new ProcessGuard().EnsureNoOtherGames(profile, pid);
        var reply = ExchangeAsync(new { op = "check", session, lease }, CancellationToken.None).GetAwaiter().GetResult();
        if (!reply.GetProperty("frozen").GetBoolean()) throw new IOException(TextCatalog.T("bridge.barrier_lost"));
    }

    public void BeginMutation()
    {
        ExchangeAsync(new { op = "begin-mutation", session, lease }, CancellationToken.None).GetAwaiter().GetResult();
        mutation = true;
    }

    public void Complete()
    {
        ExchangeAsync(new { op = "release", session, lease }, CancellationToken.None).GetAwaiter().GetResult();
        released = true; mutation = false;
    }

    public void Dispose()
    {
        if (!released && !mutation && lease.Length > 0 && pipe.IsConnected)
        {
            try { Complete(); } catch (Exception) { /* Capture lease expires/disconnects without mutating saves. */ }
        }
        // An interrupted restore deliberately leaves native I/O frozen until the game is closed.
        pipe.Dispose();
    }
}
