using FlugelKranz.Core;
using FlugelKranz.OpenVR;
using FlugelKranz.OpenXR;

namespace FlugelKranz;

public enum RuntimeBackend { Monado, SteamVr }

/// <summary>Composition root: backend selection never enters flight math or the UI.</summary>
public sealed record RuntimeConfiguration(
    RuntimeBackend Backend,
    Func<string> MonadoLibraryPath,
    SteamVrOptions SteamVr)
{
    public static RuntimeBackend SelectBackend(string selection, bool windows) => selection switch
    {
        "auto" => windows ? RuntimeBackend.SteamVr : RuntimeBackend.Monado,
        "monado" => RuntimeBackend.Monado,
        "steamvr" => RuntimeBackend.SteamVr,
        _ => throw new ArgumentException("ランタイムは auto / monado / steamvr を指定してください。", nameof(selection))
    };

    public IFlightRuntime Create(Func<ValveIndexInputSettings> getSettings) => Backend switch
    {
        RuntimeBackend.Monado when OperatingSystem.IsLinux() => new MonadoFlightRuntime(MonadoLibraryPath(), getSettings),
        RuntimeBackend.Monado => throw new PlatformNotSupportedException("Monado バックエンドは現在 Linux 向けです。Windows では --runtime steamvr を指定してください。"),
        RuntimeBackend.SteamVr => new SteamVrFlightRuntime(SteamVr, getSettings),
        _ => throw new ArgumentOutOfRangeException(nameof(Backend))
    };
}
