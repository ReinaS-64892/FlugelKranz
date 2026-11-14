using System.Numerics;
using FlugelKranz.Core;
using Xunit;

namespace FlugelKranz.Tests;

public class InfiniteWalkingManipulatorTests
{
    private static readonly RigidPose Head = new(Quaternion.Identity, new(0, 1.7f, 0));
    private static readonly RigidPose Left = new(Quaternion.Identity, new(-0.4f, 1.2f, -0.3f));
    private static readonly RigidPose Right = new(Quaternion.Identity, new(0.4f, 1.2f, -0.3f));
    private static readonly InfiniteWalkingSettings Direct =
        InfiniteWalkingSettings.Default with
        {
            DragSmoothSeconds = 0,
            TurnSmoothSeconds = 0,
            TurnHeadSmoothSeconds = 0
        };

    [Fact]
    public void DefaultsMatchInfiniteWalkingParameters()
    {
        var settings = InfiniteWalkingSettings.Default;

        Assert.Equal(0, settings.DragSmoothSeconds);
        Assert.Equal(0, settings.TurnSmoothSeconds);
        Assert.Equal(0, settings.TurnHeadSmoothSeconds);
        Assert.Equal(0, settings.TurnMovementBoostMultiplier);
        Assert.True(settings.ToeTurnOriginEnabled);
        Assert.Equal(0.15f, settings.GroundRangeMetres);
        Assert.Equal(0, (settings with { GroundRangeMetres = -1 }).Normalized().GroundRangeMetres);
        Assert.Equal(0.5f, (settings with { GroundRangeMetres = 1 }).Normalized().GroundRangeMetres);
        Assert.Equal(0.15f, (settings with { GroundRangeMetres = float.NaN }).Normalized().GroundRangeMetres);
        Assert.Equal(0, (settings with { TurnMovementBoostMultiplier = -1 }).Normalized().TurnMovementBoostMultiplier);
        Assert.Equal(2, (settings with { TurnMovementBoostMultiplier = 3 }).Normalized().TurnMovementBoostMultiplier);
        Assert.Equal(FlightMode.InfiniteWalking, FlugelKranzSettings.Default.Mode);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    public void TurnMovementBoostAmplifiesHorizontalHeadMovement(
        float multiplier,
        float expectedMovementScale)
    {
        var settings = Direct with { TurnMovementBoostMultiplier = multiplier };
        var initial = new RigidPose(
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.6f),
            new(2, 0, -1));
        var engine = new InfiniteWalkingManipulator(initial);
        engine.Update(Frame(), 0.1f, settings);
        engine.Update(Frame(leftTurn: 1), 0.1f, settings);
        var movement = new Vector3(0.2f, 0.3f, -0.4f);
        var movedHead = Head with { Position = Head.Position + movement };
        var yaw = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.5f);
        var moved = Frame(leftTurn: 1) with
        {
            Head = movedHead,
            Left = Hand(Left with { Orientation = yaw }, turn: 1)
        };

        var offset = engine.Update(moved, 0.1f, settings);

        Vector3 naturalMovement = Vector3.Transform(movement, initial.Orientation);
        Vector3 expected = initial.Transform(Head.Position) + naturalMovement;
        naturalMovement.Y = 0;
        expected += naturalMovement * (expectedMovementScale - 1);
        Near(expected, offset.Transform(movedHead.Position));
    }

    [Fact]
    public void TurnMovementBoostDoesNotAccumulateMovementWithoutRotation()
    {
        var engine = Armed();
        engine.Update(Frame(leftTurn: 1), 0.1f, Direct);
        var movedHead = Head with { Position = Head.Position + Vector3.UnitX };
        var translated = Frame(leftTurn: 1) with { Head = movedHead };

        Near(movedHead.Position, engine.Update(translated, 0.1f, Direct).Transform(movedHead.Position));

        var yaw = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.5f);
        var rotated = translated with
        {
            Left = Hand(Left with { Orientation = yaw }, turn: 1)
        };
        Near(movedHead.Position, engine.Update(rotated, 0.1f, Direct).Transform(movedHead.Position));
    }

    [Fact]
    public void TurnMovementBoostRebasesAfterHeadTrackingReturns()
    {
        var engine = Armed();
        engine.Update(Frame(leftTurn: 1), 0.1f, Direct);
        engine.Update(Frame(leftTurn: 1) with { HeadTracked = false }, 0.1f, Direct);
        var recoveredHead = Head with { Position = Head.Position + Vector3.UnitX * 5 };
        var yaw = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.5f);
        var recovered = Frame(leftTurn: 1) with
        {
            Head = recoveredHead,
            Left = Hand(Left with { Orientation = yaw }, turn: 1)
        };

        var offset = engine.Update(recovered, 0.1f, Direct);

        Near(recoveredHead.Position, offset.Transform(recoveredHead.Position));
    }

    [Fact]
    public void OneHandDragMovesOnlyTheYAxis()
    {
        var engine = Armed();
        engine.Update(Frame(leftDrag: 1), 0.1f, Direct);
        var movedLeft = Left with { Position = Left.Position + new Vector3(2, 1, 3) };

        var offset = engine.Update(
            Frame(leftDrag: 1) with { Left = Hand(movedLeft, drag: 1) },
            0.1f,
            Direct);

        Near(new(0, -1, 0), offset.Position);
    }

    [Fact]
    public void DragSmoothingFollowsTheAbsoluteTargetAndStopsAfterSettling()
    {
        var settings = Direct with { DragSmoothSeconds = 0.05f };
        var engine = Armed();
        engine.Update(Frame(leftDrag: 1), 0.05f, settings);
        var movedFrame = Frame(leftDrag: 1) with
        {
            Left = Hand(Left with { Position = Left.Position + Vector3.UnitY }, drag: 1)
        };

        var moved = engine.Update(movedFrame, 0.05f, settings);

        float alpha = 1 - MathF.Exp(-1);
        Near(new(0, -alpha, 0), moved.Position);
        var released = engine.Update(
            movedFrame with { Left = Hand(Left with { Position = Left.Position + Vector3.UnitY }, drag: 0) },
            0.05f,
            settings);
        Assert.Equal(new Vector3(0, -1, 0), released.Position);

        var settled = engine.Offset;
        for (int step = 0; step < 20; step++)
            Assert.Equal(settled, engine.Update(
                movedFrame with { Left = Hand(Left with { Position = Left.Position + Vector3.UnitY }, drag: 0) },
                0.05f,
                settings));
    }

    [Fact]
    public void TwoHandDragUsesMidpointAndDoublesItsMovement()
    {
        var engine = Armed();
        engine.Update(Frame(leftDrag: 1, rightDrag: 1), 0.1f, Direct);
        var moved = Frame(leftDrag: 1, rightDrag: 1) with
        {
            Left = Hand(Left with { Position = Left.Position + Vector3.UnitY }, drag: 1),
            Right = Hand(Right with { Position = Right.Position + Vector3.UnitY }, drag: 1)
        };

        var offset = engine.Update(moved, 0.1f, Direct);

        Near(new(0, -2, 0), offset.Position);
    }

    [Fact]
    public void AddingSecondDragHandRebasesWithoutJump()
    {
        var engine = Armed();
        engine.Update(Frame(leftDrag: 1), 0.1f, Direct);
        var oneHandMoved = Frame(leftDrag: 1) with
        {
            Left = Hand(Left with { Position = Left.Position + Vector3.UnitY * 0.5f }, drag: 1)
        };
        var before = engine.Update(oneHandMoved, 0.1f, Direct);
        var both = oneHandMoved with { Right = Hand(Right, drag: 1) };

        var rebased = engine.Update(both, 0.1f, Direct);

        Assert.True(before.NearlyEquals(rebased));
    }

    [Fact]
    public void OneHandTurnUsesOnlyYawAndKeepsHeadFixed()
    {
        var engine = Armed();
        engine.Update(Frame(leftTurn: 1), 0.1f, Direct);
        var yaw = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.7f);
        var pitched = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.5f);

        var yawed = engine.Update(
            Frame(leftTurn: 1) with { Left = Hand(Left with { Orientation = yaw }, turn: 1) },
            0.1f,
            Direct);
        Near(Quaternion.Conjugate(yaw), yawed.Orientation);
        Near(Head.Position, yawed.Transform(Head.Position));

        var pitchEngine = Armed();
        pitchEngine.Update(Frame(leftTurn: 1), 0.1f, Direct);
        var pitchResult = pitchEngine.Update(
            Frame(leftTurn: 1) with { Left = Hand(Left with { Orientation = pitched }, turn: 1) },
            0.1f,
            Direct);
        Near(Quaternion.Identity, pitchResult.Orientation);
    }

    [Fact]
    public void TwoHandTurnUsesHeadYawAndRebasesWhenOneHandReleases()
    {
        var engine = Armed();
        engine.Update(Frame(leftTurn: 1, rightTurn: 1), 0.1f, Direct);
        var yaw = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.5f);
        var controllerYaw = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.8f);
        var both = Frame(leftTurn: 1, rightTurn: 1) with
        {
            Left = Hand(Left with { Orientation = controllerYaw }, turn: 1),
            Right = Hand(Right with { Orientation = controllerYaw }, turn: 1)
        };
        Near(Quaternion.Identity, engine.Update(both, 0.1f, Direct).Orientation);

        both = both with { Head = Head with { Orientation = yaw } };
        var turned = engine.Update(both, 0.1f, Direct);
        Near(Quaternion.Conjugate(yaw), turned.Orientation);
        Near(both.Head.Position, turned.Transform(both.Head.Position));

        var oneHand = both with { Left = Hand(Left with { Orientation = yaw }) };
        var rebased = engine.Update(oneHand, 0.1f, Direct);
        Assert.True(turned.NearlyEquals(rebased));
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 0.2f)]
    [InlineData(true, 0)]
    [InlineData(true, 0.2f)]
    public void ToePivotRemainsFixedDuringSingleAndTwoHandTurnAndSmoothing(bool both, float smoothing)
    {
        var settings = Direct with { TurnSmoothSeconds = smoothing, TurnHeadSmoothSeconds = smoothing };
        var engine = Armed();
        Vector3 toe = new(-0.2f, 0.05f, -0.3f);
        var frame = Frame(leftTurn: 1, rightTurn: both ? 1 : 0) with
        {
            Trackers = [new("toe", toe, true), new("waist", new(0, 1, 0), true)]
        };
        engine.Update(frame, 0.1f, settings);
        var yaw = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.8f);
        frame = frame with
        {
            Head = frame.Head with { Orientation = yaw },
            Left = frame.Left with { Pose = Left with { Orientation = yaw } }
        };
        var result = engine.Update(frame, 0.1f, settings);
        Assert.False(result.NearlyEquals(RigidPose.Identity));
        Near(toe, result.Transform(toe));
    }

    [Fact]
    public void LessMovingToeReceivesMoreWeightThanSlightlyLowerMovingToeWithoutJumping()
    {
        var engine = Armed();
        Vector3 planted = new(-0.2f, 0.08f, 0);
        var frame = Frame(leftTurn: 1) with
        {
            Trackers = [new("planted", planted, true), new("moving", new(0.2f, 0.04f, 0), true)]
        };
        engine.Update(frame, 0.1f, Direct);
        frame = frame with
        {
            Trackers = [new("planted", planted, true), new("moving", new(0.4f, 0.04f, 0), true)]
        };
        Assert.True(engine.Update(frame, 0.1f, Direct).NearlyEquals(RigidPose.Identity));
        frame = frame with { Left = frame.Left with
        {
            Pose = Left with { Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.6f) }
        } };
        var offset = engine.Update(frame, 0.1f, Direct);
        Vector3 moving = frame.Trackers![1].Position;
        Assert.True(Vector3.Distance(planted, offset.Transform(planted)) <
            Vector3.Distance(moving, offset.Transform(moving)));
    }

    [Theory]
    [InlineData(false, true, 0.05f)]
    [InlineData(true, false, 0.05f)]
    [InlineData(true, true, -0.01f)]
    [InlineData(true, true, 0.151f)]
    [InlineData(true, true, float.NaN)]
    public void DisabledInvalidOrOutOfRangeTrackerUsesHead(bool enabled, bool tracked, float height)
    {
        var engine = Armed();
        var settings = Direct with { ToeTurnOriginEnabled = enabled };
        var frame = Frame(leftTurn: 1) with { Trackers = [new("toe", new(1, height, 0), tracked)] };
        engine.Update(frame, 0.1f, settings);
        frame = frame with { Left = frame.Left with
        {
            Pose = Left with { Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.6f) }
        } };
        Near(Head.Position, engine.Update(frame, 0.1f, settings).Transform(Head.Position));
    }

    [Fact]
    public void FloorRangeUsesUnmodifiedStageAndPhysicalPositionsDespiteFlightHeight()
    {
        var initial = new RigidPose(Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.5f), new(3, 5, -2));
        var engine = new InfiniteWalkingManipulator(initial);
        var floor = new RigidPose(Quaternion.Identity, new(0, 2, 0));
        Vector3 toe = new(0.2f, 2.125f, -0.3f);
        var frame = Frame() with { FloorPose = floor, Trackers = [new("toe", toe, true)] };
        engine.Update(frame, 0.1f, Direct);
        frame = frame with { Left = Hand(Left, turn: 1) };
        engine.Update(frame, 0.1f, Direct);
        frame = frame with { Left = Hand(Left with
        {
            Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.7f)
        }, turn: 1) };
        Near(initial.Transform(toe), engine.Update(frame, 0.1f, Direct).Transform(toe));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.15f, 0.15f)]
    [InlineData(0.5f, 0.5f)]
    public void GroundRangeIncludesFloorAndUpperBoundary(float height, float range)
    {
        var engine = Armed();
        Vector3 toe = new(0.3f, height, 0);
        var frame = Frame(leftTurn: 1) with { Trackers = [new("toe", toe, true)] };
        var settings = Direct with { GroundRangeMetres = range };
        engine.Update(frame, 0.1f, settings);
        frame = frame with { Left = Hand(Left with
        {
            Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.8f)
        }, turn: 1) };
        Near(toe, engine.Update(frame, 0.1f, settings).Transform(toe));
    }

    [Theory]
    [InlineData(0.05f, 0.05f)]
    [InlineData(0.02f, 0.12f)]
    public void MultipleToeCandidatesUseWeightedMidpoint(float leftHeight, float rightHeight)
    {
        var engine = Armed();
        Vector3 leftToe = new(-0.3f, leftHeight, 0);
        Vector3 rightToe = new(0.3f, rightHeight, 0);
        var frame = Frame(leftTurn: 1) with
        {
            Trackers = [new("left", leftToe, true), new("right", rightToe, true)]
        };
        engine.Update(frame, 0.1f, Direct);
        frame = frame with { Left = Hand(Left with
        {
            Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.8f)
        }, turn: 1) };
        float leftWeight = MathF.Exp(-(leftHeight - MathF.Min(leftHeight, rightHeight)) / 0.05f);
        float rightWeight = MathF.Exp(-(rightHeight - MathF.Min(leftHeight, rightHeight)) / 0.05f);
        Vector3 pivot = (leftToe * leftWeight + rightToe * rightWeight) / (leftWeight + rightWeight);
        Near(pivot, engine.Update(frame, 0.1f, Direct).Transform(pivot));
    }

    [Fact]
    public void LostToeFallsBackToHeadWithoutJumpingOnLoss()
    {
        var engine = Armed();
        var frame = Frame(leftTurn: 1) with { Trackers = [new("toe", new(1, 0.05f, 0), true)] };
        engine.Update(frame, 0.1f, Direct);
        frame = frame with { Trackers = [] };
        Assert.True(engine.Update(frame, 0.1f, Direct).NearlyEquals(RigidPose.Identity));
        frame = frame with { Left = Hand(Left with
        {
            Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.7f)
        }, turn: 1) };
        Near(Head.Position, engine.Update(frame, 0.1f, Direct).Transform(Head.Position));
    }

    private static InfiniteWalkingManipulator Armed()
    {
        var engine = new InfiniteWalkingManipulator(RigidPose.Identity);
        engine.Update(Frame(), 0.1f, Direct);
        return engine;
    }

    private static InputFrame Frame(
        float leftDrag = 0,
        float rightDrag = 0,
        float leftTurn = 0,
        float rightTurn = 0) =>
        new(Head, true, Hand(Left, leftDrag, leftTurn), Hand(Right, rightDrag, rightTurn));

    private static HandSample Hand(RigidPose pose, float drag = 0, float turn = 0) =>
        new(pose, drag, turn, 0, true);

    private static void Near(Vector3 expected, Vector3 actual) =>
        Assert.True(Vector3.Distance(expected, actual) < 0.0001f, $"{expected} != {actual}");

    private static void Near(Quaternion expected, Quaternion actual) =>
        Assert.True(1 - MathF.Abs(Quaternion.Dot(expected, actual)) < 0.0001f, $"{expected} != {actual}");
}
