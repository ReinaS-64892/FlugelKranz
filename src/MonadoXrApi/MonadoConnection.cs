using System.Numerics;
using System.Runtime.InteropServices;
using FlugelKranz.Core;

namespace MonadoXrApi;

public readonly record struct TrackingOriginOffset(uint Index, RigidPose Original, RigidPose Current);

/// <summary>Minimal libmonado 1.4+ ABI, checked against the read-only monado.h.</summary>
public sealed class MonadoConnection : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePose
    {
        public float X, Y, Z, W, Px, Py, Pz;
        public readonly RigidPose Managed => new(new Quaternion(X, Y, Z, W), new Vector3(Px, Py, Pz));
        public NativePose(RigidPose p) => (X, Y, Z, W, Px, Py, Pz) =
            (p.Orientation.X, p.Orientation.Y, p.Orientation.Z, p.Orientation.W, p.Position.X, p.Position.Y, p.Position.Z);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ApiVersion(out uint major, out uint minor, out uint patch);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Create(out nint root);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Destroy(ref nint root);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Role(nint root, [MarshalAs(UnmanagedType.LPUTF8Str)] string role, out int index);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Property(nint root, uint index, uint property, out uint value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetPose(nint root, uint index, out NativePose pose);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetPose(nint root, uint index, in NativePose pose);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int UpdateClients(nint root);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ClientCount(nint root, out uint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ClientId(nint root, uint index, out uint id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ClientName(nint root, uint id, out nint name);

    private nint library, root;
    private readonly Destroy destroy;
    private readonly GetPose getOrigin, getReference;
    private readonly SetPose setOrigin;
    private readonly Dictionary<uint, Origin> origins = [];
    private uint headOrigin, leftOrigin, rightOrigin;
    public RigidPose OriginalOffset => origins[headOrigin].Original;
    public RigidPose CurrentOffset => origins[headOrigin].Current;
    public RigidPose StageToRoot { get; }
    public IReadOnlyList<TrackingOriginOffset> TrackingOrigins => origins
        .OrderBy(pair => pair.Key)
        .Select(pair => new TrackingOriginOffset(pair.Key, pair.Value.Original, pair.Value.Current))
        .ToArray();

    private sealed class Origin(RigidPose offset)
    {
        public RigidPose Original { get; } = offset;
        public RigidPose Current { get; set; } = offset;
    }

    public MonadoConnection(string libraryPath)
    {
        library = NativeLibrary.Load(libraryPath);
        try
        {
            Load<ApiVersion>("mnd_api_get_version")(out var major, out var minor, out _);
            if (major != 1 || minor < 4)
                throw new NotSupportedException($"libmonado API 1.4 以降の 1.x が必要です（検出: {major}.{minor}）。");
            destroy = Load<Destroy>("mnd_root_destroy");
            getOrigin = Load<GetPose>("mnd_root_get_tracking_origin_offset");
            getReference = Load<GetPose>("mnd_root_get_reference_space_offset");
            setOrigin = Load<SetPose>("mnd_root_set_tracking_origin_offset");
            Check(Load<Create>("mnd_root_create")(out root), "Monado / WiVRn への接続");
            var role = Load<Role>("mnd_root_get_device_from_role");
            var property = Load<Property>("mnd_root_get_device_info_u32");
            foreach (var name in new[] { "head", "left", "right" })
            {
                Check(role(root, name, out int device), $"{name} デバイスの取得");
                if (device < 0) throw new InvalidOperationException($"{name} デバイスが接続されていません。");
                Check(property(root, (uint)device, 2, out uint trackingOrigin), "トラッキング原点の取得");
                if (!origins.ContainsKey(trackingOrigin)) origins.Add(trackingOrigin, new Origin(ReadOrigin(trackingOrigin)));
                switch (name)
                {
                    case "head": headOrigin = trackingOrigin; break;
                    case "left": leftOrigin = trackingOrigin; break;
                    case "right": rightOrigin = trackingOrigin; break;
                }
            }
            // Static STAGE provides the known root-to-reference transform needed to remove feedback.
            // A driver-owned, moving stage cannot be assumed to be a fixed reference frame.
            StageToRoot = ReadStage();
        }
        catch
        {
            if (root != 0) destroy?.Invoke(ref root);
            NativeLibrary.Free(library);
            library = 0;
            throw;
        }
    }

    public void VerifyClient(string applicationName)
    {
        Check(Load<UpdateClients>("mnd_root_update_client_list")(root), "クライアント一覧の更新");
        Check(Load<ClientCount>("mnd_root_get_number_clients")(root, out var count), "クライアント数の取得");
        var getId = Load<ClientId>("mnd_root_get_client_id_at_index");
        var getName = Load<ClientName>("mnd_root_get_client_name");
        for (uint i = 0; i < count; i++)
        {
            Check(getId(root, i, out var id), "クライアント ID の取得");
            Check(getName(root, id, out var name), "クライアント名の取得");
            if (Marshal.PtrToStringUTF8(name) == applicationName) return;
        }
        throw new InvalidOperationException("OpenXR と libmonado が同じサービスに接続していません。XR_RUNTIME_JSON と IPC 接続先を確認してください。");
    }

    public void VerifyUnchanged()
    {
        foreach (var (origin, state) in origins)
            if (!ReadOrigin(origin).NearlyEquals(state.Current))
                throw new InvalidOperationException("他のツールがトラッキング原点オフセットを変更したため停止しました。再度オンにして接続し直してください。");
        if (!ReadStage().NearlyEquals(StageToRoot))
            throw new InvalidOperationException("基準空間が変更されたため停止しました。再度オンにして接続し直してください。");
    }

    public InputFrame ToPhysical(InputFrame stageFrame)
    {
        return new(ToPhysical(stageFrame.Head, headOrigin), stageFrame.HeadTracked,
            stageFrame.Left with { Pose = ToPhysical(stageFrame.Left.Pose, leftOrigin) },
            stageFrame.Right with { Pose = ToPhysical(stageFrame.Right.Pose, rightOrigin) });
    }

    public void Apply(RigidPose offset)
    {
        if (!offset.IsValid) throw new ArgumentException("空間変換が不正です。", nameof(offset));
        // Keep separately calibrated tracking origins aligned by applying the same
        // root-space delta to each one. Offset remains the HMD origin for the
        // manipulator's established coordinate system.
        var delta = offset * OriginalOffset.Inverse();
        var targets = origins.ToDictionary(pair => pair.Key, pair => delta * pair.Value.Original);
        var applied = new List<uint>();
        try
        {
            foreach (var (origin, target) in targets)
            {
                var native = new NativePose(target);
                Check(setOrigin(root, origin, in native), "空間オフセットの適用");
                applied.Add(origin);
            }
        }
        catch
        {
            foreach (var origin in applied)
            {
                var native = new NativePose(origins[origin].Current);
                setOrigin(root, origin, in native);
            }
            throw;
        }
        foreach (var (origin, target) in targets) origins[origin].Current = target;
    }

    public void Restore()
    {
        // Never overwrite a concurrent tool's newer offset.
        foreach (var (origin, state) in origins)
            if (!ReadOrigin(origin).NearlyEquals(state.Current))
                throw new InvalidOperationException("外部で変更されたトラッキング原点オフセットは復元しませんでした。");
        if (!CurrentOffset.NearlyEquals(OriginalOffset)) Apply(OriginalOffset);
    }

    private RigidPose ToPhysical(RigidPose stagePose, uint origin)
    {
        // First remove the current flight offset, then express every device in
        // the HMD's original physical frame. The second transform preserves the
        // calibration between independent tracking systems such as Lighthouse
        // and Quest instead of mixing their local coordinate systems.
        var state = origins[origin];
        var originToHead = OriginalOffset.Inverse() * state.Original;
        var stageToOrigin = state.Current.Inverse() * StageToRoot;
        return originToHead * stageToOrigin * stagePose;
    }

    private RigidPose ReadOrigin(uint origin)
    {
        Check(getOrigin(root, origin, out var pose), "トラッキング原点の読み取り");
        return Validate(pose);
    }

    private RigidPose ReadStage()
    {
        Check(getReference(root, 3, out var pose), "固定 STAGE 空間の読み取り（ドライバー管理の STAGE は未対応）");
        return Validate(pose);
    }

    private static RigidPose Validate(NativePose pose) => pose.Managed.IsValid ? pose.Managed
        : throw new InvalidOperationException("ランタイムが不正な位置・姿勢を返しました。");
    private T Load<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));
    private static void Check(int result, string operation)
    {
        if (result < 0) throw new InvalidOperationException($"{operation}に失敗しました（libmonado: {result}）。");
    }

    public void Dispose()
    {
        if (root != 0) destroy(ref root);
        if (library != 0) { NativeLibrary.Free(library); library = 0; }
    }
}
