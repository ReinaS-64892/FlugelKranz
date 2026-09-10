using System.Text.Json;
using FlugelKranz.Core;

namespace FlugelKranz.ViewModels;

internal sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public SettingsStore(string? path = null) => Path = path ?? ResolvePath();

    public string Path { get; }

    public FlightMotionSettings Load()
    {
        try
        {
            if (!File.Exists(Path))
                return FlightMotionSettings.Default;

            return JsonSerializer.Deserialize<FlightMotionSettings>(File.ReadAllText(Path), JsonOptions)?.Normalized()
                ?? FlightMotionSettings.Default;
        }
        catch (IOException)
        {
            return FlightMotionSettings.Default;
        }
        catch (JsonException)
        {
            return FlightMotionSettings.Default;
        }
    }

    public void Save(FlightMotionSettings settings)
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
