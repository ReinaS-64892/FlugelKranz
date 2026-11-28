using FlugelKranz.Core;

namespace FlugelKranz.OpenXR;

/// <summary>Publishes at frame start and retains computation-only results for the next frame.</summary>
internal sealed class FrameOffsetWriter(
    Func<RigidPose> getAppliedOffset,
    Action<RigidPose, IReadOnlyList<TrackerRotationOffset>> apply)
{
    private sealed record Update(RigidPose Offset, IReadOnlyList<TrackerRotationOffset> Rotations);
    private Update? pending;
    public bool CycleActive { get; private set; }
    public RigidPose CurrentOffset => pending?.Offset ?? getAppliedOffset();

    public void Begin()
    {
        if (CycleActive) throw new InvalidOperationException("前のオフセット更新が終了していません。");
        CycleActive = true;
    }

    public void Apply(RigidPose offset, IReadOnlyList<TrackerRotationOffset> rotations)
    {
        if (!offset.IsValid) throw new ArgumentException("空間変換が不正です。", nameof(offset));
        if (CycleActive)
            pending = new(offset, rotations.ToArray());
        else
            apply(offset, rotations);
    }

    public void Commit(CancellationToken cancellationToken)
    {
        if (!CycleActive) throw new InvalidOperationException("オフセット更新が開始していません。");
        cancellationToken.ThrowIfCancellationRequested();
        if (pending is { } update)
        {
            apply(update.Offset, update.Rotations);
            pending = null;
        }
    }

    public void End(bool preservePending = false)
    {
        if (!preservePending) pending = null;
        CycleActive = false;
    }
}
