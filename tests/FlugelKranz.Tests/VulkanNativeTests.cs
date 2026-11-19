using System.Runtime.InteropServices;
using FlugelKranz.OpenXR;
using Xunit;

namespace FlugelKranz.Tests;

public class VulkanNativeTests
{
    // Sizes and pointer offsets checked against Linux vulkan_core.h with a C
    // sizeof/offsetof probe. These ABI structs must preserve native alignment.
    [Theory]
    [InlineData(typeof(VulkanNative.ApplicationInfo), 48)]
    [InlineData(typeof(VulkanNative.InstanceCreateInfo), 64)]
    [InlineData(typeof(VulkanNative.QueueFamilyProperties), 24)]
    [InlineData(typeof(VulkanNative.DeviceQueueCreateInfo), 40)]
    [InlineData(typeof(VulkanNative.DeviceCreateInfo), 72)]
    public void GraphicsBindingStructuresMatchThe64BitVulkanAbi(Type type, int size)
    {
        Assert.Equal(8, IntPtr.Size);
        Assert.Equal(size, Marshal.SizeOf(type));
    }

    [Fact]
    public void CreationInfoPointersHaveNativeAlignment()
    {
        Assert.Equal(24, Marshal.OffsetOf<VulkanNative.InstanceCreateInfo>("ApplicationInfo").ToInt32());
        Assert.Equal(32, Marshal.OffsetOf<VulkanNative.DeviceQueueCreateInfo>("Priorities").ToInt32());
        Assert.Equal(64, Marshal.OffsetOf<VulkanNative.DeviceCreateInfo>("Features").ToInt32());
    }
}
