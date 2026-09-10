using System.Text.Json;
using System.Text.Json.Serialization;

namespace Codenotch.Core;

/// <summary>
/// Load/save for <see cref="Settings"/>. A missing, unreadable or corrupt file is
/// not an error: the tray must always come up, so we fall back to the defaults
/// silently and let the settings window rewrite the file on the next save.
/// </summary>
public static class SettingsStore
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Codenotch", "settings.json");

    public static Settings Load(string path)
    {
        try
        {
            if (!File.Exists(path))
                return Settings.Default;
            string json = File.ReadAllText(path);
            Dto? dto = JsonSerializer.Deserialize<Dto>(json, ReadOptions);
            return dto is null ? Settings.Default : dto.ToSettings();
        }
        catch (Exception)
        {
            // Corrupt JSON, a locked file, a directory where a file should be:
            // none of that is worth a dialog. Defaults, and carry on.
            return Settings.Default;
        }
    }

    /// <summary>
    /// Writes through a sibling .tmp then moves it over the target, so an
    /// interrupted save leaves the previous file intact rather than a half file.
    /// </summary>
    public static void Save(string path, Settings settings)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(Dto.From(settings), WriteOptions));
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>
    /// The on-disk shape. Separate from <see cref="Settings"/> so the nullable
    /// "not set" flags survive a round trip and unknown keys are simply dropped.
    /// </summary>
    private sealed class Dto
    {
        public int? PollSeconds { get; set; }
        public string? Endpoint { get; set; }
        public bool? ShowNotch { get; set; }
        public bool? TrayPercent { get; set; }
        public bool? Notify { get; set; }
        public List<string>? Providers { get; set; }

        public static Dto From(Settings settings) => new()
        {
            PollSeconds = settings.PollSeconds,
            Endpoint = string.IsNullOrWhiteSpace(settings.Endpoint) ? null : settings.Endpoint,
            ShowNotch = settings.ShowNotch,
            TrayPercent = settings.TrayPercent,
            Notify = settings.Notify,
            Providers = settings.Providers is null ? null : [.. settings.Providers],
        };

        public Settings ToSettings()
        {
            var result = new Settings
            {
                Endpoint = string.IsNullOrWhiteSpace(Endpoint) ? null : Endpoint,
                ShowNotch = ShowNotch ?? Settings.Default.ShowNotch,
                TrayPercent = TrayPercent ?? Settings.Default.TrayPercent,
                Notify = Notify ?? Settings.Default.Notify,
                Providers = Providers is null ? null : (IReadOnlyList<string>)Providers.AsReadOnly(),
            };
            return PollSeconds.HasValue ? result with { PollSeconds = PollSeconds.Value } : result;
        }
    }
}
