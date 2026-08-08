using Clici.App.Logging;
using Clici.App.Processes;
using Clici.Core.Clipboard;
using Clici.Core.Configuration;
using Clici.Core.MarginNormalization;
using Clici.Core.Processes;

namespace Clici.App.Clipboard;

internal sealed class ClipboardNormalizationCoordinator
{
    private readonly IClipboardService _clipboardService;
    private readonly IForegroundProcessProvider _foregroundProcessProvider;
    private readonly IDiagnosticLogger _logger;
    private readonly MarginNormalizer _normalizer;
    private readonly ProcessNameMatcher _processNameMatcher;
    private readonly ClipboardSelfWriteSuppressor _selfWriteSuppressor;
    private CliciConfiguration _configuration;
    private bool _paused;
    private bool _processing;

    public ClipboardNormalizationCoordinator(
        IClipboardService clipboardService,
        IForegroundProcessProvider foregroundProcessProvider,
        IDiagnosticLogger logger,
        CliciConfiguration configuration)
    {
        _clipboardService = clipboardService;
        _foregroundProcessProvider = foregroundProcessProvider;
        _logger = logger;
        _configuration = configuration;
        _normalizer = new MarginNormalizer();
        _processNameMatcher = new ProcessNameMatcher();
        _selfWriteSuppressor = new ClipboardSelfWriteSuppressor();
    }

    public void UpdateConfiguration(CliciConfiguration configuration)
    {
        _configuration = ConfigurationValidator.Validate(configuration).Configuration;
        _selfWriteSuppressor.ClearPending();
    }

    public void SetPaused(bool paused)
    {
        _paused = paused;
        _selfWriteSuppressor.ClearPending();
    }

    public void HandleClipboardChanged()
    {
        // Reading or writing the clipboard pumps the message loop, so our own
        // write raises a WM_CLIPBOARDUPDATE that can be dispatched re-entrantly
        // before this call returns. Nested clipboard access on the single UI
        // thread stalls the shared clipboard and freezes the Win+V flyout, so a
        // notification that arrives mid-processing is dropped.
        if (_processing)
        {
            return;
        }

        string? processName = null;

        try
        {
            _processing = true;

            if (!_configuration.Enabled || _paused)
            {
                return;
            }

            var processResult = _foregroundProcessProvider.TryGetForegroundProcess();
            processName = processResult.ProcessName;

            if (!processResult.Succeeded)
            {
                if (processResult.ExceptionType is not null)
                {
                    _logger.Failure(
                        "foreground-process",
                        null,
                        processResult.ExceptionType);
                }

                return;
            }

            if (!_processNameMatcher.IsAllowed(
                    processName,
                    _configuration.AllowedProcessNames,
                    _configuration.ExcludedProcessNames))
            {
                return;
            }

            var readResult = _clipboardService.TryReadText();
            if (readResult.Status == ClipboardAccessStatus.NoText)
            {
                return;
            }

            if (readResult.Status != ClipboardAccessStatus.Success ||
                readResult.Text is null)
            {
                _logger.Failure(
                    "clipboard-read",
                    processName,
                    readResult.ExceptionType);
                return;
            }

            if (readResult.Text.Length > _configuration.MaximumTextCharacters)
            {
                _logger.Event("skipped-text-over-size-limit");
                return;
            }

            if (_selfWriteSuppressor.ShouldSuppress(readResult.Text))
            {
                return;
            }

            var result = _normalizer.Normalize(
                readResult.Text,
                _configuration.ToNormalizationOptions());
            _logger.Decision(processName, result);

            if (result.Status != MarginNormalizationStatus.Normalized ||
                string.Equals(result.Text, readResult.Text, StringComparison.Ordinal))
            {
                return;
            }

            var writeResult = _clipboardService.TryWriteText(result.Text, readResult);
            if (writeResult.Status == ClipboardAccessStatus.Success)
            {
                _selfWriteSuppressor.MarkPendingWrite(result.Text);
                return;
            }

            if (writeResult.Status == ClipboardAccessStatus.Stale)
            {
                _logger.Event("skipped-stale-clipboard-write");
                return;
            }

            _logger.Failure(
                "clipboard-write",
                processName,
                writeResult.ExceptionType);
        }
        catch (Exception exception)
        {
            _logger.Failure(
                "clipboard-notification",
                processName,
                exception.GetType().Name);
        }
        finally
        {
            _processing = false;
        }
    }
}
