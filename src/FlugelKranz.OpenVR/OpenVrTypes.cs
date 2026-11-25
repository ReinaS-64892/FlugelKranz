using System.Numerics;
using System.Runtime.InteropServices;
using FlugelKranz.Core;

namespace FlugelKranz.OpenVR;

// Minimal ABI subset verified against Valve's OpenVR SDK (see OpenVrSlots.g.cs).
// OpenVR bool is one byte. All supported targets are 64-bit; function tables
// use stdcall on Windows and the platform C ABI on Linux (identical here).
[StructLayout(LayoutKind.Sequential)]
internal struct VrMatrix
{
    public float M00, M01, M02, X, M10, M11, M12, Y, M20, M21, M22, Z;

    public readonly RigidPose ToPose()
    {
        // OpenVR uses column vectors; System.Numerics matrices use row vectors.
        var matrix = new Matrix4x4(M00, M10, M20, 0, M01, M11, M21, 0, M02, M12, M22, 0, 0, 0, 0, 1);
        var pose = new RigidPose(Quaternion.CreateFromRotationMatrix(matrix), new(X, Y, Z));
        var x = new Vector3(M00, M10, M20);
        var y = new Vector3(M01, M11, M21);
        var z = new Vector3(M02, M12, M22);
        if (!pose.IsValid || MathF.Abs(x.LengthSquared() - 1) > 0.001f ||
            MathF.Abs(y.LengthSquared() - 1) > 0.001f || MathF.Abs(z.LengthSquared() - 1) > 0.001f ||
            MathF.Abs(Vector3.Dot(x, y)) > 0.001f || Vector3.Distance(Vector3.Cross(x, y), z) > 0.001f)
            throw new InvalidOperationException("SteamVR が不正な剛体変換を返しました。");
        return pose with { Orientation = Quaternion.Normalize(pose.Orientation) };
    }

    public static VrMatrix FromPose(RigidPose pose)
    {
        if (!pose.IsValid) throw new ArgumentException("空間変換が不正です。", nameof(pose));
        var m = Matrix4x4.CreateFromQuaternion(pose.Orientation);
        return new() { M00 = m.M11, M01 = m.M21, M02 = m.M31, X = pose.Position.X,
            M10 = m.M12, M11 = m.M22, M12 = m.M32, Y = pose.Position.Y,
            M20 = m.M13, M21 = m.M23, M22 = m.M33, Z = pose.Position.Z };
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct VrTrackedPose
{
    public VrMatrix Transform;
    public Vector3 Velocity, AngularVelocity;
    public int TrackingResult;
    public byte Valid, Connected;
    public readonly bool IsTracked => Valid != 0 && Connected != 0 && TrackingResult == 200;
    public readonly RigidPose Pose => IsTracked ? Transform.ToPose() : RigidPose.Identity;
}

[StructLayout(LayoutKind.Sequential)]
internal struct VrDigital
{
    public byte Active;
    public ulong Origin;
    public byte State, Changed;
    public float UpdateTime;
    public readonly bool Pressed => Active != 0 && State != 0;
}

[StructLayout(LayoutKind.Sequential)]
internal struct VrAnalog
{
    public byte Active;
    public ulong Origin;
    public float X, Y, Z, DeltaX, DeltaY, DeltaZ, UpdateTime;
}

[StructLayout(LayoutKind.Sequential)]
internal struct VrActionSet
{
    public ulong Handle, RestrictedDevice, Secondary;
    public uint Padding;
    public int Priority;
}

[StructLayout(LayoutKind.Sequential)]
internal struct VrPathRead
{
    public ulong Path;
    public nint Buffer;
    public uint BufferSize, Tag, RequiredSize;
    public int Error;
    public nint PathString;
}

[StructLayout(LayoutKind.Sequential)]
internal struct VrPathWrite
{
    public ulong Path;
    public int WriteType, SetError;
    public nint Buffer;
    public uint BufferSize, Tag;
    public int Error;
    public nint PathString;
    public byte PostEvents, ValueChanged;
}

[StructLayout(LayoutKind.Sequential)]
internal struct VrPoseAction
{
    public byte Active;
    public ulong Origin;
    public VrTrackedPose Pose;
}
