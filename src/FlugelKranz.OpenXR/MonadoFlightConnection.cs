using System.Numerics;
using FlugelKranz.Core;
using MonadoXrApi;

namespace FlugelKranz.OpenXR;

public readonly record struct TrackingOriginOffset(uint Index, RigidPose Original, RigidPose Current);

/// <summary>FlugelKranz-specific coordinate handling on top of the libmonado wrapper.</summary>
internal sealed class MonadoFlightConnection : IDisposable
{
    private readonly MonadoRoot root;
    private readonly Dictionary<uint, Origin> origins = [];
    private uint headOrigin, leftOrigin, rightOrigin;

    private sealed class Origin(RigidPose offset)
    {
        public RigidPose Original { get; } = offset;
        public RigidPose Current { get; set; } = offset;
    }

    public RigidPose OriginalOffset => origins[headOrigin].Original;
    public RigidPose CurrentOffset => origins[headOrigin].Current;
    public RigidPose StageToRoot { get; }
    public IReadOnlyList<TrackingOriginOffset> TrackingOrigins => origins
        .OrderBy(pair => pair.Key)
        .Select(pair => new TrackingOriginOffset(pair.Key, pair.Value.Original, pair.Value.Current))
        .ToArray();

    public MonadoFlightConnection(string libraryPath)
    {
        root = new(libraryPath);
        try
        {
            foreach (var name in new[] { "head", "left", "right" })
            {
                var device = root.GetDeviceFromRole(name);
                if (device < 0) throw new InvalidOperationException($"{name} デバイスが接続されていません。");
                var trackingOrigin = root.GetTrackingOriginIndex(device);
                if (!origins.ContainsKey(trackingOrigin))
                    origins.Add(trackingOrigin, new Origin(ToRigidPose(root.GetTrackingOriginOffset(trackingOrigin))));
                switch (name)
                {
                    case "head": headOrigin = trackingOrigin; break;
                    case "left": leftOrigin = trackingOrigin; break;
                    case "right": rightOrigin = trackingOrigin; break;
                }
            }
            // STAGE must remain fixed so it can remove the offset written by this application.
            StageToRoot = ToRigidPose(root.GetReferenceSpaceOffset(MonadoReferenceSpaceType.Stage));
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
        foreach (var (origin, state) in origins)
            if (!ReadOrigin(origin).NearlyEquals(state.Current))
                throw new InvalidOperationException("他のツールがトラッキング原点オフセットを変更したため停止しました。再度オンにして接続し直してください。");
        if (!ToRigidPose(root.GetReferenceSpaceOffset(MonadoReferenceSpaceType.Stage)).NearlyEquals(StageToRoot))
            throw new InvalidOperationException("基準空間が変更されたため停止しました。再度オンにして接続し直してください。");
    }

    public InputFrame ToPhysical(InputFrame stageFrame) => new(ToPhysical(stageFrame.Head, headOrigin), stageFrame.HeadTracked,
        stageFrame.Left with { Pose = ToPhysical(stageFrame.Left.Pose, leftOrigin) },
        stageFrame.Right with { Pose = ToPhysical(stageFrame.Right.Pose, rightOrigin) });

    public void Apply(RigidPose offset)
    {
        if (!offset.IsValid) throw new ArgumentException("空間変換が不正です。", nameof(offset));
        var delta = offset * OriginalOffset.Inverse();
        var targets = origins.ToDictionary(pair => pair.Key, pair => delta * pair.Value.Original);
        var applied = new List<uint>();
        try
        {
            foreach (var (origin, target) in targets)
            {
                root.SetTrackingOriginOffset(origin, ToMonadoPose(target));
                applied.Add(origin);
            }
        }
        catch
        {
            foreach (var origin in applied) root.SetTrackingOriginOffset(origin, ToMonadoPose(origins[origin].Current));
            throw;
        }
        foreach (var (origin, target) in targets) origins[origin].Current = target;
    }

    public void Restore()
    {
        foreach (var (origin, state) in origins)
            if (!ReadOrigin(origin).NearlyEquals(state.Current))
                throw new InvalidOperationException("外部で変更されたトラッキング原点オフセットは復元しませんでした。");
        if (!CurrentOffset.NearlyEquals(OriginalOffset)) Apply(OriginalOffset);
    }

    private RigidPose ToPhysical(RigidPose stagePose, uint origin)
    {
        var state = origins[origin];
        var originToHead = OriginalOffset.Inverse() * state.Original;
        var stageToOrigin = state.Current.Inverse() * StageToRoot;
        return originToHead * stageToOrigin * stagePose;
    }

    private RigidPose ReadOrigin(uint origin) => ToRigidPose(root.GetTrackingOriginOffset(origin));

    private static RigidPose ToRigidPose(MonadoPose pose)
    {
        var result = new RigidPose(new Quaternion(pose.X, pose.Y, pose.Z, pose.W), new Vector3(pose.PositionX, pose.PositionY, pose.PositionZ));
        return result.IsValid ? result : throw new InvalidOperationException("ランタイムが不正な位置・姿勢を返しました。");
    }

    private static MonadoPose ToMonadoPose(RigidPose pose) => new(pose.Orientation.X, pose.Orientation.Y, pose.Orientation.Z,
        pose.Orientation.W, pose.Position.X, pose.Position.Y, pose.Position.Z);

    public void Dispose() => root.Dispose();
}
