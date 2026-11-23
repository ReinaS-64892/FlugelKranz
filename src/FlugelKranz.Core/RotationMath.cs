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
        if (dt <= 0) return Vector3.Zero;
        var delta = Quaternion.Normalize(to * Quaternion.Conjugate(from));
        if (delta.W < 0)
            delta = -delta;

        // At high sampling rates W can round to 1 while the imaginary part still
        // contains a real rotation. atan2 retains those small-angle velocities.
        var imaginary = new Vector3(delta.X, delta.Y, delta.Z);
        float sine = imaginary.Length();
        float angle = 2 * MathF.Atan2(sine, delta.W);
        return sine <= 0
            ? Vector3.Zero
            : imaginary / sine * (angle / dt);
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
