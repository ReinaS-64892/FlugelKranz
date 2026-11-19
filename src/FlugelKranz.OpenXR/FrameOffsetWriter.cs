using FlugelKranz.Core;

namespace FlugelKranz.OpenXR;

/// <summary>Computes against a staged offset, then publishes the final update at one deadline.</summary>
internal sealed class FrameOffsetWriter(
    Func<RigidPose> getAppliedOffset,
    Action<RigidPose, IReadOnlyList<TrackerRotationOffset>> apply,
    Action<CancellationToken> waitForApply)
{
    private sealed record Update(RigidPose Offset, IReadOnlyList<TrackerRotationOffset> Rotations);
    private Update? pending;
    public bool FrameActive { get; private set; }
    public RigidPose CurrentOffset => pending?.Offset ?? getAppliedOffset();

    public void Begin()
    {
        if (FrameActive) throw new InvalidOperationException("前のオフセット更新が終了していません。");
        FrameActive = true;
    }

    public void Apply(RigidPose offset, IReadOnlyList<TrackerRotationOffset> rotations)
    {
        if (!offset.IsValid) throw new ArgumentException("空間変換が不正です。", nameof(offset));
        if (FrameActive)
            pending = new(offset, rotations.ToArray());
        else
            apply(offset, rotations);
    }

    public void Commit(CancellationToken cancellationToken)
    {
        if (!FrameActive) throw new InvalidOperationException("オフセット更新が開始していません。");
        waitForApply(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (pending is { } update)
        {
            apply(update.Offset, update.Rotations);
            pending = null;
        }
    }

    public void End()
    {
        pending = null;
        FrameActive = false;
    }
}
