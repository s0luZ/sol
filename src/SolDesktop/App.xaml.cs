using System;
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

    private Mutex? _singleInstanceMutex;
    private bool _ownsMutex;

    private readonly SettingsService _settings = new();
    private NetworkStatusService? _network;
    private BluetoothStatusService? _bluetooth;
    private AudioStatusService? _audio;
    private BatteryStatusService? _battery;
    private NotchViewModel? _notchViewModel;
    private NotchWindow? _notchWindow;
    private TrayIconService? _tray;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out _ownsMutex);
        if (!_ownsMutex)
        {
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex) Log.Error(ex);
        };

        _settings.Load();

        _network = new NetworkStatusService(Dispatcher);
        _bluetooth = new BluetoothStatusService(Dispatcher);
        _audio = new AudioStatusService(Dispatcher);
        _battery = new BatteryStatusService(Dispatcher);

        _notchViewModel = new NotchViewModel(Dispatcher, _network, _bluetooth, _audio, _battery);
        _notchWindow = new NotchWindow { DataContext = _notchViewModel };
        if (_settings.Current.NotchEnabled)
        {
            _notchWindow.Show();
        }

        _tray = new TrayIconService(_settings.Current.NotchEnabled, StartupService.IsEnabled);
        _tray.NotchToggled += OnNotchToggled;
        _tray.StartWithWindowsToggled += OnStartWithWindowsToggled;
        _tray.QuitRequested += (_, _) => Shutdown();

        await _bluetooth.InitializeAsync();
    }

    private void OnNotchToggled(object? sender, bool enabled)
    {
        _settings.Current.NotchEnabled = enabled;
        _settings.Save();

        if (enabled) _notchWindow?.Show();
        else _notchWindow?.Hide();
    }

    private void OnStartWithWindowsToggled(object? sender, bool enabled)
    {
        try
        {
            StartupService.SetEnabled(enabled);
        }
        catch (Exception ex)
        {
            Log.Error(ex);
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
        _tray?.Dispose();
        _notchWindow?.Close();
        _notchViewModel?.Dispose();
        _network?.Dispose();
        _bluetooth?.Dispose();
        _audio?.Dispose();
        _battery?.Dispose();

        if (_ownsMutex) _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();

        base.OnExit(e);
    }
}
