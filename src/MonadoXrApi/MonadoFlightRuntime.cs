using FlugelKranz.Core;

namespace MonadoXrApi;

public sealed class MonadoFlightRuntime : IFlightRuntime
{
    private readonly MonadoConnection monado;
    private readonly OpenXrInput input;
    public RigidPose OriginalOffset => monado.OriginalOffset;
    public RigidPose CurrentOffset => monado.CurrentOffset;
    public IReadOnlyList<TrackingOriginOffset> TrackingOrigins => monado.TrackingOrigins;

    public MonadoFlightRuntime(string libraryPath)
    {
        monado = new(libraryPath);
        try
        {
            input = new();
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
    public void Dispose() { input.Dispose(); monado.Dispose(); }
}
