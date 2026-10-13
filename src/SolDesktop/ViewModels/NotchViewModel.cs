using System;
using System.Globalization;
using System.Windows.Threading;
using SolDesktop.Services.Status;

namespace SolDesktop.ViewModels;

public sealed class NotchViewModel : ObservableObject, IDisposable
{
    private const double BatteryFillMaxWidth = 15;
    private const int NetworkRefreshSeconds = 10;

    private readonly NetworkStatusService _network;
    private readonly BluetoothStatusService _bluetooth;
    private readonly AudioStatusService _audio;
    private readonly BatteryStatusService _battery;
    private readonly DispatcherTimer _clock;
    private int _ticks;

    private string _timeText = "";
    private string _dateText = "";
    private bool _isEthernet;
    private bool _isWifiIconVisible = true;
    private int _wifiBars;
    private double _networkOpacity = 1;
    private string _networkTooltip = "";
    private bool _isBluetoothAvailable;
    private double _bluetoothOpacity = 1;
    private string _bluetoothTooltip = "";
    private string _volumeGlyph = "\uE995";
    private string _volumeTooltip = "";
    private bool _hasBattery;
    private int _batteryPercent;
    private double _batteryFillWidth;
    private bool _isCharging;
    private bool _isBatteryLow;
    private string _batteryTooltip = "";

    public NotchViewModel(
        Dispatcher dispatcher,
        NetworkStatusService network,
        BluetoothStatusService bluetooth,
        AudioStatusService audio,
        BatteryStatusService battery)
    {
        _network = network;
        _bluetooth = bluetooth;
        _audio = audio;
        _battery = battery;

        _network.Changed += OnNetworkChanged;
        _bluetooth.Changed += OnBluetoothChanged;
        _audio.Changed += OnAudioChanged;
        _battery.Changed += OnBatteryChanged;

        _clock = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, OnClockTick, dispatcher);

        UpdateClock();
        UpdateNetwork();
        UpdateBluetooth();
        UpdateAudio();
        UpdateBattery();
    }

    public string TimeText { get => _timeText; private set => SetProperty(ref _timeText, value); }
    public string DateText { get => _dateText; private set => SetProperty(ref _dateText, value); }

    public bool IsEthernet { get => _isEthernet; private set => SetProperty(ref _isEthernet, value); }
    public bool IsWifiIconVisible { get => _isWifiIconVisible; private set => SetProperty(ref _isWifiIconVisible, value); }
    public int WifiBars { get => _wifiBars; private set => SetProperty(ref _wifiBars, value); }
    public double NetworkOpacity { get => _networkOpacity; private set => SetProperty(ref _networkOpacity, value); }
    public string NetworkTooltip { get => _networkTooltip; private set => SetProperty(ref _networkTooltip, value); }

    public bool IsBluetoothAvailable { get => _isBluetoothAvailable; private set => SetProperty(ref _isBluetoothAvailable, value); }
    public double BluetoothOpacity { get => _bluetoothOpacity; private set => SetProperty(ref _bluetoothOpacity, value); }
    public string BluetoothTooltip { get => _bluetoothTooltip; private set => SetProperty(ref _bluetoothTooltip, value); }

    public string VolumeGlyph { get => _volumeGlyph; private set => SetProperty(ref _volumeGlyph, value); }
    public string VolumeTooltip { get => _volumeTooltip; private set => SetProperty(ref _volumeTooltip, value); }

    public bool HasBattery { get => _hasBattery; private set => SetProperty(ref _hasBattery, value); }
    public int BatteryPercent { get => _batteryPercent; private set => SetProperty(ref _batteryPercent, value); }
    public double BatteryFillWidth { get => _batteryFillWidth; private set => SetProperty(ref _batteryFillWidth, value); }
    public bool IsCharging { get => _isCharging; private set => SetProperty(ref _isCharging, value); }
    public bool IsBatteryLow { get => _isBatteryLow; private set => SetProperty(ref _isBatteryLow, value); }
    public string BatteryTooltip { get => _batteryTooltip; private set => SetProperty(ref _batteryTooltip, value); }

    private void OnClockTick(object? sender, EventArgs e)
    {
        UpdateClock();

        // Windows raises no event when Wi-Fi signal strength changes, so poll it.
        if (++_ticks % NetworkRefreshSeconds == 0) _network.Refresh();
    }

    private void OnNetworkChanged(object? sender, EventArgs e) => UpdateNetwork();
    private void OnBluetoothChanged(object? sender, EventArgs e) => UpdateBluetooth();
    private void OnAudioChanged(object? sender, EventArgs e) => UpdateAudio();
    private void OnBatteryChanged(object? sender, EventArgs e) => UpdateBattery();

    private void UpdateClock()
    {
        DateTime now = DateTime.Now;
        TimeText = now.ToString("t", CultureInfo.CurrentCulture);
        DateText = now.ToString("ddd d MMM", CultureInfo.CurrentCulture);
    }

    private void UpdateNetwork()
    {
        NetworkKind kind = _network.Kind;
        IsEthernet = kind == NetworkKind.Ethernet;
        IsWifiIconVisible = !IsEthernet;
        WifiBars = kind is NetworkKind.WiFi or NetworkKind.Cellular ? _network.SignalBars : 0;
        NetworkOpacity = kind != NetworkKind.None && _network.HasInternet ? 1.0 : 0.45;

        string suffix = kind != NetworkKind.None && !_network.HasInternet ? " (no internet)" : "";
        NetworkTooltip = kind switch
        {
            NetworkKind.WiFi => $"Wi-Fi: {_network.Name}{suffix}",
            NetworkKind.Ethernet => $"Ethernet: {_network.Name}{suffix}",
            NetworkKind.Cellular => $"Cellular: {_network.Name}{suffix}",
            NetworkKind.Other => $"Connected: {_network.Name}{suffix}",
            _ => "Not connected",
        };
    }

    private void UpdateBluetooth()
    {
        IsBluetoothAvailable = _bluetooth.IsAvailable;
        BluetoothOpacity = _bluetooth.IsOn ? 1.0 : 0.35;
        BluetoothTooltip = _bluetooth.IsOn ? "Bluetooth: on" : "Bluetooth: off";
    }

    private void UpdateAudio()
    {
        if (!_audio.HasDevice)
        {
            VolumeGlyph = "\uE74F";
            VolumeTooltip = "No audio device";
            return;
        }

        int percent = (int)Math.Round(_audio.Volume * 100);
        VolumeGlyph = _audio.IsMuted ? "\uE74F"
            : percent == 0 ? "\uE992"
            : percent < 34 ? "\uE993"
            : percent < 67 ? "\uE994"
            : "\uE995";
        VolumeTooltip = _audio.IsMuted
            ? $"{_audio.DeviceName}: muted"
            : $"{_audio.DeviceName}: {percent}%";
    }

    private void UpdateBattery()
    {
        HasBattery = _battery.IsPresent;
        BatteryPercent = _battery.Percent;
        IsCharging = _battery.IsCharging;
        IsBatteryLow = !_battery.IsCharging && _battery.Percent <= 20;
        BatteryFillWidth = _battery.Percent == 0 ? 0 : Math.Max(1.5, BatteryFillMaxWidth * _battery.Percent / 100.0);
        BatteryTooltip = _battery.IsCharging ? $"Battery {_battery.Percent}% (charging)" : $"Battery {_battery.Percent}%";
    }

    public void Dispose()
    {
        _clock.Stop();
        _network.Changed -= OnNetworkChanged;
        _bluetooth.Changed -= OnBluetoothChanged;
        _audio.Changed -= OnAudioChanged;
        _battery.Changed -= OnBatteryChanged;
    }
}
