using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Evergine.Bindings.OpenXR;
using static Evergine.Bindings.OpenXR.OpenXRNative;

namespace FlugelKranz.OpenXR;

/// <summary>Minimal XR_MNDX_xdev_space ABI, absent from the pinned Evergine bindings.</summary>
internal sealed unsafe class XdevSpaceApi : IXdevSpaceApi
{
    // Verified against Monado 9950e2a5's XR_MNDX_xdev_space.h (spec version 3).
    // These structures and five entry points also have the version 1 layout.
    [StructLayout(LayoutKind.Sequential)]
    internal struct SystemProperties
    {
        public XrStructureType Type;
        public nint Next;
        public uint SupportsXdevSpace;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CreateListInfo
    {
        public XrStructureType Type;
        public nint Next;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GetInfo
    {
        public XrStructureType Type;
        public nint Next;
        public ulong Id;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Properties
    {
        public XrStructureType Type;
        public nint Next;
        public fixed byte Name[256];
        public fixed byte Serial[256];
        public uint CanCreateSpace;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CreateSpaceInfo
    {
        public XrStructureType Type;
        public nint Next;
        public nint List;
        public ulong Id;
        public XrPosef Offset;
    }

    private readonly XrSession session;
    private readonly XrSpace stage;
    private readonly delegate* unmanaged[Cdecl]<XrSession, CreateListInfo*, nint*, XrResult> createList;
    private readonly delegate* unmanaged[Cdecl]<nint, uint, uint*, ulong*, XrResult> enumerate;
    private readonly delegate* unmanaged[Cdecl]<nint, GetInfo*, Properties*, XrResult> getProperties;
    private readonly delegate* unmanaged[Cdecl]<XrSession, CreateSpaceInfo*, XrSpace*, XrResult> createSpace;
    private readonly delegate* unmanaged[Cdecl]<nint, XrResult> destroyList;

    public XdevSpaceApi(XrInstance instance, XrSession session, XrSpace stage)
    {
        this.session = session;
        this.stage = stage;
        createList = (delegate* unmanaged[Cdecl]<XrSession, CreateListInfo*, nint*, XrResult>)Load(instance, "xrCreateXDevListMNDX");
        enumerate = (delegate* unmanaged[Cdecl]<nint, uint, uint*, ulong*, XrResult>)Load(instance, "xrEnumerateXDevsMNDX");
        getProperties = (delegate* unmanaged[Cdecl]<nint, GetInfo*, Properties*, XrResult>)Load(instance, "xrGetXDevPropertiesMNDX");
        createSpace = (delegate* unmanaged[Cdecl]<XrSession, CreateSpaceInfo*, XrSpace*, XrResult>)Load(instance, "xrCreateXDevSpaceMNDX");
        destroyList = (delegate* unmanaged[Cdecl]<nint, XrResult>)Load(instance, "xrDestroyXDevListMNDX");
    }

    public static bool SupportsSystem(XrInstance instance, XrSystemId system)
    {
        var xdev = new SystemProperties { Type = (XrStructureType)1000444001 };
        var properties = new XrSystemProperties { type = XrStructureType.XR_TYPE_SYSTEM_PROPERTIES, next = &xdev };
        OpenXrInput.Check(xrGetSystemProperties(instance, system, &properties), "トラッカー空間対応の確認");
        return xdev.SupportsXdevSpace != 0;
    }

    public nint CreateList()
    {
        var info = new CreateListInfo { Type = (XrStructureType)1000444002 };
        nint list = 0;
        OpenXrInput.Check(createList(session, &info, &list), "トラッカー一覧の作成");
        return list;
    }

    public IReadOnlyList<TrackerDevice> Enumerate(nint list)
    {
        uint count;
        OpenXrInput.Check(enumerate(list, 0, &count, null), "トラッカー候補数の取得");
        var ids = new ulong[count];
        fixed (ulong* pointer = ids)
            OpenXrInput.Check(enumerate(list, count, &count, pointer), "トラッカー候補の列挙");
        var devices = new List<TrackerDevice>((int)count);
        for (int i = 0; i < count; i++)
        {
            var info = new GetInfo { Type = (XrStructureType)1000444003, Id = ids[i] };
            var properties = new Properties { Type = (XrStructureType)1000444004 };
            OpenXrInput.Check(getProperties(list, &info, &properties), "トラッカー候補の情報取得");
            devices.Add(new(ids[i], ReadString(properties.Name), ReadString(properties.Serial), properties.CanCreateSpace != 0));
        }
        return devices;
    }

    public XrSpace CreateSpace(nint list, ulong id)
    {
        var info = new CreateSpaceInfo
        {
            Type = (XrStructureType)1000444005,
            List = list,
            Id = id,
            Offset = new() { orientation = new() { w = 1 } }
        };
        XrSpace space;
        OpenXrInput.Check(createSpace(session, &info, &space), "トラッカー空間の作成");
        return space;
    }

    public (Vector3 Position, bool IsTracked) Locate(XrSpace space, long time)
    {
        var location = new XrSpaceLocation { type = XrStructureType.XR_TYPE_SPACE_LOCATION };
        OpenXrInput.Check(xrLocateSpace(space, stage, time, &location), "トラッカー位置の取得");
        var position = location.pose.position;
        return (new(position.x, position.y, position.z), HasTrackedPosition(location.locationFlags));
    }

    internal static bool HasTrackedPosition(ulong flags)
    {
        // POSITION_VALID (0x2) and POSITION_TRACKED (0x8): a cached valid
        // position alone must not keep a disconnected tracker in the midpoint.
        const ulong required = 0x2 | 0x8;
        return (flags & required) == required;
    }

    public void DestroySpace(XrSpace space) => xrDestroySpace(space);
    public void DestroyList(nint list) => destroyList(list);

    private static nint Load(XrInstance instance, string name)
    {
        nint function = 0;
        fixed (byte* pointer = Encoding.UTF8.GetBytes(name + '\0'))
            OpenXrInput.Check(xrGetInstanceProcAddr(instance, pointer, (nint)(&function)), name);
        if (function == 0)
            throw new NotSupportedException($"{name} が利用できません。");
        return function;
    }

    private static string ReadString(byte* pointer)
    {
        var bytes = new ReadOnlySpan<byte>(pointer, 256);
        int terminator = bytes.IndexOf((byte)0);
        return Encoding.UTF8.GetString(terminator >= 0 ? bytes[..terminator] : bytes);
    }
}
