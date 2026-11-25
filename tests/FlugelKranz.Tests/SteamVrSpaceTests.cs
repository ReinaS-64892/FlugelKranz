using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using FlugelKranz.Core;
using FlugelKranz.OpenVR;
using Xunit;

namespace FlugelKranz.Tests;

public class SteamVrSpaceTests
{
    private static readonly RigidPose Standing = new(Quaternion.CreateFromYawPitchRoll(.4f, -.2f, .3f), new(1, 2, -3));
    private static readonly RigidPose Seated = new(Quaternion.CreateFromYawPitchRoll(-.3f, .1f, -.4f), new(-2, 1, 3));
    private static readonly RigidPose Flight = new(Quaternion.CreateFromYawPitchRoll(1.2f, -.8f, 2.1f), new(100_000, -12, 3));

    [Fact]
    public void ArbitraryFlightIsAppliedOnceToBothOriginsAndRecoversPhysicalInputs()
    {
        var api = new FakeSpaces();
        var connection = new SteamVrSpaceConnection(api);
        var physical = new RigidPose(Quaternion.CreateFromYawPitchRoll(.1f, .2f, .3f), new(.3f, 1.5f, -.2f));
        connection.Apply(Flight);
        AssertPose(Standing * Flight, api.Standing);
        AssertPose(Seated * Flight, api.Seated);
        // Different application origins still agree on the same root-space flight.
        AssertPose(Flight * physical, Standing.Inverse() * (api.Standing * physical));
        AssertPose(Flight * physical, Seated.Inverse() * (api.Seated * physical));
        var report = api.Standing * physical;
        var frame = new InputFrame(report, true, new(report, 1, 0, 0, true), new(report, 0, 1, 0, true))
        { Trackers = [new("toe", report.Position, true)] };
        var recovered = connection.ToPhysical(frame);
        AssertPose(physical, recovered.Head);
        AssertPose(physical, recovered.Left.Pose);
        AssertPose(physical, recovered.Right.Pose);
        AssertPose(Standing.Inverse(), recovered.FloorPose);
        Assert.True(Vector3.Distance(physical.Position, recovered.Trackers![0].Position) < .03f);
        Assert.Equal(1, recovered.Left.Drag);
        Assert.Equal(1, recovered.Right.Turn);
        connection.Restore();
        AssertPose(Standing, api.Standing);
        AssertPose(Seated, api.Seated);
        AssertPose(RigidPose.Identity, connection.CurrentOffset);
    }

    [Fact]
    public void DiagnoseAndUnchangedOffsetDoNotWriteSpace()
    {
        var api = new FakeSpaces();
        var connection = new SteamVrSpaceConnection(api);
        connection.VerifyUnchanged();
        connection.Apply(RigidPose.Identity);
        connection.Restore();
        Assert.Equal(0, api.Writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PartialWriteFailureCanRestoreBothOrigins(bool failAfterWriting)
    {
        var api = new FakeSpaces { FailSeatedOnce = true, FailAfterWriting = failAfterWriting };
        var connection = new SteamVrSpaceConnection(api);
        Assert.Throws<IOException>(() => connection.Apply(Flight));
        connection.Restore();
        AssertPose(Standing, api.Standing);
        AssertPose(Seated, api.Seated);
    }

    [Fact]
    public void ExternalChangesArePreservedAndOtherOwnedOriginIsStillRestored()
    {
        var api = new FakeSpaces();
        var connection = new SteamVrSpaceConnection(api);
        connection.Apply(Flight);
        var external = new RigidPose(Quaternion.Identity, new(8, 9, 10));
        api.Standing = external;
        int writes = api.Writes;
        Assert.Throws<InvalidOperationException>(() => connection.Apply(RigidPose.Identity));
        Assert.Equal(writes, api.Writes);
        Assert.Throws<AggregateException>(connection.Restore);
        AssertPose(external, api.Standing);
        AssertPose(Seated, api.Seated);
    }

    [Fact]
    public void InvalidTransformNeverWritesNativeSpace()
    {
        var api = new FakeSpaces();
        var connection = new SteamVrSpaceConnection(api);
        Assert.Throws<ArgumentException>(() => connection.Apply(new(Quaternion.Identity, new(float.NaN, 0, 0))));
        Assert.Equal(0, api.Writes);
    }

    [Theory]
    [InlineData(.3f, -.9f, 1.2f)]
    [InlineData(0, 1.5707963f, 0)]
    [InlineData(-2.1f, -1.5707963f, 3.1f)]
    public void MatrixInteropPreservesAxisOrderAndTranslation(float yaw, float pitch, float roll)
    {
        var pose = new RigidPose(Quaternion.CreateFromYawPitchRoll(yaw, pitch, roll), new(3, -2, 1));
        var m = VrMatrix.FromPose(pose);
        AssertPose(pose, m.ToPose());
        var p = new Vector3(.2f, 1.5f, -.1f);
        var transformed = new Vector3(m.M00 * p.X + m.M01 * p.Y + m.M02 * p.Z + m.X,
            m.M10 * p.X + m.M11 * p.Y + m.M12 * p.Z + m.Y,
            m.M20 * p.X + m.M21 * p.Y + m.M22 * p.Z + m.Z);
        Assert.True(Vector3.Distance(pose.Transform(p), transformed) < .00001f);
    }

    [Fact]
    public void NativeMatrixRejectsScalingAndReflection()
    {
        var matrix = VrMatrix.FromPose(RigidPose.Identity);
        matrix.M00 = -1;
        Assert.Throws<InvalidOperationException>(() => matrix.ToPose());
        matrix.M00 = 2;
        Assert.Throws<InvalidOperationException>(() => matrix.ToPose());
    }

    [Fact]
    public void InteropLayoutsMatchPinnedValveX64Headers()
    {
        Assert.Equal(48, Marshal.SizeOf<VrMatrix>());
        Assert.Equal(80, Marshal.SizeOf<VrTrackedPose>());
        Assert.Equal(96, Marshal.SizeOf<VrPoseAction>());
        Assert.Equal(24, Marshal.SizeOf<VrDigital>());
        Assert.Equal(48, Marshal.SizeOf<VrAnalog>());
        Assert.Equal(32, Marshal.SizeOf<VrActionSet>());
        Assert.Equal(40, Marshal.SizeOf<VrPathRead>());
        Assert.Equal(56, Marshal.SizeOf<VrPathWrite>());
        Assert.Equal(8, Marshal.OffsetOf<VrDigital>(nameof(VrDigital.Origin)).ToInt32());
        Assert.Equal(16, Marshal.OffsetOf<VrDigital>(nameof(VrDigital.State)).ToInt32());
        Assert.Equal(40, Marshal.OffsetOf<VrPathWrite>(nameof(VrPathWrite.PathString)).ToInt32());
    }

    [Theory]
    [InlineData("auto", false, RuntimeBackend.Monado)]
    [InlineData("auto", true, RuntimeBackend.SteamVr)]
    [InlineData("monado", true, RuntimeBackend.Monado)]
    [InlineData("steamvr", false, RuntimeBackend.SteamVr)]
    public void BackendSelectionIsExplicitOrUsesHostDefault(string selection, bool windows, RuntimeBackend expected) =>
        Assert.Equal(expected, RuntimeConfiguration.SelectBackend(selection, windows));

    [Fact]
    public void BindingsReferenceExistingActionsAndProvideGripPosesForBothHands()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "steamvr");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "actions.json")));
        var actions = manifest.RootElement.GetProperty("actions").EnumerateArray()
            .ToDictionary(a => a.GetProperty("name").GetString()!, a => a.GetProperty("type").GetString());
        foreach (var binding in manifest.RootElement.GetProperty("default_bindings").EnumerateArray())
        {
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, binding.GetProperty("binding_url").GetString()!)));
            var flight = document.RootElement.GetProperty("bindings").GetProperty("/actions/flight");
            foreach (var source in flight.GetProperty("sources").EnumerateArray())
                foreach (var slot in source.GetProperty("inputs").EnumerateObject())
                    Assert.True(actions.ContainsKey(slot.Value.GetProperty("output").GetString()!));
            var poses = flight.GetProperty("poses").EnumerateArray().ToArray();
            foreach (string hand in new[] { "left", "right" })
            {
                var pose = Assert.Single(poses, p => p.GetProperty("path").GetString() == $"/user/hand/{hand}/pose/grip");
                Assert.Equal("pose", actions[pose.GetProperty("output").GetString()!]);
            }
        }
    }

    private static void AssertPose(RigidPose expected, RigidPose actual)
    {
        Assert.True(Vector3.Distance(expected.Position, actual.Position) < .03f, $"Expected {expected}, actual {actual}");
        Assert.True(1 - MathF.Abs(Quaternion.Dot(expected.Orientation, actual.Orientation)) < .00001f);
    }

    private sealed class FakeSpaces : ISteamVrSpaceApi
    {
        public RigidPose Standing = SteamVrSpaceTests.Standing, Seated = SteamVrSpaceTests.Seated;
        public bool FailSeatedOnce, FailAfterWriting;
        public int Writes;
        public RigidPose Read(bool seated) => seated ? Seated : Standing;
        public void Write(bool seated, RigidPose pose)
        {
            Writes++;
            bool fail = seated && FailSeatedOnce;
            if (fail) FailSeatedOnce = false;
            if (fail && !FailAfterWriting) throw new IOException("Simulated failure before writing");
            if (seated) Seated = pose; else Standing = pose;
            if (fail) throw new IOException("Simulated failure after writing");
        }
    }
}
