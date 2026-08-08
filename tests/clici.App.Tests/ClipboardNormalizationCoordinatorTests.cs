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
    public void EligibleProcessNormalizesMarginedTextAndWritesItBack()
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
            new StubProcessProvider(true, "pwsh"),
            logger,
            new CliciConfiguration());

        coordinator.HandleClipboardChanged();

        var write = Assert.Single(clipboard.Writes);
        Assert.Equal(Expected, write.Text);
        Assert.Contains(
            logger.Decisions,
            decision => decision.ProcessName == "pwsh"
                && decision.Status == MarginNormalizationStatus.Normalized);
    }

    [Fact]
    public void IneligibleForegroundProcessIsSkippedWithoutWritingClipboard()
    {
        var clipboard = new FakeClipboardService(
            new ClipboardReadResult(
                ClipboardAccessStatus.Success,
                Source,
                1,
                null));
        var coordinator = CreateCoordinator(
            clipboard,
            new StubProcessProvider(true, "notepad"),
            new RecordingLogger(),
            new CliciConfiguration());

        coordinator.HandleClipboardChanged();

        Assert.Empty(clipboard.Writes);
    }

    [Fact]
    public void PausedCoordinatorDoesNotWriteEvenForEligibleMarginedText()
    {
        var clipboard = new FakeClipboardService(
            new ClipboardReadResult(
                ClipboardAccessStatus.Success,
                Source,
                1,
                null));
        var coordinator = CreateCoordinator(
            clipboard,
            new RecordingLogger(),
            new CliciConfiguration());

        coordinator.SetPaused(true);
        coordinator.HandleClipboardChanged();

        Assert.Empty(clipboard.Writes);
    }

    [Fact]
    public void ForegroundProcessFailureIsLoggedAndNothingIsWritten()
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
            new StubProcessProvider(false, null, "InvalidOperationException"),
            logger,
            new CliciConfiguration());

        coordinator.HandleClipboardChanged();

        Assert.Empty(clipboard.Writes);
        Assert.Contains(
            logger.Failures,
            failure => failure.Operation == "foreground-process"
                && failure.ExceptionType == "InvalidOperationException");
    }

    [Fact]
    public void ClipboardReadFailureIsLoggedWithoutWriting()
    {
        var clipboard = new FakeClipboardService(
            new ClipboardReadResult(
                ClipboardAccessStatus.Busy,
                null,
                1,
                "ExternalException"));
        var logger = new RecordingLogger();
        var coordinator = CreateCoordinator(
            clipboard,
            logger,
            new CliciConfiguration());

        coordinator.HandleClipboardChanged();

        Assert.Empty(clipboard.Writes);
        Assert.Contains(
            logger.Failures,
            failure => failure.Operation == "clipboard-read"
                && failure.ExceptionType == "ExternalException");
    }

    [Fact]
    public void ClipboardWriteFailureIsLogged()
    {
        var clipboard = new FakeClipboardService(
            [
                new ClipboardReadResult(
                    ClipboardAccessStatus.Success,
                    Source,
                    1,
                    null)
            ],
            [
                new ClipboardWriteResult(
                    ClipboardAccessStatus.Failed,
                    1,
                    "ExternalException")
            ]);
        var logger = new RecordingLogger();
        var coordinator = CreateCoordinator(
            clipboard,
            logger,
            new CliciConfiguration());

        coordinator.HandleClipboardChanged();

        Assert.Contains(
            logger.Failures,
            failure => failure.Operation == "clipboard-write"
                && failure.ExceptionType == "ExternalException");
    }

    [Fact]
    public void AThrowingClipboardServiceIsCaughtAndLoggedRatherThanEscaping()
    {
        var logger = new RecordingLogger();
        var coordinator = CreateCoordinator(
            new ThrowingClipboardService(),
            logger,
            new CliciConfiguration());

        // Must not throw: HandleClipboardChanged runs on the UI thread from a
        // message-loop timer tick, so an escaping exception would surface on
        // the Windows message pump.
        coordinator.HandleClipboardChanged();

        Assert.Contains(
            logger.Failures,
            failure => failure.Operation == "clipboard-notification"
                && failure.ExceptionType == nameof(InvalidOperationException));
    }

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

    [Fact]
    public void ReentrantNotificationDuringWriteIsIgnoredRatherThanReprocessed()
    {
        // A clipboard write causes Windows to raise a new WM_CLIPBOARDUPDATE.
        // The WinForms OLE clipboard calls pump messages, so that notification
        // can be dispatched re-entrantly before the outer write returns. Only
        // one clipboard read is queued: if the re-entrant call is not guarded it
        // reads again, exhausts the queue, and surfaces a notification failure.
        var logger = new RecordingLogger();
        var clipboard = new ReentrantClipboardService(
            new ClipboardReadResult(
                ClipboardAccessStatus.Success,
                Source,
                1,
                null));
        var coordinator = CreateCoordinator(
            clipboard,
            new StubProcessProvider(true, "pwsh"),
            logger,
            new CliciConfiguration());
        clipboard.OnWrite = coordinator.HandleClipboardChanged;

        coordinator.HandleClipboardChanged();

        Assert.Single(clipboard.Writes);
        Assert.Equal(1, clipboard.ReadCount);
        Assert.DoesNotContain(
            logger.Failures,
            failure => failure.Operation == "clipboard-notification");
    }

    private static ClipboardNormalizationCoordinator CreateCoordinator(
        IClipboardService clipboard,
        IDiagnosticLogger logger,
        CliciConfiguration configuration) =>
        CreateCoordinator(
            clipboard,
            new StubProcessProvider(true, "pwsh"),
            logger,
            configuration);

    private static ClipboardNormalizationCoordinator CreateCoordinator(
        IClipboardService clipboard,
        IForegroundProcessProvider processProvider,
        IDiagnosticLogger logger,
        CliciConfiguration configuration) =>
        new(
            clipboard,
            processProvider,
            logger,
            configuration);

    private sealed class StubProcessProvider(
        bool succeeded,
        string? processName,
        string? exceptionType = null)
        : IForegroundProcessProvider
    {
        public ForegroundProcessResult TryGetForegroundProcess() =>
            new(succeeded, processName, exceptionType);
    }

    private sealed class ThrowingClipboardService : IClipboardService
    {
        public ClipboardReadResult TryReadText() =>
            throw new InvalidOperationException("clipboard exploded");

        public ClipboardWriteResult TryWriteText(
            string text,
            ClipboardReadResult source) =>
            throw new InvalidOperationException("clipboard exploded");
    }

    private sealed class RecordingLogger : IDiagnosticLogger
    {
        public List<string> Events { get; } = [];

        public List<(string? ProcessName, MarginNormalizationStatus Status)> Decisions { get; } = [];

        public List<(string Operation, string? ProcessName, string? ExceptionType)> Failures { get; } = [];

        public void Decision(
            string? processName,
            MarginNormalizationResult result) =>
            Decisions.Add((processName, result.Status));

        public void Failure(
            string operation,
            string? processName,
            string? exceptionType) =>
            Failures.Add((operation, processName, exceptionType));

        public void Event(string eventName) => Events.Add(eventName);
    }

    private sealed class ReentrantClipboardService : IClipboardService
    {
        private readonly ClipboardReadResult _read;

        public ReentrantClipboardService(ClipboardReadResult read) => _read = read;

        public Action? OnWrite { get; set; }

        public int ReadCount { get; private set; }

        public List<(string Text, ClipboardReadResult Source)> Writes { get; } = [];

        public ClipboardReadResult TryReadText()
        {
            ReadCount++;
            if (ReadCount > 1)
            {
                // The re-entrant call must be stopped before it reaches a read;
                // exhausting a single-item queue would otherwise throw.
                throw new InvalidOperationException("reentrant read");
            }

            return _read;
        }

        public ClipboardWriteResult TryWriteText(
            string text,
            ClipboardReadResult source)
        {
            Writes.Add((text, source));

            // Simulate the self-write notification arriving synchronously while
            // the outer write is still on the stack (WinForms pumps messages).
            OnWrite?.Invoke();

            return new ClipboardWriteResult(ClipboardAccessStatus.Success, 2, null);
        }
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
