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
        Assert.Equal(FlightMode.InfiniteWalking, FlugelKranzSettings.Default.Mode);
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
