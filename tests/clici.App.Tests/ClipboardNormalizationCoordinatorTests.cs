using Clici.App.Clipboard;
using Clici.App.Logging;
using Clici.App.Processes;
using Clici.Core.Configuration;
using Clici.Core.MarginNormalization;

namespace Clici.App.Tests;

public sealed class ClipboardNormalizationCoordinatorTests
{
    private const string Source = "  first\r\n  second";
    private const string Expected = "first\r\nsecond";

    [Fact]
    public void OversizedTextIsSkippedBeforeNormalization()
    {
        var clipboard = new FakeClipboardService(
            new ClipboardReadResult(
                ClipboardAccessStatus.Success,
                Source,
                1,
                null));
        var logger = new RecordingLogger();
        var coordinator = CreateCoordinator(
            clipboard,
            logger,
            new CliciConfiguration
            {
                MaximumTextCharacters = Source.Length - 1
            });

        coordinator.HandleClipboardChanged();

        Assert.Empty(clipboard.Writes);
        Assert.Contains("skipped-text-over-size-limit", logger.Events);
    }

    [Fact]
    public void StaleWriteDoesNotSuppressTheFollowingIdenticalSource()
    {
        var clipboard = new FakeClipboardService(
            [
                new ClipboardReadResult(
                    ClipboardAccessStatus.Success,
                    Source,
                    1,
                    null),
                new ClipboardReadResult(
                    ClipboardAccessStatus.Success,
                    Source,
                    2,
                    null)
            ],
            [
                new ClipboardWriteResult(
                    ClipboardAccessStatus.Stale,
                    2,
                    null),
                new ClipboardWriteResult(
                    ClipboardAccessStatus.Success,
                    3,
                    null)
            ]);
        var logger = new RecordingLogger();
        var coordinator = CreateCoordinator(
            clipboard,
            logger,
            new CliciConfiguration());

        coordinator.HandleClipboardChanged();
        coordinator.HandleClipboardChanged();

        Assert.Equal(2, clipboard.Writes.Count);
        Assert.All(clipboard.Writes, write => Assert.Equal(Expected, write.Text));
        Assert.Contains("skipped-stale-clipboard-write", logger.Events);
    }

    [Fact]
    public void SuccessfulWriteSuppressesItsMatchingNotification()
    {
        var clipboard = new FakeClipboardService(
            [
                new ClipboardReadResult(
                    ClipboardAccessStatus.Success,
                    Source,
                    1,
                    null),
                new ClipboardReadResult(
                    ClipboardAccessStatus.Success,
                    Expected,
                    2,
                    null)
            ]);
        var coordinator = CreateCoordinator(
            clipboard,
            new RecordingLogger(),
            new CliciConfiguration());

        coordinator.HandleClipboardChanged();
        coordinator.HandleClipboardChanged();

        Assert.Single(clipboard.Writes);
        Assert.Equal(Expected, clipboard.Writes[0].Text);
    }

    private static ClipboardNormalizationCoordinator CreateCoordinator(
        IClipboardService clipboard,
        IDiagnosticLogger logger,
        CliciConfiguration configuration) =>
        new(
            clipboard,
            new AllowedProcessProvider(),
            logger,
            configuration);

    private sealed class AllowedProcessProvider : IForegroundProcessProvider
    {
        public ForegroundProcessResult TryGetForegroundProcess() =>
            new(true, "pwsh", null);
    }

    private sealed class RecordingLogger : IDiagnosticLogger
    {
        public List<string> Events { get; } = [];

        public void Decision(
            string? processName,
            MarginNormalizationResult result)
        {
        }

        public void Failure(
            string operation,
            string? processName,
            string? exceptionType)
        {
        }

        public void Event(string eventName) => Events.Add(eventName);
    }

    private sealed class FakeClipboardService : IClipboardService
    {
        private readonly Queue<ClipboardReadResult> _reads;
        private readonly Queue<ClipboardWriteResult> _writeResults;

        public FakeClipboardService(
            ClipboardReadResult read,
            IReadOnlyList<ClipboardWriteResult>? writeResults = null)
            : this([read], writeResults)
        {
        }

        public FakeClipboardService(
            IReadOnlyList<ClipboardReadResult> reads,
            IReadOnlyList<ClipboardWriteResult>? writeResults = null)
        {
            _reads = new Queue<ClipboardReadResult>(reads);
            _writeResults = new Queue<ClipboardWriteResult>(
                writeResults ??
                [
                    new ClipboardWriteResult(
                        ClipboardAccessStatus.Success,
                        2,
                        null)
                ]);
        }

        public List<(string Text, ClipboardReadResult Source)> Writes { get; } = [];

        public ClipboardReadResult TryReadText() => _reads.Dequeue();

        public ClipboardWriteResult TryWriteText(
            string text,
            ClipboardReadResult source)
        {
            Writes.Add((text, source));
            return _writeResults.Dequeue();
        }
    }
}
