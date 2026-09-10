using System.Text.Json;

namespace FlugelKranz;

/// <summary>Finds the libmonado library advertised by the active OpenXR runtime.</summary>
public static class MonadoRuntimeLocator
{
    public const string FallbackLibraryPath = "/usr/lib/wivrn/libmonado_wivrn.so";

    public static string Resolve(string? runtimeJsonPath = null)
    {
        foreach (var candidate in GetManifestCandidates(runtimeJsonPath))
        {
            var libraryPath = TryReadLibraryPath(candidate);
            if (!string.IsNullOrWhiteSpace(libraryPath))
                return libraryPath;
        }

        return FallbackLibraryPath;
    }

    private static IEnumerable<string> GetManifestCandidates(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            yield return explicitPath;
            yield break;
        }

        var environmentPath = Environment.GetEnvironmentVariable("XR_RUNTIME_JSON");
        if (!string.IsNullOrWhiteSpace(environmentPath))
            yield return environmentPath;

        var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(configHome))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(home))
                configHome = Path.Combine(home, ".config");
        }

        if (!string.IsNullOrWhiteSpace(configHome))
            yield return Path.Combine(configHome, "openxr", "1", "active_runtime.json");

        var configDirs = Environment.GetEnvironmentVariable("XDG_CONFIG_DIRS");
        if (!string.IsNullOrWhiteSpace(configDirs))
        {
            foreach (var directory in configDirs.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                yield return Path.Combine(directory, "openxr", "1", "active_runtime.json");
        }

        yield return "/etc/openxr/1/active_runtime.json";
    }

    private static string? TryReadLibraryPath(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (!document.RootElement.TryGetProperty("runtime", out var runtime))
                return null;
            if (!runtime.TryGetProperty("MND_libmonado_path", out var library))
                return null;
            return library.ValueKind == JsonValueKind.String ? library.GetString() : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
