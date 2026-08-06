using System.Diagnostics;
using Clici.App.Clipboard;
using Clici.App.Configuration;
using Clici.App.Logging;
using Clici.App.Processes;
using Clici.Core.Configuration;

namespace Clici.App;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly JsonConfigurationStore _configurationStore;
    private readonly ClipboardNormalizationCoordinator _coordinator;
    private readonly IDiagnosticLogger _logger;
    private readonly ToolStripMenuItem _enabledMenuItem;
    private readonly ToolStripMenuItem _pauseMenuItem;
    private readonly ContextMenuStrip _trayMenu;
    private readonly NotifyIcon _notifyIcon;
    private ClipboardListenerWindow? _clipboardListener;
    private CliciConfiguration _configuration;
    private bool _resourcesDisposed;

    public TrayApplicationContext()
    {
        _configurationStore = new JsonConfigurationStore();
        var loadResult = _configurationStore.Load();
        _configuration = loadResult.Configuration;
        _logger = new DiagnosticLogger(
            _configurationStore.DirectoryPath,
            _configuration.DiagnosticLogging);

        if (loadResult.UsedFallback)
        {
            _logger.Failure(
                "configuration-load",
                null,
                loadResult.ExceptionType ?? "validation");
        }

        _coordinator = new ClipboardNormalizationCoordinator(
            new WinFormsClipboardService(),
            new WindowsForegroundProcessProvider(),
            _logger,
            _configuration);

        _enabledMenuItem = new ToolStripMenuItem("Enabled")
        {
            Checked = _configuration.Enabled,
            CheckOnClick = true
        };
        _enabledMenuItem.CheckedChanged += EnabledMenuItemOnCheckedChanged;

        _pauseMenuItem = new ToolStripMenuItem("Pause normalization")
        {
            CheckOnClick = true
        };
        _pauseMenuItem.CheckedChanged += PauseMenuItemOnCheckedChanged;

        _trayMenu = new ContextMenuStrip();
        _trayMenu.Items.Add(_enabledMenuItem);
        _trayMenu.Items.Add(_pauseMenuItem);
        _trayMenu.Items.Add(new ToolStripSeparator());
        _trayMenu.Items.Add(
            "Open configuration file",
            null,
            (_, _) => OpenPath(_configurationStore.FilePath));
        _trayMenu.Items.Add(
            "Open configuration folder",
            null,
            (_, _) => OpenPath(_configurationStore.DirectoryPath));
        _trayMenu.Items.Add(new ToolStripSeparator());
        _trayMenu.Items.Add("Exit", null, (_, _) => ExitThread());

        _notifyIcon = new NotifyIcon
        {
            ContextMenuStrip = _trayMenu,
            Icon = SystemIcons.Application,
            Text = "clici margin normalization",
            Visible = true
        };

        try
        {
            _clipboardListener = new ClipboardListenerWindow();
            _clipboardListener.ClipboardChanged += ClipboardListenerOnClipboardChanged;
            _logger.Event("started");
        }
        catch (Exception exception)
        {
            _logger.Failure(
                "clipboard-listener-registration",
                null,
                exception.GetType().Name);
            _notifyIcon.ShowBalloonTip(
                5000,
                "clici could not start",
                "Clipboard notifications could not be registered.",
                ToolTipIcon.Error);
        }
    }

    protected override void ExitThreadCore()
    {
        DisposeResources();
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposeResources();
        }

        base.Dispose(disposing);
    }

    private void EnabledMenuItemOnCheckedChanged(object? sender, EventArgs eventArgs)
    {
        _configuration = _configuration with
        {
            Enabled = _enabledMenuItem.Checked
        };
        _coordinator.UpdateConfiguration(_configuration);

        if (!_configurationStore.TrySave(_configuration))
        {
            _logger.Failure("configuration-save", null, "write-failed");
        }
    }

    private void PauseMenuItemOnCheckedChanged(object? sender, EventArgs eventArgs)
    {
        _coordinator.SetPaused(_pauseMenuItem.Checked);
        _pauseMenuItem.Text = _pauseMenuItem.Checked
            ? "Resume normalization"
            : "Pause normalization";
    }

    private void ClipboardListenerOnClipboardChanged(object? sender, EventArgs eventArgs) =>
        _coordinator.HandleClipboardChanged();

    private void OpenPath(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path)
            {
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            _logger.Failure("open-path", null, exception.GetType().Name);
        }
    }

    private void DisposeResources()
    {
        if (_resourcesDisposed)
        {
            return;
        }

        if (_clipboardListener is not null)
        {
            _clipboardListener.ClipboardChanged -= ClipboardListenerOnClipboardChanged;
            _clipboardListener.Dispose();
            _clipboardListener = null;
        }

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _trayMenu.Dispose();
        _logger.Event("stopped");
        _resourcesDisposed = true;
    }
}
