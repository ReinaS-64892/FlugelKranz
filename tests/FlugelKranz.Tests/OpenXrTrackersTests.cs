using System.Numerics;
using System.Runtime.InteropServices;
using Evergine.Bindings.OpenXR;
using FlugelKranz.OpenXR;
using Xunit;

namespace FlugelKranz.Tests;

public class OpenXrTrackersTests
{
    [Theory]
    [InlineData("Vive Tracker Gen3 (vive)", "LHR-body", true, true)]
    [InlineData("Tundra Tracker Gen3 (libsurvive)", "TUNDRA-body", true, true)]
    [InlineData("WiVRn generic tracker (waist)", "wivrn-waist", true, true)]
    [InlineData("Lighthouse Base Station Tracker", "LHB-12345678", true, false)]
    [InlineData("Generic Tracker", "lhb-12345678", true, false)]
    [InlineData("Base Station Tracker", "station", true, false)]
    [InlineData("Valve Index Left Controller (vive)", "LHR-left", true, false)]
    [InlineData("WiVRn HMD", "wivrn-hmd", true, false)]
    [InlineData("WiVRn Eye Tracker", "wivrn-eye", false, false)]
    public void RecognizesPoseTrackersAndExcludesBaseStationsAndOtherDevices(
        string name, string serial, bool canCreateSpace, bool expected)
    {
        Assert.Equal(expected, new TrackerDevice(42, name, serial, canCreateSpace).IsTracker);
    }

    [Theory]
    [InlineData(0x0, false)]
    [InlineData(0x3, false)] // Last-known valid pose, no current position tracking.
    [InlineData(0x5, false)] // Orientation tracking alone is insufficient.
    [InlineData(0x8, false)] // Tracking without a valid position is insufficient.
    [InlineData(0xA, true)] // Rotation is irrelevant to a positional midpoint.
    [InlineData(0xF, true)]
    public void RequiresAValidActivelyTrackedPosition(int flags, bool expected)
    {
        Assert.Equal(expected, XdevSpaceApi.HasTrackedPosition((ulong)flags));
    }

    [Fact]
    public void HotplugAndRemovalRefreshSnapshotsEvenWithoutGenerationChanges()
    {
        var api = new FakeXdevApi();
        using var trackers = new OpenXrTrackers(api);
        Assert.Empty(trackers.Read(10));
        Assert.Equal(1, trackers.Revision);

        api.Devices = [new(42, "Vive Tracker", "body", true), new(43, "Base Station Tracker", "LHB-station", true)];
        Assert.Empty(trackers.Read(500_000_010));
        var connected = Assert.Single(trackers.Read(1_000_000_010));
        Assert.Equal(2, trackers.Revision);
        Assert.Equal("body", connected.Serial);
        Assert.Equal(new Vector3(1, 2, 3), connected.Position);
        Assert.Single(api.LiveSpaces);

        api.Devices = [];
        Assert.Empty(trackers.Read(2_000_000_010));
        Assert.Empty(api.LiveSpaces);
        Assert.Single(api.LiveLists);

        // IDs are local to each snapshot: the returning serial may have a new ID.
        api.Devices = [new(99, "Vive Tracker", "body", true)];
        Assert.Equal("body", Assert.Single(trackers.Read(3_000_000_010)).Serial);
        trackers.Dispose();
        trackers.Dispose();
        Assert.Empty(api.LiveSpaces);
        Assert.Empty(api.LiveLists);
    }

    [Fact]
    public void TrackingLossAndRecoveryAreReflectedWithoutWaitingForAListRefresh()
    {
        var api = new FakeXdevApi { Devices = [new(42, "Tracker", "body", true)] };
        using var trackers = new OpenXrTrackers(api);
        Assert.Single(trackers.Read(10));
        api.Tracked = false;
        Assert.Empty(trackers.Read(20));
        api.Tracked = true;
        Assert.Single(trackers.Read(30));
        api.Position = new(float.NaN, 0, 0);
        Assert.Empty(trackers.Read(40));
        Assert.Equal(1, api.CreatedLists);
        Assert.Equal(new long[] { 10, 20, 30, 40 }, api.LocationTimes);
    }

    [Fact]
    public void DuplicateTrackerSerialsAndBaseStationsDoNotCreateExtraSpaces()
    {
        var api = new FakeXdevApi
        {
            Devices =
            [
                new(42, "Vive Tracker", "body", true),
                new(43, "Vive Tracker", "body", true),
                new(44, "Generic Tracker", "LHB-station", true),
                new(45, "Base Station Tracker", "station", true)
            ]
        };
        using var trackers = new OpenXrTrackers(api);
        Assert.Single(trackers.Read(10));
        Assert.Single(api.LiveSpaces);
    }

    [Fact]
    public void FailedRefreshCleansUpTheNewSnapshotAndRetainsThePreviousSnapshot()
    {
        var api = new FakeXdevApi { Devices = [new(42, "Tracker", "body", true)] };
        using var trackers = new OpenXrTrackers(api);
        trackers.Read(10);
        var previousSpace = Assert.Single(api.LiveSpaces);
        var previousList = Assert.Single(api.LiveLists);

        api.Devices = [new(42, "Tracker", "body", true), new(43, "Tracker", "foot", true)];
        api.FailSpaceId = 43;
        Assert.Throws<InvalidOperationException>(() => trackers.Read(1_000_000_010));
        Assert.Equal(previousSpace, Assert.Single(api.LiveSpaces));
        Assert.Equal(previousList, Assert.Single(api.LiveLists));
    }

    [Fact]
    public void XdevAbiMatchesThePreviewHeadersOn64BitLinux()
    {
        Assert.Equal(8, IntPtr.Size);
        Assert.Equal(24, Marshal.SizeOf<XdevSpaceApi.SystemProperties>());
        Assert.Equal(16, Marshal.SizeOf<XdevSpaceApi.CreateListInfo>());
        Assert.Equal(24, Marshal.SizeOf<XdevSpaceApi.GetInfo>());
        Assert.Equal(536, Marshal.SizeOf<XdevSpaceApi.Properties>());
        Assert.Equal(528, Marshal.OffsetOf<XdevSpaceApi.Properties>(nameof(XdevSpaceApi.Properties.CanCreateSpace)).ToInt32());
        Assert.Equal(64, Marshal.SizeOf<XdevSpaceApi.CreateSpaceInfo>());
        Assert.Equal(32, Marshal.OffsetOf<XdevSpaceApi.CreateSpaceInfo>(nameof(XdevSpaceApi.CreateSpaceInfo.Offset)).ToInt32());
    }

    private sealed class FakeXdevApi : IXdevSpaceApi
    {
        public IReadOnlyList<TrackerDevice> Devices { get; set; } = [];
        public bool Tracked { get; set; } = true;
        public Vector3 Position { get; set; } = new(1, 2, 3);
        public ulong? FailSpaceId { get; set; }
        public int CreatedLists { get; private set; }
        public HashSet<nint> LiveLists { get; } = [];
        public HashSet<XrSpace> LiveSpaces { get; } = [];
        public List<long> LocationTimes { get; } = [];
        private readonly Dictionary<nint, TrackerDevice[]> snapshots = [];
        private int nextSpace;

        public nint CreateList()
        {
            nint list = ++CreatedLists;
            LiveLists.Add(list);
            snapshots.Add(list, Devices.ToArray());
            return list;
        }

        public IReadOnlyList<TrackerDevice> Enumerate(nint list) => snapshots[list];

        public XrSpace CreateSpace(nint list, ulong id)
        {
            Assert.Contains(snapshots[list], device => device.Id == id);
            if (id == FailSpaceId)
                throw new InvalidOperationException("Tracker became unavailable.");
            var space = new XrSpace(++nextSpace);
            Assert.True(LiveSpaces.Add(space));
            return space;
        }

        public (Vector3 Position, bool IsTracked) Locate(XrSpace space, long time)
        {
            Assert.Contains(space, LiveSpaces);
            LocationTimes.Add(time);
            return (Position, Tracked);
        }

        public void DestroySpace(XrSpace space) => Assert.True(LiveSpaces.Remove(space));
        public void DestroyList(nint list)
        {
            Assert.True(LiveLists.Remove(list));
            snapshots.Remove(list);
        }
    }
}
