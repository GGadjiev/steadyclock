using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Threading;
using MyTimeTracker.App.Resources;
using MyTimeTracker.App.Services;
using MyTimeTracker.App.ViewModels;
using MyTimeTracker.Core.Abstractions;
using MyTimeTracker.Core.Models;
using MyTimeTracker.Core.Timer;
using MyTimeTracker.Data;

namespace MyTimeTracker.App;

public partial class App : Application
{
    private readonly CancellationTokenSource _lifetime = new();
    private AppPaths? _paths;
    private RotatingFileLogger? _logger;
    private SingleInstanceService? _singleInstance;
    private TimeTrackingController? _controller;
    private SettingsCoordinator? _settings;
    private HiddenMessageWindow? _hiddenWindow;
    private GlobalHotkeyService? _hotkeys;
    private TrayIconService? _tray;
    private NotificationService? _notifications;
    private IAppDialogService? _dialogs;
    private MainViewModel? _viewModel;
    private DispatcherTimer? _pollTimer;
    private bool _initialized;
    private bool _pendingShow;
    private bool _isExiting;
    private bool _exitInProgress;
    private bool _cleanupDone;
    private DateTimeOffset _lastPersistenceWarningUtc = DateTimeOffset.MinValue;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ConfigureCulture();
        RegisterGlobalExceptionHandlers();

        try
        {
            _paths = new AppPaths();
            _paths.EnsureCreated();
            _logger = new RotatingFileLogger(_paths.LogsDirectory);
            _logger.Info("Запуск приложения.");

            _singleInstance = new SingleInstanceService();
            if (!_singleInstance.TryAcquire())
            {
                await _singleInstance.SignalExistingInstanceAsync();
                _singleInstance.Dispose();
                _singleInstance = null;
                Shutdown();
                return;
            }

            _singleInstance.ShowRequested += OnExternalShowRequested;
            _singleInstance.StartListening();
            await InitializeAsync(e.Args);
        }
        catch (Exception exception)
        {
            _logger?.Error("Критическая ошибка запуска.", exception);
            MessageBox.Show(
                Strings.Get("CouldNotStart"),
                Strings.Get("AppTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
        }
    }

    private async Task InitializeAsync(string[] args)
    {
        var wallClock = new SystemWallClock();
        var monotonicClock = new StopwatchMonotonicClock();
        var idleProvider = new WindowsIdleTimeProvider();
        var repository = new SqliteTimeSessionRepository(_paths!.DatabasePath);
        var engine = new TimerEngine(wallClock, monotonicClock, idleProvider);
        _controller = new TimeTrackingController(
            engine,
            repository,
            wallClock,
            monotonicClock,
            idleProvider,
            _logger!);

        _hiddenWindow = new HiddenMessageWindow();
        _hotkeys = new GlobalHotkeyService(_hiddenWindow, _logger!);
        var autostart = new AutostartService();
        _settings = new SettingsCoordinator(
            new JsonSettingsStore(_paths.SettingsPath, _logger!),
            _controller,
            _hotkeys,
            autostart,
            _logger!);

        _hotkeys.ToggleRequested += (_, _) =>
            BeginControllerAction(token => _controller!.ToggleAsync(token));
        _hotkeys.StopRequested += (_, _) =>
            BeginControllerAction(token => _controller!.StopAsync(token));
        _hotkeys.ShowHideRequested += (_, _) => ToggleMainWindow();

        await _settings.InitializeAsync();
        await _controller.InitializeAsync();

        _notifications = new NotificationService(_logger!);
        _dialogs = new DialogService(_controller, _settings, () => MainWindow);
        _viewModel = new MainViewModel(_controller, _dialogs, _logger!);
        var mainWindow = new MyTimeTracker.App.MainWindow(_viewModel);
        MainWindow = mainWindow;
        mainWindow.CloseRequested += OnMainWindowCloseRequested;
        await _viewModel.InitializeAsync();

        _tray = new TrayIconService();
        _tray.ShowRequested += (_, _) => ShowMainWindow();
        _tray.ToggleRequested += (_, _) =>
            BeginControllerAction(token => _controller!.ToggleAsync(token));
        _tray.StopRequested += (_, _) =>
            BeginControllerAction(token => _controller!.StopAsync(token));
        _tray.SettingsRequested += (_, _) => BeginShowSettings();
        _tray.ExitRequested += (_, _) => BeginExitRequest();

        _controller.TimerStateChanged += OnTimerStateChanged;
        _controller.TickCompleted += (_, _) => UpdateTrayState();
        _controller.AutoPaused += OnAutoPaused;
        _controller.AutoResumed += (_, _) => _notifications?.CloseTag("autopause");
        _controller.ReminderDue += OnReminderDue;
        _controller.PersistenceFailed += OnPersistenceFailed;
        _settings.SettingsChanged += OnSettingsChanged;

        _hiddenWindow.SessionLocked +=
            (_, _) => BeginControllerAction(token => _controller!.HandleSessionLockAsync(token));
        _hiddenWindow.SessionUnlocked +=
            (_, _) => BeginControllerAction(token => _controller!.HandleSessionUnlockAsync(token));
        _hiddenWindow.SystemSuspending +=
            (_, _) => BeginControllerAction(token => _controller!.HandleSystemSuspendAsync(token));
        _hiddenWindow.SystemResumed +=
            (_, _) => BeginControllerAction(token => _controller!.OnSystemResumedAsync(token));
        _hiddenWindow.SystemTimeChanged += (_, _) =>
        {
            try
            {
                TimeZoneInfo.ClearCachedData();
            }
            catch
            {
                // Non-fatal; the refresh below still re-reads the current values.
            }

            if (_viewModel is not null)
            {
                _ = _viewModel.HandleSystemTimeChangedAsync();
            }
        };

        SessionEnding += OnSessionEnding;
        _pollTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _pollTimer.Tick += OnPollTimerTick;

        if (_settings.Current.AutoStartTimer && _controller.Status == TimerStatus.Stopped)
        {
            await _controller.StartIfStoppedAsync();
        }

        _pollTimer.Start();
        UpdateTrayState();

        var launchedAtLogin = args.Any(argument =>
            string.Equals(argument, "--autostart", StringComparison.OrdinalIgnoreCase));
        var startHidden = launchedAtLogin && _settings.Current.LaunchInTrayOnAutoStart;
        _initialized = true;

        if (_pendingShow || !startHidden)
        {
            ShowMainWindow();
        }

        if (!_settings.StartupHotkeyResult.Success)
        {
            _notifications.ShowWarning(
                Strings.Get("SettingsSectionHotkeys"),
                Strings.Get("HotkeyRegistrationFailed"));
        }

        _logger?.Info($"Приложение готово. Запуск в трее: {startHidden}.");
    }

    private void OnPollTimerTick(object? sender, EventArgs e)
    {
        if (_isExiting || _controller is null)
        {
            return;
        }

        _ = PollAsync();
    }

    private async Task PollAsync()
    {
        try
        {
            await _controller!.TickAsync(_lifetime.Token);
        }
        catch (OperationCanceledException)
        {
            // Shutdown.
        }
        catch (Exception exception)
        {
            _logger?.Error("Ошибка опроса таймера и простоя.", exception);
        }
    }

    private void BeginControllerAction(Func<CancellationToken, Task> action)
    {
        if (_isExiting || _controller is null)
        {
            return;
        }

        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => BeginControllerAction(action)));
            return;
        }

        _ = ExecuteControllerActionAsync(action);
    }

    private async Task ExecuteControllerActionAsync(Func<CancellationToken, Task> action)
    {
        try
        {
            await action(_lifetime.Token);
            UpdateTrayState();
        }
        catch (OperationCanceledException)
        {
            // Shutdown.
        }
        catch (Exception exception)
        {
            _logger?.Error("Ошибка действия таймера.", exception);
        }
    }

    private void OnTimerStateChanged(object? sender, TimerStatusChangedEventArgs e)
    {
        if (_isExiting)
        {
            return;
        }

        UpdateTrayState();
        if (e.Status == TimerStatus.Running)
        {
            _notifications?.CloseTag("reminder");
        }
    }

    private void OnAutoPaused(object? sender, AutoPausedEventArgs e)
    {
        if (!_isExiting)
        {
            _notifications?.ShowAutoPause(e.Cause);
        }
    }

    private void OnReminderDue(object? sender, EventArgs e)
    {
        if (_isExiting || _settings is null)
        {
            return;
        }

        _notifications?.ShowForgotTimer(
            _settings.Current.ForgotTimerAfterMinutes,
            StartTimerFromNotification);
    }

    private void OnPersistenceFailed(object? sender, EventArgs e)
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastPersistenceWarningUtc < TimeSpan.FromMinutes(5))
        {
            return;
        }

        _lastPersistenceWarningUtc = now;
        _notifications?.ShowDataWarning();
    }

    private void StartTimerFromNotification()
    {
        BeginControllerAction(token => _controller!.StartIfStoppedAsync(token));
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        if (_settings is null || _notifications is null)
        {
            return;
        }

        if (!_settings.Current.RemindersEnabled || !_settings.Current.ForgotTimerReminderEnabled)
        {
            _notifications.CloseTag("reminder");
        }
    }

    private void OnExternalShowRequested(object? sender, EventArgs e)
    {
        if (!_initialized)
        {
            _pendingShow = true;
            return;
        }

        ShowMainWindow();
    }

    private void UpdateTrayState()
    {
        if (_tray is not null && _controller is not null)
        {
            _tray.UpdateState(_controller.Status, _controller.CurrentDuration);
        }
    }

    private void ShowMainWindow()
    {
        if (MainWindow is not MyTimeTracker.App.MainWindow window)
        {
            _pendingShow = true;
            return;
        }

        if (!_initialized)
        {
            _pendingShow = true;
            return;
        }

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Show();
        window.Activate();
        window.Focus();
        var handle = new WindowInteropHelper(window).Handle;
        if (handle != IntPtr.Zero)
        {
            SetForegroundWindow(handle);
        }
    }

    private void HideMainWindow()
    {
        MainWindow?.Hide();
    }

    private void ToggleMainWindow()
    {
        if (MainWindow is { IsVisible: true })
        {
            HideMainWindow();
        }
        else
        {
            ShowMainWindow();
        }
    }

    private void BeginShowSettings()
    {
        if (_isExiting)
        {
            return;
        }

        _ = ShowSettingsAsync();
    }

    private async Task ShowSettingsAsync()
    {
        try
        {
            ShowMainWindow();
            if (_dialogs is not null)
            {
                await _dialogs.ShowSettingsAsync();
            }
        }
        catch (Exception exception)
        {
            _logger?.Error("Не удалось открыть настройки.", exception);
        }
    }

    private async void OnMainWindowCloseRequested(object? sender, EventArgs e)
    {
        if (_isExiting)
        {
            return;
        }

        if (_settings?.Current.CloseToTray == true)
        {
            HideMainWindow();
            return;
        }

        await RequestExitAsync();
    }

    private void BeginExitRequest()
    {
        if (_isExiting || _exitInProgress)
        {
            return;
        }

        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(BeginExitRequest));
            return;
        }

        _ = RequestExitAsync();
    }

    private async Task RequestExitAsync()
    {
        if (_isExiting || _exitInProgress)
        {
            return;
        }

        _exitInProgress = true;
        try
        {
            if (_controller?.HasActiveSession == true)
            {
                var choice = _dialogs is null
                    ? ExitChoice.Cancel
                    : await _dialogs.ShowExitChoiceAsync();

                switch (choice)
                {
                    case ExitChoice.StopAndExit:
                        await _controller.StopAsync(_lifetime.Token);
                        break;
                    case ExitChoice.MinimizeToTray:
                        HideMainWindow();
                        return;
                    default:
                        return;
                }
            }

            ExitApplication();
        }
        catch (OperationCanceledException)
        {
            // Shutdown.
        }
        catch (Exception exception)
        {
            _logger?.Error("Не удалось завершить приложение.", exception);
            _dialogs?.ShowError(Strings.Get("UnknownError"));
        }
        finally
        {
            _exitInProgress = false;
        }
    }

    private void ExitApplication()
    {
        if (_isExiting)
        {
            return;
        }

        _isExiting = true;
        _pollTimer?.Stop();
        _lifetime.Cancel();
        _notifications?.CloseAll();

        if (MainWindow is MyTimeTracker.App.MainWindow window)
        {
            window.AllowClose = true;
            window.Close();
        }

        Shutdown();
    }

    private void OnSessionEnding(object sender, SessionEndingCancelEventArgs e)
    {
        _isExiting = true;
        _pollTimer?.Stop();
        if (MainWindow is MyTimeTracker.App.MainWindow window)
        {
            window.AllowClose = true;
        }

        _logger?.Info("Завершение сеанса Windows.");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Cleanup();
        base.OnExit(e);
    }

    private void Cleanup()
    {
        if (_cleanupDone)
        {
            return;
        }

        _cleanupDone = true;
        _pollTimer?.Stop();
        _pollTimer = null;
        try
        {
            _lifetime.Cancel();
        }
        catch
        {
            // Ignore.
        }

        try
        {
            _hotkeys?.Dispose();
        }
        catch (Exception exception)
        {
            _logger?.Error("Ошибка отключения горячих клавиш.", exception);
        }

        try
        {
            _tray?.Dispose();
        }
        catch (Exception exception)
        {
            _logger?.Error("Ошибка удаления значка трея.", exception);
        }

        try
        {
            _notifications?.Dispose();
        }
        catch (Exception exception)
        {
            _logger?.Error("Ошибка закрытия уведомлений.", exception);
        }

        try
        {
            _viewModel?.Dispose();
        }
        catch (Exception exception)
        {
            _logger?.Error("Ошибка освобождения главного окна.", exception);
        }

        try
        {
            _controller?.Dispose();
        }
        catch (Exception exception)
        {
            _logger?.Error("Ошибка освобождения контроллера.", exception);
        }

        try
        {
            _hiddenWindow?.Dispose();
        }
        catch (Exception exception)
        {
            _logger?.Error("Ошибка скрытого окна сообщений.", exception);
        }

        try
        {
            _singleInstance?.Dispose();
        }
        catch (Exception exception)
        {
            _logger?.Error("Ошибка освобождения single-instance.", exception);
        }

        try
        {
            _logger?.Info("Завершение приложения.");
        }
        catch
        {
            // Ignore.
        }

        _singleInstance = null;
        _controller = null;
        _notifications = null;
        _tray = null;
        _hotkeys = null;
        _hiddenWindow = null;
        _viewModel = null;
        _dialogs = null;
        _settings = null;
        _paths = null;
        _logger?.Dispose();
        _logger = null;
        try
        {
            _lifetime.Dispose();
        }
        catch
        {
            // Ignore.
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private static void ConfigureCulture()
    {
        var culture = CultureInfo.GetCultureInfo("ru-RU");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(culture.IetfLanguageTag)));
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            _logger?.Error("Необработанная ошибка UI.", args.Exception);
            _dialogs?.ShowError(Strings.Get("UnknownError"));
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                _logger?.Error("Необработанное исключение.", exception);
            }
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            _logger?.Error("Необработанная ошибка фоновой задачи.", args.Exception);
            args.SetObserved();
        };
    }
}
