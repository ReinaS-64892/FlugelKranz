using System.Runtime.InteropServices;

namespace MonadoXrApi;

public enum MonadoReferenceSpaceType : uint
{
    View,
    Local,
    LocalFloor,
    Stage,
    Unbounded
}

/// <summary>Typed libmonado root client. It contains no application-specific policy.</summary>
public sealed class MonadoRoot : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Create(out nint root);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Destroy(ref nint root);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Role(nint root, [MarshalAs(UnmanagedType.LPUTF8Str)] string role, out int index);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Property(nint root, uint index, uint property, out uint value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetPose(nint root, uint index, out MonadoPose pose);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetPose(nint root, uint index, in MonadoPose pose);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int UpdateClients(nint root);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ClientCount(nint root, out uint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ClientId(nint root, uint index, out uint id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ClientName(nint root, uint id, out nint name);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int RefreshDevices(nint root);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DeviceString(nint root, uint index, uint property, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ApplyDeviceRotations(nint root, in MonadoPose stage, uint count, [In] MonadoDeviceRotation[] rotations);

    private readonly RefreshDevices? refreshDevices;
    private readonly ApplyDeviceRotations? applyDeviceRotations;
    private readonly DeviceString deviceString;
    private readonly ClientCount deviceCount;
    public bool SupportsDeviceRotationOffsets => refreshDevices is not null && applyDeviceRotations is not null;

    private readonly LibMonadoLibrary library;
    private readonly Destroy destroy;
    private readonly Role role;
    private readonly Property property;
    private readonly GetPose getOrigin, getReference;
    private readonly SetPose setReference;
    private readonly UpdateClients updateClients;
    private readonly ClientCount clientCount;
    private readonly ClientId clientId;
    private readonly ClientName clientName;
    private nint root;

    public MonadoRoot(string libraryPath)
    {
        library = new(libraryPath);
        try
        {
            refreshDevices = library.TryGetDelegate<RefreshDevices>("mnd_root_flugelkranz_refresh_devices");
            applyDeviceRotations = library.TryGetDelegate<ApplyDeviceRotations>("mnd_root_flugelkranz_apply");
            deviceString = library.GetDelegate<DeviceString>("mnd_root_get_device_info_string");
            deviceCount = library.GetDelegate<ClientCount>("mnd_root_get_device_count");
            destroy = library.GetDelegate<Destroy>("mnd_root_destroy");
            role = library.GetDelegate<Role>("mnd_root_get_device_from_role");
            property = library.GetDelegate<Property>("mnd_root_get_device_info_u32");
            getOrigin = library.GetDelegate<GetPose>("mnd_root_get_tracking_origin_offset");
            getReference = library.GetDelegate<GetPose>("mnd_root_get_reference_space_offset");
            setReference = library.GetDelegate<SetPose>("mnd_root_set_reference_space_offset");
            updateClients = library.GetDelegate<UpdateClients>("mnd_root_update_client_list");
            clientCount = library.GetDelegate<ClientCount>("mnd_root_get_number_clients");
            clientId = library.GetDelegate<ClientId>("mnd_root_get_client_id_at_index");
            clientName = library.GetDelegate<ClientName>("mnd_root_get_client_name");
            Check(library.GetDelegate<Create>("mnd_root_create")(out root), "root の作成");
        }
        catch { library.Dispose(); throw; }
    }

    public IReadOnlyList<(uint Index, string Serial)> RefreshDeviceList()
    {
        if (!SupportsDeviceRotationOffsets) throw new NotSupportedException("個別トラッカー補正拡張がありません。");
        Check(refreshDevices!(root), "デバイス一覧の更新");
        Check(deviceCount(root, out var count), "デバイス数の取得");
        var devices = new List<(uint, string)>();
        for (uint i = 0; i < count; i++)
        {
            Check(deviceString(root, i, 1, out var serial), "デバイスシリアルの取得");
            devices.Add((i, Marshal.PtrToStringUTF8(serial) ?? string.Empty));
        }
        return devices;
    }

    public void ApplyStageAndDeviceRotations(MonadoPose stage, MonadoDeviceRotation[] rotations)
    {
        if (!SupportsDeviceRotationOffsets) throw new NotSupportedException("個別トラッカー補正拡張がありません。");
        if (rotations.Length > 32) throw new ArgumentOutOfRangeException(nameof(rotations));
        Check(applyDeviceRotations!(root, in stage, (uint)rotations.Length, rotations), "基準空間・トラッカー補正の設定");
    }

    public int GetDeviceFromRole(string roleName)
    {
        Check(role(root, roleName, out var device), $"{roleName} デバイスの取得");
        return device;
    }

    public uint GetTrackingOriginIndex(int deviceIndex)
    {
        Check(property(root, (uint)deviceIndex, 2, out var origin), "トラッキング原点の取得");
        return origin;
    }

    public MonadoPose GetTrackingOriginOffset(uint originIndex)
    {
        Check(getOrigin(root, originIndex, out var pose), "トラッキング原点オフセットの取得");
        return pose;
    }

    public MonadoPose GetReferenceSpaceOffset(MonadoReferenceSpaceType referenceSpaceType)
    {
        Check(getReference(root, (uint)referenceSpaceType, out var pose), "基準空間オフセットの取得");
        return pose;
    }

    public void SetReferenceSpaceOffset(MonadoReferenceSpaceType referenceSpaceType, MonadoPose pose) =>
        Check(setReference(root, (uint)referenceSpaceType, in pose), "基準空間オフセットの設定");

    public IReadOnlyList<string> GetClientNames()
    {
        Check(updateClients(root), "クライアント一覧の更新");
        Check(clientCount(root, out var count), "クライアント数の取得");
        var names = new List<string>((int)count);
        for (uint i = 0; i < count; i++)
        {
            Check(clientId(root, i, out var id), "クライアント ID の取得");
            Check(clientName(root, id, out var name), "クライアント名の取得");
            names.Add(Marshal.PtrToStringUTF8(name) ?? string.Empty);
        }
        return names;
    }

    private static void Check(int result, string operation)
    {
        if (result < 0) throw new InvalidOperationException($"{operation}に失敗しました（libmonado: {result}）。");
    }

    public void Dispose()
    {
        if (root != 0) destroy(ref root);
        library.Dispose();
    }
}
