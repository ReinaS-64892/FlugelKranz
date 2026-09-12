using System.Numerics;
using FlugelKranz.Core;
using Xunit;

namespace FlugelKranz.Tests;

public class SpaceResetTransitionTests
{
    [Fact]
    public void FreeFlightResetLevelsTheHmdAtTheNearestYawAndKeepsTheTurnPivotFixed()
    {
        var current = new RigidPose(
            Quaternion.CreateFromYawPitchRoll(0.7f, 0.9f, -0.4f),
            new(2, 3, 4));
        var head = new RigidPose(
            Quaternion.CreateFromYawPitchRoll(-0.3f, 0.6f, 0.2f),
            new(0.1f, 1.7f, -0.2f));
        var pivot = new Vector3(0.4f, 1.1f, -0.5f);
        Vector3 pivotInRoot = current.Transform(pivot);
        var transition = SpaceResetTransition.CreateFreeFlight(current, head, pivot);

        var halfway = transition.Advance(0.5f);
        Near(pivotInRoot, halfway.Transform(pivot));
        Assert.False(transition.IsComplete);

        var complete = transition.Advance(0.5f);
        Quaternion originalHeadInRoot = Quaternion.Normalize(
            current.Orientation * head.Orientation);
        Quaternion actualHeadInRoot = Quaternion.Normalize(
            complete.Orientation * head.Orientation);
        Vector3 expectedForward = Vector3.Transform(Vector3.UnitZ, originalHeadInRoot);
        expectedForward.Y = 0;
        expectedForward = Vector3.Normalize(expectedForward);
        Vector3 actualForward = Vector3.Transform(Vector3.UnitZ, actualHeadInRoot);
        Near(expectedForward, actualForward);
        Near(Vector3.UnitY, Vector3.Transform(Vector3.UnitY, actualHeadInRoot));
        Near(pivotInRoot, complete.Transform(pivot));
        Assert.True(transition.IsComplete);
    }

    [Fact]
    public void InfiniteWalkingResetReturnsOnlyHeightOverOneSecond()
    {
        var current = new RigidPose(
            Quaternion.CreateFromYawPitchRoll(0.4f, 0.2f, -0.3f),
            new(2, 3, 4));
        var original = new RigidPose(Quaternion.Identity, new(8, 0.25f, 9));
        var transition = SpaceResetTransition.CreateInfiniteWalking(current, original);

        var halfway = transition.Advance(0.5f);
        Assert.Equal(1.625f, halfway.Position.Y, 5);
        Assert.Equal(current.Orientation, halfway.Orientation);
        Assert.Equal(current.Position.X, halfway.Position.X);
        Assert.Equal(current.Position.Z, halfway.Position.Z);

        var complete = transition.Advance(0.5f);
        Assert.Equal(0.25f, complete.Position.Y);
        Assert.Equal(current.Orientation, complete.Orientation);
        Assert.Equal(current.Position.X, complete.Position.X);
        Assert.Equal(current.Position.Z, complete.Position.Z);
        Assert.True(transition.IsComplete);
    }

    private static void Near(Vector3 expected, Vector3 actual) =>
        Assert.True(Vector3.Distance(expected, actual) < 0.0001f, $"{expected} != {actual}");

}
