namespace FlugelKranz.OpenXR;

public enum ControllerHand
{
    Left,
    Right
}

public readonly record struct ControllerInputState(
    bool DpadLeft,
    bool DpadRight,
    bool DpadDown,
    bool ThumbRestTouched,
    bool TriggerTouched,
    bool TouchInputsActive);

public readonly record struct ManipulationActions(bool Drag, bool Turn, bool ModeSwitch);

/// <summary>Maps interaction-profile controls to logical actions used by the motion engines.</summary>
public static class ControllerInputMapping
{
    public static ManipulationActions Map(ControllerHand hand, ControllerInputState input)
    {
        bool touchDrag = input.TouchInputsActive && input.ThumbRestTouched && input.TriggerTouched;
        bool touchTurn = input.TouchInputsActive && input.ThumbRestTouched && !input.TriggerTouched;
        bool indexDrag = hand == ControllerHand.Left ? input.DpadLeft : input.DpadRight;
        bool indexTurn = hand == ControllerHand.Left ? input.DpadRight : input.DpadLeft;
        return new(indexDrag || touchDrag, indexTurn || touchTurn, input.DpadDown);
    }
}
