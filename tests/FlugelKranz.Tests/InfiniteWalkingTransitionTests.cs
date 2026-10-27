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

    [Theory]
    [InlineData(0.5f, 0.3f)]
    [InlineData(1.6f, -0.8f)]
    [InlineData(2.8f, 1.4f)]
    [InlineData(3.1415927f, 0f)]
    [InlineData(0f, 3.1415927f)]
    public void TiltReturnsAroundThePivotWhileThePivotMovesOnAVerticalLine(float pitch, float roll)
    {
        var original = new RigidPose(Quaternion.CreateFromYawPitchRoll(0.2f, -0.1f, 0.05f), new(2, 0.3f, -1));
        var current = new RigidPose(
            Quaternion.Normalize(Quaternion.CreateFromYawPitchRoll(0.7f, pitch, roll) * original.Orientation),
            new(6, 5, -4));
        var pivot = new Vector3(0.8f, 1.6f, -0.4f);
        Vector3 startPoint = current.Transform(pivot);
        float landingY = original.Transform(pivot).Y;
        var transition = new InfiniteWalkingTransition(current, original, turnPivot: pivot);

        for (int step = 1; step <= 10; step++)
        {
            var offset = transition.Advance(0.1f);
            Vector3 point = offset.Transform(pivot);
            float progress = step / 10f;
            float eased = progress * progress * (3 - 2 * progress);
            Assert.Equal(startPoint.X, point.X, 4);
            Assert.Equal(startPoint.Z, point.Z, 4);
            Assert.Equal(startPoint.Y + (landingY - startPoint.Y) * eased, point.Y, 4);
        }

        Assert.True(transition.IsComplete);
        var difference = Quaternion.Normalize(transition.Current.Orientation * Quaternion.Conjugate(original.Orientation));
        Assert.Equal(0, difference.X, 4);
        Assert.Equal(0, difference.Z, 4);
        Assert.Equal(landingY, transition.Destination.Offset.Transform(pivot).Y, 4);
    }

    [Fact]
    public void InterruptedDescentKeepsTheCapturedPivotAndTheSameLandingPosition()
    {
        var current = new RigidPose(Quaternion.CreateFromYawPitchRoll(0.5f, 2.8f, -0.7f), new(4, 5, 6));
        var pivot = new Vector3(-0.3f, 1.7f, 0.6f);
        var transition = new InfiniteWalkingTransition(current, RigidPose.Identity, turnPivot: pivot);
        Vector3 startPoint = current.Transform(pivot);
        var intermediate = transition.Advance(0.3f);

        var resumed = Assert.IsType<InfiniteWalkingTransition>(transition.Interrupt(intermediate));
        Assert.Equal(intermediate, resumed.Current);
        Vector3 landingPoint = resumed.Advance(1).Transform(pivot);

        Assert.Equal(startPoint.X, landingPoint.X, 4);
        Assert.Equal(startPoint.Z, landingPoint.Z, 4);
        Assert.Equal(pivot.Y, landingPoint.Y, 4);
    }

}
