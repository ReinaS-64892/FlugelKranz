using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Evergine.Bindings.OpenXR;
using FlugelKranz.Core;
using static Evergine.Bindings.OpenXR.OpenXRNative;

namespace FlugelKranz.OpenXR;

/// <summary>A non-rendering OpenXR input client; never requests primary/focused status from libmonado.</summary>
public sealed unsafe class OpenXrInput : IDisposable
{
    private XrInstance instance;
    private XrSession session;
    private XrActionSet actionSet;
    private XrSpace stage, view;
    private readonly Hand[] hands = [new(), new()];
    private bool running;
    private XrSessionState state;
    private delegate* unmanaged[Cdecl]<XrInstance, Timespec*, long*, XrResult> convertTime;
    public string ApplicationName { get; } = $"FlugelKranz-{Environment.ProcessId}";
    public string RuntimeName { get; private set; } = "";

    private sealed class Hand
    {
        public XrAction Pose, Grip, Click;
        public XrSpace Space;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Timespec { public long Seconds, Nanoseconds; }
    [DllImport("libc", SetLastError = true)] private static extern int clock_gettime(int clockId, out Timespec time);

    public OpenXrInput()
    {
        try
        {
            CreateInstance();
            LoadFunctionPointers(instance);
            var properties = new XrInstanceProperties { type = XrStructureType.XR_TYPE_INSTANCE_PROPERTIES };
            Check(xrGetInstanceProperties(instance, &properties), "ランタイム情報の取得");
            RuntimeName = Marshal.PtrToStringUTF8((nint)properties.runtimeName) ?? "unknown";
            if (!RuntimeName.Contains("Monado", StringComparison.OrdinalIgnoreCase)
                && !RuntimeName.Contains("WiVRn", StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException($"Monado / WiVRn の OpenXR ランタイムを指定してください（検出: {RuntimeName}）。");
            nint timeFunction = 0;
            fixed (byte* name = "xrConvertTimespecTimeToTimeKHR\0"u8)
                Check(xrGetInstanceProcAddr(instance, name, (nint)(&timeFunction)), "OpenXR 時刻変換の取得");
            if (timeFunction == 0) throw new NotSupportedException("OpenXR 時刻変換が利用できません。");
            convertTime = (delegate* unmanaged[Cdecl]<XrInstance, Timespec*, long*, XrResult>)timeFunction;

            var systemInfo = new XrSystemGetInfo { type = XrStructureType.XR_TYPE_SYSTEM_GET_INFO, formFactor = XrFormFactor.XR_FORM_FACTOR_HEAD_MOUNTED_DISPLAY };
            XrSystemId system;
            Check(xrGetSystem(instance, &systemInfo, &system), "HMD の取得");
            var sessionInfo = new XrSessionCreateInfo { type = XrStructureType.XR_TYPE_SESSION_CREATE_INFO, systemId = system };
            XrSession createdSession;
            Check(xrCreateSession(instance, &sessionInfo, &createdSession), "描画なしセッションの作成");
            session = createdSession;
            stage = CreateReference(XrReferenceSpaceType.XR_REFERENCE_SPACE_TYPE_STAGE);
            view = CreateReference(XrReferenceSpaceType.XR_REFERENCE_SPACE_TYPE_VIEW);
            CreateActions();
        }
        catch { Dispose(); throw; }
    }

    private void CreateInstance()
    {
        uint count;
        Check(xrEnumerateInstanceExtensionProperties(null, 0, &count, null), "OpenXR 拡張の列挙");
        var properties = new XrExtensionProperties[count];
        for (int i = 0; i < properties.Length; i++) properties[i].type = XrStructureType.XR_TYPE_EXTENSION_PROPERTIES;
        var extensions = new HashSet<string>();
        fixed (XrExtensionProperties* p = properties)
        {
            Check(xrEnumerateInstanceExtensionProperties(null, count, &count, p), "OpenXR 拡張の取得");
            for (int i = 0; i < count; i++) extensions.Add(Marshal.PtrToStringUTF8((nint)p[i].extensionName)!);
        }
        foreach (var required in new[] { "XR_MND_headless", "XR_KHR_convert_timespec_time" })
            if (!extensions.Contains(required)) throw new NotSupportedException($"{required} が必要です。Monado / WiVRn の OpenXR ランタイムを確認してください。");

        fixed (byte* headless = "XR_MND_headless\0"u8)
        fixed (byte* time = "XR_KHR_convert_timespec_time\0"u8)
        {
            byte** names = stackalloc byte*[2] { headless, time };
            var info = new XrInstanceCreateInfo
            {
                type = XrStructureType.XR_TYPE_INSTANCE_CREATE_INFO,
                enabledExtensionCount = 2, enabledExtensionNames = names,
                applicationInfo = new XrApplicationInfo { apiVersion = 1UL << 48, applicationVersion = 1 }
            };
            Copy(ApplicationName, info.applicationInfo.applicationName, 128);
            Copy("FlugelKranz", info.applicationInfo.engineName, 128);
            XrInstance created;
            Check(xrCreateInstance(&info, &created), "OpenXR インスタンスの作成");
            instance = created;
        }
    }

    private void CreateActions()
    {
        var setInfo = new XrActionSetCreateInfo { type = XrStructureType.XR_TYPE_ACTION_SET_CREATE_INFO };
        Copy("flight", setInfo.actionSetName, 64);
        Copy("FlugelKranz flight", setInfo.localizedActionSetName, 128);
        XrActionSet created;
        Check(xrCreateActionSet(instance, &setInfo, &created), "アクションセットの作成");
        actionSet = created;
        for (int i = 0; i < hands.Length; i++)
        {
            string side = i == 0 ? "left" : "right";
            hands[i].Pose = CreateAction($"{side}_pose", XrActionType.XR_ACTION_TYPE_POSE_INPUT);
            hands[i].Grip = CreateAction($"{side}_grip", XrActionType.XR_ACTION_TYPE_FLOAT_INPUT);
            hands[i].Click = CreateAction($"{side}_click", XrActionType.XR_ACTION_TYPE_BOOLEAN_INPUT);
        }
        Suggest("/interaction_profiles/oculus/touch_controller", "squeeze/value", false);
        Suggest("/interaction_profiles/valve/index_controller", "squeeze/value", false);
        Suggest("/interaction_profiles/htc/vive_controller", "squeeze/click", true);
        Suggest("/interaction_profiles/microsoft/motion_controller", "squeeze/click", true);
        var set = actionSet;
        var attach = new XrSessionActionSetsAttachInfo
        { type = XrStructureType.XR_TYPE_SESSION_ACTION_SETS_ATTACH_INFO, countActionSets = 1, actionSets = &set };
        Check(xrAttachSessionActionSets(session, &attach), "アクションセットの接続");
        foreach (var hand in hands)
        {
            var info = new XrActionSpaceCreateInfo
            { type = XrStructureType.XR_TYPE_ACTION_SPACE_CREATE_INFO, action = hand.Pose, poseInActionSpace = IdentityPose() };
            XrSpace createdSpace;
            Check(xrCreateActionSpace(session, &info, &createdSpace), "コントローラー空間の作成");
            hand.Space = createdSpace;
        }
    }

    private XrAction CreateAction(string name, XrActionType type)
    {
        var info = new XrActionCreateInfo { type = XrStructureType.XR_TYPE_ACTION_CREATE_INFO, actionType = type };
        Copy(name, info.actionName, 64);
        Copy(name, info.localizedActionName, 128);
        XrAction action;
        Check(xrCreateAction(actionSet, &info, &action), "アクションの作成");
        return action;
    }

    private void Suggest(string profile, string gripComponent, bool click)
    {
        XrActionSuggestedBinding* bindings = stackalloc XrActionSuggestedBinding[4];
        for (int i = 0; i < hands.Length; i++)
        {
            string prefix = i == 0 ? "/user/hand/left/input/" : "/user/hand/right/input/";
            bindings[i * 2] = new() { action = hands[i].Pose, binding = Path(prefix + "grip/pose") };
            bindings[i * 2 + 1] = new() { action = click ? hands[i].Click : hands[i].Grip, binding = Path(prefix + gripComponent) };
        }
        var info = new XrInteractionProfileSuggestedBinding
        { type = XrStructureType.XR_TYPE_INTERACTION_PROFILE_SUGGESTED_BINDING, interactionProfile = Path(profile), countSuggestedBindings = 4, suggestedBindings = bindings };
        var result = xrSuggestInteractionProfileBindings(instance, &info);
        // Runtimes may omit profiles; their supported profiles must still validate correctly.
        if (result != XrResult.XR_ERROR_PATH_UNSUPPORTED) Check(result, $"{profile} の入力設定");
    }

    public InputFrame Read()
    {
        PollEvents();
        if (!running || state != XrSessionState.XR_SESSION_STATE_FOCUSED) return default;
        var active = new XrActiveActionSet { actionSet = actionSet };
        var sync = new XrActionsSyncInfo { type = XrStructureType.XR_TYPE_ACTIONS_SYNC_INFO, countActiveActionSets = 1, activeActionSets = &active };
        var result = xrSyncActions(session, &sync);
        if (result == XrResult.XR_SESSION_NOT_FOCUSED) return default;
        Check(result, "入力の同期");
        if (clock_gettime(1, out var now) != 0) throw new InvalidOperationException("単調増加時計を取得できません。");
        long time;
        Check(convertTime(instance, &now, &time), "OpenXR 時刻の変換");
        var (head, tracked) = Locate(view, time);
        return new(head, tracked, ReadHand(hands[0], time), ReadHand(hands[1], time));
    }

    private HandSample ReadHand(Hand hand, long time)
    {
        var get = new XrActionStateGetInfo { type = XrStructureType.XR_TYPE_ACTION_STATE_GET_INFO, action = hand.Pose };
        var poseState = new XrActionStatePose { type = XrStructureType.XR_TYPE_ACTION_STATE_POSE };
        Check(xrGetActionStatePose(session, &get, &poseState), "姿勢アクションの取得");
        get.action = hand.Grip;
        var grip = new XrActionStateFloat { type = XrStructureType.XR_TYPE_ACTION_STATE_FLOAT };
        Check(xrGetActionStateFloat(session, &get, &grip), "グリップ値の取得");
        get.action = hand.Click;
        var click = new XrActionStateBoolean { type = XrStructureType.XR_TYPE_ACTION_STATE_BOOLEAN };
        Check(xrGetActionStateBoolean(session, &get, &click), "グリップボタンの取得");
        if (!poseState.isActive || (!grip.isActive && !click.isActive)) return default;
        var (pose, tracked) = Locate(hand.Space, time);
        return new(pose, grip.isActive ? grip.currentState : click.currentState ? 1 : 0, tracked);
    }

    private (RigidPose, bool) Locate(XrSpace space, long time)
    {
        var location = new XrSpaceLocation { type = XrStructureType.XR_TYPE_SPACE_LOCATION };
        Check(xrLocateSpace(space, stage, time, &location), "位置・姿勢の取得");
        // OpenXR distinguishes a valid pose from an actively tracked pose. A runtime can
        // provide a valid last-known position without POSITION_TRACKED (as Monado's
        // simulated HMD does); keep using that pose while it remains valid.
        const ulong requiredValidFlags = 5UL;
        if ((location.locationFlags & requiredValidFlags) != requiredValidFlags)
            return (RigidPose.Identity, false);
        var p = location.pose;
        var pose = new RigidPose(new Quaternion(p.orientation.x, p.orientation.y, p.orientation.z, p.orientation.w),
            new Vector3(p.position.x, p.position.y, p.position.z));
        return (pose, pose.IsValid);
    }

    private void PollEvents()
    {
        while (true)
        {
            var buffer = new XrEventDataBuffer { type = XrStructureType.XR_TYPE_EVENT_DATA_BUFFER };
            var result = xrPollEvent(instance, &buffer);
            if (result == XrResult.XR_EVENT_UNAVAILABLE) return;
            Check(result, "OpenXR イベントの取得");
            if (buffer.type == XrStructureType.XR_TYPE_EVENT_DATA_INSTANCE_LOSS_PENDING)
                throw new InvalidOperationException("OpenXR ランタイムへの接続が失われました。");
            if (buffer.type == XrStructureType.XR_TYPE_EVENT_DATA_REFERENCE_SPACE_CHANGE_PENDING)
                throw new InvalidOperationException("OpenXR 基準空間が変更されました。再接続してください。");
            if (buffer.type != XrStructureType.XR_TYPE_EVENT_DATA_SESSION_STATE_CHANGED) continue;
            var change = (XrEventDataSessionStateChanged*)&buffer;
            state = change->state;
            if (state == XrSessionState.XR_SESSION_STATE_READY && !running)
            {
                var begin = new XrSessionBeginInfo { type = XrStructureType.XR_TYPE_SESSION_BEGIN_INFO, primaryViewConfigurationType = XrViewConfigurationType.XR_VIEW_CONFIGURATION_TYPE_PRIMARY_STEREO };
                Check(xrBeginSession(session, &begin), "入力セッションの開始");
                running = true;
            }
            if (state == XrSessionState.XR_SESSION_STATE_STOPPING)
            {
                if (running) Check(xrEndSession(session), "入力セッションの終了");
                running = false;
                throw new InvalidOperationException("OpenXR セッションが停止しました。再接続してください。");
            }
            if (state is XrSessionState.XR_SESSION_STATE_EXITING or XrSessionState.XR_SESSION_STATE_LOSS_PENDING)
                throw new InvalidOperationException("OpenXR セッションが終了しました。");
        }
    }

    private XrSpace CreateReference(XrReferenceSpaceType type)
    {
        var info = new XrReferenceSpaceCreateInfo { type = XrStructureType.XR_TYPE_REFERENCE_SPACE_CREATE_INFO, referenceSpaceType = type, poseInReferenceSpace = IdentityPose() };
        XrSpace space;
        Check(xrCreateReferenceSpace(session, &info, &space), "基準空間の作成");
        return space;
    }

    private XrPath Path(string value)
    {
        XrPath path;
        fixed (byte* str = Encoding.UTF8.GetBytes(value + '\0')) Check(xrStringToPath(instance, str, &path), "入力パスの変換");
        return path;
    }
    private static XrPosef IdentityPose() => new() { orientation = new XrQuaternionf { w = 1 } };
    private static void Copy(string value, byte* target, int size)
    {
        var span = new Span<byte>(target, size);
        span.Clear();
        Encoding.UTF8.GetBytes(value, span[..(size - 1)]);
    }
    private static void Check(XrResult result, string operation)
    {
        if ((int)result < 0) throw new InvalidOperationException($"{operation}に失敗しました（OpenXR: {result}）。");
        if (result == XrResult.XR_SESSION_LOSS_PENDING) throw new InvalidOperationException("OpenXR セッションの接続が失われます。");
    }

    public void Dispose()
    {
        foreach (var hand in hands)
            if (hand.Space != XrSpace.Null) { xrDestroySpace(hand.Space); hand.Space = XrSpace.Null; }
        if (view != XrSpace.Null) { xrDestroySpace(view); view = XrSpace.Null; }
        if (stage != XrSpace.Null) { xrDestroySpace(stage); stage = XrSpace.Null; }
        if (session != XrSession.Null) { xrDestroySession(session); session = XrSession.Null; }
        if (actionSet != XrActionSet.Null) { xrDestroyActionSet(actionSet); actionSet = XrActionSet.Null; }
        if (instance != XrInstance.Null) { xrDestroyInstance(instance); instance = XrInstance.Null; }
    }
}
