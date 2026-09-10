using System.Numerics;
using FlugelKranz.Core;
using Xunit;

namespace FlugelKranz.Tests;

public class InertiaTests
{
    private static readonly RigidPose Identity = RigidPose.Identity;
    private static readonly RigidPose Head = new(Quaternion.Identity, new(0, 1.7f, 0));
    private static readonly FlightMotionSettings Unfiltered = new()
    {
        InertiaCutoffEnabled = false,
        DirectionCorrectionEnabled = false,
        InertiaDecelerationPerSecond = 0,
        TurnAccelerationMultiplier = 1,
        DragSmoothSeconds = 0,
        TurnSmoothSeconds = 0
    };

    [Fact]
    public void DefaultsMatchTheExposedFlightParameters()
    {
        var settings = FlightMotionSettings.Default;

        Assert.False(settings.StepMode);
        Assert.True(settings.InertiaCutoffEnabled);
        Assert.Equal(0.4f, settings.DragCutoffMetresPerSecond);
        Assert.Equal(MathF.PI / 4, settings.TurnCutoffRadiansPerSecond);
        Assert.Equal(1, settings.DragAccelerationMultiplier);
        Assert.Equal(0.5f, settings.TurnAccelerationMultiplier);
        Assert.Equal(1, settings.VectorRotationMultiplier);
        Assert.Equal(2, settings.InertiaDecelerationPerSecond);
        Assert.True(settings.DecelerationExemptionEnabled);
        Assert.Equal(0.2f, settings.DragDecelerationExemptionDurationRatio);
        Assert.Equal(0.05f, settings.TurnDecelerationExemptionDurationRatio);
        Assert.Equal(0.9f, settings.DecelerationExemptionStrength);
        Assert.Equal(0.01f, settings.DragSmoothSeconds);
        Assert.Equal(0.05f, settings.TurnSmoothSeconds);
        Assert.Equal(1, settings.BrakeStrength);
        Assert.True(settings.DirectionCorrectionEnabled);
        Assert.Equal(1, settings.DragCorrectionMaxSeconds);
        Assert.Equal(1, settings.DragCorrectionStrength);
        Assert.Equal(0.5f, settings.TurnCorrectionMaxSeconds);
        Assert.Equal(1, settings.TurnCorrectionStrength);
    }

    [Fact]
    public void ReleasedDragContinuesAtMeasuredVelocity()
    {
        var engine = BeginDrag(Unfiltered);
        engine.Update(Frame(leftX: 1, leftGrip: 1), 0.1f, Unfiltered);

        var released = engine.Update(Frame(leftX: 1), 0.1f, Unfiltered);
        var continued = engine.Update(Frame(leftX: 1), 0.1f, Unfiltered);

        Near(new(-2, 0, 0), released.Position);
        Near(new(-3, 0, 0), continued.Position);
        Assert.True(engine.HasLinearInertia);
    }

    [Fact]
    public void StepModeStopsAtRelease()
    {
        var settings = Unfiltered with { StepMode = true };
        var engine = BeginDrag(settings);
        var moved = engine.Update(Frame(leftX: 1, leftGrip: 1), 0.1f, settings);
        var released = engine.Update(Frame(leftX: 1), 0.1f, settings);

        Assert.Equal(moved, released);
        Assert.False(engine.HasLinearInertia);
    }

    [Fact]
    public void DragCutoffRejectsVelocityBelowThreshold()
    {
        var settings = Unfiltered with
        {
            InertiaCutoffEnabled = true,
            DragCutoffMetresPerSecond = 0.05f
        };
        var engine = BeginDrag(settings);
        var moved = engine.Update(Frame(leftX: 0.004f, leftGrip: 1), 0.1f, settings);
        var released = engine.Update(Frame(leftX: 0.004f), 0.1f, settings);

        Assert.Equal(moved, released);
        Assert.False(engine.HasLinearInertia);
    }

    [Fact]
    public void DragMultiplierScalesReleasedVelocity()
    {
        var settings = Unfiltered with { DragAccelerationMultiplier = 0.5f };
        var engine = BeginDrag(settings);
        engine.Update(Frame(leftX: 1, leftGrip: 1), 0.1f, settings);

        var released = engine.Update(Frame(leftX: 1), 0.1f, settings);

        Near(new(-1.5f, 0, 0), released.Position);
    }

    [Fact]
    public void InertiaDecelerationReducesVelocityEveryFreeStep()
    {
        var settings = Unfiltered with
        {
            InertiaDecelerationPerSecond = 1,
            DecelerationExemptionEnabled = false
        };
        var engine = BeginDrag(settings);
        engine.Update(Frame(leftX: 1, leftGrip: 1), 0.1f, settings);
        engine.Update(Frame(leftX: 1), 0.1f, settings);

        var continued = engine.Update(Frame(leftX: 1), 0.1f, settings);

        Near(new(-2 - MathF.Exp(-0.1f), 0, 0), continued.Position);
    }

    [Fact]
    public void DecelerationDoesNotApplyCutoffAfterInertiaStarts()
    {
        var settings = Unfiltered with
        {
            InertiaCutoffEnabled = true,
            DragCutoffMetresPerSecond = 0.05f,
            InertiaDecelerationPerSecond = 10,
            DecelerationExemptionEnabled = false
        };
        var engine = BeginDrag(settings);
        engine.Update(Frame(leftX: 0.006f, leftGrip: 1), 0.1f, settings);

        var released = engine.Update(Frame(leftX: 0.006f), 0.1f, settings);
        var continued = engine.Update(Frame(leftX: 0.006f), 0.1f, settings);

        Assert.NotEqual(released, continued);
        Assert.True(engine.HasLinearInertia);
    }

    [Fact]
    public void FullDecelerationExemptionPreservesVelocityDuringExemptPeriod()
    {
        var settings = Unfiltered with
        {
            InertiaDecelerationPerSecond = 1,
            DecelerationExemptionEnabled = true,
            DragDecelerationExemptionDurationRatio = 0.2f,
            DecelerationExemptionStrength = 1
        };
        var engine = BeginDrag(settings);
        engine.Update(Frame(leftX: 1, leftGrip: 1), 0.1f, settings);
        engine.Update(Frame(leftX: 1), 0.1f, settings);

        var continued = engine.Update(Frame(leftX: 1), 0.1f, settings);

        Near(new(-3, 0, 0), continued.Position);
    }

    [Fact]
    public void TurnDecelerationExemptionUsesTurnDuration()
    {
        var settings = Unfiltered with
        {
            InertiaDecelerationPerSecond = 1,
            DecelerationExemptionEnabled = true,
            DragDecelerationExemptionDurationRatio = 0,
            TurnDecelerationExemptionDurationRatio = 1,
            DecelerationExemptionStrength = 1
        };
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.2f);
        var engine = BeginTurn(settings);
        engine.Update(Frame(rightGrip: 1, rightRotation: rotation), 0.1f, settings);
        engine.Update(Frame(rightRotation: rotation), 0.1f, settings);

        var continued = engine.Update(Frame(rightRotation: rotation), 0.1f, settings);

        Near(Quaternion.CreateFromAxisAngle(Vector3.UnitY, -0.6f), continued.Orientation);
    }

    [Fact]
    public void HandTrackingLossCancelsInertiaInsteadOfLaunching()
    {
        var engine = BeginDrag(Unfiltered);
        engine.Update(Frame(leftX: 1, leftGrip: 1), 0.1f, Unfiltered);
        var lost = Frame(leftX: 1, leftGrip: 1) with { Left = default };

        var atLoss = engine.Update(lost, 0.1f, Unfiltered);
        var afterLoss = engine.Update(Frame(leftX: 1), 0.1f, Unfiltered);

        Near(new(-1, 0, 0), atLoss.Position);
        Assert.Equal(atLoss, afterLoss);
        Assert.False(engine.HasLinearInertia);
    }

    [Fact]
    public void HeadTrackingLossPausesAndResumesInertia()
    {
        var engine = BeginDrag(Unfiltered);
        engine.Update(Frame(leftX: 1, leftGrip: 1), 0.1f, Unfiltered);
        var moving = engine.Update(Frame(leftX: 1), 0.1f, Unfiltered);

        var paused = engine.Update(Frame(leftX: 1) with { HeadTracked = false }, 0.1f, Unfiltered);
        var resumed = engine.Update(Frame(leftX: 1), 0.1f, Unfiltered);

        Assert.Equal(moving, paused);
        Assert.NotEqual(paused, resumed);
        Assert.True(engine.HasLinearInertia);
    }

    [Fact]
    public void SmoothUsesElapsedTime()
    {
        var settings = Unfiltered with { DragSmoothSeconds = 0.05f };
        var engine = BeginDrag(settings);

        var moved = engine.Update(Frame(leftX: 1, leftGrip: 1), 0.05f, settings);

        float expected = -(1 - MathF.Exp(-1));
        Near(new(expected, 0, 0), moved.Position);
    }

    [Fact]
    public void DragInertiaUsesUnsmoothedControllerMotion()
    {
        var settings = Unfiltered with { DragSmoothSeconds = 1 };
        var engine = BeginDrag(settings);
        var moved = engine.Update(Frame(leftX: 1, leftGrip: 1), 0.1f, settings);

        var released = engine.Update(Frame(leftX: 1), 0.1f, settings);

        Near(moved.Position + new Vector3(-1, 0, 0), released.Position);
    }

    [Theory]
    [InlineData(1, -2)]
    [InlineData(0.5f, -2.5f)]
    [InlineData(0, -3)]
    public void RegripBrakeRemovesConfiguredShareOfInertia(float brake, float expectedX)
    {
        var settings = Unfiltered with { BrakeStrength = brake };
        var engine = BeginDrag(settings);
        engine.Update(Frame(leftX: 1, leftGrip: 1), 0.1f, settings);
        engine.Update(Frame(leftX: 1), 0.1f, settings);

        var regripped = engine.Update(Frame(leftX: 1, leftGrip: 1), 0.1f, settings);

        Near(new(expectedX, 0, 0), regripped.Position);
    }

    [Fact]
    public void TurnGripDoesNotBrakeLinearInertia()
    {
        var settings = Unfiltered with { BrakeStrength = 1 };
        var engine = BeginDrag(settings);
        engine.Update(Frame(leftX: 1, leftGrip: 1), 0.1f, settings);
        engine.Update(Frame(leftX: 1), 0.1f, settings);

        var beganTurn = engine.Update(Frame(leftX: 1, rightGrip: 1), 0.1f, settings);

        Near(new(-3, 0, 0), beganTurn.Position);
        Assert.True(engine.HasLinearInertia);
    }

    [Fact]
    public void DragGripDoesNotBrakeAngularInertia()
    {
        var settings = Unfiltered with { BrakeStrength = 1 };
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.2f);
        var engine = BeginTurn(settings);
        engine.Update(Frame(rightGrip: 1, rightRotation: rotation), 0.1f, settings);
        engine.Update(Frame(rightRotation: rotation), 0.1f, settings);

        var beganDrag = engine.Update(Frame(leftGrip: 1, rightRotation: rotation), 0.1f, settings);

        Near(Quaternion.CreateFromAxisAngle(Vector3.UnitY, -0.6f), beganDrag.Orientation);
        Assert.True(engine.HasAngularInertia);
    }

    [Fact]
    public void DragBrakeClearsLinearDecelerationExemption()
    {
        var settings = Unfiltered with
        {
            BrakeStrength = 0.5f,
            InertiaDecelerationPerSecond = 1,
            DecelerationExemptionEnabled = true,
            DragDecelerationExemptionDurationRatio = 1,
            DecelerationExemptionStrength = 1
        };
        var engine = BeginDrag(settings);
        engine.Update(Frame(leftX: 1, leftGrip: 1), 0.1f, settings);
        var beforeBrake = engine.Update(Frame(leftX: 1), 0.1f, settings);
        var braking = engine.Update(Frame(leftX: 1, leftGrip: 1), 0.1f, settings);
        var afterBrake = engine.Update(Frame(leftX: 1, leftGrip: 1), 0.1f, settings);

        float brakingStep = Vector3.Distance(beforeBrake.Position, braking.Position);
        float followingStep = Vector3.Distance(braking.Position, afterBrake.Position);
        Assert.True(followingStep < brakingStep, $"{followingStep} >= {brakingStep}");
    }

    [Fact]
    public void StraightShortDragUsesWholeGestureDirection()
    {
        var settings = Unfiltered with
        {
            DirectionCorrectionEnabled = true,
            DragCorrectionMaxSeconds = 1,
            DragCorrectionStrength = 1
        };
        var engine = BeginDrag(settings);
        engine.Update(Frame(new Vector3(0.5f, 0, 0), leftGrip: 1), 0.1f, settings);
        engine.Update(Frame(new Vector3(1, -0.1f, 0), leftGrip: 1), 0.1f, settings);

        var released = engine.Update(Frame(new Vector3(1, -0.1f, 0)), 0.1f, settings);

        Near(new(-1.5f, 0.15f, 0), released.Position);
    }

    [Fact]
    public void CurvedDragKeepsReleaseDirection()
    {
        var settings = Unfiltered with
        {
            DirectionCorrectionEnabled = true,
            DragCorrectionMaxSeconds = 1,
            DragCorrectionStrength = 1
        };
        var engine = BeginDrag(settings);
        engine.Update(Frame(new Vector3(1, 0, 0), leftGrip: 1), 0.1f, settings);
        engine.Update(Frame(new Vector3(1, 1, 0), leftGrip: 1), 0.1f, settings);

        var released = engine.Update(Frame(new Vector3(1, 1, 0)), 0.1f, settings);

        Near(new(-1, -2, 0), released.Position);
    }

    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(0, 1, 0)]
    [InlineData(0, 0, 1)]
    public void ReleasedTurnContinuesOnEveryAxis(float x, float y, float z)
    {
        var axis = Vector3.Normalize(new(x, y, z));
        var engine = BeginTurn(Unfiltered);
        engine.Update(Frame(rightGrip: 1, rightRotation: Quaternion.CreateFromAxisAngle(axis, 0.2f)), 0.1f, Unfiltered);

        var released = engine.Update(Frame(rightRotation: Quaternion.CreateFromAxisAngle(axis, 0.2f)), 0.1f, Unfiltered);

        Near(Quaternion.CreateFromAxisAngle(axis, -0.4f), released.Orientation);
        Near(Head.Position, released.Transform(Head.Position));
        Assert.True(engine.HasAngularInertia);
    }

    [Fact]
    public void TurnCutoffRejectsAngularVelocityBelowThreshold()
    {
        var settings = Unfiltered with
        {
            InertiaCutoffEnabled = true,
            TurnCutoffRadiansPerSecond = MathF.PI / 36
        };
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.005f);
        var engine = BeginTurn(settings);
        var moved = engine.Update(Frame(rightGrip: 1, rightRotation: rotation), 0.1f, settings);

        var released = engine.Update(Frame(rightRotation: rotation), 0.1f, settings);

        Assert.True(moved.NearlyEquals(released));
        Assert.False(engine.HasAngularInertia);
    }

    [Fact]
    public void TurnMultiplierScalesReleasedAngularVelocity()
    {
        var settings = Unfiltered with { TurnAccelerationMultiplier = 0.5f };
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.2f);
        var engine = BeginTurn(settings);
        engine.Update(Frame(rightGrip: 1, rightRotation: rotation), 0.1f, settings);

        var released = engine.Update(Frame(rightRotation: rotation), 0.1f, settings);

        Near(Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -0.3f), released.Orientation);
    }

    [Fact]
    public void TurnSmoothUsesElapsedTime()
    {
        var settings = Unfiltered with { TurnSmoothSeconds = 0.05f };
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 1);
        var engine = BeginTurn(settings);

        var moved = engine.Update(Frame(rightGrip: 1, rightRotation: rotation), 0.05f, settings);

        float expectedAngle = -(1 - MathF.Exp(-1));
        Near(Quaternion.CreateFromAxisAngle(Vector3.UnitX, expectedAngle), moved.Orientation);
        Near(Head.Position, moved.Transform(Head.Position));
    }

    [Fact]
    public void TurnInertiaUsesUnsmoothedControllerMotion()
    {
        var settings = Unfiltered with { TurnSmoothSeconds = 1 };
        var controllerRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.2f);
        var engine = BeginTurn(settings);
        var moved = engine.Update(
            Frame(rightGrip: 1, rightRotation: controllerRotation),
            0.1f,
            settings);

        var released = engine.Update(Frame(rightRotation: controllerRotation), 0.1f, settings);

        var expected = Integrate(moved.Orientation, new(0, 0, -2), 0.1f);
        Near(expected, released.Orientation);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.5f)]
    [InlineData(1)]
    public void TurnRotatesLinearInertiaByConfiguredMultiplier(float multiplier)
    {
        var settings = Unfiltered with
        {
            BrakeStrength = 0,
            VectorRotationMultiplier = multiplier
        };
        var engine = BeginDrag(settings);
        engine.Update(Frame(leftX: 1, leftGrip: 1), 0.1f, settings);
        engine.Update(Frame(leftX: 1), 0.1f, settings);
        engine.Update(Frame(leftX: 1, rightGrip: 1), 0.1f, settings);
        var beforeTurn = engine.Offset.Transform(Head.Position);

        var controllerRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.1f);
        engine.Update(
            Frame(leftX: 1, rightGrip: 1, rightRotation: controllerRotation),
            0.1f,
            settings);
        var afterTurn = engine.Offset.Transform(Head.Position);
        engine.Update(
            Frame(leftX: 1, rightGrip: 1, rightRotation: controllerRotation),
            0.1f,
            settings);
        var afterFollowingStep = engine.Offset.Transform(Head.Position);

        var firstStep = afterTurn - beforeTurn;
        var secondStep = afterFollowingStep - afterTurn;
        var expectedRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -0.1f * multiplier);
        Near(Vector3.Transform(firstStep, expectedRotation), secondStep);
    }

    [Theory]
    [InlineData(1, -0.4f)]
    [InlineData(0.5f, -0.5f)]
    [InlineData(0, -0.6f)]
    public void RegripBrakeRemovesConfiguredShareOfAngularInertia(float brake, float expectedAngle)
    {
        var settings = Unfiltered with { BrakeStrength = brake };
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.2f);
        var engine = BeginTurn(settings);
        engine.Update(Frame(rightGrip: 1, rightRotation: rotation), 0.1f, settings);
        engine.Update(Frame(rightRotation: rotation), 0.1f, settings);

        var regripped = engine.Update(Frame(rightGrip: 1, rightRotation: rotation), 0.1f, settings);

        Near(Quaternion.CreateFromAxisAngle(Vector3.UnitY, expectedAngle), regripped.Orientation);
    }

    [Fact]
    public void StraightShortTurnUsesWholeGestureAngularVelocity()
    {
        var settings = Unfiltered with
        {
            DirectionCorrectionEnabled = true,
            TurnCorrectionMaxSeconds = 0.5f,
            TurnCorrectionStrength = 1
        };
        var engine = BeginTurn(settings);
        engine.Update(
            Frame(rightGrip: 1, rightRotation: Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.1f)),
            0.1f,
            settings);
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.3f);
        engine.Update(Frame(rightGrip: 1, rightRotation: rotation), 0.1f, settings);

        var released = engine.Update(Frame(rightRotation: rotation), 0.1f, settings);

        Near(Quaternion.CreateFromAxisAngle(Vector3.UnitX, -0.45f), released.Orientation);
    }

    private static SpaceManipulator BeginDrag(FlightMotionSettings settings)
    {
        var engine = new SpaceManipulator(Identity);
        engine.Update(Frame(), 0.1f, settings);
        engine.Update(Frame(leftGrip: 1), 0.1f, settings);
        return engine;
    }

    private static SpaceManipulator BeginTurn(FlightMotionSettings settings)
    {
        var engine = new SpaceManipulator(Identity);
        engine.Update(Frame(), 0.1f, settings);
        engine.Update(Frame(rightGrip: 1), 0.1f, settings);
        return engine;
    }

    private static InputFrame Frame(
        float leftX = 0,
        float leftGrip = 0,
        float rightGrip = 0,
        Quaternion? rightRotation = null) =>
        Frame(new Vector3(leftX, 0, 0), leftGrip, rightGrip, rightRotation);

    private static InputFrame Frame(
        Vector3 leftPosition,
        float leftGrip = 0,
        float rightGrip = 0,
        Quaternion? rightRotation = null) => new(
            Head,
            true,
            new(new(Quaternion.Identity, leftPosition), leftGrip, true),
            new(new(rightRotation ?? Quaternion.Identity, Vector3.Zero), rightGrip, true));

    private static void Near(Vector3 expected, Vector3 actual) =>
        Assert.True(Vector3.Distance(expected, actual) < 0.0001f, $"{expected} != {actual}");

    private static void Near(Quaternion expected, Quaternion actual) =>
        Assert.True(1 - MathF.Abs(Quaternion.Dot(expected, actual)) < 0.0001f, $"{expected} != {actual}");

    private static Quaternion Integrate(Quaternion rotation, Vector3 velocity, float elapsedSeconds)
    {
        float speed = velocity.Length();
        return Quaternion.Normalize(
            Quaternion.CreateFromAxisAngle(velocity / speed, speed * elapsedSeconds) * rotation);
    }
}
