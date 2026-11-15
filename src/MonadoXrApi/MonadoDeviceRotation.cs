using System.Runtime.InteropServices;

namespace MonadoXrApi;

/// <summary>Private WiVRn extension ABI: device index and root-axis orientation correction.</summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct MonadoDeviceRotation(uint DeviceIndex, float X, float Y, float Z, float W);
