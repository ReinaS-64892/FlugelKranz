using System.Numerics;

namespace FlugelKranz.Core;

/// <summary>Blends floor-level tracker positions using physical height and translation speed.</summary>
internal sealed class ToeTurnOrigin
{
    private Dictionary<string, (Vector3 Position, float Speed)> previousPositions = [];
    internal readonly record struct Support(string Serial, Vector3 Position, float Height, float Speed, float Score);
    public IReadOnlyList<Support> Supports { get; private set; } = [];

    public void Clear()
    {
        previousPositions.Clear();
        Supports = [];
    }

    public Vector3? Update(InputFrame frame, float elapsedSeconds, InfiniteWalkingSettings settings)
    {
        if (!settings.ToeTurnOriginEnabled || !frame.FloorPose.IsValid)
        {
            Clear();
            return null;
        }

        RigidPose rootToFloor = frame.FloorPose.Inverse();
        var positions = new Dictionary<string, (Vector3 Position, float Speed)>();
        var candidates = new List<Support>();
        Supports = [];
        foreach (var tracker in frame.Trackers ?? [])
        {
            if (!tracker.IsUsable || string.IsNullOrEmpty(tracker.Serial) ||
                !positions.TryAdd(tracker.Serial, (tracker.Position, 0)))
                continue;
            float speed = 0;
            if (elapsedSeconds > 0 && previousPositions.TryGetValue(tracker.Serial, out var previous))
            {
                float measuredSpeed = Vector3.Distance(previous.Position, tracker.Position) / elapsedSeconds;
                speed = float.Lerp(previous.Speed, measuredSpeed, 1 - MathF.Exp(-elapsedSeconds / 0.1f));
            }
            positions[tracker.Serial] = (tracker.Position, speed);
            float height = rootToFloor.Transform(tracker.Position).Y;
            if (height < 0 || height > settings.GroundRangeMetres)
                continue;
            // Compare height with the distance travelled in 100 ms. Without history, prefer the lower toe.
            candidates.Add(new(tracker.Serial, tracker.Position, height, speed, height + speed * 0.1f));
        }
        previousPositions = positions;
        if (candidates.Count == 0)
            return null;

        // Equal scores give the midpoint. A 5 cm score difference changes weight by e.
        // Subtract the minimum to avoid underflow when all candidates are moving quickly.
        float minimumScore = candidates.Min(candidate => candidate.Score);
        // Include both equally planted feet; ignore candidates below half the strongest weight.
        Supports = candidates.Where(candidate => candidate.Score - minimumScore <= 0.05f * MathF.Log(2)).ToArray();
        Vector3 weightedPosition = Vector3.Zero;
        float totalWeight = 0;
        foreach (var candidate in candidates)
        {
            float weight = MathF.Exp(-(candidate.Score - minimumScore) / 0.05f);
            weightedPosition += candidate.Position * weight;
            totalWeight += weight;
        }
        return weightedPosition / totalWeight;
    }
}
