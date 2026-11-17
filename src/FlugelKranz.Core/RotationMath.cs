using System.Numerics;

namespace FlugelKranz.Core;

internal static class RotationMath
{
    // Inputs are unit directions. At an exact half turn the rotation plane is
    // ambiguous; choose a stable perpendicular axis instead of normalizing zero.
    public static Quaternion RotationBetweenDirections(Vector3 from, Vector3 to)
    {
        Vector3 cross = Vector3.Cross(from, to);
        float dot = Math.Clamp(Vector3.Dot(from, to), -1, 1);
        if (cross.LengthSquared() > 0.0000000001f)
            return Quaternion.CreateFromAxisAngle(Vector3.Normalize(cross), MathF.Atan2(cross.Length(), dot));
        if (dot >= 0)
            return Quaternion.Identity;

        Vector3 perpendicular = Vector3.Cross(from, MathF.Abs(from.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY);
        return Quaternion.CreateFromAxisAngle(Vector3.Normalize(perpendicular), MathF.PI);
    }

    public static Quaternion IntegrateRotation(Quaternion rotation, Vector3 velocity, float dt)
    {
        float speed = velocity.Length();
        if (speed <= 0 || dt <= 0)
            return rotation;

        return Quaternion.Normalize(Quaternion.CreateFromAxisAngle(velocity / speed, speed * dt) * rotation);
    }

    public static Vector3 RotationVelocity(Quaternion from, Quaternion to, float dt)
    {
        var delta = Quaternion.Normalize(to * Quaternion.Conjugate(from));
        if (delta.W < 0)
            delta = -delta;

        float angle = 2 * MathF.Acos(Math.Clamp(delta.W, -1, 1));
        float sine = MathF.Sqrt(MathF.Max(0, 1 - delta.W * delta.W));
        return sine < 0.00001f
            ? Vector3.Zero
            : new Vector3(delta.X, delta.Y, delta.Z) / sine * (angle / dt);
    }

    public static Quaternion TwistAroundAxis(Quaternion rotation, Vector3 axis)
    {
        axis = axis.LengthSquared() <= 0.0000000001f ? Vector3.Zero : Vector3.Normalize(axis);
        if (axis.LengthSquared() <= 0)
            return Quaternion.Identity;

        var imaginary = new Vector3(rotation.X, rotation.Y, rotation.Z);
        var projected = axis * Vector3.Dot(imaginary, axis);
        var twist = new Quaternion(projected, rotation.W);
        return twist.LengthSquared() <= 0.0000000001f
            ? Quaternion.Identity
            : Quaternion.Normalize(twist);
    }

}
