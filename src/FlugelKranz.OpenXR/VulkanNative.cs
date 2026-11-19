using System.Runtime.InteropServices;

namespace FlugelKranz.OpenXR;

// Minimal Vulkan ABI used only to create an OpenXR graphics binding. Layouts
// follow vulkan_core.h; no images, command buffers or GPU submissions are made.
internal static unsafe class VulkanNative
{
    private const string Library = "libvulkan.so.1";

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void vkGetPhysicalDeviceQueueFamilyProperties(nint device, uint* count, QueueFamilyProperties* properties);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void vkDestroyDevice(nint device, void* allocator);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void vkDestroyInstance(nint instance, void* allocator);

    [StructLayout(LayoutKind.Sequential)]
    internal struct ApplicationInfo
    {
        public uint Type;
        public void* Next;
        public byte* ApplicationName;
        public uint ApplicationVersion;
        public byte* EngineName;
        public uint EngineVersion, ApiVersion;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct InstanceCreateInfo
    {
        public uint Type;
        public void* Next;
        public uint Flags;
        public ApplicationInfo* ApplicationInfo;
        public uint LayerCount;
        public byte** LayerNames;
        public uint ExtensionCount;
        public byte** ExtensionNames;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct QueueFamilyProperties
    {
        public uint Flags, QueueCount, TimestampValidBits;
        public uint TransferWidth, TransferHeight, TransferDepth;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DeviceQueueCreateInfo
    {
        public uint Type;
        public void* Next;
        public uint Flags, FamilyIndex, QueueCount;
        public float* Priorities;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DeviceCreateInfo
    {
        public uint Type;
        public void* Next;
        public uint Flags, QueueCreateInfoCount;
        public DeviceQueueCreateInfo* QueueCreateInfos;
        public uint LayerCount;
        public byte** LayerNames;
        public uint ExtensionCount;
        public byte** ExtensionNames;
        public void* Features;
    }
}
