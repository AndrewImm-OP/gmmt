using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace Gmmt.Desktop;

/// <summary>English/Russian UI strings and an atomic, per-user language preference.</summary>
public sealed class LocalizationService
{
    private static readonly IReadOnlyDictionary<string, Dictionary<string, string>> Catalogs = LoadCatalogs();
    private readonly string settingsPath;
    private readonly Func<string> systemLanguage;
    public string Preference { get; private set; }
    public string Language => Resolve(Preference, systemLanguage());
    public static IEnumerable<string> Keys => Catalogs["en"].Keys;

    public LocalizationService(string? settingsPath = null, Func<string>? systemLanguage = null)
    {
        this.settingsPath = settingsPath ?? Environment.GetEnvironmentVariable("GMMT_SETTINGS") ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "gmmt", "settings.json");
        this.systemLanguage = systemLanguage ?? DetectSystemLanguage;
        Preference = "system";
        try
        {
            if (File.Exists(this.settingsPath))
            {
                var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(this.settingsPath));
                if (settings?.Language is "en" or "ru" or "system") Preference = settings.Language;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { /* Start in system mode if settings are unreadable. */ }
    }

    public static string Resolve(string preference, string systemLanguage) => preference switch
    {
        "en" => "en", "ru" => "ru", "system" => systemLanguage.Split('_', '-', '.', '@')[0].Equals("ru", StringComparison.OrdinalIgnoreCase) ? "ru" : "en",
        _ => throw new ArgumentException("Unsupported language preference: " + preference)
    };

    private static string DetectSystemLanguage()
    {
        if (OperatingSystem.IsLinux())
        {
            foreach (var variable in new[] { "LC_ALL", "LC_MESSAGES", "LANGUAGE" })
            {
                var value = Environment.GetEnvironmentVariable(variable);
                if (!string.IsNullOrWhiteSpace(value)) return value.Split(':')[0];
            }
        }
        return CultureInfo.CurrentUICulture.Name;
    }

    public void SetPreference(string preference)
    {
        _ = Resolve(preference, systemLanguage());
        if (preference == Preference) return;
        var fullPath = Path.GetFullPath(settingsPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(new Settings(preference), new JsonSerializerOptions { WriteIndented = true }) + "\n");
            File.Move(temporary, fullPath, true);
            Preference = preference;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public string Text(string key) => Catalogs[Language].TryGetValue(key, out var value) ? value
        : Catalogs["en"].TryGetValue(key, out value) ? value : throw new KeyNotFoundException("Unknown localization key: " + key);
    public string Format(string key, params object[] values) => string.Format(CultureInfo.GetCultureInfo(Language), Text(key), values);

    private static IReadOnlyDictionary<string, Dictionary<string, string>> LoadCatalogs()
    {
        var catalogs = new Dictionary<string, Dictionary<string, string>>();
        foreach (var language in new[] { "en", "ru" })
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Gmmt.Desktop.Localization." + language + ".json")
                ?? throw new InvalidDataException("Missing localization catalog: " + language);
            catalogs[language] = JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? throw new InvalidDataException("Empty localization catalog");
        }
        if (!catalogs["en"].Keys.Order().SequenceEqual(catalogs["ru"].Keys.Order())) throw new InvalidDataException("Localization catalogs must have identical keys.");
        return catalogs;
    }
    private sealed record Settings(string Language);
}
