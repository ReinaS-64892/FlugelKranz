using FlugelKranz.Core;

namespace FlugelKranz.OpenVR;

public sealed record SteamVrOptions(double UpdateRate = 120, bool SynchronizeFrames = true)
{
    public void Validate()
    {
        if (!double.IsFinite(UpdateRate) || UpdateRate < 30 || UpdateRate > 1000)
            throw new ArgumentOutOfRangeException(nameof(UpdateRate), "SteamVR 更新レートは 30〜1000 Hz を指定してください。");
    }
}

/// <summary>Non-rendering OpenVR overlay; shares flight math and frame publication with Monado.</summary>
public sealed class SteamVrFlightRuntime : IFlightRuntime, IFramePacedFlightRuntime, IReferenceSpaceOffsetProvider, IHapticFeedback
{
    private readonly OpenVrSession session;
    private readonly OpenVrInput input;
    private readonly SteamVrSpaceConnection space;
    private readonly FrameOffsetWriter writes;
    private readonly Func<ValveIndexInputSettings> getSettings;
    private readonly MonotonicFrameClock clock;
    private readonly SteamVrFramePacer pacer;
    private bool disposed;
    public RigidPose OriginalOffset => RigidPose.Identity;
    public RigidPose CurrentOffset => writes.CurrentOffset;
    public RigidPose ReferenceSpaceOffset => space.ReferenceSpaceOffset;
    public float FrameElapsedSeconds => pacer.ElapsedSeconds;
    public bool FrameSynchronized => pacer.FrameSynchronized;

    public SteamVrFlightRuntime(SteamVrOptions? options = null, Func<ValveIndexInputSettings>? getValveIndexSettings = null)
    {
        options ??= new();
        options.Validate();
        getSettings = getValveIndexSettings ?? (() => ValveIndexInputSettings.Default);
        session = new();
        try
        {
            space = new(new SteamVrSpaceApi(session));
            input = new(session);
            writes = new(() => space.CurrentOffset, (offset, _) => space.Apply(offset));
            clock = new();
            pacer = new(options, session.WaitFrameSync, clock);
        }
        catch { session.Dispose(); throw; }
    }

    public bool WaitFrame()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        pacer.Wait();
        writes.Begin();
        return true;
    }

    public bool WaitForMidFrameUpdate(CancellationToken cancellationToken) => false;
    public void CommitFrame(CancellationToken cancellationToken) => writes.Commit(cancellationToken);
    public void EndUpdateCycle(bool preservePending = false) => writes.End(preservePending);
    public InputFrame ReadPhysical()
    {
        space.VerifyUnchanged();
        return space.ToPhysical(input.Read(getSettings().Normalized()));
    }
    public void Apply(RigidPose offset) => writes.Apply(offset, []);
    public void Restore()
    {
        if (writes.CycleActive) writes.Apply(OriginalOffset, []);
        else { writes.End(); space.Restore(); }
    }
    public void SendHapticPulse(float durationSeconds, float frequencyHz, float amplitude) => input.Haptic(durationSeconds, frequencyHz, amplitude);
    public void Dispose()
    {
        if (disposed) return;
        try { writes.End(); space.Restore(); }
        finally { disposed = true; clock.Dispose(); session.Dispose(); }
    }
}
