using System.Numerics;
using FlugelKranz.Core;
using FlugelKranz.OpenXR;
using Xunit;

namespace FlugelKranz.Tests;

public class ControllerInputMappingTests
{
    [Fact]
    public void ValveIndexDefaultsMatchUiParameters()
    {
        Assert.Equal(0.3f, ValveIndexInputSettings.Default.PositionDeadZone);
        Assert.Equal(0.2f, ValveIndexInputSettings.Default.ForceThreshold);
    }

    [Theory]
    [InlineData(ControllerHand.Left, -1, 0, false, true)]
    [InlineData(ControllerHand.Left, 1, 0, true, false)]
    [InlineData(ControllerHand.Right, -1, 0, true, false)]
    [InlineData(ControllerHand.Right, 1, 0, false, true)]
    public void IndexDpadDirectionsMapToHandSpecificActions(
        ControllerHand hand,
        float x,
        float y,
        bool drag,
        bool turn)
    {
        var result = ControllerInputMapping.Map(
            hand,
            new(new(x, y), true, 1, false, false, false));

        Assert.Equal(drag, result.Drag);
        Assert.Equal(turn, result.Turn);
    }

    [Theory]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    public void OculusThumbRestUsesTriggerTouchToChooseAction(
        bool triggerTouched,
        bool drag,
        bool turn)
    {
        var result = ControllerInputMapping.Map(
            ControllerHand.Left,
            new(Vector2.Zero, false, 0, true, triggerTouched, true));

        Assert.Equal(drag, result.Drag);
        Assert.Equal(turn, result.Turn);
    }

    [Fact]
    public void IndexDpadDownMapsToModeSwitch()
    {
        var result = ControllerInputMapping.Map(
            ControllerHand.Right,
            new(new(0, -1), true, 1, false, false, false));

        Assert.True(result.ModeSwitch);
        Assert.False(result.Drag);
        Assert.False(result.Turn);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.2, 0)]
    [InlineData(0.29, 0)]
    [InlineData(0, 1)]
    public void CenterAndUpDoNotActivateFlightActions(float x, float y)
    {
        var result = ControllerInputMapping.Map(
            ControllerHand.Left,
            new(new(x, y), true, 1, false, false, false));

        Assert.False(result.Drag);
        Assert.False(result.Turn);
        Assert.False(result.ModeSwitch);
    }

    [Fact]
    public void TrackpadActivatesImmediatelyOutsidePositionDeadZone()
    {
        var result = ControllerInputMapping.Map(
            ControllerHand.Left,
            new(new(0.31f, 0), true, 1, false, false, false));

        Assert.True(result.Drag);
        Assert.False(result.Turn);
    }

    [Fact]
    public void TrackpadPositionRequiresTouch()
    {
        var result = ControllerInputMapping.Map(
            ControllerHand.Left,
            new(new(-1, 0), false, 1, false, false, false));

        Assert.Equal(default, result);
    }

    [Theory]
    [InlineData(0.50)]
    [InlineData(0.74)]
    public void TrackpadRequiresDeepEnoughForce(float force)
    {
        var result = ControllerInputMapping.Map(
            ControllerHand.Left,
            new(new(1, 0), true, force, false, false, false),
            ValveIndexInputSettings.Default with { ForceThreshold = 0.75f });

        Assert.Equal(default, result);
    }

    [Fact]
    public void TrackpadActivatesAtForceThreshold()
    {
        var result = ControllerInputMapping.Map(
            ControllerHand.Left,
            new(new(1, 0), true, 0.75f, false, false, false),
            ValveIndexInputSettings.Default with { ForceThreshold = 0.75f });

        Assert.True(result.Drag);
    }

    [Fact]
    public void CustomPositionDeadZoneIsApplied()
    {
        var input = new ControllerInputState(new(0.6f, 0), true, 1, false, false, false);

        Assert.True(ControllerInputMapping.Map(
            ControllerHand.Left,
            input,
            ValveIndexInputSettings.Default with { PositionDeadZone = 0.5f }).Drag);
        Assert.False(ControllerInputMapping.Map(
            ControllerHand.Left,
            input,
            ValveIndexInputSettings.Default with { PositionDeadZone = 0.7f }).Drag);
    }
}
