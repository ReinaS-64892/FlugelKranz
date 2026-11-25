using System.Numerics;
using System.Text;
using FlugelKranz.Core;
using static FlugelKranz.OpenVR.OpenVrSession;

namespace FlugelKranz.OpenVR;

internal sealed unsafe class OpenVrInput
{
    private readonly OpenVrSession session;
    private readonly nint input, system;
    private readonly ulong actionSet;
    private readonly HandActions[] hands;
    private sealed class HandActions
    {
        public required ControllerHand Hand { get; init; }
        public required ulong Pose { get; init; }
        public required ulong Trackpad { get; init; }
        public required ulong TrackpadTouch { get; init; }
        public required ulong Force { get; init; }
        public required ulong ThumbRest { get; init; }
        public required ulong TriggerTouch { get; init; }
        public required ulong Drag { get; init; }
        public required ulong Turn { get; init; }
        public required ulong Reset { get; init; }
        public required ulong Haptic { get; init; }
    }

    public OpenVrInput(OpenVrSession session)
    {
        this.session = session;
        input = session.Input;
        system = session.System;
        byte[] path = Encoding.UTF8.GetBytes(Path.Combine(AppContext.BaseDirectory, "steamvr", "actions.json") + '\0');
        fixed (byte* p = path)
            Check(((delegate* unmanaged[Stdcall]<byte*, int>)Slot(input,
                OpenVrSlots.Input.SetActionManifestPath))(p), "Action manifest の登録");
        actionSet = Handle("/actions/flight", true);
        hands = [CreateHand(ControllerHand.Left), CreateHand(ControllerHand.Right)];
    }

    private HandActions CreateHand(ControllerHand hand)
    {
        string prefix = hand == ControllerHand.Left ? "left_" : "right_";
        ulong In(string name) => Handle("/actions/flight/in/" + prefix + name);
        return new() { Hand = hand, Pose = In("pose"), Trackpad = In("trackpad"), TrackpadTouch = In("trackpad_touch"),
            Force = In("force"), ThumbRest = In("thumbrest"), TriggerTouch = In("trigger_touch"),
            Drag = In("drag"), Turn = In("turn"), Reset = In("reset"),
            Haptic = Handle("/actions/flight/out/" + prefix + "haptic") };
    }

    private ulong Handle(string name, bool set = false)
    {
        ulong handle = 0;
        byte[] bytes = Encoding.UTF8.GetBytes(name + '\0');
        fixed (byte* p = bytes)
            Check(((delegate* unmanaged[Stdcall]<byte*, ulong*, int>)Slot(input, set ?
                OpenVrSlots.Input.GetActionSetHandle : OpenVrSlots.Input.GetActionHandle))(p, &handle), "Action handle の取得");
        return handle;
    }

    public InputFrame Read(ValveIndexInputSettings settings)
    {
        session.PumpEvents();
        var set = new VrActionSet { Handle = actionSet };
        Check(((delegate* unmanaged[Stdcall]<VrActionSet*, uint, uint, int>)Slot(input,
            OpenVrSlots.Input.UpdateActionState))(&set, (uint)sizeof(VrActionSet), 1), "入力の同期");
        VrTrackedPose* poses = stackalloc VrTrackedPose[64];
        ((delegate* unmanaged[Stdcall]<int, float, VrTrackedPose*, uint, void>)Slot(system,
            OpenVrSlots.System.GetDeviceToAbsoluteTrackingPose))(1, 0, poses, 64); // Standing, no prediction.
        List<TrackerSample> trackers = [];
        for (uint i = 1; i < 64; i++)
        {
            if (!poses[i].IsTracked || ((delegate* unmanaged[Stdcall]<uint, int>)Slot(system,
                OpenVrSlots.System.GetTrackedDeviceClass))(i) != 3) continue;
            string serial = Serial(i);
            if (serial.Length > 0) trackers.Add(new(serial, poses[i].Pose.Position, true));
        }
        return new(poses[0].Pose, poses[0].IsTracked, ReadHand(hands[0], settings),
            ReadHand(hands[1], settings)) { Trackers = trackers };
    }

    private HandSample ReadHand(HandActions hand, ValveIndexInputSettings settings)
    {
        VrPoseAction pose = default;
        Check(((delegate* unmanaged[Stdcall]<ulong, int, float, VrPoseAction*, uint, ulong, int>)Slot(input,
            OpenVrSlots.Input.GetPoseActionDataRelativeToNow))(hand.Pose, 1, 0, &pose, (uint)sizeof(VrPoseAction), 0), "手の姿勢の取得");
        if (pose.Active == 0 || !pose.Pose.IsTracked) return default;
        var trackpad = Analog(hand.Trackpad);
        var force = Analog(hand.Force);
        var thumb = Digital(hand.ThumbRest);
        var trigger = Digital(hand.TriggerTouch);
        var actions = ControllerInputMapping.Map(hand.Hand,
            new(new Vector2(trackpad.X, trackpad.Y), Digital(hand.TrackpadTouch).Pressed, force.Active != 0 ? force.X : 0,
                thumb.Pressed, trigger.Pressed, thumb.Active != 0 && trigger.Active != 0)
            { DragButton = Digital(hand.Drag).Pressed, TurnButton = Digital(hand.Turn).Pressed, ResetButton = Digital(hand.Reset).Pressed }, settings);
        return new(pose.Pose.Pose, actions.Drag ? 1 : 0, actions.Turn ? 1 : 0, actions.DpadDown ? 1 : 0, true)
        { TrackpadForceActive = force.Active != 0, TrackpadForce = force.X };
    }

    private VrDigital Digital(ulong handle)
    {
        VrDigital data = default;
        Check(((delegate* unmanaged[Stdcall]<ulong, VrDigital*, uint, ulong, int>)Slot(input,
            OpenVrSlots.Input.GetDigitalActionData))(handle, &data, (uint)sizeof(VrDigital), 0), "ボタン入力の取得");
        return data;
    }

    private VrAnalog Analog(ulong handle)
    {
        VrAnalog data = default;
        Check(((delegate* unmanaged[Stdcall]<ulong, VrAnalog*, uint, ulong, int>)Slot(input,
            OpenVrSlots.Input.GetAnalogActionData))(handle, &data, (uint)sizeof(VrAnalog), 0), "アナログ入力の取得");
        return data;
    }

    private string Serial(uint device)
    {
        byte* buffer = stackalloc byte[256];
        int error = 0;
        uint length = ((delegate* unmanaged[Stdcall]<uint, int, byte*, uint, int*, uint>)Slot(system,
            OpenVrSlots.System.GetStringTrackedDeviceProperty))(device, 1002, buffer, 256, &error);
        return error == 0 && length > 1 && length <= 256 ? Encoding.UTF8.GetString(buffer, (int)length - 1) : "";
    }

    public void Haptic(float duration, float frequency, float amplitude)
    {
        foreach (var hand in hands)
            Check(((delegate* unmanaged[Stdcall]<ulong, float, float, float, float, ulong, int>)Slot(input,
                OpenVrSlots.Input.TriggerHapticVibrationAction))(hand.Haptic, 0, duration, frequency, amplitude, 0), "振動の送信");
    }
}
