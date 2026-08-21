using Clici.App.Clipboard;
using Clici.App.Logging;
using Clici.App.Processes;
using Clici.Core.Configuration;
using Clici.Core.MarginNormalization;

namespace Clici.App.Tests;

public sealed class ClipboardImageExportCoordinatorTests
{
    private const string ExportPath =
        @"\\wsl.localhost\Ubuntu\home\mdn87\agent-sandbox\drop\clipboard.png";

    [Fact]
    public void ConfiguredImageIsExportedWithoutReadingTextClipboard()
    {
        var clipboard = new RecordingClipboardService();
        var exporter = new RecordingImageExporter(
            new ClipboardImageExportResult(ClipboardImageExportStatus.Exported));
        var logger = new RecordingLogger();
        var coordinator = CreateCoordinator(
            clipboard,
            exporter,
            logger,
            new CliciConfiguration
            {
                ClipboardImageExportPath = ExportPath
            });

        coordinator.HandleClipboardChanged();

        Assert.Equal([ExportPath], exporter.DestinationPaths);
        Assert.Equal(0, clipboard.ReadCount);
        Assert.Empty(clipboard.Writes);
        Assert.Contains("exported-clipboard-image", logger.Events);
    }

    [Fact]
    public void NoImageFallsThroughToNormalTextHandling()
    {
        var clipboard = new RecordingClipboardService(
            new ClipboardReadResult(
                ClipboardAccessStatus.Success,
                "  first\r\n  second\r\n  third",
                1,
                null));
        var exporter = new RecordingImageExporter(
            new ClipboardImageExportResult(ClipboardImageExportStatus.NoImage));
        var coordinator = CreateCoordinator(
            clipboard,
            exporter,
            new RecordingLogger(),
            new CliciConfiguration
            {
                ClipboardImageExportPath = ExportPath
            });

        coordinator.HandleClipboardChanged();

        Assert.Equal(1, clipboard.ReadCount);
        var write = Assert.Single(clipboard.Writes);
        Assert.Equal("first\r\nsecond\r\nthird", write.Text);
    }

    [Fact]
    public void PauseNormalizationDoesNotPauseImageExport()
    {
        var clipboard = new RecordingClipboardService();
        var exporter = new RecordingImageExporter(
            new ClipboardImageExportResult(ClipboardImageExportStatus.Exported));
        var coordinator = CreateCoordinator(
            clipboard,
            exporter,
            new RecordingLogger(),
            new CliciConfiguration
            {
                ClipboardImageExportPath = ExportPath
            });

        coordinator.SetPaused(true);
        coordinator.HandleClipboardChanged();

        Assert.Equal([ExportPath], exporter.DestinationPaths);
        Assert.Equal(0, clipboard.ReadCount);
    }

    [Fact]
    public void DisabledCoordinatorDoesNotExportImages()
    {
        var exporter = new RecordingImageExporter(
            new ClipboardImageExportResult(ClipboardImageExportStatus.Exported));
        var coordinator = CreateCoordinator(
            new RecordingClipboardService(),
            exporter,
            new RecordingLogger(),
            new CliciConfiguration
            {
                Enabled = false,
                ClipboardImageExportPath = ExportPath
            });

        coordinator.HandleClipboardChanged();

        Assert.Empty(exporter.DestinationPaths);
    }

    [Fact]
    public void ImageExportFailureIsLoggedWithoutReadingTextClipboard()
    {
        var clipboard = new RecordingClipboardService();
        var exporter = new RecordingImageExporter(
            new ClipboardImageExportResult(
                ClipboardImageExportStatus.Failed,
                "IOException"));
        var logger = new RecordingLogger();
        var coordinator = CreateCoordinator(
            clipboard,
            exporter,
            logger,
            new CliciConfiguration
            {
                ClipboardImageExportPath = ExportPath
            });

        coordinator.HandleClipboardChanged();

        Assert.Equal(0, clipboard.ReadCount);
        Assert.Contains(
            logger.Failures,
            failure => failure.Operation == "clipboard-image-export" &&
                failure.ExceptionType == "IOException");
    }

    [Fact]
    public void MonitorExcludedImageIsNotExportedOrReadAsText()
    {
        var clipboard = new RecordingClipboardService();
        var exporter = new RecordingImageExporter(
            new ClipboardImageExportResult(
                ClipboardImageExportStatus.SkippedMonitorProcessing));
        var logger = new RecordingLogger();
        var coordinator = CreateCoordinator(
            clipboard,
            exporter,
            logger,
            new CliciConfiguration
            {
                ClipboardImageExportPath = ExportPath
            });

        coordinator.HandleClipboardChanged();

        Assert.Equal(0, clipboard.ReadCount);
        Assert.Contains("skipped-monitor-processing-exclusion", logger.Events);
    }

    private static ClipboardNormalizationCoordinator CreateCoordinator(
        IClipboardService clipboard,
        IClipboardImageExporter exporter,
        IDiagnosticLogger logger,
        CliciConfiguration configuration) =>
        new(
            clipboard,
            new StubProcessProvider(),
            logger,
            configuration,
            exporter);

    private sealed class StubProcessProvider : IForegroundProcessProvider
    {
        public ForegroundProcessResult TryGetForegroundProcess() =>
            new(true, "pwsh", null);
    }

    private sealed class RecordingImageExporter(
        ClipboardImageExportResult result)
        : IClipboardImageExporter
    {
        public List<string> DestinationPaths { get; } = [];

        public ClipboardImageExportResult TryExport(string destinationPath)
        {
            DestinationPaths.Add(destinationPath);
            return result;
        }
    }

    private sealed class RecordingClipboardService : IClipboardService
    {
        private readonly ClipboardReadResult _read;

        public RecordingClipboardService(
            ClipboardReadResult? read = null)
        {
            _read = read ?? new ClipboardReadResult(
                ClipboardAccessStatus.NoText,
                null,
                1,
                null);
        }

        public int ReadCount { get; private set; }

        public List<(string Text, ClipboardReadResult Source)> Writes { get; } = [];

        public ClipboardReadResult TryReadText()
        {
            ReadCount++;
            return _read;
        }

        public ClipboardWriteResult TryWriteText(
            string text,
            ClipboardReadResult source)
        {
            Writes.Add((text, source));
            return new ClipboardWriteResult(
                ClipboardAccessStatus.Success,
                source.SequenceNumber + 1,
                null);
        }
    }

    private sealed class RecordingLogger : IDiagnosticLogger
    {
        public List<string> Events { get; } = [];

        public List<(string Operation, string? ExceptionType)> Failures { get; } = [];

        public void Decision(
            string? processName,
            MarginNormalizationResult result)
        {
        }

        public void Failure(
            string operation,
            string? processName,
            string? exceptionType) =>
            Failures.Add((operation, exceptionType));

        public void Event(string eventName) => Events.Add(eventName);
    }
}
