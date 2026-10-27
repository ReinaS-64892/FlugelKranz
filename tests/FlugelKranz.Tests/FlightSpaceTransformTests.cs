using System.Numerics;
using FlugelKranz.Core;
using FlugelKranz.OpenXR;
using Xunit;

namespace FlugelKranz.Tests;

public class FlightSpaceTransformTests
{
    private static readonly RigidPose InitialStage = new(
        Quaternion.CreateFromYawPitchRoll(-0.2f, 0.1f, 0.3f), new(2, 1, -3));
    private static readonly RigidPose Flight = new(
        Quaternion.CreateFromYawPitchRoll(0.4f, -0.6f, 0.2f), new(3, -1, 2));
    private static readonly RigidPose Hand = new(Quaternion.Identity, new(-0.3f, 1.2f, -0.4f));

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(0.7f, -0.3f, 0.1f)]
    [InlineData(-0.4f, 0.5f, -0.2f)]
    public void StageAppliesFlightOnceAndInputRecoveryRetainsDeviceCalibration(float yaw, float pitch, float roll)
    {
        var space = new FlightSpaceTransform(InitialStage, Flight);
        var calibration = new RigidPose(Quaternion.CreateFromYawPitchRoll(yaw, pitch, roll), new(1, 2, -3));
        var calibrated = calibration * Hand;

        var reported = space.StageOffset.Inverse() * calibrated;

        Assert.True(reported.NearlyEquals(InitialStage.Inverse() * Flight * calibrated));
        Assert.True(space.ToPhysical(reported).NearlyEquals(calibrated));
        // Starting a new connection with the updated calibration must recover the same pose.
        var neutral = new FlightSpaceTransform(InitialStage, RigidPose.Identity);
        Assert.True(neutral.ToPhysical(neutral.StageOffset.Inverse() * calibrated).NearlyEquals(calibrated));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DragAfterDriverCalibrationChangeKeepsCalibratedPointFixed(bool rotatedFlight)
    {
        var space = new FlightSpaceTransform(InitialStage, rotatedFlight ? Flight : RigidPose.Identity);
        var manipulator = new FreeFlightManipulator(space.Offset);
        manipulator.Update(Frame(space.ToPhysical(space.StageOffset.Inverse() * Hand)));

        // Only this hand's driver space changes after connecting, before the grab.
        var calibration = new RigidPose(Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2), new(1, 0, 2));
        var calibrated = calibration * Hand;
        var anchor = space.Offset.Transform(calibrated.Position);
        var before = Frame(space.ToPhysical(space.StageOffset.Inverse() * calibrated), drag: 1);
        manipulator.Update(before);

        var moved = calibration * (Hand with { Position = Hand.Position + new Vector3(0.1f, 0, 0) });
        var result = manipulator.Update(Frame(space.ToPhysical(space.StageOffset.Inverse() * moved), drag: 1));

        Near(anchor, result.Transform(moved.Position));
        // The new STAGE pose must keep that same visible anchor, too.
        var next = space with { Offset = result };
        var reported = next.StageOffset.Inverse() * moved;
        Near(InitialStage.Inverse().Transform(anchor), reported.Position);
        Assert.True(next.ToPhysical(reported).NearlyEquals(moved));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TurnAfterDriverCalibrationChangeUsesTheCalibratedRotationAxis(bool rotatedFlight)
    {
        var space = new FlightSpaceTransform(InitialStage, rotatedFlight ? Flight : RigidPose.Identity);
        var manipulator = new FreeFlightManipulator(space.Offset);
        manipulator.Update(Frame(space.ToPhysical(space.StageOffset.Inverse() * Hand)));
        var calibration = new RigidPose(Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2), new(1, 0, 2));
        var calibrated = calibration * Hand;
        var anchor = space.Offset * calibrated;
        manipulator.Update(Frame(space.ToPhysical(space.StageOffset.Inverse() * calibrated), turn: 1));

        var moved = calibration * (Hand with { Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.3f) });
        var result = manipulator.Update(Frame(space.ToPhysical(space.StageOffset.Inverse() * moved), turn: 1));

        var transformed = result * moved;
        Assert.True(1 - MathF.Abs(Quaternion.Dot(anchor.Orientation, transformed.Orientation)) < 0.0001f);
        var next = space with { Offset = result };
        Assert.True(next.ToPhysical(next.StageOffset.Inverse() * moved).NearlyEquals(moved));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DriverRebaseCompensatedByCalibrationDoesNotGenerateDragOrTurn(bool turn)
    {
        var space = new FlightSpaceTransform(InitialStage, Flight);
        var manipulator = new FreeFlightManipulator(space.Offset);
        InputFrame Read(RigidPose calibrated, bool active) => Frame(
            space.ToPhysical(space.StageOffset.Inverse() * calibrated),
            drag: active && !turn ? 1 : 0, turn: active && turn ? 1 : 0);
        manipulator.Update(Read(Hand, false));
        manipulator.Update(Read(Hand, true));

        // A driver-coordinate reset and its calibration correction leave the visible pose unchanged.
        var calibration = new RigidPose(Quaternion.CreateFromYawPitchRoll(1.2f, 0.3f, -0.2f), new(2, -1, 3));
        var rebasedRaw = calibration.Inverse() * Hand;
        var frame = Read(calibration * rebasedRaw, true);
        var result = manipulator.Update(frame);

        Assert.True(frame.Left.Pose.NearlyEquals(Hand));
        Assert.True(result.NearlyEquals(Flight));
    }

    [Fact]
    public void RecoveryUsesOneCoordinateSystemForHeadAndBothHandsAndPreservesInputState()
    {
        var space = new FlightSpaceTransform(InitialStage, Flight);
        var head = new RigidPose(Quaternion.CreateFromYawPitchRoll(0.2f, 0.1f, 0), new(0, 1.7f, 0));
        var right = new RigidPose(Quaternion.CreateFromYawPitchRoll(-0.4f, 0, 0.1f), new(0.3f, 1.2f, -0.4f));
        var reported = new InputFrame(space.StageOffset.Inverse() * head, true,
            new(space.StageOffset.Inverse() * Hand, 1, 0, 0.5f, true)
                { TrackpadForceActive = true, TrackpadForce = 0.8f },
            new(space.StageOffset.Inverse() * right, 0, 1, 0, false));

        var recovered = space.ToPhysical(reported);

        Assert.True(recovered.Head.NearlyEquals(head));
        Assert.True(recovered.Left.Pose.NearlyEquals(Hand));
        Assert.True(recovered.Right.Pose.NearlyEquals(right));
        Assert.Equal(reported, recovered with { Head = reported.Head,
            Left = recovered.Left with { Pose = reported.Left.Pose },
            Right = recovered.Right with { Pose = reported.Right.Pose } });
    }

    [Fact]
    public void RestoringIdentityFlightReturnsInitialStageAndRetainsCalibration()
    {
        var space = new FlightSpaceTransform(InitialStage, Flight);
        var restored = space with { Offset = RigidPose.Identity };
        var calibration = new RigidPose(Quaternion.CreateFromYawPitchRoll(0.7f, 0.2f, -0.1f), new(1, -2, 3));
        var calibrated = calibration * Hand;

        Assert.True(restored.StageOffset.NearlyEquals(InitialStage));
        Assert.True(restored.ToPhysical(restored.StageOffset.Inverse() * calibrated).NearlyEquals(calibrated));
    }

    private static InputFrame Frame(RigidPose hand, float drag = 0, float turn = 0) => new(
        new(Quaternion.Identity, new(0, 1.7f, 0)), true,
        new(hand, drag, turn, 0, true),
        new(new(Quaternion.Identity, new(0.3f, 1.2f, -0.4f)), 0, 0, 0, true));

    private static void Near(Vector3 expected, Vector3 actual) =>
        Assert.True(Vector3.Distance(expected, actual) < 0.0001f, $"{expected} != {actual}");
}
