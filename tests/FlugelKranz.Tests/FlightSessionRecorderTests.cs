using System.Text.Json;
using FlugelKranz.Core;
using Xunit;

namespace FlugelKranz.Tests;

public class FlightSessionRecorderTests
{
    [Fact]
    public void FlushOnDisposePreservesNumericPoseFieldsLogicalInputsAndNonfiniteValues()
    {
        var output = new StringWriter();
        using (var recorder = new FlightSessionRecorder(output))
        {
            recorder.Record("sample", new
            {
                input = new InputFrame(RigidPose.Identity, true,
                    new(RigidPose.Identity with { Position = new(1, 2, 3) }, 1, 0, 0, true), default),
                invalidValue = float.NaN
            });
        }
        var entries = Parse(output);
        Assert.Equal(["start", "sample", "stop"], entries.Select(e => e.GetProperty("type").GetString()));
        var header = entries[0].GetProperty("data");
        Assert.Equal(1, header.GetProperty("schemaVersion").GetInt32());
        Assert.True(header.GetProperty("stopwatchFrequency").GetInt64() > 0);
        var sample = entries[1].GetProperty("data");
        var hand = sample.GetProperty("input").GetProperty("left");
        Assert.Equal(1, hand.GetProperty("drag").GetSingle());
        Assert.True(hand.GetProperty("isTracked").GetBoolean());
        Assert.Equal(2, hand.GetProperty("pose").GetProperty("position").GetProperty("y").GetSingle());
        Assert.Equal(1, hand.GetProperty("pose").GetProperty("orientation").GetProperty("w").GetSingle());
        Assert.Equal("NaN", sample.GetProperty("invalidValue").GetString());
        Assert.Equal(0, entries[^1].GetProperty("data").GetProperty("droppedRecords").GetInt64());
    }

    [Fact]
    public async Task SlowDiskDropsRecordsWithoutBlockingProducerAndReportsMissingSequenceNumbers()
    {
        var output = new BlockingWriter();
        var recorder = new FlightSessionRecorder(output, capacity: 2);
        await output.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        try
        {
            for (int i = 0; i < 20; i++) recorder.Record("sample", new { i });
        }
        finally { output.Resume.SetResult(); }
        await Task.Run(recorder.Dispose, TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var entries = Parse(output);
        Assert.Equal(18, entries[^1].GetProperty("data").GetProperty("droppedRecords").GetInt64());
        Assert.Equal(22, entries[^1].GetProperty("sequence").GetInt64());
        Assert.Equal(4, entries.Length);
    }

    [Fact]
    public async Task DiskFailureDoesNotEscapeOrHangShutdown()
    {
        var output = new FailingWriter();
        var recorder = new FlightSessionRecorder(output);
        await output.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        for (int i = 0; i < 1000; i++) recorder.Record("sample", new { i });
        await Task.Run(recorder.Dispose, TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact]
    public void ExistingFileIsNeverOverwritten()
    {
        string path = Path.Combine(Path.GetTempPath(), $"flugel-recording-{Guid.NewGuid():N}.jsonl");
        try
        {
            File.WriteAllText(path, "existing");
            Assert.Throws<IOException>(() => new FlightSessionRecorder(path));
            Assert.Equal("existing", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    private static JsonElement[] Parse(StringWriter writer) => writer.ToString()
        .Split('\n', StringSplitOptions.RemoveEmptyEntries)
        .Select(line => { using var document = JsonDocument.Parse(line); return document.RootElement.Clone(); })
        .ToArray();

    private sealed class BlockingWriter : StringWriter
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async Task WriteLineAsync(string? value)
        {
            Started.TrySetResult();
            await Resume.Task;
            await base.WriteLineAsync(value);
        }
    }

    private sealed class FailingWriter : StringWriter
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override Task WriteLineAsync(string? value)
        {
            Started.TrySetResult();
            throw new IOException("test disk failure");
        }
    }
}
