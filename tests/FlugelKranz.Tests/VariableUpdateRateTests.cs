using System.Numerics;
using FlugelKranz.Core;
using Xunit;

namespace FlugelKranz.Tests;

public class VariableUpdateRateTests
{
    private static readonly FlightMotionSettings Settings = FlightMotionSettings.Default with
    {
        InertiaCutoffEnabled = false,
        InertiaAccelerationBoostEnabled = false,
        DecelerationExemptionEnabled = false,
        DragAccelerationMultiplier = 1,
        TurnAccelerationMultiplier = 1,
        ZAccelerationMultiplier = 1,
        DragSmoothSeconds = 0,
        TurnSmoothSeconds = 0,
        TurnOrigin = TurnOrigin.Head
    };

    [Theory]
    [InlineData(72)]
    [InlineData(90)]
    [InlineData(120)]
    [InlineData(180)]
    [InlineData(240)]
    public void StopSensitivityDoesNotDependOnUpdateRate(int rate)
    {
        var settings = Settings with { InertiaDecelerationPerSecond = 0 };
        foreach (bool angular in new[] { false, true })
        {
            var (slow, frame) = Moving(0.08f, angular, settings);
            slow.Update(frame, 1f / rate, settings);
            Assert.False(angular ? slow.HasAngularInertia : slow.HasLinearInertia);
            var (fast, _) = Moving(0.12f, angular, settings);
            fast.Update(frame, 1f / rate, settings);
            Assert.True(angular ? fast.HasAngularInertia : fast.HasLinearInertia);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExponentialDecayAndStoppingDistanceAgreeAcrossVariableIntervals(bool angular)
    {
        foreach (var intervals in Intervals(4))
        {
            var settings = Settings with { InertiaDecelerationPerSecond = 2 };
            var (engine, frame) = Moving(2, angular, settings);
            var initial = engine.Offset;
            foreach (float dt in intervals) engine.Update(frame, dt, settings);
            // Integral of 2 exp(-2t) until the common stopping speed of 0.1 units/s.
            float distance = (2 - 0.1f) / 2;
            if (angular)
            {
                Near(Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.UnitY, -distance) * initial.Orientation), engine.Offset.Orientation);
                Assert.False(engine.HasAngularInertia);
            }
            else
            {
                Near(new(-distance, 0, 0), engine.Offset.Position - initial.Position);
                Assert.False(engine.HasLinearInertia);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExemptionBoundaryAndGrabBrakeAgreeAcrossVariableIntervals(bool braking)
    {
        RigidPose? expected = null;
        foreach (var intervals in Intervals(0.8f))
        {
            var settings = Settings with
            {
                InertiaDecelerationPerSecond = 2,
                DecelerationExemptionEnabled = true,
                DragDecelerationExemptionDurationRatio = 0.03f,
                DecelerationExemptionStrength = 0.7f,
                BrakeRampSeconds = 0.4f
            };
            var (engine, frame) = Moving(2, false, settings);
            if (braking) frame = frame with { Left = frame.Left with { Drag = 1 } };
            foreach (float dt in intervals) engine.Update(frame, dt, settings);
            if (expected is { } prior) Near(prior.Position, engine.Offset.Position);
            expected = engine.Offset;
        }
    }

    [Fact]
    public void LongIntervalsPreserveTheFullElapsedMovement()
    {
        var settings = Settings with { InertiaDecelerationPerSecond = 0 };
        var (engine, frame) = Moving(1, false, settings);
        var initial = engine.Offset;
        engine.Update(frame, 0.2f, settings);
        Near(new(-0.2f, 0, 0), engine.Offset.Position - initial.Position);
    }

    [Theory]
    [InlineData(72)]
    [InlineData(90)]
    [InlineData(120)]
    [InlineData(180)]
    [InlineData(240)]
    public void SmallControllerRotationsRetainTheirVelocityAtHighSamplingRates(int rate)
    {
        var settings = Settings with { InertiaDecelerationPerSecond = 0, InertiaStopDisplacementMetres = 0.000001f };
        var (engine, frame) = Moving(0, true, settings);
        frame = frame with { Right = frame.Right with { Turn = 1 } };
        engine.Update(frame, 0, settings);
        float dt = 1f / rate;
        for (int i = 1; i <= 10; i++)
        {
            frame = frame with { Right = frame.Right with
            {
                Pose = new(Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.05f * i * dt), Vector3.Zero)
            } };
            engine.Update(frame, dt, settings);
        }
        frame = frame with { Right = frame.Right with { Turn = 0 } };
        var initial = engine.Offset;
        engine.Update(frame, 0.2f, settings);
        Assert.True(engine.HasAngularInertia);
        // Compare the imaginary part as well: a quaternion-dot tolerance can hide tiny missing rotations.
        var expected = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.UnitY, -0.01f) * initial.Orientation);
        Assert.True(MathF.Abs(expected.Y - engine.Offset.Orientation.Y) < 0.000001f);
    }

    private static IEnumerable<float[]> Intervals(float seconds)
    {
        foreach (int rate in new[] { 72, 90, 120, 180, 240 })
        {
            int count = (int)(rate * seconds);
            yield return Enumerable.Repeat(seconds / count, count).ToArray();
        }
        float[] weights = [0.004f, 0.018f, 0.006f, 0.032f, 0.2f, 0.009f];
        yield return weights.Select(weight => weight * seconds / weights.Sum()).ToArray();
    }

    private static (FreeFlightManipulator Engine, InputFrame Frame) Moving(float speed, bool angular, FlightMotionSettings settings)
    {
        var engine = new FreeFlightManipulator(RigidPose.Identity);
        var frame = new InputFrame(new(Quaternion.Identity, new(0, 1.7f, 0)), true,
            new(RigidPose.Identity, 0, 0, 0, true), new(RigidPose.Identity, 0, 0, 0, true));
        engine.Update(frame, 0, settings);
        var held = angular ? frame with { Right = frame.Right with { Turn = 1 } }
            : frame with { Left = frame.Left with { Drag = 1 } };
        engine.Update(held, 0, settings);
        held = angular ? held with { Right = held.Right with { Pose = new(Quaternion.CreateFromAxisAngle(Vector3.UnitY, speed * 0.1f), Vector3.Zero) } }
            : held with { Left = held.Left with { Pose = new(Quaternion.Identity, new(speed * 0.1f, 0, 0)) } };
        engine.Update(held, 0.1f, settings);
        frame = held with { Left = held.Left with { Drag = 0 }, Right = held.Right with { Turn = 0 } };
        engine.Update(frame, 0, settings);
        return (engine, frame);
    }

    private static void Near(Vector3 expected, Vector3 actual) =>
        Assert.True(Vector3.Distance(expected, actual) < 0.0001f, $"{expected} != {actual}");
    private static void Near(Quaternion expected, Quaternion actual) =>
        Assert.True(1 - MathF.Abs(Quaternion.Dot(expected, actual)) < 0.000001f, $"{expected} != {actual}");
}
