using System.Numerics;
using Evergine.Bindings.OpenXR;
using FlugelKranz.Core;

namespace FlugelKranz.OpenXR;

internal readonly record struct TrackerDevice(ulong Id, string Name, string Serial, bool CanCreateSpace)
{
    // XDev properties have no device-class field. Match the tracker names used
    // by Monado/WiVRn, excluding pose-only Lighthouse calibration base stations.
    public bool IsTracker => CanCreateSpace
        && Name.Contains("tracker", StringComparison.OrdinalIgnoreCase)
        && !Name.Contains("base station", StringComparison.OrdinalIgnoreCase)
        && !Serial.StartsWith("LHB-", StringComparison.OrdinalIgnoreCase);
}

internal interface IXdevSpaceApi
{
    nint CreateList();
    IReadOnlyList<TrackerDevice> Enumerate(nint list);
    XrSpace CreateSpace(nint list, ulong id);
    (Vector3 Position, bool IsTracked) Locate(XrSpace space, long time);
    void DestroySpace(XrSpace space);
    void DestroyList(nint list);
}

/// <summary>Owns optional tracker spaces and refreshes immutable XDev snapshots.</summary>
internal sealed class OpenXrTrackers(IXdevSpaceApi api) : IDisposable
{
    public const string ExtensionName = "XR_MNDX_xdev_space";
    private const long RefreshIntervalNanoseconds = 1_000_000_000;
    private readonly List<(TrackerDevice Device, XrSpace Space)> spaces = [];
    private nint list;
    private long nextRefreshTime;

    public int Revision { get; private set; }

    public IReadOnlyList<TrackerSample> Read(long time)
    {
        // The pinned Monado reports generation 1 forever. Periodic snapshots
        // also discover hotplug on runtimes whose generation never changes.
        if (list == 0 || time >= nextRefreshTime)
            Refresh(time);

        var samples = new List<TrackerSample>(spaces.Count);
        foreach (var (device, space) in spaces)
        {
            var (position, tracked) = api.Locate(space, time);
            var sample = new TrackerSample(device.Serial, position, tracked);
            if (sample.IsUsable)
                samples.Add(sample);
        }
        return samples;
    }

    private void Refresh(long time)
    {
        nint nextList = api.CreateList();
        var nextSpaces = new List<(TrackerDevice Device, XrSpace Space)>();
        try
        {
            var serials = new HashSet<string>(StringComparer.Ordinal);
            foreach (var device in api.Enumerate(nextList))
            {
                if (!device.IsTracker || (device.Serial.Length > 0 && !serials.Add(device.Serial)))
                    continue;
                nextSpaces.Add((device, api.CreateSpace(nextList, device.Id)));
            }
        }
        catch
        {
            foreach (var (_, space) in nextSpaces)
                api.DestroySpace(space);
            api.DestroyList(nextList);
            throw;
        }

        Dispose();
        list = nextList;
        spaces.AddRange(nextSpaces);
        nextRefreshTime = time + RefreshIntervalNanoseconds;
        Revision++;
    }

    public void Dispose()
    {
        foreach (var (_, space) in spaces)
            api.DestroySpace(space);
        spaces.Clear();
        if (list != 0)
        {
            api.DestroyList(list);
            list = 0;
        }
    }
}
