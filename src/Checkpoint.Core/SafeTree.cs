using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Checkpoint.Core;

public static class SafeTree
{
    public static string Canonical(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) throw new IOException(TextCatalog.T("error.absolute_path"));
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (full.StartsWith(@"\\") || full.Equals(Path.GetPathRoot(full), StringComparison.OrdinalIgnoreCase))
            throw new IOException(TextCatalog.T("error.root_or_network"));
        NoLinks(full);
        return full;
    }

    public static void NoLinks(string path)
    {
        for (string? p = Path.GetFullPath(path); p != null; p = Path.GetDirectoryName(p))
            if ((Directory.Exists(p) || File.Exists(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)
                throw new IOException(TextCatalog.T("error.links") + p);
    }

    public static bool Within(string path, string root) => path.Equals(root, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    public static string Child(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':') ||
            relative.Split('/', '\\').Any(p => p is "" or "." or ".." || p.EndsWith('.') || p.EndsWith(' ')))
            throw new IOException(TextCatalog.T("error.manifest_path"));
        var result = Path.GetFullPath(Path.Combine(root, relative));
        if (!Within(result, root) || result.Equals(root, StringComparison.OrdinalIgnoreCase)) throw new IOException(TextCatalog.T("error.outside_checkpoint"));
        NoLinks(result);
        return result;
    }

    private static IEnumerable<string> Walk(string root)
    {
        NoLinks(root);
        foreach (var file in Directory.EnumerateFiles(root)) { NoLinks(file); yield return file; }
        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            NoLinks(dir);
            foreach (var file in Walk(dir)) yield return file;
        }
    }

    public static Entry[] Scan(string root)
    {
        return Walk(root).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).Select(file =>
        {
            using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            return new Entry(Path.GetRelativePath(root, file), input.Length, Convert.ToHexString(SHA256.HashData(input)));
        }).ToArray();
    }

    public static void ValidateLayout(Entry[] entries)
    {
        if (!entries.Any(e => Regex.IsMatch(e.Path.Replace('\\', '/'), @"^\d+/CUSA\d{5}/SPRJ0005/userdata0010$")))
            throw new IOException(TextCatalog.T("error.save_structure"));
        foreach (var e in entries)
        {
            if (e.Path.EndsWith(".bbtmp", StringComparison.OrdinalIgnoreCase)) throw new IOException(TextCatalog.T("error.temporary_save"));
            var m = Regex.Match(Path.GetFileName(e.Path), @"^(userdata|backup)(\d{4})$");
            if (m.Success && int.TryParse(m.Groups[2].Value, out var slot) && slot <= 10 && e.Size != (slot == 10 ? 0x40000 : 0x140000))
                throw new IOException(TextCatalog.T("error.save_size") + e.Path);
            if (Path.GetFileName(e.Path) == "userdata0010" && e.Sha256 == Convert.ToHexString(SHA256.HashData(new byte[0x40000])))
                throw new IOException(TextCatalog.T("error.empty_system") + e.Path);
        }
    }

    public static void Equal(Entry[] expected, Entry[] actual)
    {
        var a = expected.OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase).ToArray();
        var b = actual.OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase).ToArray();
        if (!a.SequenceEqual(b)) throw new IOException(TextCatalog.T("error.hash_mismatch"));
    }

    public static void Copy(string source, string destination, Entry[] entries)
    {
        Directory.CreateDirectory(destination);
        foreach (var e in entries)
        {
            var target = Child(destination, e.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var input = new FileStream(Child(source, e.Path), FileMode.Open, FileAccess.Read, FileShare.Read);
            using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            input.CopyTo(output);
            output.Flush(true);
        }
        Equal(entries, Scan(destination));
        Equal(entries, Scan(source));
    }
}
