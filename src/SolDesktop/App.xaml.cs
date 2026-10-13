using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using SolDesktop.Services;
using SolDesktop.Services.Status;
using SolDesktop.ViewModels;
using SolDesktop.Views;

namespace SolDesktop;

public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\SolDesktop.SingleInstance";
    private const string ShowSettingsEventName = @"Local\SolDesktop.ShowSettings";
    private const string QuitEventName = @"Local\SolDesktop.Quit";
    private const string QuitArgument = "--quit";

    private Mutex? _singleInstanceMutex;
    private bool _ownsMutex;
    private EventWaitHandle? _showSettingsEvent;
    private RegisteredWaitHandle? _showSettingsWait;
    private EventWaitHandle? _quitEvent;
    private RegisteredWaitHandle? _quitWait;

    private readonly SettingsService _settings = new();
    private DispatcherTimer? _saveTimer;

    private NetworkStatusService? _network;
    private BluetoothStatusService? _bluetooth;
    private AudioStatusService? _audio;
    private BatteryStatusService? _battery;
    private WifiService? _wifi;
    private BrightnessService? _brightness;
    private MediaService? _media;
    private WindowTracker? _tracker;
    private IconService? _icons;
    private TaskbarService? _taskbar;

    private NotchViewModel? _notchViewModel;
    private NotchWindow? _notchWindow;
    private ControlCentreViewModel? _controlCentreViewModel;
    private ControlCentreWindow? _controlCentre;
    private DockViewModel? _dockViewModel;
    private DockWindow? _dockWindow;
    private SettingsWindow? _settingsWindow;
    private SettingsViewModel? _settingsViewModel;
    private TrayIconService? _tray;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        bool background = e.Args.Any(a => a.Equals(StartupService.BackgroundArgument, StringComparison.OrdinalIgnoreCase));
        bool quit = e.Args.Any(a => a.Equals(QuitArgument, StringComparison.OrdinalIgnoreCase));

        _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out _ownsMutex);
        if (!_ownsMutex || quit)
        {
            // Already running: forward the request (show the window, or quit cleanly) to that instance.
            string eventName = quit ? QuitEventName : ShowSettingsEventName;
            if (!background && EventWaitHandle.TryOpenExisting(eventName, out EventWaitHandle? existing))
            {
                existing.Set();
                existing.Dispose();
            }

            Shutdown();
            return;
        }

        _showSettingsEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSettingsEventName);
        _showSettingsWait = ThreadPool.RegisterWaitForSingleObject(
            _showSettingsEvent, (_, _) => Dispatcher.InvokeAsync(ShowSettings), null, Timeout.Infinite, false);
        _quitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, QuitEventName);
        _quitWait = ThreadPool.RegisterWaitForSingleObject(
            _quitEvent, (_, _) => Dispatcher.InvokeAsync(() => Shutdown()), null, Timeout.Infinite, true);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex) Log.Error(ex);
            _taskbar?.Restore();
        };

        _settings.Load();
        ThemeService.Apply(_settings.Current);

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            _settings.Save();
        };

        _network = new NetworkStatusService(Dispatcher);
        _bluetooth = new BluetoothStatusService(Dispatcher);
        _audio = new AudioStatusService(Dispatcher);
        _battery = new BatteryStatusService(Dispatcher);
        _wifi = new WifiService(Dispatcher);
        _brightness = new BrightnessService();
        _media = new MediaService(Dispatcher);
        _tracker = new WindowTracker(Dispatcher);
        _icons = new IconService();
        _taskbar = new TaskbarService(_settings);

        _notchViewModel = new NotchViewModel(Dispatcher, _network, _bluetooth, _audio, _battery);
        _notchWindow = new NotchWindow { DataContext = _notchViewModel };

        _controlCentreViewModel = new ControlCentreViewModel(Dispatcher, _network, _wifi, _bluetooth, _audio, _brightness, _media);
        _controlCentre = new ControlCentreWindow(_controlCentreViewModel);
        _notchWindow.PullStarted += (_, _) =>
        {
            _notchWindow.Controller.KeepOpen = true;
            _controlCentre.BeginPull();
        };
        _notchWindow.PullMoved += (_, distance) => _controlCentre.UpdatePull(distance);
        _notchWindow.PullEnded += (_, _) => _controlCentre.EndPull();
        _notchWindow.Clicked += (_, _) =>
        {
            _notchWindow.Controller.KeepOpen = true;
            _controlCentre.Toggle();
        };
        _controlCentre.PanelClosed += (_, _) => _notchWindow.Controller.KeepOpen = false;

        _dockViewModel = new DockViewModel(_tracker, _settings, _icons);
        _dockWindow = new DockWindow(_dockViewModel);

        _tray = new TrayIconService();
        _tray.SettingsRequested += (_, _) => ShowSettings();
        _tray.NotchToggled += (_, enabled) => ModifySettings(s => s.NotchEnabled = enabled);
        _tray.DockToggled += (_, enabled) => ModifySettings(s => s.DockEnabled = enabled);
        _tray.QuitRequested += (_, _) => Shutdown();

        _tracker.Changed += OnTrackerChanged;
        ApplySettings();

        if (StartupService.IsEnabled)
        {
            // Re-register so the entry follows the exe if it was moved, and carries --background.
            try
            {
                StartupService.SetEnabled(true);
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
        }

        if (!background) ShowSettings();

        await _bluetooth.InitializeAsync();
        await _wifi.InitializeAsync();
        await _media.InitializeAsync();
    }

    public void ShowSettings()
    {
        if (_settingsWindow is null)
        {
            _settingsViewModel = new SettingsViewModel(_settings, OnSettingsChanged);
            _settingsWindow = new SettingsWindow { DataContext = _settingsViewModel };
            _settingsWindow.Closed += (_, _) =>
            {
                _settingsWindow = null;
                _settingsViewModel = null;
            };
            _settingsWindow.Show();
        }
        else if (_settingsWindow.WindowState == WindowState.Minimized)
        {
            _settingsWindow.WindowState = WindowState.Normal;
        }

        _settingsWindow.Activate();
    }

    /// <summary>For changes made outside the settings window (tray, notch/dock menus).</summary>
    public void ModifySettings(Action<AppSettings> change)
    {
        change(_settings.Current);
        OnSettingsChanged();
        _settingsViewModel?.Refresh();
    }

    private void OnSettingsChanged()
    {
        ApplySettings();
        _saveTimer?.Stop();
        _saveTimer?.Start();
    }

    private void ApplySettings()
    {
        AppSettings s = _settings.Current;
        ThemeService.Apply(s);

        if (_notchWindow is not null)
        {
            _notchWindow.Controller.Mode = s.NotchMode;
            if (!s.NotchEnabled)
            {
                _notchWindow.Hide();
                _controlCentre?.ClosePanel();
            }
            else if (!_notchWindow.IsVisible)
            {
                _notchWindow.Show();
            }
        }

        if (_dockWindow is not null)
        {
            _dockWindow.Controller.Mode = s.DockMode;
            _dockWindow.ApplyLayout();
            if (!s.DockEnabled) _dockWindow.Hide();
            else if (!_dockWindow.IsVisible) _dockWindow.Show();
        }

        _taskbar?.Apply(s.DockEnabled ? s.TaskbarMode : TaskbarMode.Leave, s.CenterStartMenu);
        _tray?.Sync(s.NotchEnabled, s.DockEnabled);
        UpdateBarEnvironment();
    }

    private void OnTrackerChanged(object? sender, EventArgs e)
    {
        UpdateBarEnvironment();
        _taskbar?.Enforce();
    }

    private void UpdateBarEnvironment()
    {
        if (_tracker is null) return;
        bool fullscreen = _tracker.IsForegroundFullscreen;

        if (_notchWindow is { IsVisible: true })
        {
            _notchWindow.Controller.SetEnvironment(_tracker.AnyWindowIntersects(_notchWindow.GetBarZone()), fullscreen);
        }

        if (_dockWindow is { IsVisible: true })
        {
            _dockWindow.Controller.SetEnvironment(_tracker.AnyWindowIntersects(_dockWindow.GetBarZone()), fullscreen);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // A background shell app should survive a single bad event rather than vanish.
        Log.Error(e.Exception);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _showSettingsWait?.Unregister(null);
        _showSettingsEvent?.Dispose();
        _quitWait?.Unregister(null);
        _quitEvent?.Dispose();

        _taskbar?.Restore();
        if (_saveTimer?.IsEnabled == true) _settings.Save();

        _tray?.Dispose();
        _settingsWindow?.Close();
        _notchWindow?.Close();
        _controlCentre?.Close();
        _dockWindow?.Close();
        _notchViewModel?.Dispose();
        _controlCentreViewModel?.Dispose();
        _dockViewModel?.Dispose();
        _tracker?.Dispose();
        _icons?.Dispose();
        _network?.Dispose();
        _bluetooth?.Dispose();
        _audio?.Dispose();
        _battery?.Dispose();
        _wifi?.Dispose();
        _brightness?.Dispose();
        _media?.Dispose();

        if (_ownsMutex) _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();

        base.OnExit(e);
    }
}
