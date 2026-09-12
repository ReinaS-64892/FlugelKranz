using System.Numerics;

namespace FlugelKranz.OpenXR;

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
    bool TouchInputsActive);

public readonly record struct ManipulationActions(bool Drag, bool Turn, bool ModeSwitch);

/// <summary>Maps interaction-profile controls to logical actions used by the motion engines.</summary>
public static class ControllerInputMapping
{
    private const float TrackpadCenterRadius = 0.3f;
    private const float TrackpadForceThreshold = 0.75f;

    public static ManipulationActions Map(ControllerHand hand, ControllerInputState input)
    {
        bool touchDrag = input.TouchInputsActive && input.ThumbRestTouched && input.TriggerTouched;
        bool touchTurn = input.TouchInputsActive && input.ThumbRestTouched && !input.TriggerTouched;
        var dpad = TrackpadDpad(
            input.TrackpadPosition,
            input.TrackpadTouched,
            input.TrackpadForce);
        bool indexDrag = hand == ControllerHand.Left ? dpad.Right : dpad.Left;
        bool indexTurn = hand == ControllerHand.Left ? dpad.Left : dpad.Right;
        return new(indexDrag || touchDrag, indexTurn || touchTurn, dpad.Down);
    }

    private static (bool Left, bool Right, bool Down) TrackpadDpad(
        Vector2 position,
        bool touched,
        float force)
    {
        if (!touched || !float.IsFinite(force) || force < TrackpadForceThreshold ||
            !float.IsFinite(position.X) || !float.IsFinite(position.Y) ||
            position.LengthSquared() < TrackpadCenterRadius * TrackpadCenterRadius)
            return default;

        if (MathF.Abs(position.X) >= MathF.Abs(position.Y))
            return position.X < 0 ? (true, false, false) : (false, true, false);
        return position.Y < 0 ? (false, false, true) : default;
    }
}
