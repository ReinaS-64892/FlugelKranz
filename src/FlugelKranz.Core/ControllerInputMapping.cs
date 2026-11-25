using System.Numerics;

namespace FlugelKranz.Core;

public enum ControllerHand
{
    Left,
    Right
}

public readonly record struct ControllerInputState(
    Vector2 TrackpadPosition,
    bool TrackpadTouched,
    float TrackpadForce,
    bool ThumbRestTouched,
    bool TriggerTouched,
    bool TouchInputsActive)
{
    public bool DragButton { get; init; }
    public bool TurnButton { get; init; }
    public bool ResetButton { get; init; }
}

public readonly record struct ManipulationActions(bool Drag, bool Turn, bool DpadDown);

/// <summary>Maps interaction-profile controls to logical actions used by the motion engines.</summary>
public static class ControllerInputMapping
{
    public static ManipulationActions Map(
        ControllerHand hand,
        ControllerInputState input,
        ValveIndexInputSettings? valveIndexSettings = null)
    {
        var settings = (valveIndexSettings ?? ValveIndexInputSettings.Default).Normalized();
        bool touchDrag = input.TouchInputsActive && input.ThumbRestTouched && input.TriggerTouched;
        bool touchTurn = input.TouchInputsActive && input.ThumbRestTouched && !input.TriggerTouched;
        var dpad = TrackpadDpad(
            input.TrackpadPosition,
            input.TrackpadTouched,
            input.TrackpadForce,
            settings);
        bool indexDrag = hand == ControllerHand.Left ? dpad.Right : dpad.Left;
        bool indexTurn = hand == ControllerHand.Left ? dpad.Left : dpad.Right;
        return new(
            indexDrag || touchDrag || input.DragButton,
            indexTurn || touchTurn || input.TurnButton,
            dpad.Down || input.ResetButton);
    }

    private static (bool Left, bool Right, bool Down) TrackpadDpad(
        Vector2 position,
        bool touched,
        float force,
        ValveIndexInputSettings settings)
    {
        if (!touched || !float.IsFinite(force) || force < settings.ForceThreshold ||
            !float.IsFinite(position.X) || !float.IsFinite(position.Y) ||
            position.LengthSquared() < settings.PositionDeadZone * settings.PositionDeadZone)
            return default;

        if (MathF.Abs(position.X) >= MathF.Abs(position.Y))
            return position.X < 0 ? (true, false, false) : (false, true, false);
        return position.Y < 0 ? (false, false, true) : default;
    }
}
