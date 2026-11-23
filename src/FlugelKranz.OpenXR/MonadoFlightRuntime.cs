using FlugelKranz.Core;
using MonadoXrApi;

namespace FlugelKranz.OpenXR;

public sealed class MonadoFlightRuntime : IFlightRuntime, IReferenceSpaceOffsetProvider, IHapticFeedback, ITrackerRotationRuntime, IFramePacedFlightRuntime
{
    private readonly MonadoFlightConnection monado;
    private readonly OpenXrInput input;
    private readonly FrameOffsetWriter writes;
    public RigidPose OriginalOffset => monado.OriginalOffset;
    public RigidPose CurrentOffset => writes.CurrentOffset;
    public RigidPose ReferenceSpaceOffset => monado.CurrentReferenceSpaceOffset;
    public bool TrackerRotationOffsetsSupported => monado.TrackerRotationOffsetsSupported;
    public bool TrackerSpacesSupported => input.TrackerSpacesSupported;
    public float FrameElapsedSeconds => input.FrameElapsedSeconds;
    public long FramePeriodNanoseconds => input.FramePeriodNanoseconds;

    public MonadoFlightRuntime(
        string libraryPath,
        Func<ValveIndexInputSettings>? getValveIndexSettings = null)
    {
        monado = new(libraryPath);
        try
        {
            input = new(getValveIndexSettings);
            writes = new(() => monado.CurrentOffset, monado.Apply);
            try { monado.VerifyClient(input.ApplicationName); }
            catch { input.Dispose(); throw; }
        }
        catch { monado.Dispose(); throw; }
    }

    public bool WaitFrame()
    {
        if (!input.WaitFrame()) return false;
        writes.Begin();
        return true;
    }
    public bool WaitForMidFrameUpdate(CancellationToken cancellationToken) => input.WaitForMidFrameUpdate(cancellationToken);
    public void CommitFrame(CancellationToken cancellationToken) => writes.Commit(cancellationToken);
    public void EndUpdateCycle(bool preservePending = false)
    {
        writes.End(preservePending);
        input.EndUpdateCycle();
    }

    public InputFrame ReadPhysical()
    {
        monado.VerifyUnchanged();
        var frame = input.Read();
        monado.RefreshTrackers(input.TrackerRevision);
        return monado.ToPhysical(frame);
    }
    public void Apply(RigidPose offset) => writes.Apply(offset, []);
    public void Apply(RigidPose offset, IReadOnlyList<TrackerRotationOffset> rotations) => writes.Apply(offset, rotations);
    public void Restore()
    {
        if (!writes.CycleActive) { writes.End(); monado.Restore(); return; }
        monado.VerifyUnchanged();
        writes.Apply(OriginalOffset, []);
    }
    public void SendHapticPulse(float durationSeconds, float frequencyHz, float amplitude) =>
        input.SendHapticPulse(durationSeconds, frequencyHz, amplitude);
    public void Dispose() { input.Dispose(); monado.Dispose(); }
}
