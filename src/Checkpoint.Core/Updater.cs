using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Checkpoint.Core;

public sealed record ReleaseInfo(string Version,string Tag,string Name,string Notes,string PageUrl,string ZipUrl,long ZipSize,string? Sha256);

/// <summary>Checks the project's GitHub releases and installs a newer Windows package.
/// The package must carry GitHub's published SHA-256 asset digest; it is verified before extraction,
/// extracted without path traversal, and applied by a script that keeps a backup and rolls back on failure.</summary>
public static partial class Updater
{
    public const string Owner="santiagovalencia109", Repository="BloodborneCheckpointManager";
    public static readonly string ReleasesApi=$"https://api.github.com/repos/{Owner}/{Repository}/releases?per_page=30";
    public static readonly string ReleasesPage=$"https://github.com/{Owner}/{Repository}/releases";
    private const long MaxPackage=200L*1024*1024, MaxExtracted=600L*1024*1024;
    // Files a manager package must contain to be installed over the current one.
    public static readonly string[] RequiredFiles=["BloodborneCheckpointManager.exe","BloodborneCheckpointManager.dll","Checkpoint.Core.dll",
        "Native/bcm-external.dll","Native/bcm-awaken.dll"];

    [GeneratedRegex(@"^v?(\d+)\.(\d+)\.(\d+)(?:-([0-9A-Za-z.-]+))?$")] private static partial Regex VersionPattern();

    public static bool TryParse(string? text,out (int Major,int Minor,int Patch) version)
    {
        version=default;
        var match=VersionPattern().Match(text?.Trim() ?? "");
        if(!match.Success) return false;
        version=(int.Parse(match.Groups[1].Value),int.Parse(match.Groups[2].Value),int.Parse(match.Groups[3].Value));
        return true;
    }
    /// <summary>Numeric comparison; the "-experimental" label is shared by every release and is ignored.</summary>
    public static int Compare(string a,string b)
    {
        if(!TryParse(a,out var x) || !TryParse(b,out var y)) throw new ArgumentException("Invalid version.");
        return x.CompareTo(y);
    }
    public static string Normalize(string version)=>version.Trim().TrimStart('v','V');

    /// <summary>Newest non-draft release, newer than <paramref name="current"/>, that has a Windows package.</summary>
    public static ReleaseInfo? SelectNewest(string releasesJson,string current)
    {
        using var document=JsonDocument.Parse(releasesJson);
        if(document.RootElement.ValueKind!=JsonValueKind.Array) throw new IOException("Unexpected GitHub response.");
        ReleaseInfo? best=null;
        foreach(var release in document.RootElement.EnumerateArray())
        {
            if(release.TryGetProperty("draft",out var draft) && draft.GetBoolean()) continue;
            var tag=release.GetProperty("tag_name").GetString() ?? "";
            if(!TryParse(tag,out _)) continue;
            var version=Normalize(tag);
            if(Compare(version,current)<=0 || (best!=null && Compare(version,best.Version)<=0)) continue;
            var assetName=$"BloodborneCheckpointManager-v{version}-windows.zip";
            if(!release.TryGetProperty("assets",out var assets)) continue;
            foreach(var asset in assets.EnumerateArray())
            {
                if(!string.Equals(asset.GetProperty("name").GetString(),assetName,StringComparison.OrdinalIgnoreCase)) continue;
                string? sha=null;
                if(asset.TryGetProperty("digest",out var digest) && digest.GetString() is { } d && d.StartsWith("sha256:",StringComparison.OrdinalIgnoreCase))
                    sha=d[7..].ToUpperInvariant();
                var url=asset.GetProperty("browser_download_url").GetString() ?? "";
                if(!url.StartsWith($"https://github.com/{Owner}/{Repository}/releases/download/",StringComparison.OrdinalIgnoreCase)) break;
                best=new(version,tag,release.TryGetProperty("name",out var n) ? n.GetString() ?? tag : tag,
                    release.TryGetProperty("body",out var b) ? b.GetString() ?? "" : "",
                    release.TryGetProperty("html_url",out var h) ? h.GetString() ?? ReleasesPage : ReleasesPage,
                    url,asset.GetProperty("size").GetInt64(),sha);
                break;
            }
        }
        return best;
    }

    public static HttpClient CreateClient(string current)
    {
        var http=new HttpClient { Timeout=TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("BloodborneCheckpointManager",Normalize(current)));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        http.DefaultRequestHeaders.Add("X-GitHub-Api-Version","2022-11-28");
        return http;
    }

    public static async Task<ReleaseInfo?> CheckAsync(HttpClient http,string current,CancellationToken cancellation)
    {
        using var response=await http.GetAsync(ReleasesApi,cancellation).ConfigureAwait(false);
        if(!response.IsSuccessStatusCode) throw new IOException($"GitHub returned {(int)response.StatusCode} {response.ReasonPhrase}.");
        return SelectNewest(await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false),current);
    }

    /// <summary>Downloads and verifies the package, then extracts it to a fresh staging folder that is returned.</summary>
    public static async Task<string> DownloadAsync(HttpClient http,ReleaseInfo release,string updatesDirectory,IProgress<int>? progress,CancellationToken cancellation)
    {
        if(release.Sha256 is not { Length:64 }) throw new UpdateException("update.no_checksum");
        if(release.ZipSize<=0 || release.ZipSize>MaxPackage) throw new IOException("Unexpected update size.");
        var folder=Path.Combine(updatesDirectory,release.Version);
        if(Directory.Exists(folder)) Directory.Delete(folder,true);
        Directory.CreateDirectory(folder);
        var zip=Path.Combine(folder,"package.zip");
        using(var response=await http.GetAsync(release.ZipUrl,HttpCompletionOption.ResponseHeadersRead,cancellation).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            await using var input=await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
            await using var output=new FileStream(zip,FileMode.CreateNew,FileAccess.Write,FileShare.None);
            var buffer=new byte[81920]; long total=0; int read, last=-1;
            while((read=await input.ReadAsync(buffer,cancellation).ConfigureAwait(false))>0)
            {
                total+=read; if(total>release.ZipSize) throw new IOException("Update is larger than published.");
                await output.WriteAsync(buffer.AsMemory(0,read),cancellation).ConfigureAwait(false);
                var percent=(int)(total*100/release.ZipSize); if(percent!=last) { last=percent; progress?.Report(percent); }
            }
            if(total!=release.ZipSize) throw new IOException("Update download is incomplete.");
        }
        string hash;
        await using(var file=File.OpenRead(zip)) hash=Convert.ToHexString(await SHA256.HashDataAsync(file,cancellation).ConfigureAwait(false));
        if(hash!=release.Sha256) throw new IOException("Update checksum does not match the published release.");
        var staging=Path.Combine(folder,"package");
        ExtractSafely(zip,staging);
        ValidatePackage(staging);
        return staging;
    }

    public static void ExtractSafely(string zip,string destination)
    {
        var root=Path.GetFullPath(destination)+Path.DirectorySeparatorChar;
        Directory.CreateDirectory(root);
        using var archive=ZipFile.OpenRead(zip);
        long total=0;
        foreach(var entry in archive.Entries)
        {
            var target=Path.GetFullPath(Path.Combine(root,entry.FullName));
            if(!target.StartsWith(root,StringComparison.OrdinalIgnoreCase)) throw new IOException("Update package contains an unsafe path.");
            if(entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) { Directory.CreateDirectory(target); continue; }
            total+=entry.Length; if(total>MaxExtracted) throw new IOException("Update package is too large.");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target,overwrite:false);
        }
    }

    public static void ValidatePackage(string directory)
    {
        var missing=RequiredFiles.FirstOrDefault(f=>!File.Exists(Path.Combine(directory,f.Replace('/',Path.DirectorySeparatorChar))));
        if(missing!=null) throw new IOException("Update package is incomplete: "+missing);
    }

    public static bool IsWritable(string directory)
    {
        try
        {
            var probe=Path.Combine(directory,".bcm-update-"+Guid.NewGuid().ToString("N"));
            File.WriteAllBytes(probe,[]); File.Delete(probe); return true;
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>PowerShell applier: waits for the manager to exit, backs up the install folder, copies the
    /// package over it (restoring the backup on failure) and starts the manager again.</summary>
    public const string ApplyScript="""
        param([int]$ManagerPid,[string]$Source,[string]$Target,[string]$Backup,[string]$Log,[string]$DataDirectory)
        $ErrorActionPreference='Stop'
        function Write-UpdateLog([string]$Message) { Add-Content -LiteralPath $Log -Value ((Get-Date).ToString('o')+' '+$Message) }
        function Copy-Tree([string]$From,[string]$To) {
            & robocopy.exe $From $To /E /R:5 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
            if($LASTEXITCODE -ge 8) { throw "robocopy $From -> $To failed with $LASTEXITCODE" }
        }
        try {
            try { Wait-Process -Id $ManagerPid -Timeout 120 -ErrorAction Stop } catch { }
            if(Get-Process -Id $ManagerPid -ErrorAction SilentlyContinue) { throw 'The manager did not close.' }
            if(Test-Path -LiteralPath $Backup) { Remove-Item -LiteralPath $Backup -Recurse -Force }
            Copy-Tree $Target $Backup
            try { Copy-Tree $Source $Target; Write-UpdateLog "Update applied from $Source" }
            catch { Write-UpdateLog ("Update copy failed, restoring previous version: "+$_); Copy-Tree $Backup $Target }
        } catch { Write-UpdateLog ("Update failed: "+$_) }
        $exe=Join-Path $Target 'BloodborneCheckpointManager.exe'
        if($DataDirectory) { Start-Process -FilePath $exe -WorkingDirectory $Target -ArgumentList @('--data-directory',('"'+$DataDirectory+'"')) }
        else { Start-Process -FilePath $exe -WorkingDirectory $Target }
        """;

    /// <summary>Starts the applier for the current process. The caller must exit right after.</summary>
    public static void StartApply(string staging,string installDirectory,string updatesDirectory,string log,string? dataDirectory)
    {
        var script=Path.Combine(updatesDirectory,"apply-update.ps1");
        File.WriteAllText(script,ApplyScript);
        var backup=Path.Combine(updatesDirectory,"previous");
        var start=new ProcessStartInfo("powershell.exe") { UseShellExecute=false, CreateNoWindow=true, WorkingDirectory=updatesDirectory };
        foreach(var argument in new[]{"-NoProfile","-ExecutionPolicy","Bypass","-WindowStyle","Hidden","-File",script,
            "-ManagerPid",Environment.ProcessId.ToString(),"-Source",staging,"-Target",installDirectory.TrimEnd('\\','/'),
            "-Backup",backup,"-Log",log,"-DataDirectory",dataDirectory ?? ""}) start.ArgumentList.Add(argument);
        using var process=Process.Start(start) ?? throw new IOException("Could not start the updater.");
    }
}

/// <summary>Update failure with a localized message key.</summary>
public sealed class UpdateException(string key) : IOException(key)
{
    public string Key { get; } = key;
}
