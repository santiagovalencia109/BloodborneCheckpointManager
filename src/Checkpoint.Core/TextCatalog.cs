using System.Globalization;
using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;

namespace Checkpoint.Core;

public sealed record LanguagePack(int SchemaVersion, string Language, string Name, Dictionary<string, string> Strings);
public sealed record LanguageChoice(string Code, string Name)
{
    public override string ToString() => Name;
}

/// <summary>Versioned JSON catalogs, English fallback and positional formatting.</summary>
public sealed class TextCatalog
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };
    private static readonly LanguagePack English = ReadEnglish();
    private readonly LanguagePack pack;
    public static TextCatalog Current { get; set; } = new(English);
    public string Language => pack.Language;
    public string? LoadWarning { get; private init; }
    private TextCatalog(LanguagePack pack) => this.pack = pack;
    private static LanguagePack ReadEnglish()
    {
        using var stream = typeof(TextCatalog).Assembly.GetManifestResourceStream("Checkpoint.Core.Languages.en.json")
            ?? throw new InvalidOperationException("The built-in English language pack is missing.");
        return JsonSerializer.Deserialize<LanguagePack>(stream, Options)!;
    }
    private static LanguagePack Read(string path)
    {
        if (new FileInfo(path).Length > 1_048_576) throw new IOException("Language file exceeds 1 MB.");
        var p = JsonSerializer.Deserialize<LanguagePack>(File.ReadAllText(path), Options);
        if (p == null || p.SchemaVersion != 1 || p.Strings == null || string.IsNullOrWhiteSpace(p.Name) ||
            !Regex.IsMatch(p.Language ?? "", "^[A-Za-z0-9-]{2,35}$")) throw new IOException("Invalid language file.");
        foreach (var (key, value) in p.Strings)
        {
            if (value == null) throw new IOException("Null translation: " + key);
            if (English.Strings.TryGetValue(key, out var baseline))
            {
                var actual = CompositeFormat.Parse(value);
                var expected = CompositeFormat.Parse(baseline);
                if (actual.MinimumArgumentCount != expected.MinimumArgumentCount)
                    throw new IOException("Translation placeholders do not match: " + key);
                var expectedSlots=Regex.Matches(baseline,@"(?<!\{)\{(\d+)(?:[,}:])").Select(m=>m.Groups[1].Value).ToHashSet();
                var actualSlots=Regex.Matches(value,@"(?<!\{)\{(\d+)(?:[,}:])").Select(m=>m.Groups[1].Value).ToHashSet();
                if (!expectedSlots.SetEquals(actualSlots)) throw new IOException("Missing translation placeholder: " + key);
            }
        }
        return p;
    }
    public static TextCatalog Load(string directory, string language)
    {
        try
        {
            if (!Regex.IsMatch(language, "^[A-Za-z0-9-]{2,35}$")) throw new IOException("Invalid language code.");
            var pack=Read(Path.Combine(directory, language + ".json"));
            if (!string.Equals(pack.Language,language,StringComparison.OrdinalIgnoreCase)) throw new IOException("Language code does not match its filename.");
            return new(pack);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException)
        { return new(English) { LoadWarning=ex.Message }; }
    }
    public static LanguageChoice[] Discover(string directory)
    {
        var choices=new Dictionary<string,LanguageChoice>(StringComparer.OrdinalIgnoreCase) { ["en"]=new("en",English.Name) };
        try
        {
        if (Directory.Exists(directory))
            foreach (var path in Directory.EnumerateFiles(directory,"*.json"))
            {
                var code=Path.GetFileNameWithoutExtension(path);
                var catalog=Load(directory,code);
                if (catalog.LoadWarning==null) choices[code]=new(code,catalog.pack.Name);
            }
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException) { }
        return choices.Values.OrderBy(p=>p.Code=="en" ? 0 : 1).ThenBy(p=>p.Name).ToArray();
    }
    public static string T(string key, params object?[] args) => Current.Get(key,args);
    public static string ServerMessage(string message)
    {
        var match=Regex.Match(message,@"^No se obtuvo acceso seguro; no se reemplazaron saves\. Estado: mundo=(\d+), carga=(\d+), titulo=(\d+), archivos abiertos=(\d+)$");
        if (match.Success) return T("native.safe_state",match.Groups[1].Value,match.Groups[2].Value,match.Groups[3].Value,match.Groups[4].Value);
        foreach(var (key,value) in English.Strings.Where(p=>p.Key.StartsWith("native.",StringComparison.Ordinal)))
            if(value==message) return T(key);
        return message; // Unrecognized external diagnostics remain verbatim.
    }
    public string Get(string key, params object?[] args)
    {
        var baseline=English.Strings.GetValueOrDefault(key,key);
        var template=pack.Strings.GetValueOrDefault(key,baseline);
        var culture=CultureInfo.InvariantCulture;
        try { culture=CultureInfo.GetCultureInfo(pack.Language); } catch (CultureNotFoundException) { }
        try { return string.Format(culture,template,args); }
        catch (FormatException) { return string.Format(CultureInfo.InvariantCulture,baseline,args); }
    }
}
