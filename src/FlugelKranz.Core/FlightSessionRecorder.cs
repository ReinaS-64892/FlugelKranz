using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;

namespace FlugelKranz.Core;

/// <summary>Bounded, nonblocking diagnostic capture; only shutdown waits for disk I/O.</summary>
public sealed class FlightSessionRecorder : IDisposable
{
    private readonly Channel<Entry> queue;
    private readonly Task writerTask;
    private long sequence, dropped;
    private int disposed;
    private sealed record Entry(long Sequence, long TimestampTicks, string Type, object Data);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        IncludeFields = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    public FlightSessionRecorder(string path) : this(new StreamWriter(
        new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))) { }

    internal FlightSessionRecorder(TextWriter output, int capacity = 512)
    {
        queue = Channel.CreateBounded<Entry>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true, SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
        writerTask = Task.Run(() => WriteAsync(output));
        Record("start", new
        {
            schemaVersion = 1,
            startedUtc = DateTimeOffset.UtcNow,
            stopwatchFrequency = Stopwatch.Frequency,
            version = typeof(FlightSessionRecorder).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            coordinateSystem = "Monado root; metres; quaternion x/y/z/w; A*B applies B first"
        });
    }

    public void Record(string type, object data)
    {
        if (Volatile.Read(ref disposed) != 0) return;
        var entry = new Entry(Interlocked.Increment(ref sequence), Stopwatch.GetTimestamp(), type, data);
        if (!queue.Writer.TryWrite(entry)) Interlocked.Increment(ref dropped);
    }

    private async Task WriteAsync(TextWriter output)
    {
        try
        {
            using (output)
            {
                long lastFlush = Stopwatch.GetTimestamp();
                await foreach (var entry in queue.Reader.ReadAllAsync())
                {
                    await output.WriteLineAsync(JsonSerializer.Serialize(entry, JsonOptions));
                    if (entry.Type == "start" || Stopwatch.GetElapsedTime(lastFlush).TotalSeconds >= 1)
                    {
                        await output.FlushAsync();
                        lastFlush = Stopwatch.GetTimestamp();
                    }
                }
                await output.FlushAsync();
            }
        }
        catch (Exception exception)
        {
            // Recording failure must not interrupt flight or block its worker.
            queue.Writer.TryComplete(exception);
            Console.Error.WriteLine($"セッション記録に失敗しました（飛行は継続します）: {exception.Message}");
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        try
        {
            queue.Writer.WriteAsync(new(Interlocked.Increment(ref sequence), Stopwatch.GetTimestamp(), "stop",
                new { stoppedUtc = DateTimeOffset.UtcNow, droppedRecords = Interlocked.Read(ref dropped) }))
                .AsTask().GetAwaiter().GetResult();
        }
        catch (ChannelClosedException) { }
        queue.Writer.TryComplete();
        writerTask.GetAwaiter().GetResult();
    }
}

/// <summary>One computed frame, completed with the offset confirmed after publication.</summary>
public sealed record RecordedFlightFrame(
    long ReadStartedTimestampTicks,
    long ReadCompletedTimestampTicks,
    float ElapsedSeconds,
    InputFrame Input,
    bool Enabled,
    FlightMode Mode,
    string ActiveMode,
    bool Dragging,
    bool Turning,
    bool Released,
    bool Reset,
    RigidPose OffsetBefore,
    RigidPose ComputedOffset,
    IReadOnlyList<TrackerRotationOffset> TrackerRotations)
{
    public bool FramePaced { get; init; }
    public RigidPose AppliedOffset { get; init; }
    public long AppliedTimestampTicks { get; init; }
}
