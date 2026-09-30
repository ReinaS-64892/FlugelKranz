using System.Numerics;

namespace FlugelKranz.Core;

internal static class RotationMath
{
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
