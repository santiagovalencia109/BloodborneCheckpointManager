namespace Checkpoint.Core;

public sealed record BatchDeleteResult(int Deleted,string[] Residuals,string? Error);

public sealed class CheckpointStore
{
    private readonly Profile profile;
    private readonly IGameGuard guard;
    private readonly Action<string>? fault;
    private string JournalPath => Path.Combine(profile.Library, "pending-restore.json");

    public CheckpointStore(Profile profile, IGameGuard guard, Action<string>? fault = null)
    {
        this.profile = profile with { Saves = SafeTree.Canonical(profile.Saves), Library = SafeTree.Canonical(profile.Library) };
        if (SafeTree.Within(this.profile.Saves, this.profile.Library) || SafeTree.Within(this.profile.Library, this.profile.Saves))
            throw new IOException(TextCatalog.T("error.overlap"));
        this.guard = guard;
        this.fault = fault;
    }

    private IDisposable Lock(bool recovering = false)
    {
        SafeTree.NoLinks(profile.Library);
        SafeTree.NoLinks(profile.Saves);
        Directory.CreateDirectory(profile.Library);
        SafeTree.NoLinks(Path.Combine(profile.Library, ".operation.lock"));
        SafeTree.NoLinks(JournalPath);
        var handle = new FileStream(Path.Combine(profile.Library, ".operation.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            if (!recovering && File.Exists(JournalPath))
                throw new IOException(TextCatalog.T("error.pending_restore"));
            var targetLock = profile.Saves + ".bcm.lock";
            SafeTree.NoLinks(targetLock);
            return new Lease(handle, new FileStream(targetLock, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
        }
        catch { handle.Dispose(); throw; }
    }

    private sealed class Lease(FileStream libraryLock, FileStream targetLock) : IDisposable
    {
        public void Dispose() { targetLock.Dispose(); libraryLock.Dispose(); }
    }

    private static string Id(string id) => Guid.TryParseExact(id, "N", out _) ? id : throw new IOException(TextCatalog.T("error.id"));
    private string Folder(string id) => SafeTree.Child(profile.Library, Id(id));
    private Manifest Read(string id)
    {
        var m = JsonDisk.Read<Manifest>(SafeTree.Child(Folder(id), "manifest.json"));
        if (m.Version != 1 || m.Id != id || m.Files == null || m.Files.Length == 0 ||
            m.Files.Select(e => e.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != m.Files.Length)
            throw new IOException(TextCatalog.T("error.manifest"));
        foreach (var e in m.Files) _ = SafeTree.Child(Path.Combine(Folder(id), "data"), e.Path);
        return m;
    }

    public Manifest[] List()
    {
        using var handle = Lock(true);
        return Directory.EnumerateDirectories(profile.Library).Where(d => Guid.TryParseExact(Path.GetFileName(d), "N", out _))
            .Select(d => Read(Path.GetFileName(d))).Where(m => string.Equals(m.Source, profile.Saves, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(m => m.Created).ToArray();
    }

    public bool HasPendingRestore => File.Exists(JournalPath);

    // Read-only preflight before requesting game closure. Restore repeats all checks under lock.
    public void ValidateRestore(string id)
    {
        using var handle = Lock();
        var m = Read(id);
        if (!string.Equals(m.Source, profile.Saves, StringComparison.OrdinalIgnoreCase))
            throw new IOException(TextCatalog.T("error.profile"));
        SafeTree.ValidateLayout(m.Files);
        SafeTree.Equal(m.Files, SafeTree.Scan(SafeTree.Child(Folder(id), "data")));
    }

    private Manifest Snapshot(string name, bool emergency)
    {
        guard.EnsureClosed(profile);
        var files = SafeTree.Scan(profile.Saves);
        // Emergency backups must preserve even a damaged current save for forensic recovery.
        if (!emergency) SafeTree.ValidateLayout(files);
        if (files.Length == 0) throw new IOException(TextCatalog.T("error.no_files"));
        var id = Guid.NewGuid().ToString("N");
        var stage = SafeTree.Child(profile.Library, ".partial-" + id);
        SafeTree.Copy(profile.Saves, Path.Combine(stage, "data"), files);
        guard.EnsureClosed(profile);
        var m = new Manifest(1, id, CleanName(name), DateTimeOffset.UtcNow, profile.Saves, emergency, files);
        JsonDisk.Write(Path.Combine(stage, "manifest.json"), m);
        Directory.Move(stage, Folder(id));
        return m;
    }

    private static string CleanName(string name) => !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= 120
        ? name.Trim() : throw new IOException(TextCatalog.T("error.name"));

    public Manifest Create(string name)
    {
        using var handle = Lock();
        return Snapshot(name, false);
    }

    public void Rename(string id, string name)
    {
        using var handle = Lock();
        var m = Read(id);
        JsonDisk.Write(Path.Combine(Folder(id), "manifest.json"), m with { Name = CleanName(name) });
    }

    public string? Delete(string id)
    {
        var result=DeleteMany([id]);
        if(result.Error!=null) throw new IOException(result.Error);
        return result.Residuals.FirstOrDefault();
    }

    public BatchDeleteResult DeleteMany(IEnumerable<string> checkpointIds)
    {
        var ids=checkpointIds.Select(Id).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if(ids.Length==0) return new(0,[],null);
        using var handle=Lock();
        // Validate the complete selection before unpublishing any checkpoint.
        var folders=ids.Select(id=>
        {
            var manifest=Read(id);
            if(!string.Equals(manifest.Source,profile.Saves,StringComparison.OrdinalIgnoreCase))
                throw new IOException(TextCatalog.T("error.profile"));
            var folder=Folder(id); _=SafeTree.Scan(folder);
            return(id,folder);
        }).ToArray();
        int deleted=0; var residuals=new List<string>();
        foreach(var (id,folder) in folders)
        {
            var removed=SafeTree.Child(profile.Library,".deleted-"+Id(id)+"-"+Guid.NewGuid().ToString("N"));
            try { fault?.Invoke("BeforeDeleteUnpublish"); Directory.Move(folder,removed); }
            catch(Exception error) when(error is IOException or UnauthorizedAccessException)
            { return new(deleted,residuals.ToArray(),error.Message); }
            deleted++;
            try
            {
                fault?.Invoke("DeleteUnpublished");
                _=SafeTree.Scan(removed);
                Directory.Delete(removed,recursive:true);
            }
            catch(Exception error) when(error is IOException or UnauthorizedAccessException) { residuals.Add(removed); }
        }
        return new(deleted,residuals.ToArray(),null);
    }

    public string Restore(string id)
    {
        using var handle = Lock();
        guard.EnsureClosed(profile);
        var m = Read(id);
        if (!string.Equals(m.Source, profile.Saves, StringComparison.OrdinalIgnoreCase))
            throw new IOException(TextCatalog.T("error.profile"));
        SafeTree.ValidateLayout(m.Files);
        var data = SafeTree.Child(Folder(id), "data");
        SafeTree.Equal(m.Files, SafeTree.Scan(data));
        var backupName=TextCatalog.T("backup.name",m.Name[..Math.Min(m.Name.Length,80)]);
        var backup = Snapshot(backupName[..Math.Min(backupName.Length,120)], true);
        var token = Guid.NewGuid().ToString("N");
        var prefix = profile.Saves + ".bcm-" + token;
        var journal = new RestoreJournal(profile.Saves, prefix + ".stage", prefix + ".original", prefix + ".displaced", id);
        SafeTree.Copy(data, journal.Stage, m.Files);
        guard.EnsureClosed(profile);
        SafeTree.Equal(backup.Files, SafeTree.Scan(profile.Saves));
        JsonDisk.Write(JournalPath, journal); // Durable intent before either directory rename.
        fault?.Invoke("Prepared");
        try
        {
            guard.EnsureClosed(profile);
            Directory.Move(profile.Saves, journal.Original);
            fault?.Invoke("OriginalMoved");
            guard.EnsureClosed(profile);
            Directory.Move(journal.Stage, profile.Saves);
            fault?.Invoke("Installed");
            SafeTree.Equal(m.Files, SafeTree.Scan(profile.Saves));
            guard.EnsureClosed(profile);
            File.Delete(JournalPath);
        }
        catch (Exception ex)
        {
            throw new IOException(TextCatalog.T("error.interrupted"), ex);
        }
        return journal.Original;
    }

    public void Recover()
    {
        using var handle = Lock(true);
        guard.EnsureClosed(profile);
        if (!File.Exists(JournalPath)) throw new IOException(TextCatalog.T("error.no_pending"));
        var j = JsonDisk.Read<RestoreJournal>(JournalPath);
        if (!string.Equals(j.Target, profile.Saves, StringComparison.OrdinalIgnoreCase))
            throw new IOException(TextCatalog.T("error.recovery_target") + j.Target);
        var prefix = profile.Saves + ".bcm-";
        if (!j.Stage.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !j.Stage.EndsWith(".stage") ||
            !Guid.TryParseExact(j.Stage[prefix.Length..^6], "N", out _) ||
            j.Original != j.Stage[..^6] + ".original" || j.Displaced != j.Stage[..^6] + ".displaced")
            throw new IOException(TextCatalog.T("error.journal"));
        foreach (var p in new[] { j.Target, j.Stage, j.Original, j.Displaced }) SafeTree.NoLinks(p);
        if (Directory.Exists(j.Original))
        {
            guard.EnsureClosed(profile);
            if (Directory.Exists(j.Target)) Directory.Move(j.Target, j.Displaced);
            Directory.Move(j.Original, j.Target);
        }
        else if (!Directory.Exists(j.Target)) throw new IOException(TextCatalog.T("error.recovery_missing"));
        File.Delete(JournalPath);
    }
}
