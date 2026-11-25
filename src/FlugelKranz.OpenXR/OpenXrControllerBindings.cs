using FlugelKranz.Core;

namespace FlugelKranz.OpenXR;

internal static class OpenXrControllerBindings
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
            prefix + (hand == ControllerHand.Left ? "view/click" : "menu/click"));
    }

}
