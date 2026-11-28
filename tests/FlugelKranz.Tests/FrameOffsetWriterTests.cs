using System.Numerics;
using FlugelKranz.Core;
using FlugelKranz.OpenXR;
using Xunit;

namespace FlugelKranz.Tests;

public class FrameOffsetWriterTests
{
    [Fact]
    public void ReleaseResetAndMovementAreCoalescedIntoOneImmediateWrite()
    {
        var applied = Pose(1);
        int publications = 0;
        var writer = new FrameOffsetWriter(() => applied, (offset, rotations) =>
        {
            publications++;
            applied = offset;
            Assert.Equal("toe", Assert.Single(rotations).Serial);
        });
        writer.Begin();
        writer.Apply(applied, []);
        writer.Apply(RigidPose.Identity, []);
        Assert.Equal(RigidPose.Identity, writer.CurrentOffset);
        writer.Apply(Pose(2), [new("toe", Quaternion.Identity)]);
        Assert.Equal(Pose(2), writer.CurrentOffset);
        Assert.Equal(Pose(1), applied);
        Assert.Equal(0, publications);
        writer.Commit(CancellationToken.None);
        writer.End();
        Assert.Equal(1, publications);
        Assert.Equal(Pose(2), applied);
        Assert.Equal(applied, writer.CurrentOffset);
    }

    [Fact]
    public void ComputationOnlyMovementAndTrackerCorrectionsSurviveUntilTheNextCommit()
    {
        var applied = Pose(1);
        int publications = 0;
        IReadOnlyList<TrackerRotationOffset>? appliedRotations = null;
        var writer = new FrameOffsetWriter(() => applied, (offset, rotations) =>
        {
            applied = offset;
            appliedRotations = rotations;
            publications++;
        });
        writer.Begin();
        writer.Apply(Pose(2), []);
        writer.Commit(CancellationToken.None);
        TrackerRotationOffset[] corrections = [new("toe", Quaternion.Identity)];
        writer.Apply(Pose(3), corrections); // Computation-only midpoint.
        corrections[0] = new("changed", Quaternion.Identity);
        writer.End(preservePending: true);
        Assert.Equal(Pose(2), applied);
        Assert.Equal(Pose(3), writer.CurrentOffset);
        Assert.Equal(1, publications);
        writer.Begin();
        writer.Commit(CancellationToken.None);
        writer.End();
        Assert.Equal(2, publications);
        Assert.Equal(Pose(3), applied);
        Assert.Equal("toe", Assert.Single(appliedRotations!).Serial);
    }

    [Fact]
    public void AFailedCycleDropsItsPendingWriteAndTheNextCycleStartsFromTheAppliedOffset()
    {
        var applied = Pose(1);
        var writer = new FrameOffsetWriter(() => applied, (offset, _) => applied = offset);
        writer.Begin();
        writer.Apply(Pose(2), []);
        writer.Commit(CancellationToken.None);
        writer.Apply(Pose(3), []);
        writer.End(); // Failure after the initial publication.
        writer.Begin();
        Assert.Equal(Pose(2), writer.CurrentOffset);
        writer.Commit(CancellationToken.None);
        writer.End();
        Assert.Equal(Pose(2), applied);
    }

    [Fact]
    public void CancellationBeforeCommitDoesNotPublishPendingMovement()
    {
        using var cancellation = new CancellationTokenSource();
        var applied = Pose(1);
        var writer = new FrameOffsetWriter(() => applied, (offset, _) => applied = offset);
        writer.Begin();
        writer.Apply(Pose(2), []);
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => writer.Commit(cancellation.Token));
        writer.End();
        Assert.Equal(Pose(1), applied);
    }

    [Fact]
    public void ShutdownDiscardsTheRetainedMidpointAndRestoresImmediately()
    {
        var applied = Pose(1);
        var writer = new FrameOffsetWriter(() => applied, (offset, _) => applied = offset);
        writer.Begin();
        writer.Apply(Pose(2), []);
        writer.End(preservePending: true);
        writer.End();
        writer.Apply(RigidPose.Identity, []);
        Assert.Equal(RigidPose.Identity, applied);
        Assert.Equal(applied, writer.CurrentOffset);
    }

    private static RigidPose Pose(float x) => new(Quaternion.Identity, new(x, 0, 0));
}
