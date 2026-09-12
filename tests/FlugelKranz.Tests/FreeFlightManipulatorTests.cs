using System.Numerics;
using FlugelKranz.Core;
using Xunit;

namespace FlugelKranz.Tests;

public class FreeFlightManipulatorTests
{
    private static readonly RigidPose Head = new(Quaternion.Identity, new(0, 1.7f, 0));
    private static readonly RigidPose Left = new(Quaternion.Identity, new(-0.3f, 1.2f, -0.4f));
    private static readonly RigidPose Right = new(Quaternion.Identity, new(0.3f, 1.2f, -0.4f));
    private static InputFrame Frame(float left = 0, float right = 0) =>
        new(Head, true, new(Left, left, 0, 0, true), new(Right, 0, right, 0, true));
    private static FreeFlightManipulator Armed(RigidPose? offset = null)
    {
        var engine = new FreeFlightManipulator(offset ?? RigidPose.Identity);
        engine.Update(Frame());
        return engine;
    }
    private static void Near(Vector3 expected, Vector3 actual) => Assert.True(Vector3.Distance(expected, actual) < 0.0001f, $"{expected} != {actual}");
    private static void Near(Quaternion expected, Quaternion actual) => Assert.True(1 - MathF.Abs(Quaternion.Dot(expected, actual)) < 0.0001f);

    private static readonly FlightMotionSettings HeadOrigin = new()
    {
        TurnOrigin = TurnOrigin.Head,
        InertiaCutoffEnabled = false,
        InertiaAccelerationBoostEnabled = false,
        DragAccelerationMultiplier = 0,
        TurnAccelerationMultiplier = 0,
        DragSmoothSeconds = 0,
        TurnSmoothSeconds = 0
    };

    [Fact]
    public void DragKeepsGrabbedPointFixedInWorld()
    {
        var engine = Armed();
        engine.Update(Frame(1));
        var moved = Frame(1) with { Left = new(Left with { Position = Left.Position + new Vector3(1, 2, 3) }, 1, 0, 0, true) };
        var offset = engine.Update(moved);
        Near(new(-1, -2, -3), offset.Position);
        Near(Left.Position, offset.Transform(moved.Left.Pose.Position));
    }

    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(0, 1, 0)]
    [InlineData(0, 0, 1)]
    [InlineData(1, 2, 3)]
    public void TurnInvertsRotationOnEveryAxisAndKeepsTrackedMidpointFixed(float x, float y, float z)
    {
        var engine = Armed();
        engine.Update(Frame(right: 1));
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.Normalize(new(x, y, z)), 1.1f);
        var offset = engine.Update(Frame(right: 1) with { Right = new(Right with { Orientation = rotation }, 0, 1, 0, true) });
        Near(Quaternion.Conjugate(rotation), offset.Orientation);
        var midpoint = (Head.Position + Left.Position + Right.Position) / 3;
        Near(midpoint, offset.Transform(midpoint));
    }

    [Fact]
    public void HeadTurnOriginOptionKeepsHeadFixed()
    {
        var engine = new FreeFlightManipulator(RigidPose.Identity);
        engine.Update(Frame(), 0.1f, HeadOrigin);
        engine.Update(Frame(right: 1), 0.1f, HeadOrigin);
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.8f);

        var offset = engine.Update(
            Frame(right: 1) with
            {
                Right = new(Right with { Orientation = rotation }, 0, 1, 0, true)
            },
            0.1f,
            HeadOrigin);

        Near(Head.Position, offset.Transform(Head.Position));
    }

    [Fact]
    public void TrackedMidpointExcludesInvalidHands()
    {
        var engine = new FreeFlightManipulator(RigidPose.Identity);
        var initial = Frame() with { Left = default };
        engine.Update(initial);
        engine.Update(initial with { Right = new(Right, 0, 1, 0, true) });
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.6f);
        var moved = initial with
        {
            Right = new(Right with { Orientation = rotation }, 0, 1, 0, true)
        };

        var offset = engine.Update(moved);

        var midpoint = (Head.Position + Right.Position) * 0.5f;
        Near(midpoint, offset.Transform(midpoint));
    }

    [Fact]
    public void TwoHandDragUsesControllerMidpoint()
    {
        var engine = Armed();
        var both = new InputFrame(
            Head,
            true,
            new(Left, 1, 0, 0, true),
            new(Right, 1, 0, 0, true));
        engine.Update(both);
        var spread = both with
        {
            Left = new(Left with { Position = Left.Position - Vector3.UnitX }, 1, 0, 0, true),
            Right = new(Right with { Position = Right.Position + Vector3.UnitX }, 1, 0, 0, true)
        };

        var offset = engine.Update(spread);

        Assert.True(RigidPose.Identity.NearlyEquals(offset));
    }

    [Fact]
    public void AddingSecondDragHandRebasesWithoutJumpOrReleaseVelocity()
    {
        var settings = HeadOrigin with { DragAccelerationMultiplier = 1 };
        var engine = new FreeFlightManipulator(RigidPose.Identity);
        engine.Update(Frame(), 0.1f, settings);
        engine.Update(Frame(1), 0.1f, settings);
        var moved = Frame(1) with
        {
            Left = new(Left with { Position = Left.Position + Vector3.UnitX }, 1, 0, 0, true)
        };
        var before = engine.Update(moved, 0.1f, settings);
        var both = moved with { Right = new(Right, 1, 0, 0, true) };

        var rebased = engine.Update(both, 0.1f, settings);
        var released = engine.Update(Frame(), 0.1f, settings);

        Assert.True(before.NearlyEquals(rebased));
        Assert.True(rebased.NearlyEquals(released));
        Assert.False(engine.HasLinearInertia);
    }

    [Fact]
    public void ReleasingOneOfTwoDragHandsForcesTheRemainingHandToRegrip()
    {
        var engine = Armed();
        var both = new InputFrame(
            Head,
            true,
            new(Left, 1, 0, 0, true),
            new(Right, 1, 0, 0, true));
        engine.Update(both);

        var rightHeld = both with { Left = new(Left, 0, 0, 0, true) };
        var released = engine.Update(rightHeld);
        Assert.False(engine.IsDragging);

        var movedRight = Right with { Position = Right.Position + Vector3.UnitX };
        var stillHeld = rightHeld with { Right = new(movedRight, 1, 0, 0, true) };
        Assert.True(released.NearlyEquals(engine.Update(stillHeld)));
        Assert.False(engine.IsDragging);

        engine.Update(stillHeld with { Right = new(movedRight, 0, 0, 0, true) });
        engine.Update(stillHeld);
        Assert.True(engine.IsDragging);

        var movedAgain = stillHeld with
        {
            Right = new(
                movedRight with { Position = movedRight.Position + Vector3.UnitX },
                1,
                0,
                0,
                true)
        };
        Assert.False(engine.Update(movedAgain).NearlyEquals(released));
    }

    [Fact]
    public void TwoHandTurnIsConstrainedToControllerLine()
    {
        var engine = Armed();
        var both = new InputFrame(
            Head,
            true,
            new(Left, 0, 1, 0, true),
            new(Right, 0, 1, 0, true));
        engine.Update(both);
        var aroundPole = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.7f);
        var rotated = both with
        {
            Left = new(Left with { Orientation = aroundPole }, 0, 1, 0, true),
            Right = new(Right with { Orientation = aroundPole }, 0, 1, 0, true)
        };

        var offset = engine.Update(rotated);
        Near(Quaternion.Conjugate(aroundPole), offset.Orientation);

        var other = Armed();
        other.Update(both);
        var outsideAxis = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.7f);
        var ignored = both with
        {
            Left = new(Left with { Orientation = outsideAxis }, 0, 1, 0, true),
            Right = new(Right with { Orientation = outsideAxis }, 0, 1, 0, true)
        };
        Near(Quaternion.Identity, other.Update(ignored).Orientation);
    }

    [Fact]
    public void AddingSecondTurnHandRebasesWithoutJump()
    {
        var engine = Armed();
        engine.Update(new(Head, true, new(Left, 0, 1, 0, true), new(Right, 0, 0, 0, true)));
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.5f);
        var oneHand = new InputFrame(
            Head,
            true,
            new(Left with { Orientation = rotation }, 0, 1, 0, true),
            new(Right, 0, 0, 0, true));
        var before = engine.Update(oneHand);
        var both = oneHand with { Right = new(Right, 0, 1, 0, true) };

        var rebased = engine.Update(both);

        Assert.True(before.NearlyEquals(rebased));
    }

    [Fact]
    public void DragMovementIsIndependentOfTurnTranslation()
    {
        var turnOnly = Armed();
        turnOnly.Update(Frame(1, 1));
        var rotation = Quaternion.CreateFromYawPitchRoll(0.4f, 0.6f, 0.8f);
        var turnOnlyOffset = turnOnly.Update(
            Frame(right: 1) with { Right = new(Right with { Orientation = rotation }, 0, 1, 0, true) });

        var engine = Armed();
        engine.Update(Frame(1, 1));
        var movedLeft = Left with { Position = new(-1, 1, -1) };
        var movedRight = Right with { Orientation = rotation };
        var offset = engine.Update(new(Head, true, new(movedLeft, 1, 0, 0, true), new(movedRight, 0, 1, 0, true)));

        Near(turnOnlyOffset.Orientation, offset.Orientation);
        Near(Left.Position, offset.Transform(movedLeft.Position));
    }

    [Fact]
    public void MixedDragAndTurnKeepsDragHandAtItsGrabbedPointAcrossFrames()
    {
        var engine = Armed();
        engine.Update(Frame(1, 1));

        for (int step = 1; step <= 8; step++)
        {
            float amount = step / 8f;
            var movedLeft = Left with
            {
                Position = Left.Position + new Vector3(amount, amount * 0.5f, -amount * 0.25f)
            };
            var movedRight = Right with
            {
                Orientation = Quaternion.CreateFromYawPitchRoll(
                    amount * 0.8f,
                    amount * 0.5f,
                    -amount * 0.4f)
            };

            var offset = engine.Update(new(
                Head,
                true,
                new(movedLeft, 1, 0, 0, true),
                new(movedRight, 0, 1, 0, true)));

            Near(Left.Position, offset.Transform(movedLeft.Position));
        }
    }

    [Fact]
    public void SmoothedMixedDragAndTurnConvergesToTheGrabbedPoint()
    {
        var settings = HeadOrigin with { DragSmoothSeconds = 0.05f };
        var engine = new FreeFlightManipulator(RigidPose.Identity);
        engine.Update(Frame(), 0.01f, settings);
        engine.Update(Frame(1, 1), 0.01f, settings);
        var movedLeft = Left with { Position = Left.Position + new Vector3(1, 0.5f, -0.25f) };
        var movedRight = Right with
        {
            Orientation = Quaternion.CreateFromYawPitchRoll(0.8f, 0.5f, -0.4f)
        };
        var moved = new InputFrame(
            Head,
            true,
            new(movedLeft, 1, 0, 0, true),
            new(movedRight, 0, 1, 0, true));

        for (int step = 0; step < 100; step++)
            engine.Update(moved, 0.01f, settings);

        Near(Left.Position, engine.Offset.Transform(movedLeft.Position));
    }

    [Fact]
    public void ExistingOffsetAndRotatedCoordinatesArePreserved()
    {
        var initial = new RigidPose(Quaternion.CreateFromYawPitchRoll(0.4f, 0.5f, 0.6f), new(2, 3, 4));
        var engine = Armed(initial);
        engine.Update(Frame(1));
        var moved = Left with { Position = Left.Position + Vector3.UnitX };
        var offset = engine.Update(Frame(1) with { Left = new(moved, 1, 0, 0, true) });
        Near(initial.Transform(Left.Position), offset.Transform(moved.Position));
        Near(initial.Orientation, offset.Orientation);
    }

    [Fact]
    public void ReleasingGripRetainsOffsetAndNextGrabDoesNotJump()
    {
        var engine = Armed();
        engine.Update(Frame(1));
        var offset = engine.Update(Frame(1) with { Left = new(Left with { Position = Vector3.Zero }, 1, 0, 0, true) });
        Assert.Equal(offset, engine.Update(Frame()));
        Assert.True(offset.NearlyEquals(engine.Update(Frame(1))));
    }

    [Fact]
    public void GripsHeldOnEnableMustBeReleasedBeforeManipulation()
    {
        var engine = new FreeFlightManipulator(RigidPose.Identity);
        engine.Update(Frame(1, 1));
        Assert.False(engine.IsDragging);
        Assert.False(engine.IsTurning);
        engine.Update(Frame());
        engine.Update(Frame(1, 1));
        Assert.True(engine.IsDragging);
        Assert.True(engine.IsTurning);
    }

    [Fact]
    public void HeadTrackingLossPausesAndResumesHeldGrips()
    {
        var engine = Armed();
        engine.Update(Frame(1, 1));
        var offset = engine.Offset;
        Assert.Equal(offset, engine.Update(Frame(1, 1) with { HeadTracked = false }));
        Assert.True(engine.IsDragging);
        Assert.True(engine.IsTurning);

        var movedLeft = Left with { Position = Left.Position + Vector3.UnitX };
        var resumed = engine.Update(Frame(1, 1) with { Left = new(movedLeft, 1, 0, 0, true) });
        Assert.NotEqual(offset, resumed);
        Assert.True(engine.IsDragging);
        Assert.True(engine.IsTurning);
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
        var frame = Frame(1) with { Left = new(Left with { Position = new(float.NaN, 0, 0) }, 1, 0, 0, true) };
        Assert.Equal(RigidPose.Identity, engine.Update(frame));
        frame = Frame(1) with { Left = new(Left, float.NaN, 0, 0, true) };
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
        var frame = Frame(right: 1) with { Right = new(Right with { Orientation = Quaternion.CreateFromYawPitchRoll(0.3f, 0.7f, 1.4f) }, 0, 1, 0, true) };
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
