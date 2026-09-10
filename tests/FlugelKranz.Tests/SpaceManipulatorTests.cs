using System.Numerics;
using FlugelKranz.Core;
using Xunit;

namespace FlugelKranz.Tests;

public class SpaceManipulatorTests
{
    private static readonly RigidPose Head = new(Quaternion.Identity, new(0, 1.7f, 0));
    private static readonly RigidPose Left = new(Quaternion.Identity, new(-0.3f, 1.2f, -0.4f));
    private static readonly RigidPose Right = new(Quaternion.Identity, new(0.3f, 1.2f, -0.4f));
    private static InputFrame Frame(float left = 0, float right = 0) => new(Head, true, new(Left, left, true), new(Right, right, true));
    private static SpaceManipulator Armed(RigidPose? offset = null)
    {
        var engine = new SpaceManipulator(offset ?? RigidPose.Identity);
        engine.Update(Frame());
        return engine;
    }
    private static void Near(Vector3 expected, Vector3 actual) => Assert.True(Vector3.Distance(expected, actual) < 0.0001f, $"{expected} != {actual}");
    private static void Near(Quaternion expected, Quaternion actual) => Assert.True(1 - MathF.Abs(Quaternion.Dot(expected, actual)) < 0.0001f);

    [Fact]
    public void DragKeepsGrabbedPointFixedInWorld()
    {
        var engine = Armed();
        engine.Update(Frame(1));
        var moved = Frame(1) with { Left = new(Left with { Position = Left.Position + new Vector3(1, 2, 3) }, 1, true) };
        var offset = engine.Update(moved);
        Near(new(-1, -2, -3), offset.Position);
        Near(Left.Position, offset.Transform(moved.Left.Pose.Position));
    }

    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(0, 1, 0)]
    [InlineData(0, 0, 1)]
    [InlineData(1, 2, 3)]
    public void TurnInvertsRotationOnEveryAxisAndKeepsHeadFixed(float x, float y, float z)
    {
        var engine = Armed();
        engine.Update(Frame(right: 1));
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.Normalize(new(x, y, z)), 1.1f);
        var offset = engine.Update(Frame(right: 1) with { Right = new(Right with { Orientation = rotation }, 1, true) });
        Near(Quaternion.Conjugate(rotation), offset.Orientation);
        Near(Head.Position, offset.Transform(Head.Position));
    }

    [Fact]
    public void BothHandsUseLeftAnchorWhileTurning()
    {
        var engine = Armed();
        engine.Update(Frame(1, 1));
        var movedLeft = Left with { Position = new(-1, 1, -1) };
        var movedRight = Right with { Orientation = Quaternion.CreateFromYawPitchRoll(0.4f, 0.6f, 0.8f) };
        var offset = engine.Update(new(Head, true, new(movedLeft, 1, true), new(movedRight, 1, true)));
        Near(Left.Position, offset.Transform(movedLeft.Position));
        Near(Quaternion.Conjugate(movedRight.Orientation), offset.Orientation);
    }

    [Fact]
    public void ExistingOffsetAndRotatedCoordinatesArePreserved()
    {
        var initial = new RigidPose(Quaternion.CreateFromYawPitchRoll(0.4f, 0.5f, 0.6f), new(2, 3, 4));
        var engine = Armed(initial);
        engine.Update(Frame(1));
        var moved = Left with { Position = Left.Position + Vector3.UnitX };
        var offset = engine.Update(Frame(1) with { Left = new(moved, 1, true) });
        Near(initial.Transform(Left.Position), offset.Transform(moved.Position));
        Near(initial.Orientation, offset.Orientation);
    }

    [Fact]
    public void ReleasingGripRetainsOffsetAndNextGrabDoesNotJump()
    {
        var engine = Armed();
        engine.Update(Frame(1));
        var offset = engine.Update(Frame(1) with { Left = new(Left with { Position = Vector3.Zero }, 1, true) });
        Assert.Equal(offset, engine.Update(Frame()));
        Assert.True(offset.NearlyEquals(engine.Update(Frame(1))));
    }

    [Fact]
    public void GripsHeldOnEnableMustBeReleasedBeforeManipulation()
    {
        var engine = new SpaceManipulator(RigidPose.Identity);
        engine.Update(Frame(1, 1));
        Assert.False(engine.IsDragging);
        Assert.False(engine.IsTurning);
        engine.Update(Frame());
        engine.Update(Frame(1, 1));
        Assert.True(engine.IsDragging);
        Assert.True(engine.IsTurning);
    }

    [Fact]
    public void TrackingLossRequiresReleaseBeforeRearming()
    {
        var engine = Armed();
        engine.Update(Frame(1, 1));
        var offset = engine.Offset;
        Assert.Equal(offset, engine.Update(Frame(1, 1) with { HeadTracked = false }));
        engine.Update(Frame(1, 1));
        Assert.False(engine.IsDragging);
        Assert.False(engine.IsTurning);
    }

    [Fact]
    public void MissingLeftTrackingDoesNotStopValidRightTurn()
    {
        var engine = Armed();
        engine.Update(Frame(1, 1) with { Left = default });
        Assert.False(engine.IsDragging);
        Assert.True(engine.IsTurning);
    }

    [Fact]
    public void InvalidPoseAndGripCannotAffectOffset()
    {
        var engine = Armed();
        var frame = Frame(1) with { Left = new(Left with { Position = new(float.NaN, 0, 0) }, 1, true) };
        Assert.Equal(RigidPose.Identity, engine.Update(frame));
        frame = Frame(1) with { Left = new(Left, float.NaN, true) };
        Assert.Equal(RigidPose.Identity, engine.Update(frame));
    }

    [Fact]
    public void GripHysteresisPreventsChattering()
    {
        var engine = Armed();
        engine.Update(Frame(0.6f));
        Assert.False(engine.IsDragging);
        engine.Update(Frame(0.7f));
        Assert.True(engine.IsDragging);
        engine.Update(Frame(0.5f));
        Assert.True(engine.IsDragging);
        engine.Update(Frame(0.3f));
        Assert.False(engine.IsDragging);
    }

    [Fact]
    public void UnmovingControllerDoesNotAccumulateRotationOrTranslation()
    {
        var engine = Armed();
        engine.Update(Frame(right: 1));
        var frame = Frame(right: 1) with { Right = new(Right with { Orientation = Quaternion.CreateFromYawPitchRoll(0.3f, 0.7f, 1.4f) }, 1, true) };
        var initial = engine.Update(frame);
        for (int i = 0; i < 10000; i++) engine.Update(frame);
        Assert.True(initial.NearlyEquals(engine.Offset));
    }

    [Fact]
    public void RemovingAppliedOffsetFromStagePosePreventsInputFeedback()
    {
        var stage = new RigidPose(Quaternion.CreateFromYawPitchRoll(0.1f, 0.2f, 0.3f), new(1, 0, 2));
        var offset = new RigidPose(Quaternion.CreateFromYawPitchRoll(0.5f, -0.4f, 1.2f), new(3, 4, 5));
        var reported = stage.Inverse() * offset * Left;
        var recovered = offset.Inverse() * stage * reported;
        Assert.True(Left.NearlyEquals(recovered));
    }

    [Fact]
    public void CommonRootDeltaPreservesMixedTrackingOriginAlignment()
    {
        var headOrigin = new RigidPose(Quaternion.CreateFromYawPitchRoll(0.2f, -0.1f, 0.4f), new(1, 2, 3));
        var leftOrigin = new RigidPose(Quaternion.CreateFromYawPitchRoll(-0.5f, 0.3f, 0.1f), new(-2, 1, 4));
        var rightOrigin = new RigidPose(Quaternion.CreateFromYawPitchRoll(0.4f, 0.2f, -0.3f), new(3, -1, 2));
        var newHeadOrigin = new RigidPose(Quaternion.CreateFromYawPitchRoll(0.7f, -0.2f, 0.6f), new(5, 6, 7));

        var delta = newHeadOrigin * headOrigin.Inverse();
        var leftAfter = delta * leftOrigin;
        var rightAfter = delta * rightOrigin;

        Assert.True(newHeadOrigin.NearlyEquals(delta * headOrigin));
        Assert.True((delta * (leftOrigin * Left)).NearlyEquals(leftAfter * Left));
        Assert.True((delta * (rightOrigin * Right)).NearlyEquals(rightAfter * Right));
    }

    [Fact]
    public void MixedOriginsRecoverTheHmdPhysicalFrameForManipulation()
    {
        var stage = new RigidPose(Quaternion.CreateFromYawPitchRoll(-0.1f, 0.3f, 0.2f), new(2, 1, -3));
        var headOrigin = new RigidPose(Quaternion.CreateFromYawPitchRoll(0.2f, -0.1f, 0.4f), new(1, 2, 3));
        var handOrigin = new RigidPose(Quaternion.CreateFromYawPitchRoll(-0.5f, 0.3f, 0.1f), new(-2, 1, 4));
        var delta = new RigidPose(Quaternion.CreateFromYawPitchRoll(0.4f, 0.6f, -0.2f), new(3, -1, 2));
        var currentHead = delta * headOrigin;
        var currentHand = delta * handOrigin;
        var handInOwnOrigin = new RigidPose(Quaternion.CreateFromYawPitchRoll(0.1f, 0.2f, 0.3f), new(-0.3f, 1.2f, -0.4f));
        var reportedInStage = stage.Inverse() * currentHand * handInOwnOrigin;

        var recoveredInHeadOrigin = headOrigin.Inverse() * handOrigin * currentHand.Inverse() * stage * reportedInStage;

        Assert.True(recoveredInHeadOrigin.NearlyEquals(headOrigin.Inverse() * handOrigin * handInOwnOrigin));
        Assert.True((currentHead * recoveredInHeadOrigin).NearlyEquals(currentHand * handInOwnOrigin));
    }

    [Fact]
    public void QuaternionSignDoesNotChangePoseEquality()
    {
        var q = Quaternion.CreateFromYawPitchRoll(1, 2, 3);
        Assert.True(new RigidPose(q, Vector3.Zero).NearlyEquals(new(-q, Vector3.Zero)));
    }
}
