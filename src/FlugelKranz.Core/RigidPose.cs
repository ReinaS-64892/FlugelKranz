using System.Numerics;

namespace FlugelKranz.Core;

/// <summary>A right-handed rigid transform, in metres. A * B applies B first.</summary>
public readonly record struct RigidPose(Quaternion Orientation, Vector3 Position)
{
    public static RigidPose Identity => new(Quaternion.Identity, Vector3.Zero);
    public bool IsValid => float.IsFinite(Position.X) && float.IsFinite(Position.Y)
        && float.IsFinite(Position.Z) && float.IsFinite(Orientation.LengthSquared())
        && MathF.Abs(Orientation.LengthSquared() - 1) < 0.01f;

    public Vector3 Transform(Vector3 point) => Vector3.Transform(point, Orientation) + Position;
    public RigidPose Inverse()
    {
        var inverse = Quaternion.Conjugate(Orientation);
        return new(inverse, Vector3.Transform(-Position, inverse));
    }

    public static RigidPose operator *(RigidPose a, RigidPose b) =>
        new(Quaternion.Normalize(a.Orientation * b.Orientation), a.Transform(b.Position));

    public bool NearlyEquals(RigidPose other, float tolerance = 0.0001f) =>
        Vector3.Distance(Position, other.Position) < tolerance
        && 1 - MathF.Abs(Quaternion.Dot(Orientation, other.Orientation)) < tolerance;
}
