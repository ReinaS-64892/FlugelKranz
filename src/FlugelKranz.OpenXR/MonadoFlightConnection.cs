using System.Numerics;
using FlugelKranz.Core;
using MonadoXrApi;

namespace FlugelKranz.OpenXR;

/// <summary>FlugelKranz-specific coordinate handling on top of the libmonado wrapper.</summary>
internal sealed class MonadoFlightConnection : IDisposable
{
    private readonly MonadoRoot root;
    private FlightSpaceTransform space;
    private Dictionary<string, uint> deviceIndices = new(StringComparer.Ordinal);
    private MonadoDeviceRotation[] appliedRotations = [];
    private int trackerRevision = -1;

    public bool TrackerRotationOffsetsSupported => root.SupportsDeviceRotationOffsets;

    public RigidPose OriginalOffset => RigidPose.Identity;
    public RigidPose CurrentOffset => space.Offset;
    public RigidPose CurrentReferenceSpaceOffset => space.StageOffset;

    public MonadoFlightConnection(string libraryPath)
    {
        root = new(libraryPath);
        try
        {
            space = new(ToRigidPose(root.GetReferenceSpaceOffset(MonadoReferenceSpaceType.Stage)), RigidPose.Identity);
        }
        catch { root.Dispose(); throw; }
    }

    public void VerifyClient(string applicationName)
    {
        if (!root.GetClientNames().Contains(applicationName, StringComparer.Ordinal))
            throw new InvalidOperationException("OpenXR と libmonado が同じサービスに接続していません。XR_RUNTIME_JSON と IPC 接続先を確認してください。");
    }

    public void VerifyUnchanged()
    {
        if (!ToRigidPose(root.GetReferenceSpaceOffset(MonadoReferenceSpaceType.Stage)).NearlyEquals(space.StageOffset))
            throw new InvalidOperationException("基準空間が変更されたため停止しました。再度オンにして接続し直してください。");
    }

    public InputFrame ToPhysical(InputFrame stageFrame) => space.ToPhysical(stageFrame);

    public void RefreshTrackers(int revision)
    {
        if (!root.SupportsDeviceRotationOffsets || revision == trackerRevision) return;
        deviceIndices = root.RefreshDeviceList().Where(d => d.Serial.Length > 0)
            .GroupBy(d => d.Serial, StringComparer.Ordinal).Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single().Index, StringComparer.Ordinal);
        trackerRevision = revision;
    }

    public void Apply(RigidPose offset) => Apply(offset, []);

    public void Apply(RigidPose offset, IReadOnlyList<TrackerRotationOffset> rotations)
    {
        if (!offset.IsValid) throw new ArgumentException("空間変換が不正です。", nameof(offset));
        var next = space with { Offset = offset };
        if (root.SupportsDeviceRotationOffsets)
        {
            var updates = rotations.Where(r => deviceIndices.ContainsKey(r.Serial)).Select(r =>
                new MonadoDeviceRotation(deviceIndices[r.Serial], r.Orientation.X, r.Orientation.Y, r.Orientation.Z, r.Orientation.W)).ToArray();
            if (offset == space.Offset && updates.SequenceEqual(appliedRotations)) return;
            root.ApplyStageAndDeviceRotations(ToMonadoPose(next.StageOffset), updates);
            appliedRotations = updates;
        }
        else if (offset != space.Offset)
            root.SetReferenceSpaceOffset(MonadoReferenceSpaceType.Stage, ToMonadoPose(next.StageOffset));
        space = next;
    }

    public void Restore()
    {
        VerifyUnchanged();
        if (space.Offset != RigidPose.Identity || appliedRotations.Length > 0)
            Apply(RigidPose.Identity);
    }

    private static RigidPose ToRigidPose(MonadoPose pose)
    {
        var result = new RigidPose(new Quaternion(pose.X, pose.Y, pose.Z, pose.W), new Vector3(pose.PositionX, pose.PositionY, pose.PositionZ));
        return result.IsValid ? result : throw new InvalidOperationException("ランタイムが不正な位置・姿勢を返しました。");
    }

    private static MonadoPose ToMonadoPose(RigidPose pose) => new(pose.Orientation.X, pose.Orientation.Y, pose.Orientation.Z,
        pose.Orientation.W, pose.Position.X, pose.Position.Y, pose.Position.Z);

    public void Dispose() => root.Dispose();
}
