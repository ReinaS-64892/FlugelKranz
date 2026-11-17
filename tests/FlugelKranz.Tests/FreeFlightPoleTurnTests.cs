using System.Numerics;
using FlugelKranz.Core;
using Xunit;

namespace FlugelKranz.Tests;

public class FreeFlightPoleTurnTests
{
    private static readonly Vector3 Pivot = new(0, 1.2f, -0.4f);
    private static readonly FlightMotionSettings Settings = FlightMotionSettings.Default with
    {
        InertiaCutoffEnabled = false,
        InertiaDecelerationPerSecond = 0,
        TurnAccelerationMultiplier = 0,
        TwoHandTurnAccelerationMultiplier = 0,
        TurnSmoothSeconds = 0,
        DragSmoothSeconds = 0,
        BrakeRampSeconds = 0,
        VectorRotationMultiplier = 0
    };

    private static InputFrame Frame(Vector3 axis, Quaternion rotation, float turn = 1, bool drag = false) => new(
        new(Quaternion.Identity, new(0, 1.7f, 0)), true,
        new(new(rotation, Pivot - axis * 0.3f), drag ? 1 : 0, turn, 0, true),
        new(new(rotation, Pivot + axis * 0.3f), drag ? 1 : 0, turn, 0, true));

    private static FreeFlightManipulator Armed(RigidPose initial, FlightMotionSettings settings)
    {
        var engine = new FreeFlightManipulator(initial);
        engine.Update(Frame(Vector3.UnitX, Quaternion.Identity, 0), 0.01f, settings);
        return engine;
    }

    private static void Near(Vector3 expected, Vector3 actual) =>
        Assert.True(Vector3.Distance(expected, actual) < 0.0001f, $"{expected} != {actual}");

    private static void Near(Quaternion expected, Quaternion actual) =>
        Assert.True(1 - MathF.Abs(Quaternion.Dot(expected, actual)) < 0.0001f, $"{expected} != {actual}");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MovingHandLineKeepsWorldDirectionAndPivotWithoutLinearInertia(bool drag)
    {
        var initial = new RigidPose(Quaternion.CreateFromYawPitchRoll(0.4f, -0.3f, 0.2f), new(2, 3, 4));
        var engine = Armed(initial, Settings);
        engine.Update(Frame(Vector3.UnitX, Quaternion.Identity, drag: drag), 0.01f, Settings);
        for (int step = 1; step <= 60; step++)
        {
            var swing = Quaternion.CreateFromAxisAngle(Vector3.UnitY, step * 0.04f);
            var axis = Vector3.Transform(Vector3.UnitX, swing);
            // Moving positions alone drives swing, even with stationary wrists.
            var offset = engine.Update(Frame(axis, Quaternion.Identity, drag: drag), 0.01f, Settings);
            Near(initial.Orientation * Quaternion.Conjugate(swing), offset.Orientation);
            Near(Vector3.Transform(Vector3.UnitX, initial.Orientation), Vector3.Transform(axis, offset.Orientation));
            Near(initial.Transform(Pivot), offset.Transform(Pivot));
        }
        engine.Update(Frame(Vector3.UnitX, Quaternion.Identity, 0), 0.01f, Settings);
        Assert.False(engine.HasLinearInertia);
    }

    [Fact]
    public void SwingAndWristTwistAreComposedWithoutCountingSwingTwice()
    {
        var engine = Armed(RigidPose.Identity, Settings);
        engine.Update(Frame(Vector3.UnitX, Quaternion.Identity), 0.01f, Settings);
        for (int step = 1; step <= 60; step++)
        {
            var swing = Quaternion.CreateFromAxisAngle(Vector3.UnitY, step * 0.03f);
            var axis = Vector3.Transform(Vector3.UnitX, swing);
            var rotation = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(axis, step * 0.02f) * swing);
            var offset = engine.Update(Frame(axis, rotation), 0.01f, Settings);
            Near(Quaternion.Conjugate(rotation), offset.Orientation);
            Near(Vector3.UnitX, Vector3.Transform(axis, offset.Orientation));
        }
    }

    [Fact]
    public void TranslatingAndSpreadingHandsDoesNotMoveOrAccelerateSpace()
    {
        var engine = Armed(RigidPose.Identity, Settings);
        engine.Update(Frame(Vector3.UnitX, Quaternion.Identity), 0.01f, Settings);
        var frame = Frame(Vector3.UnitX * 3, Quaternion.Identity);
        frame = frame with
        {
            Left = frame.Left with { Pose = frame.Left.Pose with { Position = frame.Left.Pose.Position + Vector3.One } },
            Right = frame.Right with { Pose = frame.Right.Pose with { Position = frame.Right.Pose.Position + Vector3.One } }
        };
        var offset = engine.Update(frame, 0.01f, Settings);
        Assert.True(RigidPose.Identity.NearlyEquals(offset));
        engine.Update(frame with { Left = frame.Left with { Turn = 0 } }, 0.01f, Settings);
        Assert.False(engine.HasAngularInertia);
        Assert.False(engine.HasLinearInertia);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.2f)]
    public void ReleasingOneHandUsesRawSwingVelocityAndRequiresRegrip(float smoothSeconds)
    {
        var settings = Settings with { TurnSmoothSeconds = smoothSeconds, TurnAccelerationMultiplier = 4, TwoHandTurnAccelerationMultiplier = 0.3f };
        var engine = Armed(RigidPose.Identity, settings);
        engine.Update(Frame(Vector3.UnitX, Quaternion.Identity), 0.01f, settings);
        var swing = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.1f);
        var frame = Frame(Vector3.Transform(Vector3.UnitX, swing), Quaternion.Identity);
        var before = engine.Update(frame, 0.01f, settings);
        if (smoothSeconds > 0)
            Assert.True(MathF.Abs(Quaternion.Dot(Quaternion.Conjugate(swing), before.Orientation)) < 0.9999f);
        var released = frame with { Left = frame.Left with { Turn = 0 } };
        var after = engine.Update(released, 0.01f, settings);
        Near(Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -0.03f) * before.Orientation, after.Orientation);
        Near(Pivot, after.Transform(Pivot));
        Assert.True(engine.HasAngularInertia);
        Assert.False(engine.HasLinearInertia);
        Assert.False(engine.IsTurning);
        engine.Update(released, 0.01f, settings);
        Assert.False(engine.IsTurning);
        engine.Update(released with { Right = released.Right with { Turn = 0 } }, 0.01f, settings);
        engine.Update(released, 0.01f, settings);
        Assert.True(engine.IsTurning);
    }

    [Fact]
    public void SmoothedSwingConvergesWhilePreservingPivotAndDiscardsRemainderOnRelease()
    {
        var settings = Settings with { TurnSmoothSeconds = 0.1f };
        var engine = Armed(RigidPose.Identity, settings);
        engine.Update(Frame(Vector3.UnitX, Quaternion.Identity), 0.01f, settings);
        var swing = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.8f);
        var frame = Frame(Vector3.Transform(Vector3.UnitX, swing), Quaternion.Identity);
        var first = engine.Update(frame, 0.01f, settings);
        Near(Quaternion.Slerp(Quaternion.Identity, Quaternion.Conjugate(swing), 1 - MathF.Exp(-0.1f)), first.Orientation);
        Near(Pivot, first.Transform(Pivot));
        for (int step = 0; step < 200; step++)
            engine.Update(frame, 0.01f, settings);
        Near(Quaternion.Conjugate(swing), engine.Offset.Orientation);
        // A further swing is only partly presented before release.
        frame = Frame(Vector3.UnitZ, Quaternion.Identity);
        var before = engine.Update(frame, 0.01f, settings);
        var after = engine.Update(frame with { Left = frame.Left with { Turn = 0 } }, 0.01f, settings);
        Assert.True(before.NearlyEquals(after));
    }

    [Fact]
    public void CoincidentHandsRebaseDirectionWithoutJumpAndOppositeDirectionsStayFinite()
    {
        var engine = Armed(RigidPose.Identity, Settings);
        engine.Update(Frame(Vector3.UnitX, Quaternion.Identity), 0.01f, Settings);
        Assert.True(RigidPose.Identity.NearlyEquals(engine.Update(Frame(Vector3.Zero, Quaternion.Identity), 0.01f, Settings)));
        Assert.True(RigidPose.Identity.NearlyEquals(engine.Update(Frame(Vector3.UnitZ, Quaternion.Identity), 0.01f, Settings)));
        var after = engine.Update(Frame(-Vector3.UnitZ, Quaternion.Identity), 0.01f, Settings);
        Assert.True(after.IsValid);
        Near(Vector3.UnitZ, Vector3.Transform(-Vector3.UnitZ, after.Orientation));
    }

    [Fact]
    public void AddingSecondHandRebasesPoleAndStationaryReleaseDoesNotAccelerate()
    {
        var settings = Settings with { TwoHandTurnAccelerationMultiplier = 1 };
        var engine = Armed(RigidPose.Identity, settings);
        var one = Frame(Vector3.UnitX, Quaternion.Identity);
        one = one with { Left = one.Left with { Turn = 0 } };
        engine.Update(one, 0.01f, settings);
        one = one with
        {
            Right = one.Right with { Pose = one.Right.Pose with { Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.4f) } }
        };
        var before = engine.Update(one, 0.01f, settings);
        var both = one with { Left = one.Left with { Turn = 1 } };
        var rebased = engine.Update(both, 0.01f, settings);
        Assert.True(before.NearlyEquals(rebased));
        var axis = Vector3.Transform(Vector3.UnitX, Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.3f));
        var moved = Frame(axis, Quaternion.Identity);
        var rotated = engine.Update(moved, 0.01f, settings);
        Near(Vector3.Transform(Vector3.UnitX, before.Orientation), Vector3.Transform(axis, rotated.Orientation));
        engine.Update(moved, 0.01f, settings);
        var released = engine.Update(moved with { Left = moved.Left with { Turn = 0 } }, 0.01f, settings);
        Assert.True(rotated.NearlyEquals(released));
        Assert.False(engine.HasAngularInertia);
    }

    [Theory]
    [InlineData(true, 0, false)]
    [InlineData(true, 1, false)]
    [InlineData(true, 2.5f, false)]
    [InlineData(true, 2.5f, true)]
    [InlineData(false, 0, false)]
    [InlineData(false, 2.5f, false)]
    public void TurnReleaseUsesOnlyTheMultiplierForItsHandCount(bool both, float multiplier, bool releaseOnlyLeft)
    {
        var settings = Settings with
        {
            TurnAccelerationMultiplier = 0.2f,
            TwoHandTurnAccelerationMultiplier = multiplier
        };
        var engine = Armed(RigidPose.Identity, settings);
        var frame = Frame(Vector3.UnitX, Quaternion.Identity);
        if (!both)
            frame = frame with { Left = frame.Left with { Turn = 0 } };
        engine.Update(frame, 0.01f, settings);
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.1f);
        frame = frame with
        {
            Left = frame.Left with { Pose = frame.Left.Pose with { Orientation = rotation } },
            Right = frame.Right with { Pose = frame.Right.Pose with { Orientation = rotation } }
        };
        var before = engine.Update(frame, 0.01f, settings);
        Near(Quaternion.Conjugate(rotation), before.Orientation);
        var released = frame with
        {
            Left = frame.Left with { Turn = 0 },
            Right = frame.Right with { Turn = releaseOnlyLeft ? 1 : 0 }
        };
        var after = engine.Update(released, 0.01f, settings);
        float appliedMultiplier = both ? multiplier : 0.2f;
        Near(Quaternion.CreateFromAxisAngle(Vector3.UnitX, -0.1f * appliedMultiplier) * before.Orientation, after.Orientation);
        Assert.Equal(appliedMultiplier > 0, engine.HasAngularInertia);
        Assert.False(engine.IsTurning);
    }
}
