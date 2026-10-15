using Checkpoint.Core;

namespace Checkpoint.App;

internal sealed record UiPreferences(string Language="en")
{
    public static string LanguagesDirectory => Path.Combine(AppContext.BaseDirectory,"Languages");
    public static void Initialize(string directory)
    {
        var language="en";
        try { var path=Path.Combine(directory,"ui-settings.json"); if (File.Exists(path)) language=JsonDisk.Read<UiPreferences>(path).Language; }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException) { }
        TextCatalog.Current=TextCatalog.Load(LanguagesDirectory,language ?? "en");
    }
}
