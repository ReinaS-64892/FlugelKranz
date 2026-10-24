using System.Numerics;
using FlugelKranz.Core;

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
    bool TouchInputsActive)
{
    public bool FrameBumperClicked { get; init; }
    public bool FrameTurnButtonClicked { get; init; }
    public bool FrameResetButtonClicked { get; init; }
}

public readonly record struct ManipulationActions(bool Drag, bool Turn, bool DpadDown);

/// <summary>Maps interaction-profile controls to logical actions used by the motion engines.</summary>
public static class ControllerInputMapping
{
    internal const string SteamFrameExtension = "XR_VALVE_frame_controller_interaction";
    internal const string SteamFrameProfile = "/interaction_profiles/valve/frame_controller_valve";

    // Valve's native Frame profile exposes bumpers separately from the analog triggers.
    // https://partner.steamgames.com/doc/steamhardware/steamframe/input
    internal static (string Drag, string Turn, string Reset) SteamFrameButtonPaths(ControllerHand hand)
    {
        string prefix = hand == ControllerHand.Left ? "/user/hand/left/input/" : "/user/hand/right/input/";
        return (
            prefix + "bumper/click",
            prefix + (hand == ControllerHand.Left ? "dpad_up/click" : "y/click"),
            prefix + (hand == ControllerHand.Left ? "view/click" : "x/click"));
    }

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
            indexDrag || touchDrag || input.FrameBumperClicked,
            indexTurn || touchTurn || input.FrameTurnButtonClicked,
            dpad.Down || input.FrameResetButtonClicked);
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
