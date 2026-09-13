using FlugelKranz.Core;
using MonadoXrApi;

namespace FlugelKranz.OpenXR;

public sealed class MonadoFlightRuntime : IFlightRuntime, IReferenceSpaceOffsetProvider, IHapticFeedback
{
    private readonly MonadoFlightConnection monado;
    private readonly OpenXrInput input;
    public RigidPose OriginalOffset => monado.OriginalOffset;
    public RigidPose CurrentOffset => monado.CurrentOffset;
    public RigidPose ReferenceSpaceOffset => monado.CurrentReferenceSpaceOffset;
    public IReadOnlyList<TrackingOriginOffset> TrackingOrigins => monado.TrackingOrigins;

    public MonadoFlightRuntime(
        string libraryPath,
        Func<ValveIndexInputSettings>? getValveIndexSettings = null)
    {
        monado = new(libraryPath);
        try
        {
            input = new(getValveIndexSettings);
            try { monado.VerifyClient(input.ApplicationName); }
            catch { input.Dispose(); throw; }
        }
        catch { monado.Dispose(); throw; }
    }

    public InputFrame ReadPhysical()
    {
        monado.VerifyUnchanged();
        return monado.ToPhysical(input.Read());
    }
    public void Apply(RigidPose offset) => monado.Apply(offset);
    public void Restore() => monado.Restore();
    public void SendHapticPulse(float durationSeconds, float frequencyHz, float amplitude) =>
        input.SendHapticPulse(durationSeconds, frequencyHz, amplitude);
    public void Dispose() { input.Dispose(); monado.Dispose(); }
}
