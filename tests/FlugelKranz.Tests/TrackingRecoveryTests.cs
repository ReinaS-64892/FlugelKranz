using System.Numerics;
using FlugelKranz.Core;
using FlugelKranz.OpenXR;
using Xunit;

namespace FlugelKranz.Tests;

public class TrackingRecoveryTests
{
    private static readonly FlightMotionSettings Flight = FlightMotionSettings.Default with
    {
        DragSmoothSeconds = 0, TurnSmoothSeconds = 0,
        InertiaDecelerationPerSecond = 0, ZAccelerationMultiplier = 1,
        TurnAccelerationMultiplier = 1
    };

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void HeldFlightGripRebasesAfterHeadLossAndMeasuresOnlySubsequentMotion(bool turn, bool both)
    {
        var engine = new FreeFlightManipulator(RigidPose.Identity);
        engine.Update(Frame(), .01f, Flight);
        engine.Update(Frame(held: true, turn: turn, both: both), .01f, Flight);
        var before = engine.Offset;
        engine.Update(Frame(held: true, turn: turn, both: both) with { HeadTracked = false }, .01f, Flight);
        var recovered = Frame(1, held: true, turn: turn, both: both);
        var resumed = engine.Update(recovered, .01f, Flight);
        Assert.True(before.NearlyEquals(resumed));
        Assert.Equal(!turn, engine.IsDragging);
        Assert.Equal(turn, engine.IsTurning);
        var released = engine.Update(Frame(1), .01f, Flight);
        Assert.True(resumed.NearlyEquals(released));
        Assert.False(engine.HasLinearInertia);
        Assert.False(engine.HasAngularInertia);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReleaseDuringHeadLossDoesNotReusePreLossVelocity(bool turn)
    {
        var engine = new FreeFlightManipulator(RigidPose.Identity);
        engine.Update(Frame(), .01f, Flight);
        engine.Update(Frame(held: true, turn: turn), .01f, Flight);
        var before = engine.Update(Frame(.1f, held: true, turn: turn), .01f, Flight);
        engine.Update(Frame(1) with { HeadTracked = false }, .01f, Flight);
        var after = engine.Update(Frame(1), .01f, Flight);
        Assert.True(before.NearlyEquals(after));
        Assert.False(engine.HasLinearInertia);
        Assert.False(engine.HasAngularInertia);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InfiniteWalkingRebasesHeldGripsAfterHeadLoss(bool both)
    {
        var engine = new InfiniteWalkingManipulator(RigidPose.Identity);
        var settings = InfiniteWalkingSettings.Default with { ToeTurnOriginEnabled = false };
        engine.Update(Frame(), .01f, settings);
        var held = Frame(held: true, both: both);
        held = held with { Right = held.Right with { Turn = 1 } };
        engine.Update(held, .01f, settings);
        engine.Update(held with { HeadTracked = false }, .01f, settings);
        var moved = Frame(1, held: true, both: both);
        moved = moved with { Right = moved.Right with { Turn = 1 } };
        Assert.True(engine.Update(moved, .01f, settings).NearlyEquals(RigidPose.Identity));
        Assert.True(engine.IsDragging);
        Assert.True(engine.IsTurning);
    }

    [Theory]
    [InlineData(0UL, false, false)]
    [InlineData(5UL, true, false)]
    [InlineData(7UL, true, false)]
    [InlineData(13UL, true, false)]
    [InlineData(15UL, true, true)]
    [InlineData(10UL, false, false)]
    public void HandsRequireBothValidAndTrackedWhileHeadAllowsValidOnly(ulong flags, bool head, bool hand)
    {
        Assert.Equal(head, OpenXrInput.IsPoseUsable(flags, requireTracking: false));
        Assert.Equal(hand, OpenXrInput.IsPoseUsable(flags, requireTracking: true));
    }

    private static InputFrame Frame(float motion = 0, bool held = false, bool turn = false, bool both = false)
    {
        HandSample Hand(float x, bool active) => new(
            new(Quaternion.CreateFromAxisAngle(Vector3.UnitY, motion), new(x + motion, 1 + motion, 0)),
            active && !turn ? 1 : 0, active && turn ? 1 : 0, 0, true);
        return new(new(Quaternion.Identity, new(0, 1.7f, 0)), true,
            Hand(-.3f, held), Hand(.3f, held && both));
    }
}
