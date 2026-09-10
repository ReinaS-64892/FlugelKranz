using System.Runtime.InteropServices;

namespace MonadoXrApi;

/// <summary>A libmonado ABI pose: quaternion followed by position, in metres.</summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct MonadoPose(float X, float Y, float Z, float W, float PositionX, float PositionY, float PositionZ)
{
    public static MonadoPose Identity => new(0, 0, 0, 1, 0, 0, 0);
}
