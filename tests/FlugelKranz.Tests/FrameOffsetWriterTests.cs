using System.Numerics;
using FlugelKranz.Core;
using FlugelKranz.OpenXR;
using Xunit;

namespace FlugelKranz.Tests;

public class FrameOffsetWriterTests
{
    [Fact]
    public void ReleaseResetAndMovementAreCoalescedIntoOneWriteAfterTheWait()
    {
        var applied = Pose(1);
        var calls = new List<string>();
        var writer = new FrameOffsetWriter(() => applied, (offset, rotations) =>
        {
            calls.Add("apply");
            applied = offset;
            Assert.Equal("toe", Assert.Single(rotations).Serial);
        }, _ =>
        {
            calls.Add("wait");
            Assert.Equal(Pose(1), applied);
        });
        writer.Begin();
        writer.Apply(applied, []); // Release corrections.
        writer.Apply(RigidPose.Identity, []); // Reset.
        Assert.Equal(RigidPose.Identity, writer.CurrentOffset);
        writer.Apply(Pose(2), [new("toe", Quaternion.Identity)]);
        Assert.Equal(Pose(2), writer.CurrentOffset);
        Assert.Equal(Pose(1), applied);
        Assert.Empty(calls);
        writer.Commit(CancellationToken.None);
        writer.End();
        Assert.Equal(["wait", "apply"], calls);
        Assert.Equal(Pose(2), applied);
        Assert.Equal(applied, writer.CurrentOffset);
    }

    [Fact]
    public void AFailedFrameDropsItsPendingWriteAndTheNextFrameStartsFromTheAppliedOffset()
    {
        var applied = Pose(1);
        var writer = new FrameOffsetWriter(() => applied, (offset, _) => applied = offset, _ => { });
        writer.Begin();
        writer.Apply(Pose(2), []);
        writer.End(); // End without commit, as on a failed computation.
        writer.Begin();
        Assert.Equal(Pose(1), writer.CurrentOffset);
        writer.Commit(CancellationToken.None);
        writer.End();
        Assert.Equal(Pose(1), applied);
    }

    [Fact]
    public void CancellationDuringTheWaitDoesNotPublishPendingMovement()
    {
        using var cancellation = new CancellationTokenSource();
        var applied = Pose(1);
        var writer = new FrameOffsetWriter(() => applied, (offset, _) => applied = offset,
            _ => cancellation.Cancel());
        writer.Begin();
        writer.Apply(Pose(2), []);
        Assert.Throws<OperationCanceledException>(() => writer.Commit(cancellation.Token));
        writer.End();
        Assert.Equal(Pose(1), applied);
    }

    [Fact]
    public void TrackerCorrectionsAreSnapshottedBeforeWaiting()
    {
        TrackerRotationOffset[] rotations = [new("toe", Quaternion.Identity)];
        IReadOnlyList<TrackerRotationOffset>? applied = null;
        var writer = new FrameOffsetWriter(() => RigidPose.Identity, (_, value) => applied = value,
            _ => rotations[0] = new("changed", Quaternion.Identity));
        writer.Begin();
        writer.Apply(Pose(2), rotations);
        writer.Commit(CancellationToken.None);
        writer.End();
        Assert.Equal("toe", Assert.Single(applied!).Serial);
    }

    [Fact]
    public void ShutdownRestorationOutsideAFrameDoesNotWaitForAnotherDeadline()
    {
        var applied = Pose(1);
        var writer = new FrameOffsetWriter(() => applied, (offset, _) => applied = offset,
            _ => throw new InvalidOperationException("Must not wait during teardown"));
        writer.Apply(RigidPose.Identity, []);
        Assert.Equal(RigidPose.Identity, applied);
    }

    private static RigidPose Pose(float x) => new(Quaternion.Identity, new(x, 0, 0));
}
