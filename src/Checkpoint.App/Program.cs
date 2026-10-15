using Checkpoint.Core;

namespace Checkpoint.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        string? dataDirectory = null;
        if (args.Length > 0)
        {
            if (args.Length != 2 || args[0] != "--data-directory" || !Path.IsPathFullyQualified(args[1]))
            { AppDialog.Show(null,TextCatalog.T("app.name"),TextCatalog.T("startup.usage")); return; }
            dataDirectory = Path.GetFullPath(args[1]);
        }
        var suffix = dataDirectory == null ? "v1" : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(dataDirectory.ToUpperInvariant())))[..16];
        UiPreferences.Initialize(dataDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BloodborneCheckpointManager"));
        using var mutex = new Mutex(true, @"Local\BloodborneCheckpointManager-" + suffix, out var first);
        if (!first) { AppDialog.Show(null,TextCatalog.T("app.name"),TextCatalog.T("startup.open")); return; }
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(dataDirectory));
    }
}
