using FlugelKranz.OpenXR;
using Xunit;

namespace FlugelKranz.Tests;

public class ControllerInputMappingTests
{
    [Theory]
    [InlineData(ControllerHand.Left, true, false, true, false)]
    [InlineData(ControllerHand.Left, false, true, false, true)]
    [InlineData(ControllerHand.Right, true, false, false, true)]
    [InlineData(ControllerHand.Right, false, true, true, false)]
    public void IndexDpadDirectionsMapToHandSpecificActions(
        ControllerHand hand,
        bool left,
        bool right,
        bool drag,
        bool turn)
    {
        var result = ControllerInputMapping.Map(
            hand,
            new(left, right, false, false, false, false));

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
            new(false, false, false, true, triggerTouched, true));

        Assert.Equal(drag, result.Drag);
        Assert.Equal(turn, result.Turn);
    }

    [Fact]
    public void IndexDpadDownMapsToModeSwitch()
    {
        var result = ControllerInputMapping.Map(
            ControllerHand.Right,
            new(false, false, true, false, false, false));

        Assert.True(result.ModeSwitch);
        Assert.False(result.Drag);
        Assert.False(result.Turn);
    }
}
