using FlugelKranz.Core;
using static FlugelKranz.OpenVR.OpenVrSession;

namespace FlugelKranz.OpenVR;

internal interface ISteamVrSpaceApi
{
    RigidPose Read(bool seated);
    void Write(bool seated, RigidPose rawToUniverse);
}

/// <summary>Experimental SteamVR chaperone path access, isolated from flight policy.</summary>
internal sealed unsafe class SteamVrSpaceApi : ISteamVrSpaceApi
{
    // Runtime-internal container/path contract, not a portable OpenVR guarantee.
    private const ulong ChaperoneRoot = 0x0000000600000003;
    private const uint MatrixTag = 20;
    private readonly nint paths;
    private readonly ulong standing, seated;

    public SteamVrSpaceApi(OpenVrSession session)
    {
        paths = session.Paths;
        standing = Resolve("/chaperone/standing/transform\0"u8);
        seated = Resolve("/chaperone/seated/transform\0"u8);
        var reported = ((delegate* unmanaged[Stdcall]<VrMatrix>)Slot(session.System,
            OpenVrSlots.System.GetRawZeroPoseToStandingAbsoluteTrackingPose))().ToPose();
        if (!Read(false).NearlyEquals(reported))
            throw new NotSupportedException("SteamVR の Chaperone パスが実際の Standing 変換と一致しません。");
    }

    private ulong Resolve(ReadOnlySpan<byte> path)
    {
        ulong handle = 0;
        fixed (byte* p = path)
            Check(((delegate* unmanaged[Stdcall]<ulong*, byte*, int>)Slot(paths,
                OpenVrSlots.Paths.StringToHandle))(&handle, p), "空間パスの解決");
        if (handle == 0) throw new NotSupportedException("SteamVR の空間パスが無効です。");
        return handle;
    }

    public RigidPose Read(bool isSeated)
    {
        VrMatrix matrix = default;
        var read = new VrPathRead { Path = isSeated ? seated : standing,
            Buffer = (nint)(&matrix), BufferSize = (uint)sizeof(VrMatrix) };
        Check(((delegate* unmanaged[Stdcall]<ulong, VrPathRead*, uint, int>)Slot(paths,
            OpenVrSlots.Paths.ReadPathBatch))(ChaperoneRoot, &read, 1), "空間変換の取得");
        Check(read.Error, "空間変換の取得");
        if (read.Tag != MatrixTag || read.RequiredSize != sizeof(VrMatrix))
            throw new NotSupportedException("SteamVR の空間変換の型・サイズが対応 ABI と一致しません。");
        return matrix.ToPose();
    }

    public void Write(bool isSeated, RigidPose pose)
    {
        var matrix = VrMatrix.FromPose(pose);
        var write = new VrPathWrite { Path = isSeated ? seated : standing, Buffer = (nint)(&matrix),
            BufferSize = (uint)sizeof(VrMatrix), Tag = MatrixTag };
        Check(((delegate* unmanaged[Stdcall]<ulong, VrPathWrite*, uint, int>)Slot(paths,
            OpenVrSlots.Paths.WritePathBatch))(ChaperoneRoot, &write, 1), "空間変換の適用");
        Check(write.Error, "空間変換の適用");
    }
}

/// <summary>Applies one root-space flight transform to standing and seated origins.</summary>
internal sealed class SteamVrSpaceConnection
{
    private readonly ISteamVrSpaceApi api;
    private readonly RigidPose[] original, applied;
    private readonly RigidPose?[] attempted = new RigidPose?[2];
    private ReferenceSpaceTransform space;
    public RigidPose CurrentOffset => space.Offset;
    public RigidPose ReferenceSpaceOffset => space.ReferenceOffset;

    public SteamVrSpaceConnection(ISteamVrSpaceApi api)
    {
        this.api = api;
        original = [api.Read(false), api.Read(true)];
        applied = original.ToArray();
        space = new(original[0].Inverse(), RigidPose.Identity);
    }

    public void VerifyUnchanged()
    {
        for (int i = 0; i < 2; i++)
            if (!api.Read(i == 1).NearlyEquals(applied[i]))
                throw new InvalidOperationException("SteamVR の基準空間が外部から変更されました。再度オンにして接続し直してください。");
    }

    public InputFrame ToPhysical(InputFrame standingFrame) => space.ToPhysical(standingFrame);

    public void Apply(RigidPose offset)
    {
        if (!offset.IsValid) throw new ArgumentException("空間変換が不正です。", nameof(offset));
        VerifyUnchanged();
        if (offset == space.Offset) return;
        // Each original raw-to-universe T becomes T * D. Thus application poses
        // are T * D * physicalPose. No Euler decomposition or chaperone preview.
        for (int i = 0; i < 2; i++)
        {
            var next = original[i] * offset;
            attempted[i] = next; // Retain ownership even if a native call fails after writing.
            api.Write(i == 1, next);
            if (!api.Read(i == 1).NearlyEquals(next))
                throw new InvalidOperationException("SteamVR が要求した空間変換を保持していません。");
            applied[i] = next;
            attempted[i] = null;
        }
        space = space with { Offset = offset };
    }

    public void Restore()
    {
        // Restore each origin independently, including a partially successful Apply.
        // Never overwrite another tool's changed transform during cleanup.
        List<Exception> errors = [];
        for (int i = 0; i < 2; i++)
        {
            try
            {
                var actual = api.Read(i == 1);
                if (actual.NearlyEquals(original[i])) continue;
                if (!actual.NearlyEquals(applied[i]) &&
                    !(attempted[i] is { } candidate && actual.NearlyEquals(candidate)))
                    throw new InvalidOperationException("外部から変更された SteamVR の基準空間は復元しません。");
                api.Write(i == 1, original[i]);
                if (!api.Read(i == 1).NearlyEquals(original[i]))
                    throw new InvalidOperationException("SteamVR の基準空間を復元できませんでした。");
                applied[i] = original[i];
                attempted[i] = null;
            }
            catch (Exception e) { errors.Add(e); }
        }
        if (errors.Count > 0) throw new AggregateException("SteamVR 空間の復元に失敗しました。", errors);
        space = space with { Offset = RigidPose.Identity };
    }
}
