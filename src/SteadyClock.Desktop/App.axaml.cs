using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using MyTimeTracker.Core.Abstractions;
using MyTimeTracker.Core.Common;
using MyTimeTracker.Core.Models;
using MyTimeTracker.Core.Timer;
using MyTimeTracker.Data;
using SteadyClock.Desktop.Resources;
using SteadyClock.Desktop.Services;
using SteadyClock.Desktop.ViewModels;
using SteadyClock.Desktop.Views;

namespace SteadyClock.Desktop;

public partial class App : Application
{
    private readonly CancellationTokenSource _lifetime = new();
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private SingleInstanceService? _singleInstance;
    private AppPaths? _paths;
    private RotatingFileLogger? _logger;
    private TimeTrackingController? _controller;
    private MainViewModel? _viewModel;
    private MainWindow? _mainWindow;
    private TrayIcon? _trayIcon;
    private NativeMenuItem? _trayToggleItem;
    private NativeMenuItem? _trayStopItem;
    private DispatcherTimer? _pollTimer;
    private bool _isInitialized;
    private bool _isExiting;
    private bool _pendingShow;
    private bool _disposed;
    private DateTimeOffset _lastWarningAt = DateTimeOffset.MinValue;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        _desktop = desktop;
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        ConfigureCulture();

        _paths = new AppPaths();
        _paths.EnsureCreated();
        _logger = new RotatingFileLogger(_paths.LogsDirectory);
        _logger.Info("Запуск SteadyClock Desktop.");

        _singleInstance = new SingleInstanceService();
        if (!_singleInstance.TryAcquire())
        {
            try
            {
                _singleInstance.SignalExistingInstanceAsync().GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                _logger.Error("Не удалось передать запрос существующему экземпляру.", exception);
            }

            _singleInstance.Dispose();
            _singleInstance = null;
            desktop.Shutdown();
            base.OnFrameworkInitializationCompleted();
            return;
        }

        _singleInstance.ShowRequested += OnShowRequested;
        _singleInstance.StartListening();
        desktop.Exit += OnDesktopExit;

        _ = InitializeAsync(desktop);
        base.OnFrameworkInitializationCompleted();
    }

    private async Task InitializeAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        try
        {
            var settingsStore = new JsonSettingsStore(_paths!.SettingsPath, _logger!);
            var repository = new SqliteTimeSessionRepository(_paths.DatabasePath);
            var wallClock = new SystemWallClock();
            var monotonicClock = new StopwatchMonotonicClock();
            var idleProvider = new PlatformIdleTimeProvider();
            var engine = new TimerEngine(wallClock, monotonicClock, idleProvider);
            _controller = new TimeTrackingController(
                engine,
                repository,
                wallClock,
                monotonicClock,
                idleProvider,
                _logger!);

            var settings = await settingsStore.LoadAsync(_lifetime.Token);
            _controller.UpdateSettings(settings);
            await _controller.InitializeAsync(_lifetime.Token);

            var dialogs = new AvaloniaDialogService(
                _controller,
                _logger!,
                () => _mainWindow);
            _viewModel = new MainViewModel(_controller, dialogs, _logger!);
            _mainWindow = new MainWindow { DataContext = _viewModel };
            _mainWindow.Closing += OnMainWindowClosing;
            desktop.MainWindow = _mainWindow;

            await _viewModel.InitializeAsync();

            CreateTray();
            _pollTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, OnPollTick);
            _pollTimer.Start();

            _controller.TimerStateChanged += OnTimerStateChanged;
            _controller.TickCompleted += OnControllerTick;
            _controller.PersistenceFailed += OnPersistenceFailed;

            if (settings.AutoStartTimer && _controller.Status == TimerStatus.Stopped)
            {
                await _controller.StartIfStoppedAsync(_lifetime.Token);
            }

            _isInitialized = true;
            var launchedAtLogin = desktop.Args?.Any(argument =>
                string.Equals(argument, "--autostart", StringComparison.OrdinalIgnoreCase)) == true;
            var stayInTray = launchedAtLogin &&
                             settings.LaunchInTrayOnAutoStart &&
                             _trayIcon is not null;

            if (_pendingShow || !stayInTray)
            {
                ShowMainWindow();
            }
            else
            {
                _logger?.Info("SteadyClock запущен в трее.");
            }

            UpdateTray();
        }
        catch (Exception exception)
        {
            _logger?.Error("Критическая ошибка запуска кроссплатформенной версии.", exception);
            ShowStartupError(desktop, Strings.Get("CouldNotStart"));
        }
    }

    private void ConfigureCulture()
    {
        var culture = CultureInfo.GetCultureInfo("ru-RU");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    private void CreateTray()
    {
        try
        {
            using var iconStream = AssetLoader.Open(
                new Uri("avares://SteadyClock/Assets/app.png"));
            var menu = new NativeMenu();
            var showItem = new NativeMenuItem(Strings.Get("TrayShow"));
            showItem.Click += (_, _) => ShowMainWindow();

            _trayToggleItem = new NativeMenuItem(Strings.Get("StartTimerFromTray"));
            _trayToggleItem.Click += async (_, _) => await ToggleFromTrayAsync();

            _trayStopItem = new NativeMenuItem(Strings.Get("StopTimerFromTray"));
            _trayStopItem.Click += async (_, _) => await StopFromTrayAsync();

            var exitItem = new NativeMenuItem(Strings.Get("TrayExit"));
            exitItem.Click += async (_, _) => await ExitAsync(stopActiveSession: true);

            menu.Add(showItem);
            menu.Add(new NativeMenuItemSeparator());
            menu.Add(_trayToggleItem);
            menu.Add(_trayStopItem);
            menu.Add(new NativeMenuItemSeparator());
            menu.Add(exitItem);

            _trayIcon = new TrayIcon
            {
                Icon = new WindowIcon(iconStream),
                ToolTipText = Strings.Get("AppTitle"),
                Menu = menu,
                IsVisible = true
            };
            _trayIcon.Clicked += (_, _) => ShowMainWindow();
            TrayIcon.SetIcons(this, new TrayIcons { _trayIcon });
        }
        catch (Exception exception)
        {
            _logger?.Warn($"Системный трей недоступен: {exception.Message}");
            _trayIcon = null;
        }
    }

    private void OnPollTick(object? sender, EventArgs e)
    {
        if (!_isInitialized || _isExiting || _controller is null)
        {
            return;
        }

        _ = TickAsync();
    }

    private async Task TickAsync()
    {
        try
        {
            await _controller!.TickAsync(_lifetime.Token);
            UpdateTray();
        }
        catch (OperationCanceledException)
        {
            // Shutdown.
        }
        catch (Exception exception)
        {
            _logger?.Error("Ошибка опроса таймера.", exception);
        }
    }

    private void OnShowRequested(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_isInitialized)
            {
                ShowMainWindow();
            }
            else
            {
                _pendingShow = true;
            }
        });
    }

    private void ShowMainWindow()
    {
        if (_mainWindow is null)
        {
            _pendingShow = true;
            return;
        }

        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }

        _mainWindow.Show();
        _mainWindow.Activate();
        _pendingShow = false;
    }

    private async Task ToggleFromTrayAsync()
    {
        if (_isExiting || _controller is null)
        {
            return;
        }

        try
        {
            await _controller.ToggleAsync(_lifetime.Token);
            UpdateTray();
        }
        catch (Exception exception)
        {
            _logger?.Error("Не удалось переключить таймер из трея.", exception);
        }
    }

    private async Task StopFromTrayAsync()
    {
        if (_isExiting || _controller is null)
        {
            return;
        }

        try
        {
            await _controller.StopAsync(_lifetime.Token);
            UpdateTray();
        }
        catch (Exception exception)
        {
            _logger?.Error("Не удалось остановить таймер из трея.", exception);
        }
    }

    private void OnMainWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_mainWindow?.AllowClose == true || _isExiting)
        {
            return;
        }

        if (_trayIcon is not null)
        {
            e.Cancel = true;
            _mainWindow?.Hide();
            _logger?.Info("Окно свёрнуто в трей.");
            return;
        }

        e.Cancel = true;
        _ = ExitAsync(stopActiveSession: true);
    }

    private async Task ExitAsync(bool stopActiveSession)
    {
        if (_isExiting)
        {
            return;
        }

        _isExiting = true;
        _pollTimer?.Stop();

        if (stopActiveSession && _controller?.HasActiveSession == true)
        {
            try
            {
                await _controller.StopAsync(_lifetime.Token);
            }
            catch (Exception exception)
            {
                _logger?.Error("Не удалось сохранить сессию перед выходом.", exception);
            }
        }

        _viewModel?.Dispose();
        if (_mainWindow is not null)
        {
            _mainWindow.AllowClose = true;
            _mainWindow.Close();
        }

        DisposeTray();
        _desktop?.Shutdown();
    }

    private void OnTimerStateChanged(object? sender, TimerStatusChangedEventArgs e) => UpdateTray();

    private void OnControllerTick(object? sender, EventArgs e) => UpdateTray();

    private void OnPersistenceFailed(object? sender, EventArgs e)
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastWarningAt < TimeSpan.FromMinutes(5))
        {
            return;
        }

        _lastWarningAt = now;
        _logger?.Warn("Не удалось сохранить активную сессию; повтор будет выполнен на следующем heartbeat.");
    }

    private void UpdateTray()
    {
        if (_trayIcon is null || _controller is null)
        {
            return;
        }

        var status = _controller.Status;
        _trayToggleItem!.Header = status switch
        {
            TimerStatus.Running => Strings.Get("PauseTimerFromTray"),
            TimerStatus.PausedManual or TimerStatus.PausedIdle => Strings.Get("ResumeTimerFromTray"),
            _ => Strings.Get("StartTimerFromTray")
        };
        _trayStopItem!.IsEnabled = status != TimerStatus.Stopped;

        var time = TimeText.FormatClock(_controller.CurrentDuration);
        _trayIcon.ToolTipText = status switch
        {
            TimerStatus.Running => Strings.Format("TrayRunningTooltipFormat", time),
            TimerStatus.PausedManual => Strings.Format("TrayPausedManualTooltipFormat", time),
            TimerStatus.PausedIdle => Strings.Format("TrayPausedIdleTooltipFormat", time),
            _ => Strings.Get("TrayStoppedTooltip")
        };
    }

    private void DisposeTray()
    {
        try
        {
            if (_trayIcon is not null)
            {
                TrayIcon.SetIcons(this, new TrayIcons());
                _trayIcon.IsVisible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }
        }
        catch (Exception exception)
        {
            _logger?.Warn($"Не удалось удалить значок трея: {exception.Message}");
        }
    }

    private void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _pollTimer?.Stop();
        _pollTimer = null;
        _lifetime.Cancel();
        DisposeTray();

        try
        {
            _viewModel?.Dispose();
        }
        catch
        {
            // Ignore shutdown races.
        }

        try
        {
            _controller?.Dispose();
        }
        catch
        {
            // Ignore shutdown races.
        }

        try
        {
            _singleInstance?.Dispose();
        }
        catch
        {
            // Ignore shutdown races.
        }

        _singleInstance = null;
        _logger?.Info("Завершение SteadyClock Desktop.");
        _logger?.Dispose();
        _logger = null;
        _lifetime.Dispose();
    }

    private static void ShowStartupError(IClassicDesktopStyleApplicationLifetime desktop, string message)
    {
        var window = new Window
        {
            Title = Strings.Get("AppTitle"),
            Width = 520,
            Height = 220,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = new TextBlock
            {
                Text = message,
                Margin = new Thickness(24),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 14
            }
        };
        window.Closed += (_, _) => desktop.Shutdown();
        desktop.MainWindow = window;
        window.Show();
    }
}

