using Evergine.Bindings.OpenXR;
using NativeLibrary = System.Runtime.InteropServices.NativeLibrary;
using static Evergine.Bindings.OpenXR.OpenXRNative;
using static FlugelKranz.OpenXR.OpenXrInput;
using static FlugelKranz.OpenXR.VulkanNative;

namespace FlugelKranz.OpenXR;

/// <summary>Owns the minimal Vulkan device required by a non-headless overlay.</summary>
internal sealed unsafe class OpenXrVulkanGraphics : IDisposable
{
    private nint vulkanLibrary, vulkanInstance, device;
    public XrGraphicsBindingVulkanKHR Binding { get; private set; }

    public OpenXrVulkanGraphics(XrInstance instance, XrSystemId system)
    {
        try
        {
            var requirements = new XrGraphicsRequirementsVulkanKHR
            {
                type = XrStructureType.XR_TYPE_GRAPHICS_REQUIREMENTS_VULKAN_KHR
            };
            var getRequirements = (delegate* unmanaged[Cdecl]<XrInstance, XrSystemId, XrGraphicsRequirementsVulkanKHR*, XrResult>)
                GetFunction(instance, "xrGetVulkanGraphicsRequirements2KHR");
            Check(getRequirements(instance, system, &requirements), "Vulkan 要件の取得");
            // XrVersion uses 16/16/32 bits; Vulkan's version uses 3/7/10/12.
            ulong version = Math.Max(requirements.minApiVersionSupported, 1UL << 48);
            uint apiVersion = checked((uint)(((version >> 48) << 22)
                | (((version >> 32) & 0xffff) << 12) | (version & 0xffffffff)));
            var application = new ApplicationInfo { Type = 0, ApiVersion = apiVersion };
            var createInstance = new InstanceCreateInfo { Type = 1, ApplicationInfo = &application };
            vulkanLibrary = NativeLibrary.Load("libvulkan.so.1");
            nint getProcAddr = NativeLibrary.GetExport(vulkanLibrary, "vkGetInstanceProcAddr");
            var xrInstanceInfo = new XrVulkanInstanceCreateInfoKHR
            {
                type = XrStructureType.XR_TYPE_VULKAN_INSTANCE_CREATE_INFO_KHR,
                systemId = system,
                pfnGetInstanceProcAddr = getProcAddr,
                vulkanCreateInfo = (nint)(&createInstance)
            };
            nint createdInstance = 0;
            int result = 0;
            Check(xrCreateVulkanInstanceKHR(instance, &xrInstanceInfo, (nint)(&createdInstance), (nint)(&result)), "Vulkan インスタンスの作成");
            CheckVulkan(result, "Vulkan インスタンスの作成");
            vulkanInstance = createdInstance;

            var deviceInfo = new XrVulkanGraphicsDeviceGetInfoKHR
            {
                type = XrStructureType.XR_TYPE_VULKAN_GRAPHICS_DEVICE_GET_INFO_KHR,
                systemId = system,
                vulkanInstance = vulkanInstance
            };
            nint physicalDevice = 0;
            Check(xrGetVulkanGraphicsDevice2KHR(instance, &deviceInfo, (nint)(&physicalDevice)), "OpenXR の Vulkan GPU の取得");
            uint count = 0;
            vkGetPhysicalDeviceQueueFamilyProperties(physicalDevice, &count, null);
            var queues = new QueueFamilyProperties[count];
            fixed (QueueFamilyProperties* properties = queues)
                vkGetPhysicalDeviceQueueFamilyProperties(physicalDevice, &count, properties);
            int family = Array.FindIndex(queues, queue => queue.QueueCount > 0 && (queue.Flags & 1) != 0);
            if (family < 0) throw new NotSupportedException("OpenXR GPU に Vulkan graphics queue がありません。");
            float priority = 1;
            var queueInfo = new DeviceQueueCreateInfo
            {
                Type = 2, FamilyIndex = (uint)family, QueueCount = 1, Priorities = &priority
            };
            var createDevice = new DeviceCreateInfo { Type = 3, QueueCreateInfoCount = 1, QueueCreateInfos = &queueInfo };
            var xrDeviceInfo = new XrVulkanDeviceCreateInfoKHR
            {
                type = XrStructureType.XR_TYPE_VULKAN_DEVICE_CREATE_INFO_KHR,
                systemId = system,
                pfnGetInstanceProcAddr = getProcAddr,
                vulkanPhysicalDevice = physicalDevice,
                vulkanCreateInfo = (nint)(&createDevice)
            };
            nint createdDevice = 0;
            Check(xrCreateVulkanDeviceKHR(instance, &xrDeviceInfo, (nint)(&createdDevice), (nint)(&result)), "Vulkan デバイスの作成");
            CheckVulkan(result, "Vulkan デバイスの作成");
            device = createdDevice;
            Binding = new()
            {
                type = XrStructureType.XR_TYPE_GRAPHICS_BINDING_VULKAN_KHR,
                instance = vulkanInstance, physicalDevice = physicalDevice, device = device,
                queueFamilyIndex = (uint)family, queueIndex = 0
            };
        }
        catch { Dispose(); throw; }
    }

    internal static nint GetFunction(XrInstance instance, string name)
    {
        nint function = 0;
        fixed (byte* utf8 = System.Text.Encoding.UTF8.GetBytes(name + '\0'))
            Check(xrGetInstanceProcAddr(instance, utf8, (nint)(&function)), $"{name} の取得");
        if (function == 0) throw new NotSupportedException($"{name} が利用できません。");
        return function;
    }

    private static void CheckVulkan(int result, string operation)
    {
        if (result != 0) throw new InvalidOperationException($"{operation}に失敗しました（Vulkan: {result}）。");
    }

    public void Dispose()
    {
        // There are no application GPU submissions to drain. OpenXR session
        // destruction must precede this graphics device's destruction.
        if (device != 0) { vkDestroyDevice(device, null); device = 0; }
        if (vulkanInstance != 0) { vkDestroyInstance(vulkanInstance, null); vulkanInstance = 0; }
        if (vulkanLibrary != 0) { NativeLibrary.Free(vulkanLibrary); vulkanLibrary = 0; }
    }
}
