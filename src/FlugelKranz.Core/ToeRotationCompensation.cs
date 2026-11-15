using System.Numerics;

namespace FlugelKranz.Core;

public readonly record struct TrackerRotationOffset(string Serial, Quaternion Orientation);

/// <summary>Accumulates only applied walking yaw; never consumes the physical foot orientation.</summary>
internal sealed class ToeRotationCompensation
{
    private const float ContactSpeedMetresPerSecond = 0.1f;
    private const float ReleaseSpeedMetresPerSecond = 0.2f;
    private const float ReleaseDistanceMetres = 0.05f;
    private const float ReleaseLiftMetres = 0.03f;
    private const float LandingHeightToleranceMetres = 0.015f;

    private sealed class Contact(Vector3 position, float height)
    {
        public Vector3 Position { get; } = position;
        public float Height { get; } = height;
        public Quaternion Rotation = Quaternion.Identity;
        public Quaternion ReleaseStart;
        public float ReleaseElapsed;
        public bool Releasing;
    }

    private readonly Dictionary<string, Contact> contacts = new(StringComparer.Ordinal);
    public IReadOnlyList<TrackerRotationOffset> Offsets { get; private set; } = [];

    public void Clear()
    {
        contacts.Clear();
        Offsets = [];
    }

    public void Update(InputFrame frame, IReadOnlyList<ToeTurnOrigin.Support> supports,
        Quaternion before, Quaternion after, float dt, InfiniteWalkingSettings settings)
    {
        if (!frame.HeadTracked || !settings.ToeTurnOriginEnabled || !settings.ToeRotationCompensationEnabled)
        {
            Clear();
            return;
        }

        var tracked = (frame.Trackers ?? []).Where(t => t.IsUsable).Select(t => t.Serial).ToHashSet(StringComparer.Ordinal);
        var planted = supports.ToDictionary(s => s.Serial, StringComparer.Ordinal);
        var correctionStep = Quaternion.Normalize(Quaternion.Conjugate(after) * before);
        foreach (var serial in contacts.Keys.ToArray())
        {
            var contact = contacts[serial];
            if (!tracked.Contains(serial))
            {
                contacts.Remove(serial);
                continue;
            }
            bool stationary = planted.TryGetValue(serial, out var support)
                && support.Speed <= ReleaseSpeedMetresPerSecond
                && Vector3.Distance(contact.Position, support.Position) <= ReleaseDistanceMetres
                && support.Height - contact.Height <= ReleaseLiftMetres;
            if (!stationary && !contact.Releasing)
            {
                contact.Releasing = true;
                contact.ReleaseStart = contact.Rotation;
            }
            if (contact.Releasing)
            {
                contact.ReleaseElapsed += dt;
                float amount = settings.ToeRotationReleaseSeconds <= 0 ? 1
                    : Math.Clamp(contact.ReleaseElapsed / settings.ToeRotationReleaseSeconds, 0, 1);
                // Smoothstep has zero endpoint slope and reaches identity at the configured duration.
                contact.Rotation = amount >= 1 ? Quaternion.Identity
                    : Quaternion.Normalize(Quaternion.Slerp(contact.ReleaseStart, Quaternion.Identity, amount * amount * (3 - 2 * amount)));
                // A foot held above its previous contact must not acquire a new
                // anchor merely because it stops moving inside the broad floor range.
                if (amount >= 1 && planted.TryGetValue(serial, out support)
                    && support.Speed <= ContactSpeedMetresPerSecond
                    && support.Height - contact.Height <= LandingHeightToleranceMetres)
                    contacts[serial] = new(support.Position, support.Height);
            }
            else
                contact.Rotation = Quaternion.Normalize(correctionStep * contact.Rotation);
        }
        foreach (var support in supports)
        {
            // A released/moving foot must settle before a new contact can begin.
            if (support.Speed <= ContactSpeedMetresPerSecond && !contacts.ContainsKey(support.Serial))
                contacts.Add(support.Serial, new(support.Position, support.Height));
        }
        Offsets = contacts.Where(pair => 1 - MathF.Abs(pair.Value.Rotation.W) > 0.00000001f)
            .Select(pair => new TrackerRotationOffset(pair.Key, pair.Value.Rotation)).ToArray();
    }
}
