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

        string? sourceName = null;

        try
        {
            _processing = true;

            if (!_configuration.Enabled || _paused)
            {
                return;
            }

            var processResult = _foregroundProcessProvider.TryGetForegroundProcess();
            var foregroundName = processResult.ProcessName;

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

            var snapshot = _clipboardService.TryReadText();
            if (snapshot.Status == ClipboardAccessStatus.NoText)
            {
                return;
            }

            if (snapshot.Status != ClipboardAccessStatus.Success ||
                snapshot.Text is null)
            {
                _logger.Failure(
                    "clipboard-read",
                    foregroundName,
                    snapshot.ExceptionType);
                return;
            }

            // Oversized text is filtered before any hashing or classification so
            // a large clipboard item is never scanned.
            if (snapshot.Text.Length > _configuration.MaximumTextCharacters)
            {
                _logger.Event("skipped-text-over-size-limit");
                return;
            }

            // 1. Reject clici's own write. The private marker is authoritative;
            // the content hash remains as a fallback for brokers that drop it.
            if (snapshot.IsCliciWrite)
            {
                _selfWriteSuppressor.ClearPending();
                _logger.Event("skipped-self-write-marker");
                return;
            }

            if (_selfWriteSuppressor.ShouldSuppress(snapshot.Text))
            {
                return;
            }

            // 2. Honor the source's clipboard privacy policy: never process an
            // item the source excluded from monitor processing.
            if (snapshot.PrivacyPolicy?.ExcludeFromMonitorProcessing == true)
            {
                _logger.Event("skipped-monitor-processing-exclusion");
                return;
            }

            // 3. Require a safe native format bundle. Rich, non-text, and unknown
            // application formats are skipped in automatic mode.
            if (snapshot.HasDisallowedFormat)
            {
                _logger.Event("skipped-rich-or-nontext-content");
                return;
            }

            // 4. Source confidence. The clipboard owner is the primary signal;
            // the foreground process is only a fallback when the owner is unknown.
            sourceName = snapshot.OwnerProcessName ?? foregroundName;
            if (!_processNameMatcher.IsAllowed(
                    sourceName,
                    _configuration.AllowedProcessNames,
                    _configuration.ExcludedProcessNames))
            {
                _logger.Event("skipped-untrusted-source");
                return;
            }

            // 5. Layout confidence.
            var result = _normalizer.Normalize(
                snapshot.Text,
                _configuration.ToNormalizationOptions());
            _logger.Decision(sourceName, result);

            if (result.Status != MarginNormalizationStatus.Normalized ||
                string.Equals(result.Text, snapshot.Text, StringComparison.Ordinal))
            {
                return;
            }

            // 6. Write, preserving the source privacy policy. The service rechecks
            // the clipboard sequence immediately before writing.
            var writeResult = _clipboardService.TryWriteText(result.Text, snapshot);
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
                sourceName,
                writeResult.ExceptionType);
        }
        catch (Exception exception)
        {
            _logger.Failure(
                "clipboard-notification",
                sourceName,
                exception.GetType().Name);
        }
        finally
        {
            _processing = false;
        }
    }
}
