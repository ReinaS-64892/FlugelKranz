using System.Numerics;
using FlugelKranz.Core;
using Xunit;

namespace FlugelKranz.Tests;

public class ToeRotationCompensationTests
{
    private static readonly InfiniteWalkingSettings Settings = InfiniteWalkingSettings.Default;
    private static Quaternion Yaw(float angle) => Quaternion.CreateFromAxisAngle(Vector3.UnitY, angle);
    private static InputFrame Frame(float angle = 0, float height = 0.05f, float x = 0) => new(
        new(Quaternion.Identity, new(0, 1.7f, 0)), true,
        new(new(Yaw(angle), new(-0.3f, 1, 0)), 0, 1, 0, true),
        new(new(Quaternion.Identity, new(0.3f, 1, 0)), 0, 0, 0, true))
        { Trackers = [new("toe", new(x, height, 0), true)] };

    private static InfiniteWalkingManipulator Turn(float angle = 0.6f)
    {
        var engine = new InfiniteWalkingManipulator(RigidPose.Identity);
        engine.Update(Frame() with { Left = Frame().Left with { Turn = 0 } }, 0.01f, Settings);
        engine.Update(Frame(), 0.01f, Settings);
        engine.Update(Frame(angle), 0.01f, Settings);
        return engine;
    }

    private static void Near(Quaternion expected, Quaternion actual) =>
        Assert.True(1 - MathF.Abs(Quaternion.Dot(expected, actual)) < 0.00001f, $"{expected} != {actual}");

    [Fact]
    public void CancelsOnlyAppliedWalkingYawAndPreservesPhysicalAnkleRotation()
    {
        var engine = Turn();
        var correction = Assert.Single(engine.TrackerRotations).Orientation;
        var physicalAnkle = Quaternion.CreateFromYawPitchRoll(0.3f, 0.5f, -0.2f);
        Near(physicalAnkle, engine.Offset.Orientation * correction * physicalAnkle);
        // A second turn accumulates instead of re-anchoring every frame.
        engine.Update(Frame(1), 0.01f, Settings);
        Near(physicalAnkle, engine.Offset.Orientation * Assert.Single(engine.TrackerRotations).Orientation * physicalAnkle);
    }

    [Fact]
    public void CompensationUsesAppliedSmoothedRotationAndDoesNotCancelExistingFlightRotation()
    {
        var engine = new InfiniteWalkingManipulator(new(Yaw(0.7f), Vector3.Zero));
        var settings = Settings with { TurnSmoothSeconds = 0.5f };
        engine.Update(Frame() with { Left = Frame().Left with { Turn = 0 } }, 0.01f, settings);
        engine.Update(Frame(), 0.01f, settings);
        engine.Update(Frame(0.8f), 0.01f, settings);
        Near(Yaw(0.7f), engine.Offset.Orientation * Assert.Single(engine.TrackerRotations).Orientation);
    }

    [Theory]
    [InlineData(0.09f, 0)] // Lifted above contact height while still inside the ground range.
    [InlineData(0.3f, 0)] // Above the ground range.
    [InlineData(0.05f, 0.08f)] // Large horizontal movement.
    public void ReleasesSmoothlyOverConfiguredDurationEvenWithNoMoreSpaceRotation(float height, float x)
    {
        var engine = Turn();
        var start = Assert.Single(engine.TrackerRotations).Orientation;
        engine.Update(Frame(0.6f, height, x), 0.1f, Settings);
        Near(Quaternion.Slerp(start, Quaternion.Identity, 0.5f), Assert.Single(engine.TrackerRotations).Orientation);
        engine.Update(Frame(0.6f, height, x), 0.1f, Settings);
        Assert.Empty(engine.TrackerRotations);
    }

    [Fact]
    public void LiftedFootCannotReanchorUntilItReturnsToContactHeightAndSettles()
    {
        var engine = Turn();
        engine.Update(Frame(0.6f, 0.09f), 0.2f, Settings);
        for (int i = 0; i < 20; i++) engine.Update(Frame(0.6f, 0.09f), 0.1f, Settings);
        engine.Update(Frame(0.8f, 0.09f), 0.1f, Settings);
        Assert.Empty(engine.TrackerRotations);
        // Landing and settling establishes a fresh baseline without restoring the old correction.
        for (int i = 0; i < 20; i++) engine.Update(Frame(0.8f), 0.1f, Settings);
        Assert.Empty(engine.TrackerRotations);
        engine.Update(Frame(1), 0.1f, Settings);
        Near(Yaw(0.2f), Assert.Single(engine.TrackerRotations).Orientation);
    }

    [Fact]
    public void ZeroDurationReleasesImmediatelyAndTrackingLossNeverRetainsCorrection()
    {
        var engine = Turn();
        engine.Update(Frame(0.6f, 0.3f), 0.01f, Settings with { ToeRotationReleaseSeconds = 0 });
        Assert.Empty(engine.TrackerRotations);
        engine = Turn();
        engine.Update(Frame(0.6f) with { Trackers = [] }, 0.01f, Settings);
        Assert.Empty(engine.TrackerRotations);
        engine.Update(Frame(0.6f), 0.01f, Settings);
        Assert.Empty(engine.TrackerRotations); // New contact starts at identity.
    }

    [Fact]
    public void DisableReleaseAndHeadTrackingLossClearAllCompensation()
    {
        var engine = Turn();
        engine.Update(Frame(0.6f), 0.01f, Settings with { ToeRotationCompensationEnabled = false });
        Assert.Empty(engine.TrackerRotations);
        engine = Turn();
        engine.Update(Frame(0.6f), 0.01f, Settings with { ToeTurnOriginEnabled = false });
        Assert.Empty(engine.TrackerRotations);
        engine = Turn();
        engine.Release();
        Assert.Empty(engine.TrackerRotations);
        engine = Turn();
        engine.Update(Frame(0.6f) with { HeadTracked = false }, 0.01f, Settings);
        Assert.Empty(engine.TrackerRotations);
    }

    [Fact]
    public void EqualPlantedFeetBothReceiveCorrectionButRaisedFootDoesNot()
    {
        var engine = new InfiniteWalkingManipulator(RigidPose.Identity);
        var frame = Frame() with { Trackers = [new("left", new(-0.2f, 0.05f, 0), true), new("right", new(0.2f, 0.05f, 0), true)] };
        engine.Update(frame with { Left = frame.Left with { Turn = 0 } }, 0.01f, Settings);
        engine.Update(frame, 0.01f, Settings);
        frame = frame with { Left = Frame(0.6f).Left };
        engine.Update(frame, 0.01f, Settings);
        Assert.Equal(2, engine.TrackerRotations.Count);
        frame = frame with { Trackers = [frame.Trackers![0], new("right", new(0.2f, 0.3f, 0), true)] };
        engine.Update(frame, 0.2f, Settings);
        Assert.Equal("left", Assert.Single(engine.TrackerRotations).Serial);
    }

    [Fact]
    public void SettingDefaultsAndBoundsMatchRequestedValues()
    {
        Assert.True(Settings.ToeRotationCompensationEnabled);
        Assert.Equal(0.2f, Settings.ToeRotationReleaseSeconds);
        Assert.Equal(0, (Settings with { ToeRotationReleaseSeconds = -1 }).Normalized().ToeRotationReleaseSeconds);
        Assert.Equal(1, (Settings with { ToeRotationReleaseSeconds = 2 }).Normalized().ToeRotationReleaseSeconds);
        Assert.Equal(0.2f, (Settings with { ToeRotationReleaseSeconds = float.NaN }).Normalized().ToeRotationReleaseSeconds);
    }
}
