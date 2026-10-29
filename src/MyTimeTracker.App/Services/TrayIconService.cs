using System.Drawing;
using MyTimeTracker.App.Resources;
using MyTimeTracker.Core.Common;
using MyTimeTracker.Core.Models;
using Forms = System.Windows.Forms;

namespace MyTimeTracker.App.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ToolStripMenuItem _showItem;
    private readonly Forms.ToolStripMenuItem _actionItem;
    private readonly Forms.ToolStripMenuItem _stopItem;
    private readonly Forms.ToolStripMenuItem _settingsItem;
    private readonly Forms.ToolStripMenuItem _exitItem;
    private readonly Dictionary<TimerStatus, Icon> _icons;
    private readonly Forms.ContextMenuStrip _menu;
    private string _lastTooltip = string.Empty;
    private TimerStatus? _lastStatus;

    public TrayIconService()
    {
        _icons = TrayIconFactory.CreateIcons();
        _menu = new Forms.ContextMenuStrip();
        _showItem = new Forms.ToolStripMenuItem(Strings.Get("TrayShow"));
        _actionItem = new Forms.ToolStripMenuItem(Strings.Get("StartTimerFromTray"));
        _stopItem = new Forms.ToolStripMenuItem(Strings.Get("StopTimerFromTray"));
        _settingsItem = new Forms.ToolStripMenuItem(Strings.Get("TraySettings"));
        _exitItem = new Forms.ToolStripMenuItem(Strings.Get("TrayExit"));

        _showItem.Click += (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty);
        _actionItem.Click += (_, _) => ToggleRequested?.Invoke(this, EventArgs.Empty);
        _stopItem.Click += (_, _) => StopRequested?.Invoke(this, EventArgs.Empty);
        _settingsItem.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
        _exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        _menu.Items.Add(_showItem);
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add(_actionItem);
        _menu.Items.Add(_stopItem);
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add(_settingsItem);
        _menu.Items.Add(_exitItem);

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _icons[TimerStatus.Stopped],
            ContextMenuStrip = _menu,
            Text = Strings.Get("TrayStoppedTooltip"),
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty);
        _notifyIcon.MouseClick += (_, args) =>
        {
            if (args.Button == Forms.MouseButtons.Left)
            {
                ShowRequested?.Invoke(this, EventArgs.Empty);
            }
        };

        UpdateState(TimerStatus.Stopped, TimeSpan.Zero);
    }

    public event EventHandler? ShowRequested;
    public event EventHandler? ToggleRequested;
    public event EventHandler? StopRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? ExitRequested;

    public void UpdateState(TimerStatus status, TimeSpan duration)
    {
        if (_lastStatus != status)
        {
            _notifyIcon.Icon = _icons[status];
            _actionItem.Text = status switch
            {
                TimerStatus.Running => Strings.Get("PauseTimerFromTray"),
                TimerStatus.PausedManual or TimerStatus.PausedIdle => Strings.Get("ResumeTimerFromTray"),
                _ => Strings.Get("StartTimerFromTray")
            };
            _stopItem.Enabled = status != TimerStatus.Stopped;
            _lastStatus = status;
        }

        var tooltip = status switch
        {
            TimerStatus.Running => Strings.Format("TrayRunningTooltipFormat", TimeText.FormatClock(duration)),
            TimerStatus.PausedManual => Strings.Format("TrayPausedManualTooltipFormat", TimeText.FormatClock(duration)),
            TimerStatus.PausedIdle => Strings.Format("TrayPausedIdleTooltipFormat", TimeText.FormatClock(duration)),
            _ => Strings.Get("TrayStoppedTooltip")
        };

        if (tooltip != _lastTooltip)
        {
            _notifyIcon.Text = tooltip.Length > 63 ? tooltip[..63] : tooltip;
            _lastTooltip = tooltip;
        }
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        foreach (var icon in _icons.Values)
        {
            icon.Dispose();
        }
    }
}
