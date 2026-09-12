using System.Numerics;
using FlugelKranz.Core;
using Xunit;

namespace FlugelKranz.Tests;

public class InfiniteWalkingTransitionTests
{
    [Fact]
    public void TargetReturnsHeightAndTiltWhileKeepingYaw()
    {
        var original = new RigidPose(
            Quaternion.CreateFromYawPitchRoll(0.1f, -0.2f, 0.05f),
            new(1, 0.25f, 2));
        var difference = Quaternion.CreateFromYawPitchRoll(0.8f, 0.6f, -0.4f);
        var current = new RigidPose(
            Quaternion.Normalize(difference * original.Orientation),
            new(5, 3, 7));

        var target = InfiniteWalkingTransition.CreateTarget(current, original);

        Assert.Equal(original.Position.Y, target.Position.Y);
        Assert.Equal(current.Position.X, target.Position.X);
        Assert.Equal(current.Position.Z, target.Position.Z);
        var remaining = Quaternion.Normalize(
            target.Orientation * Quaternion.Conjugate(original.Orientation));
        Assert.True(MathF.Abs(remaining.X) < 0.0001f);
        Assert.True(MathF.Abs(remaining.Z) < 0.0001f);
    }

    [Fact]
    public void AdvanceUsesOneSecondSmoothTransitionAndEndsExactlyAtTarget()
    {
        var current = new RigidPose(
            Quaternion.CreateFromYawPitchRoll(0.4f, 0.8f, 0.3f),
            new(1, 2, 3));
        var transition = new InfiniteWalkingTransition(current, RigidPose.Identity);

        var halfway = transition.Advance(0.5f);
        Assert.Equal(1, halfway.Position.Y, 4);
        Assert.False(transition.IsComplete);

        var complete = transition.Advance(0.5f);
        Assert.True(transition.IsComplete);
        Assert.True(InfiniteWalkingTransition.CreateTarget(current, RigidPose.Identity)
            .NearlyEquals(complete));
    }
}
