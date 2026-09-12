using System.Text.Json;
using System.Text.Json.Serialization;
using FlugelKranz.Core;

namespace FlugelKranz.ViewModels;

internal sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public SettingsStore(string? path = null) => Path = path ?? ResolvePath();

    public string Path { get; }

    public FlugelKranzSettings Load()
    {
        try
        {
            if (!File.Exists(Path))
                return FlugelKranzSettings.Default;

            string json = File.ReadAllText(Path);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            bool current = root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("schemaVersion", out var version) &&
                version.ValueKind == JsonValueKind.Number &&
                version.TryGetInt32(out int schemaVersion) &&
                schemaVersion == FlugelKranzSettings.CurrentSchemaVersion &&
                root.TryGetProperty("freeFlight", out _) &&
                root.TryGetProperty("infiniteWalking", out _);
            if (!current)
                return ResetToCurrentDefaults();

            return JsonSerializer.Deserialize<FlugelKranzSettings>(json, JsonOptions)?.Normalized()
                ?? ResetToCurrentDefaults();
        }
        catch (IOException)
        {
            return FlugelKranzSettings.Default;
        }
        catch (JsonException)
        {
            return ResetToCurrentDefaults();
        }
    }

    public void Save(FlugelKranzSettings settings)
    {
        try
        {
            string? directory = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            string temporaryPath = Path + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings.Normalized(), JsonOptions));
            File.Move(temporaryPath, Path, true);
        }
        catch (IOException)
        {
            // Configuration persistence must not stop tracking or the UI.
        }
        catch (UnauthorizedAccessException)
        {
            // Configuration persistence must not stop tracking or the UI.
        }
    }

    private FlugelKranzSettings ResetToCurrentDefaults()
    {
        var settings = FlugelKranzSettings.Default;
        Save(settings);
        return settings;
    }

    private static string ResolvePath()
    {
        string? explicitPath = Environment.GetEnvironmentVariable("FLUGELKRANZ_CONFIG");
        if (!string.IsNullOrWhiteSpace(explicitPath))
            return System.IO.Path.GetFullPath(explicitPath);

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string? xdgConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        string configHome = string.IsNullOrWhiteSpace(xdgConfigHome)
            ? System.IO.Path.Combine(home, ".config")
            : xdgConfigHome;
        if (!System.IO.Path.IsPathRooted(configHome))
            configHome = System.IO.Path.Combine(home, configHome);

        return System.IO.Path.Combine(configHome, "FlugelKranz", "config.json");
    }
}
