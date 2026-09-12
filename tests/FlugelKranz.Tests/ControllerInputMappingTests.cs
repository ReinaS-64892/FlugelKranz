using System.Numerics;
using FlugelKranz.OpenXR;
using Xunit;

namespace FlugelKranz.Tests;

public class ControllerInputMappingTests
{
    [Theory]
    [InlineData(ControllerHand.Left, -1, 0, true, false)]
    [InlineData(ControllerHand.Left, 1, 0, false, true)]
    [InlineData(ControllerHand.Right, -1, 0, false, true)]
    [InlineData(ControllerHand.Right, 1, 0, true, false)]
    public void IndexDpadDirectionsMapToHandSpecificActions(
        ControllerHand hand,
        float x,
        float y,
        bool drag,
        bool turn)
    {
        var result = ControllerInputMapping.Map(
            hand,
            new(new(x, y), true, false, false, false));

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
            new(Vector2.Zero, false, true, triggerTouched, true));

        Assert.Equal(drag, result.Drag);
        Assert.Equal(turn, result.Turn);
    }

    [Fact]
    public void IndexDpadDownMapsToModeSwitch()
    {
        var result = ControllerInputMapping.Map(
            ControllerHand.Right,
            new(new(0, -1), true, false, false, false));

        Assert.True(result.ModeSwitch);
        Assert.False(result.Drag);
        Assert.False(result.Turn);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.2, 0)]
    [InlineData(0, 1)]
    public void CenterAndUpDoNotActivateFlightActions(float x, float y)
    {
        var result = ControllerInputMapping.Map(
            ControllerHand.Left,
            new(new(x, y), true, false, false, false));

        Assert.False(result.Drag);
        Assert.False(result.Turn);
        Assert.False(result.ModeSwitch);
    }

    [Fact]
    public void TrackpadPositionRequiresTouch()
    {
        var result = ControllerInputMapping.Map(
            ControllerHand.Left,
            new(new(-1, 0), false, false, false, false));

        Assert.Equal(default, result);
    }
}
