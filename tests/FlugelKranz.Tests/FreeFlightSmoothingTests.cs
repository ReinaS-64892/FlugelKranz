using System.Numerics;
using FlugelKranz.Core;
using Xunit;

namespace FlugelKranz.Tests;

public class FreeFlightSmoothingTests
{
    private const float StepSeconds = 0.01f;
    private static readonly Vector3 Head = new(0, 1.7f, 0);
    private static readonly Vector3 Left = new(-0.3f, 1.2f, -0.4f);
    private static readonly Vector3 Right = new(0.3f, 1.2f, -0.4f);
    private static readonly FlightMotionSettings Settings = FlightMotionSettings.Default with
    {
        TurnOrigin = TurnOrigin.Head,
        InertiaCutoffEnabled = false,
        InertiaAccelerationBoostEnabled = false,
        InertiaDecelerationPerSecond = 0,
        InertiaStopDisplacementMetres = 0.000001f,
        DecelerationExemptionEnabled = false,
        ZAccelerationMultiplier = 1,
        TurnAccelerationMultiplier = 0,
        TwoHandTurnAccelerationMultiplier = 0,
        VectorRotationMultiplier = 0
    };

    [Theory]
    [InlineData(0, 0.05f)]
    [InlineData(0.01f, 0.05f)]
    [InlineData(0.5f, 0)]
    [InlineData(1, 1)]
    public void StartingHoldingAndReleasingTurnPreservesLinearInertia(float dragSmooth, float turnSmooth)
    {
        var settings = Settings with { DragSmoothSeconds = dragSmooth, TurnSmoothSeconds = turnSmooth };
        var engine = Moving(settings, RigidPose.Identity);
        var expectedStep = new Vector3(-StepSeconds, 0, 0);

        for (int step = 0; step < 30; step++)
        {
            var before = engine.Offset;
            var after = engine.Update(Frame(turn: step < 15), StepSeconds, settings);
            Near(expectedStep, after.Transform(Head) - before.Transform(Head));
            Assert.True(engine.HasLinearInertia);
        }
    }

    [Theory]
    [InlineData(0, 0.05f, false)]
    [InlineData(0.01f, 0.05f, false)]
    [InlineData(0.05f, 0.05f, false)]
    [InlineData(1, 0, false)]
    [InlineData(0.01f, 0.05f, true)]
    public void SmoothedTurnKeepsItsPivotOnTheInertiaPath(float dragSmooth, float turnSmooth, bool twoHands)
    {
        var settings = Settings with { DragSmoothSeconds = dragSmooth, TurnSmoothSeconds = turnSmooth };
        var initial = new RigidPose(Quaternion.CreateFromYawPitchRoll(0.3f, -0.4f, 0.2f), new(3, 2, -1));
        var engine = Moving(settings, initial);
        var frame = Frame(turn: true, twoHands: twoHands);
        Vector3 pivot = twoHands ? (Left + Right) * 0.5f : Head;
        Vector3 expectedStep = Vector3.Transform(new Vector3(-StepSeconds, 0, 0), initial.Orientation);

        for (int step = 0; step < 30; step++)
        {
            var before = engine.Offset;
            var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, step * 0.02f);
            frame = frame with
            {
                Left = frame.Left with { Pose = frame.Left.Pose with { Orientation = rotation } },
                Right = frame.Right with { Pose = frame.Right.Pose with { Orientation = rotation } }
            };
            var after = engine.Update(frame, StepSeconds, settings);
            Near(expectedStep, after.Transform(pivot) - before.Transform(pivot));
        }
    }

    [Theory]
    [InlineData(TurnOrigin.Head)]
    [InlineData(TurnOrigin.TrackedElementsMidpoint)]
    public void SmoothedTurnPreservesTheCurrentPhysicalPivotWhileItMoves(TurnOrigin origin)
    {
        var settings = Settings with { TurnOrigin = origin, TurnSmoothSeconds = 0.2f };
        var engine = new FreeFlightManipulator(RigidPose.Identity);
        engine.Update(Frame(), StepSeconds, settings);
        engine.Update(Frame(turn: true), StepSeconds, settings);

        for (int step = 1; step <= 30; step++)
        {
            var rotation = Quaternion.CreateFromYawPitchRoll(step * 0.03f, step * 0.04f, -step * 0.02f);
            var displacement = new Vector3(0.01f, -0.02f, 0.03f) * step;
            var frame = Frame(turn: true) with
            {
                Head = new(Quaternion.Identity, Head + displacement),
                Left = new(new(Quaternion.Identity, Left + displacement * 2), 0, 0, 0, true),
                Right = new(new(rotation, Right - displacement), 0, 1, 0, true)
            };
            Vector3 pivot = origin == TurnOrigin.Head ? frame.Head.Position :
                (frame.Head.Position * 2.5f + frame.Left.Pose.Position + frame.Right.Pose.Position) / 4.5f;
            Vector3 expected = engine.Offset.Transform(pivot);
            var after = engine.Update(frame, StepSeconds, settings);
            Near(expected, after.Transform(pivot));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.5f)]
    [InlineData(1)]
    public void InertiaDirectionFollowsTheAppliedSmoothedRotation(float multiplier)
    {
        var settings = Settings with { TurnSmoothSeconds = 0.1f, VectorRotationMultiplier = multiplier };
        var engine = Moving(settings, RigidPose.Identity);
        engine.Update(Frame(turn: true), StepSeconds, settings);
        var expectedStep = new Vector3(-StepSeconds, 0, 0);

        for (int step = 1; step <= 30; step++)
        {
            var before = engine.Offset;
            var frame = Frame(turn: true) with
            {
                Right = new(new(Quaternion.CreateFromAxisAngle(Vector3.UnitY, step * 0.03f), Right), 0, 1, 0, true)
            };
            var after = engine.Update(frame, StepSeconds, settings);
            Near(expectedStep, after.Transform(Head) - before.Transform(Head));
            var delta = Quaternion.Normalize(after.Orientation * Quaternion.Conjugate(before.Orientation));
            expectedStep = Vector3.Transform(expectedStep, Quaternion.Slerp(Quaternion.Identity, delta, multiplier));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.01f)]
    [InlineData(1)]
    public void RegrippedDragAppliesBrakingInertiaWithoutSmoothing(float dragSmooth)
    {
        var settings = Settings with { DragSmoothSeconds = dragSmooth, BrakeRampSeconds = 0.4f };
        var engine = Moving(settings, RigidPose.Identity);

        for (int step = 1; step <= 40; step++)
        {
            float progress = step * StepSeconds / settings.BrakeRampSeconds;
            float factor = 1 - progress * progress * (3 - 2 * progress);
            Vector3 before = engine.Offset.Transform(Left);
            var after = engine.Update(Frame(drag: true), StepSeconds, settings);
            Near(new Vector3(-StepSeconds * factor, 0, 0), after.Transform(Left) - before);
        }
        Assert.False(engine.HasLinearInertia);
    }

    [Fact]
    public void ReleasingSmoothedTurnDuringDragDiscardsTheUnappliedRotation()
    {
        var settings = Settings with { TurnSmoothSeconds = 0.2f };
        var engine = new FreeFlightManipulator(RigidPose.Identity);
        engine.Update(Frame(), StepSeconds, settings);
        engine.Update(Frame(drag: true, turn: true), StepSeconds, settings);
        var frame = Frame(drag: true, turn: true) with
        {
            Right = new(new(Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.5f), Right), 0, 1, 0, true)
        };
        var before = engine.Update(frame, StepSeconds, settings);
        var after = engine.Update(frame with { Right = frame.Right with { Turn = 0 } }, StepSeconds, settings);

        Assert.True(before.NearlyEquals(after));
        Near(before.Transform(Left), after.Transform(Left));
        Assert.False(engine.HasAngularInertia);
    }

    private static FreeFlightManipulator Moving(FlightMotionSettings settings, RigidPose initial)
    {
        var engine = new FreeFlightManipulator(initial);
        engine.Update(Frame(), StepSeconds, settings);
        engine.Update(Frame(drag: true), StepSeconds, settings);
        var moved = Frame(drag: true) with
        {
            Left = new(new(Quaternion.Identity, Left + new Vector3(StepSeconds, 0, 0)), 1, 0, 0, true)
        };
        engine.Update(moved, StepSeconds, settings);
        engine.Update(moved with { Left = moved.Left with { Drag = 0 } }, StepSeconds, settings);
        Assert.True(engine.HasLinearInertia);
        return engine;
    }

    private static InputFrame Frame(bool drag = false, bool turn = false, bool twoHands = false) => new(
        new(Quaternion.Identity, Head), true,
        new(new(Quaternion.Identity, Left), drag ? 1 : 0, turn && twoHands ? 1 : 0, 0, true),
        new(new(Quaternion.Identity, Right), 0, turn ? 1 : 0, 0, true));

    private static void Near(Vector3 expected, Vector3 actual) =>
        Assert.True(Vector3.Distance(expected, actual) < 0.0001f, $"{expected} != {actual}");
}
