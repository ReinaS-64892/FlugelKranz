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
        TurnOrigin = TurnOrigin.Head,
        InertiaCutoffEnabled = false,
        InertiaAccelerationBoostEnabled = false,
        InertiaDecelerationPerSecond = 0,
        TurnAccelerationMultiplier = 1,
        DragSmoothSeconds = 0,
        TurnSmoothSeconds = 0
    };

    [Fact]
    public void DefaultsMatchTheExposedFlightParameters()
    {
        var settings = FlightMotionSettings.Default;

        Assert.True(settings.InertiaCutoffEnabled);
        Assert.Equal(0.4f, settings.DragCutoffMetresPerSecond);
        Assert.Equal(MathF.PI / 4, settings.TurnCutoffRadiansPerSecond);
        Assert.Equal(1, settings.DragAccelerationMultiplier);
        Assert.Equal(0.3f, settings.TurnAccelerationMultiplier);
        Assert.Equal(2, settings.ZAccelerationMultiplier);
        Assert.True(settings.InertiaAccelerationBoostEnabled);
        Assert.Equal(4, settings.InertiaAccelerationBoostMaximumMultiplier);
        Assert.Equal(1, settings.VectorRotationMultiplier);
        Assert.Equal(2, settings.InertiaDecelerationPerSecond);
        Assert.True(settings.DecelerationExemptionEnabled);
        Assert.Equal(0.2f, settings.DragDecelerationExemptionDurationRatio);
        Assert.Equal(0.15f, settings.TurnDecelerationExemptionDurationRatio);
        Assert.Equal(0.9f, settings.DecelerationExemptionStrength);
        Assert.Equal(0.01f, settings.DragSmoothSeconds);
        Assert.Equal(0.05f, settings.TurnSmoothSeconds);
        Assert.Equal(0.4f, settings.BrakeRampSeconds);
    }

    [Fact]
    public void ZAccelerationIsClampedToItsDisabledThroughMaximumRange()
    {
        Assert.Equal(1, new FlightMotionSettings { ZAccelerationMultiplier = 0 }.Normalized().ZAccelerationMultiplier);
        Assert.Equal(5, new FlightMotionSettings { ZAccelerationMultiplier = 8 }.Normalized().ZAccelerationMultiplier);
    }

    [Fact]
    public void InertiaAccelerationBoostMaximumMultiplierIsClampedToItsSupportedRange()
    {
        Assert.Equal(1, new FlightMotionSettings { InertiaAccelerationBoostMaximumMultiplier = 0 }.Normalized().InertiaAccelerationBoostMaximumMultiplier);
        Assert.Equal(6, new FlightMotionSettings { InertiaAccelerationBoostMaximumMultiplier = 8 }.Normalized().InertiaAccelerationBoostMaximumMultiplier);
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
    public void ZAccelerationBoostsForwardAndReverseDrag()
    {
        var settings = Unfiltered with { ZAccelerationMultiplier = 2 };
        var engine = BeginDrag(settings);
        engine.Update(Frame(new Vector3(0, 0, -1), leftGrip: 1), 0.1f, settings);

        var released = engine.Update(Frame(new Vector3(0, 0, -1)), 0.1f, settings);

        Near(new(0, 0, 3), released.Position);
    }

    [Fact]
    public void ZAccelerationLeavesLateralDragUnchanged()
    {
        var settings = Unfiltered with { ZAccelerationMultiplier = 3 };
        var engine = BeginDrag(settings);
        engine.Update(Frame(new Vector3(1, 0, 0), leftGrip: 1), 0.1f, settings);

        var released = engine.Update(Frame(new Vector3(1, 0, 0)), 0.1f, settings);

        Near(new(-2, 0, 0), released.Position);
    }

    [Fact]
    public void ZAccelerationOnlyScalesTheForwardComponentOfDiagonalDrag()
    {
        var settings = Unfiltered with { ZAccelerationMultiplier = 2 };
        var engine = BeginDrag(settings);
        engine.Update(Frame(new Vector3(1, 0, -1), leftGrip: 1), 0.1f, settings);

        var released = engine.Update(Frame(new Vector3(1, 0, -1)), 0.1f, settings);
        float forwardSpeed = 10 * (1 + (2 - 1) * MathF.Sqrt(0.5f));

        Near(new(-2, 0, 1 + forwardSpeed * 0.1f), released.Position);
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
    public void InertiaDecelerationStopsAtTheResidualMotionCutoff()
    {
        var settings = Unfiltered with
        {
            InertiaDecelerationPerSecond = 2,
            DecelerationExemptionEnabled = false
        };
        var engine = BeginDrag(settings);
        engine.Update(Frame(leftX: 1, leftGrip: 1), 0.1f, settings);
        var frame = Frame(leftX: 1);

        for (int i = 0; i < 100; i++)
            engine.Update(frame, 0.1f, settings);

        Assert.False(engine.HasLinearInertia);
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
        engine.Update(Frame(leftX: 0.1f, leftGrip: 1), 0.1f, settings);

        var released = engine.Update(Frame(leftX: 0.1f), 0.1f, settings);
        var continued = engine.Update(Frame(leftX: 0.1f), 0.1f, settings);

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
    public void DragSmoothingFollowsTheAbsoluteTargetAndSettlesExactly()
    {
        var settings = Unfiltered with
        {
            DragAccelerationMultiplier = 0,
            DragSmoothSeconds = 0.05f
        };
        var engine = BeginDrag(settings);
        var movedFrame = Frame(leftX: 1, leftGrip: 1);

        var moved = engine.Update(movedFrame, 0.05f, settings);
        float alpha = 1 - MathF.Exp(-1);
        Near(new(-alpha, 0, 0), moved.Position);

        var releasedFrame = Frame(leftX: 1);
        engine.Update(releasedFrame, 0.05f, settings);
        Assert.Equal(new Vector3(-alpha, 0, 0), engine.Offset.Position);

        var settled = engine.Offset;
        for (int step = 0; step < 20; step++)
            Assert.Equal(settled, engine.Update(releasedFrame, 0.05f, settings));
    }

    [Fact]
    public void DragInertiaUsesUnsmoothedControllerMotion()
    {
        var settings = Unfiltered with { DragSmoothSeconds = 1 };
        var engine = BeginDrag(settings);
        var moved = engine.Update(Frame(leftX: 1, leftGrip: 1), 0.1f, settings);

        var released = engine.Update(Frame(leftX: 1), 0.1f, settings);

        float smoothedPosition = moved.Position.X;
        Near(new(smoothedPosition - 1, 0, 0), released.Position);
        Near(new(smoothedPosition - 2, 0, 0), engine.Update(Frame(leftX: 1), 0.1f, settings).Position);
    }

    [Fact]
    public void RegripBrakeUsesFullStrengthOverConfiguredRamp()
    {
        var settings = Unfiltered with { BrakeRampSeconds = 0.2f };
        var engine = BeginDrag(settings);
        engine.Update(Frame(leftX: 1, leftGrip: 1), 0.1f, settings);
        engine.Update(Frame(leftX: 1), 0.1f, settings);

        var regripped = engine.Update(Frame(leftX: 1, leftGrip: 1), 0.1f, settings);

        Near(new(-2.5f, 0, 0), regripped.Position);
    }

    [Fact]
    public void ZeroBrakeRampRetainsImmediateBrakeBehavior()
    {
        var settings = Unfiltered with { BrakeRampSeconds = 0 };
        var engine = BeginDrag(settings);
        engine.Update(Frame(leftX: 1, leftGrip: 1), 0.1f, settings);
        engine.Update(Frame(leftX: 1), 0.1f, settings);

        var regripped = engine.Update(Frame(leftX: 1, leftGrip: 1), 0.1f, settings);

        Near(new(-2, 0, 0), regripped.Position);
    }

    [Fact]
    public void ReleasingBeforeDragBrakeFinishesKeepsResidualInertia()
    {
        var settings = Unfiltered with
        {
            BrakeRampSeconds = 1
        };
        var engine = BeginDrag(settings);
        engine.Update(Frame(leftX: 1, leftGrip: 1), 0.1f, settings);
        engine.Update(Frame(leftX: 1), 0.1f, settings);

        var regripped = engine.Update(Frame(leftX: 1, leftGrip: 1), 0.1f, settings);
        var released = engine.Update(Frame(leftX: 1), 0.1f, settings);

        Assert.True(released.Position.X < regripped.Position.X);
        Assert.True(engine.HasLinearInertia);
    }

    [Fact]
    public void InertiaAccelerationBoostUsesControllerSpeedAndCapsAddedAcceleration()
    {
        var settings = Unfiltered with
        {
            InertiaAccelerationBoostEnabled = true,
            InertiaAccelerationBoostMaximumMultiplier = 1.5f,
            InertiaCutoffEnabled = true,
            DragCutoffMetresPerSecond = 0.4f,
            BrakeRampSeconds = 1
        };
        var engine = BeginDrag(settings);
        engine.Update(Frame(leftX: 1, leftGrip: 1), 0.1f, settings);
        engine.Update(Frame(leftX: 1), 0.1f, settings);

        var regripped = engine.Update(Frame(leftX: 1, leftGrip: 1), 0.1f, settings);
        var moved = engine.Update(Frame(leftX: 2, leftGrip: 1), 0.1f, settings);
        var released = engine.Update(Frame(leftX: 2), 0.1f, settings);

        float brakeFactor = 1 - 0.1f * 0.1f * (3 - 2 * 0.1f);
        Near(new(-2 - brakeFactor, 0, 0), regripped.Position);
        Near(new(-2 - 2 * brakeFactor - 1, 0, 0), moved.Position);
        float residualSpeed = 10 * brakeFactor;
        float maximumBoostedSpeed = MathF.Max(residualSpeed, 10) * 1.5f;
        Near(new(-2 - 2 * brakeFactor - 1 - maximumBoostedSpeed * 0.1f, 0, 0), released.Position);
    }

    [Fact]
    public void InertiaAccelerationBoostUsesNewAccelerationAsLimitBasisWhenItIsLarger()
    {
        var firstSettings = Unfiltered with
        {
            InertiaAccelerationBoostEnabled = true,
            InertiaCutoffEnabled = true,
            DragCutoffMetresPerSecond = 0.4f,
            DragAccelerationMultiplier = 0.5f,
            BrakeRampSeconds = 1
        };
        var secondSettings = firstSettings with { DragAccelerationMultiplier = 1 };
        var engine = BeginDrag(firstSettings);
        engine.Update(Frame(leftX: 1, leftGrip: 1), 0.1f, firstSettings);
        engine.Update(Frame(leftX: 1), 0.1f, firstSettings);

        var regripped = engine.Update(Frame(leftX: 1, leftGrip: 1), 0.1f, secondSettings);
        var moved = engine.Update(Frame(leftX: 2, leftGrip: 1), 0.1f, secondSettings);
        var released = engine.Update(Frame(leftX: 2), 0.1f, secondSettings);

        float residualSpeed = 5 * (1 - 0.1f * 0.1f * (3 - 2 * 0.1f));
        float expectedReleaseSpeed = residualSpeed + 10;
        Near(new(-1.5f - residualSpeed * 0.1f, 0, 0), regripped.Position);
        Near(new(-1.5f - 2 * residualSpeed * 0.1f - 1, 0, 0), moved.Position);
        Near(new(-1.5f - 2 * residualSpeed * 0.1f - 1 - expectedReleaseSpeed * 0.1f, 0, 0), released.Position);
    }

    [Fact]
    public void InertiaAccelerationBoostRestartsBrakeWhenControllerStops()
    {
        var settings = Unfiltered with
        {
            InertiaAccelerationBoostEnabled = true,
            InertiaCutoffEnabled = true,
            DragCutoffMetresPerSecond = 0.4f,
            BrakeRampSeconds = 1
        };
        var engine = BeginDrag(settings);
        engine.Update(Frame(leftX: 1, leftGrip: 1), 0.1f, settings);
        engine.Update(Frame(leftX: 1), 0.1f, settings);

        var regripped = engine.Update(Frame(leftX: 1, leftGrip: 1), 0.1f, settings);
        var moved = engine.Update(Frame(leftX: 2, leftGrip: 1), 0.1f, settings);
        var stopped = engine.Update(Frame(leftX: 2, leftGrip: 1), 0.1f, settings);

        float firstBrakeFactor = 1 - 0.1f * 0.1f * (3 - 2 * 0.1f);
        float secondBrakeFactor = 1 - 0.2f * 0.2f * (3 - 2 * 0.2f);
        Near(new(-2 - firstBrakeFactor, 0, 0), regripped.Position);
        Near(new(-2 - 2 * firstBrakeFactor - 1, 0, 0), moved.Position);
        Near(new(-2 - 2 * firstBrakeFactor - 1 - secondBrakeFactor, 0, 0), stopped.Position);
    }

    [Fact]
    public void InertiaAccelerationBoostAllowsRepeatedAccelerationCycles()
    {
        var settings = Unfiltered with
        {
            InertiaAccelerationBoostEnabled = true,
            InertiaCutoffEnabled = true,
            DragCutoffMetresPerSecond = 0.4f,
            BrakeRampSeconds = 1
        };
        var engine = BeginDrag(settings);

        engine.Update(Frame(leftX: 1, leftGrip: 1), 0.1f, settings);
        var firstRelease = engine.Update(Frame(leftX: 1), 0.1f, settings);
        engine.Update(Frame(leftX: 1, leftGrip: 1), 0.1f, settings);
        engine.Update(Frame(leftX: 2, leftGrip: 1), 0.1f, settings);
        var secondRelease = engine.Update(Frame(leftX: 2), 0.1f, settings);
        engine.Update(Frame(leftX: 2, leftGrip: 1), 0.1f, settings);
        engine.Update(Frame(leftX: 3, leftGrip: 1), 0.1f, settings);
        var thirdRelease = engine.Update(Frame(leftX: 3), 0.1f, settings);

        Assert.True(firstRelease.Position.X > secondRelease.Position.X);
        Assert.True(secondRelease.Position.X > thirdRelease.Position.X);
        Assert.True(engine.HasLinearInertia);
    }

    [Fact]
    public void InertiaAccelerationBoostDoesNotCapNewAccelerationWhenResidualIsBelowCutoff()
    {
        var slowSettings = Unfiltered with
        {
            InertiaAccelerationBoostEnabled = true,
            InertiaCutoffEnabled = true,
            DragCutoffMetresPerSecond = 0.4f,
            DragAccelerationMultiplier = 0.02f,
            BrakeRampSeconds = 1
        };
        var fastSettings = slowSettings with { DragAccelerationMultiplier = 1 };
        var engine = BeginDrag(slowSettings);
        engine.Update(Frame(leftX: 1, leftGrip: 1), 0.1f, slowSettings);
        engine.Update(Frame(leftX: 1), 0.1f, slowSettings);

        engine.Update(Frame(leftX: 1, leftGrip: 1), 0.1f, fastSettings);
        engine.Update(Frame(leftX: 2, leftGrip: 1), 0.1f, fastSettings);
        var released = engine.Update(Frame(leftX: 2), 0.1f, fastSettings);
        var continued = engine.Update(Frame(leftX: 2), 0.1f, fastSettings);

        Assert.True(released.Position.X < -3, $"{released.Position.X} should include the full new acceleration");
        Assert.True(continued.Position.X < released.Position.X - 0.9f, $"{continued.Position.X} did not retain the new acceleration");
    }

    [Fact]
    public void TurnGripDoesNotBrakeLinearInertia()
    {
        var settings = Unfiltered;
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
        var settings = Unfiltered;
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
        var directSettings = settings with { TurnSmoothSeconds = 0 };
        var directEngine = BeginTurn(directSettings);
        var target = directEngine.Update(
            Frame(rightGrip: 1, rightRotation: rotation),
            0.05f,
            directSettings);

        var moved = engine.Update(Frame(rightGrip: 1, rightRotation: rotation), 0.05f, settings);

        float expectedAngle = -(1 - MathF.Exp(-1));
        Near(Quaternion.CreateFromAxisAngle(Vector3.UnitX, expectedAngle), moved.Orientation);
        Near(Head.Position, moved.Transform(Head.Position));

        for (int step = 1; step < 10; step++)
            engine.Update(Frame(rightGrip: 1, rightRotation: rotation), 0.05f, settings);

        Assert.Equal(target, engine.Offset);
        var settled = engine.Offset;
        Assert.Equal(
            settled,
            engine.Update(Frame(rightGrip: 1, rightRotation: rotation), 0.05f, settings));
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

        float smoothedAngle = -0.2f * (1 - MathF.Exp(-0.1f));
        Near(Quaternion.CreateFromAxisAngle(Vector3.UnitZ, smoothedAngle - 0.2f), released.Orientation);
        Near(Quaternion.CreateFromAxisAngle(Vector3.UnitZ, smoothedAngle - 0.4f), engine.Update(
            Frame(rightRotation: controllerRotation), 0.1f, settings).Orientation);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.5f)]
    [InlineData(1)]
    public void TurnRotatesLinearInertiaByConfiguredMultiplier(float multiplier)
    {
        var settings = Unfiltered with
        {
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

    [Fact]
    public void RegripBrakeUsesFullStrengthForAngularInertia()
    {
        var settings = Unfiltered with { BrakeRampSeconds = 0.2f };
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.2f);
        var engine = BeginTurn(settings);
        engine.Update(Frame(rightGrip: 1, rightRotation: rotation), 0.1f, settings);
        engine.Update(Frame(rightRotation: rotation), 0.1f, settings);

        var regripped = engine.Update(Frame(rightGrip: 1, rightRotation: rotation), 0.1f, settings);

        Near(Quaternion.CreateFromAxisAngle(Vector3.UnitY, -0.5f), regripped.Orientation);
    }

    [Fact]
    public void TwoHandDragAlsoBrakesAngularInertia()
    {
        var settings = Unfiltered with { BrakeRampSeconds = 0.2f };
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.2f);
        var engine = BeginTurn(settings);
        engine.Update(Frame(rightGrip: 1, rightRotation: rotation), 0.1f, settings);
        engine.Update(Frame(rightRotation: rotation), 0.1f, settings);
        var bothDrag = new InputFrame(
            Head,
            true,
            new(RigidPose.Identity, 1, 0, 0, true),
            new(RigidPose.Identity, 1, 0, 0, true));

        var braking = engine.Update(bothDrag, 0.1f, settings);
        var stopped = engine.Update(bothDrag, 0.1f, settings);
        var released = engine.Update(Frame(), 0.1f, settings);

        Near(Quaternion.CreateFromAxisAngle(Vector3.UnitY, -0.5f), braking.Orientation);
        Near(braking.Orientation, stopped.Orientation);
        Near(stopped.Orientation, released.Orientation);
        Assert.False(engine.HasAngularInertia);
    }

    private static FreeFlightManipulator BeginDrag(FlightMotionSettings settings)
    {
        var engine = new FreeFlightManipulator(Identity);
        engine.Update(Frame(), 0.1f, settings);
        engine.Update(Frame(leftGrip: 1), 0.1f, settings);
        return engine;
    }

    private static FreeFlightManipulator BeginTurn(FlightMotionSettings settings)
    {
        var engine = new FreeFlightManipulator(Identity);
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
            new(new(Quaternion.Identity, leftPosition), leftGrip, 0, 0, true),
            new(new(rightRotation ?? Quaternion.Identity, Vector3.Zero), 0, rightGrip, 0, true));

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
