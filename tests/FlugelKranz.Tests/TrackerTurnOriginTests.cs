using System.Numerics;
using FlugelKranz.Core;
using Xunit;

namespace FlugelKranz.Tests;

public class TrackerTurnOriginTests
{
    private static readonly Vector3 Head = new(0, 1.7f, 0);
    private static readonly Vector3 Left = new(-0.3f, 1.2f, -0.4f);
    private static readonly Vector3 Right = new(0.3f, 1.2f, -0.4f);
    private static readonly TrackerSample Waist = new("waist", new(0.2f, 0.9f, -0.1f), true);
    private static readonly TrackerSample Foot = new("foot", new(-0.4f, 0.1f, 0.2f), true);
    private static readonly FlightMotionSettings Direct = FlightMotionSettings.Default with
    {
        DragSmoothSeconds = 0,
        TurnSmoothSeconds = 0,
        DragAccelerationMultiplier = 0,
        TurnAccelerationMultiplier = 0,
        TwoHandTurnAccelerationMultiplier = 0
    };

    [Theory]
    [InlineData(0, 1)]
    [InlineData(0, 1.5f)]
    [InlineData(0, 4)]
    [InlineData(0.05f, 1.5f)]
    public void SingleHandTurnWeightsOnlyTheHeadAmongHandsAndValidTrackers(float smoothSeconds, float headWeight)
    {
        var settings = Direct with { TurnSmoothSeconds = smoothSeconds, TurnOriginHeadWeightMultiplier = headWeight };
        var frame = Frame() with
        {
            Trackers =
            [
                Waist, Foot,
                new("lost", new(10, 20, 30), false),
                new("nan", new(float.NaN, 0, 0), true),
                new("infinite", new(0, float.PositiveInfinity, 0), true)
            ]
        };
        var engine = BeginTurn(frame, settings);
        Vector3 pivot = (Head * settings.TurnOriginHeadWeightMultiplier + Left + Right + Waist.Position + Foot.Position) /
            (settings.TurnOriginHeadWeightMultiplier + 4);
        var rotated = RotateRight(frame);

        var after = engine.Update(rotated, 0.01f, settings);

        Near(pivot, after.Transform(pivot));
        Assert.False(after.NearlyEquals(RigidPose.Identity));
    }

    [Fact]
    public void MissingHandIsExcludedWhileValidTrackersRemainInTheMidpoint()
    {
        var frame = Frame() with { Left = default, Trackers = [Waist] };
        var engine = BeginTurn(frame, Direct);
        Vector3 pivot = (Head * Direct.TurnOriginHeadWeightMultiplier + Right + Waist.Position) / (Direct.TurnOriginHeadWeightMultiplier + 2);
        var after = engine.Update(RotateRight(frame), 0.01f, Direct);
        Near(pivot, after.Transform(pivot));
    }

    [Fact]
    public void HeadOriginSettingIgnoresTrackers()
    {
        var settings = Direct with { TurnOrigin = TurnOrigin.Head };
        var frame = Frame() with { Trackers = [Waist, Foot] };
        var engine = BeginTurn(frame, settings);
        var after = engine.Update(RotateRight(frame), 0.01f, settings);
        Near(Head, after.Transform(Head));
    }

    [Fact]
    public void SimultaneousDragKeepsItsPriorityOverTheTrackerMidpoint()
    {
        var frame = Frame() with { Trackers = [Waist, Foot] };
        var engine = new FreeFlightManipulator(RigidPose.Identity);
        engine.Update(frame, 0.01f, Direct);
        var held = frame with { Left = frame.Left with { Drag = 1 }, Right = frame.Right with { Turn = 1 } };
        engine.Update(held, 0.01f, Direct);
        var after = engine.Update(RotateRight(held), 0.01f, Direct);
        Near(Left, after.Transform(Left));
    }

    [Fact]
    public void TwoHandTurnRetainsItsGrabStartHandPivotWhenTrackersArePresent()
    {
        var frame = Frame() with { Trackers = [Waist, Foot] };
        var engine = new FreeFlightManipulator(RigidPose.Identity);
        engine.Update(frame, 0.01f, Direct);
        var held = frame with { Left = frame.Left with { Turn = 1 }, Right = frame.Right with { Turn = 1 } };
        engine.Update(held, 0.01f, Direct);
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.5f);
        var rotated = held with
        {
            Left = held.Left with { Pose = held.Left.Pose with { Orientation = rotation } },
            Right = held.Right with { Pose = held.Right.Pose with { Orientation = rotation } }
        };
        Vector3 pivot = (Left + Right) * 0.5f;
        var after = engine.Update(rotated, 0.01f, Direct);
        Near(pivot, after.Transform(pivot));
    }

    [Fact]
    public void TrackerArrivalLossAndRecoveryUpdateThePivotWithoutAnOffsetJump()
    {
        var frame = Frame();
        var engine = BeginTurn(frame, Direct);
        frame = RotateRight(frame);
        engine.Update(frame, 0.01f, Direct);

        foreach (bool tracked in new[] { true, false, true })
        {
            frame = frame with { Trackers = [Waist with { IsTracked = tracked }] };
            var before = engine.Offset;
            var changed = engine.Update(frame, 0.01f, Direct);
            Assert.True(before.NearlyEquals(changed));

            Vector3 pivot = tracked ? (Head * Direct.TurnOriginHeadWeightMultiplier + Left + Right + Waist.Position) / (Direct.TurnOriginHeadWeightMultiplier + 3)
                : (Head * Direct.TurnOriginHeadWeightMultiplier + Left + Right) / (Direct.TurnOriginHeadWeightMultiplier + 2);
            Vector3 expected = changed.Transform(pivot);
            var rotation = Quaternion.Normalize(frame.Right.Pose.Orientation * Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.2f));
            frame = frame with { Right = frame.Right with { Pose = frame.Right.Pose with { Orientation = rotation } } };
            var after = engine.Update(frame, 0.01f, Direct);
            Near(expected, after.Transform(pivot));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResetAndModeEntryCaptureTheTrackerMidpoint(bool modeEntry)
    {
        var current = new RigidPose(Quaternion.CreateFromYawPitchRoll(0.3f, 0.8f, -0.4f), new(3, 4, 5));
        var frame = Frame() with { Trackers = [Waist, Foot] };
        var settings = FlugelKranzSettings.Default with { FreeFlight = Direct };
        var flight = new FreeFlightManipulator(current);
        MovementTransition transition = modeEntry
            ? Assert.IsType<InfiniteWalkingTransition>(new InfiniteWalkingManipulator(current)
                .EnterFrom(flight, new(current, RigidPose.Identity, frame, settings)))
            : Assert.IsType<SpaceResetTransition>(flight.CreateReset(frame, RigidPose.Identity, settings));
        Vector3 pivot = (Head * Direct.TurnOriginHeadWeightMultiplier + Left + Right + Waist.Position + Foot.Position) / (Direct.TurnOriginHeadWeightMultiplier + 4);
        Vector3 start = current.Transform(pivot);
        // Later tracker motion/loss must not change the pivot captured at entry.
        var result = transition.Update(frame with { Trackers = [] }, 0.5f, settings);
        Vector3 expected = modeEntry ? new(start.X, (start.Y + pivot.Y) * 0.5f, start.Z) : start;
        Near(expected, result.Transform(pivot));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(1.5f, 1.5f)]
    [InlineData(4, 4)]
    [InlineData(5, 4)]
    [InlineData(float.NaN, 2.5f)]
    [InlineData(float.PositiveInfinity, 2.5f)]
    public void HeadWeightIsNormalizedToTheSupportedRange(float value, float expected)
    {
        var settings = Direct with { TurnOriginHeadWeightMultiplier = value };
        Assert.Equal(expected, settings.Normalized().TurnOriginHeadWeightMultiplier);
    }

    private static FreeFlightManipulator BeginTurn(InputFrame frame, FlightMotionSettings settings)
    {
        var engine = new FreeFlightManipulator(RigidPose.Identity);
        engine.Update(frame, 0.01f, settings);
        engine.Update(frame with { Right = frame.Right with { Turn = 1 } }, 0.01f, settings);
        return engine;
    }

    private static InputFrame Frame() => new(new(Quaternion.Identity, Head), true,
        new(new(Quaternion.Identity, Left), 0, 0, 0, true),
        new(new(Quaternion.Identity, Right), 0, 0, 0, true));

    private static InputFrame RotateRight(InputFrame frame) => frame with
    {
        Right = frame.Right with { Turn = 1, Pose = frame.Right.Pose with { Orientation = Quaternion.CreateFromYawPitchRoll(0.4f, 0.6f, 0.2f) } }
    };

    private static void Near(Vector3 expected, Vector3 actual) =>
        Assert.True(Vector3.Distance(expected, actual) < 0.0001f, $"{expected} != {actual}");
}
