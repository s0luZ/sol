using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SolDesktop.Services;
using SolDesktop.Services.Status;
using Windows.Devices.WiFi;

namespace SolDesktop.ViewModels;

public enum ControlCentrePage
{
    Main,
    WiFi,
    Bluetooth,
    Sound,
    Ethernet,
}

public sealed class WifiNetworkItem : ObservableObject
{
    private WifiNetwork _network;
    private bool _isExpanded;
    private bool _needsPassword;
    private bool _isBusy;
    private string? _message;

    public WifiNetworkItem(WifiNetwork network)
    {
        _network = network;
    }

    public WifiNetwork Network => _network;
    public string Ssid => _network.Ssid;
    public int SignalBars => _network.SignalBars;
    public bool IsSecured => _network.IsSecured;
    public bool IsConnected => _network.IsConnected;
    public string Detail => IsBusy ? "Connecting…" : _message ?? (IsConnected ? "Connected" : IsSecured ? "Secured" : "Open");

    public bool IsExpanded { get => _isExpanded; set => SetProperty(ref _isExpanded, value); }
    public bool NeedsPassword { get => _needsPassword; set => SetProperty(ref _needsPassword, value); }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value)) OnPropertyChanged(nameof(Detail));
        }
    }

    public string? Message
    {
        get => _message;
        set
        {
            if (SetProperty(ref _message, value)) OnPropertyChanged(nameof(Detail));
        }
    }

    public void Update(WifiNetwork network)
    {
        bool connectionChanged = network.IsConnected != _network.IsConnected;
        _network = network;
        if (connectionChanged) _message = null;
        OnPropertyChanged(nameof(Network));
        OnPropertyChanged(nameof(SignalBars));
        OnPropertyChanged(nameof(IsSecured));
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(Detail));
    }
}

/// <summary>The pull-down panel: quick toggles, sliders, now playing, and detail pages.</summary>
public sealed class ControlCentreViewModel : ObservableObject, IDisposable
{
    private readonly NetworkStatusService _network;
    private readonly WifiService _wifi;
    private readonly BluetoothStatusService _bluetooth;
    private readonly AudioStatusService _audio;
    private readonly BrightnessService _brightness;
    private readonly MediaService _media;
    private readonly DispatcherTimer _clock;

    private ControlCentrePage _page;
    private string _timeText = "";
    private string _dateText = "";
    private string? _wifiMessage;
    private bool _isScanning;
    private string? _bluetoothMessage;
    private IReadOnlyList<BluetoothDeviceEntry> _bluetoothDevices = Array.Empty<BluetoothDeviceEntry>();
    private IReadOnlyList<EthernetAdapter> _ethernetAdapters = Array.Empty<EthernetAdapter>();
    private IReadOnlyList<AudioDeviceEntry> _outputDevices = Array.Empty<AudioDeviceEntry>();
    private double _volume;
    private double _brightnessLevel;
    private string? _lastDeviceName;

    public ControlCentreViewModel(
        Dispatcher dispatcher,
        NetworkStatusService network,
        WifiService wifi,
        BluetoothStatusService bluetooth,
        AudioStatusService audio,
        BrightnessService brightness,
        MediaService media)
    {
        _network = network;
        _wifi = wifi;
        _bluetooth = bluetooth;
        _audio = audio;
        _brightness = brightness;
        _media = media;

        _network.Changed += OnNetworkChanged;
        _wifi.Changed += OnWifiChanged;
        _bluetooth.Changed += OnBluetoothChanged;
        _audio.Changed += OnAudioChanged;
        _media.Changed += OnMediaChanged;

        _clock = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => UpdateClock(), dispatcher);
        _clock.Stop();

        ShowWifiCommand = new RelayCommand(() => ShowPage(ControlCentrePage.WiFi));
        ShowBluetoothCommand = new RelayCommand(() => ShowPage(ControlCentrePage.Bluetooth));
        ShowSoundCommand = new RelayCommand(() => ShowPage(ControlCentrePage.Sound));
        ShowEthernetCommand = new RelayCommand(() => ShowPage(ControlCentrePage.Ethernet));
        BackCommand = new RelayCommand(() => Page = ControlCentrePage.Main);

        ToggleWifiCommand = new RelayCommand(() => _ = ToggleWifiAsync());
        RescanWifiCommand = new RelayCommand(() => _ = ScanWifiAsync());
        SelectNetworkCommand = new RelayCommand<WifiNetworkItem>(item => _ = SelectNetworkAsync(item));
        DisconnectCommand = new RelayCommand<WifiNetworkItem>(item => _ = DisconnectAsync(item));
        ToggleBluetoothCommand = new RelayCommand(() => _ = ToggleBluetoothAsync());
        ToggleMuteCommand = new RelayCommand(() => _audio.SetMuted(!_audio.IsMuted));
        SelectOutputCommand = new RelayCommand<AudioDeviceEntry>(SelectOutput);

        PlayPauseCommand = new RelayCommand(() => _ = _media.TogglePlayPauseAsync());
        NextCommand = new RelayCommand(() => _ = _media.NextAsync());
        PreviousCommand = new RelayCommand(() => _ = _media.PreviousAsync());

        OpenSolSettingsCommand = new RelayCommand(() =>
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
            ((App)Application.Current).ShowSettings();
        });
        OpenWifiSettingsCommand = new RelayCommand(() => OpenUri("ms-settings:network-wifi"));
        OpenLocationSettingsCommand = new RelayCommand(() => OpenUri("ms-settings:privacy-location"));
        OpenBluetoothSettingsCommand = new RelayCommand(() => OpenUri("ms-settings:bluetooth"));
        OpenSoundSettingsCommand = new RelayCommand(() => OpenUri("ms-settings:sound"));
        OpenEthernetSettingsCommand = new RelayCommand(() => OpenUri("ms-settings:network-ethernet"));

        UpdateClock();
        UpdateWifi();
        UpdateBluetooth();
        UpdateAudio();
        UpdateEthernet();
        UpdateMedia();
    }

    /// <summary>The panel should close (e.g. a Windows settings page was opened).</summary>
    public event EventHandler? CloseRequested;

    public ICommand ShowWifiCommand { get; }
    public ICommand ShowBluetoothCommand { get; }
    public ICommand ShowSoundCommand { get; }
    public ICommand ShowEthernetCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand ToggleWifiCommand { get; }
    public ICommand RescanWifiCommand { get; }
    public ICommand SelectNetworkCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand ToggleBluetoothCommand { get; }
    public ICommand ToggleMuteCommand { get; }
    public ICommand SelectOutputCommand { get; }
    public ICommand PlayPauseCommand { get; }
    public ICommand NextCommand { get; }
    public ICommand PreviousCommand { get; }
    public ICommand OpenSolSettingsCommand { get; }
    public ICommand OpenWifiSettingsCommand { get; }
    public ICommand OpenLocationSettingsCommand { get; }
    public ICommand OpenBluetoothSettingsCommand { get; }
    public ICommand OpenSoundSettingsCommand { get; }
    public ICommand OpenEthernetSettingsCommand { get; }

    // ---- pages ----

    public ControlCentrePage Page
    {
        get => _page;
        private set
        {
            if (!SetProperty(ref _page, value)) return;
            OnPropertyChanged(nameof(IsMainPage));
            OnPropertyChanged(nameof(IsWifiPage));
            OnPropertyChanged(nameof(IsBluetoothPage));
            OnPropertyChanged(nameof(IsSoundPage));
            OnPropertyChanged(nameof(IsEthernetPage));
        }
    }

    public bool IsMainPage => Page == ControlCentrePage.Main;
    public bool IsWifiPage => Page == ControlCentrePage.WiFi;
    public bool IsBluetoothPage => Page == ControlCentrePage.Bluetooth;
    public bool IsSoundPage => Page == ControlCentrePage.Sound;
    public bool IsEthernetPage => Page == ControlCentrePage.Ethernet;

    public string TimeText { get => _timeText; private set => SetProperty(ref _timeText, value); }
    public string DateText { get => _dateText; private set => SetProperty(ref _dateText, value); }

    // ---- Wi-Fi ----

    public bool IsWifiAvailable => _wifi.IsAvailable;
    public bool IsWifiOn => _wifi.IsOn;
    public string WifiStatus => !_wifi.IsAvailable ? "No adapter" : !_wifi.IsOn ? "Off" : _wifi.ConnectedSsid ?? "Not connected";
    public bool HasWifiAccessProblem => _wifi.AccessProblem is not null && _wifi.IsOn;
    public string? WifiAccessProblem => _wifi.AccessProblem;
    public ObservableCollection<WifiNetworkItem> WifiNetworks { get; } = new();
    public string? WifiMessage { get => _wifiMessage; private set => SetProperty(ref _wifiMessage, value); }
    public bool IsScanning { get => _isScanning; private set => SetProperty(ref _isScanning, value); }

    // ---- Bluetooth ----

    public bool IsBluetoothAvailable => _bluetooth.IsAvailable;
    public bool IsBluetoothOn => _bluetooth.IsOn;

    public string BluetoothStatus
    {
        get
        {
            if (!_bluetooth.IsAvailable) return "No adapter";
            if (!_bluetooth.IsOn) return "Off";
            int connected = _bluetoothDevices.Count(d => d.IsConnected);
            return connected switch
            {
                0 => "On",
                1 => _bluetoothDevices.First(d => d.IsConnected).Name,
                _ => $"{connected} devices",
            };
        }
    }

    public IReadOnlyList<BluetoothDeviceEntry> BluetoothDevices
    {
        get => _bluetoothDevices;
        private set
        {
            if (!SetProperty(ref _bluetoothDevices, value)) return;
            OnPropertyChanged(nameof(BluetoothStatus));
            OnPropertyChanged(nameof(HasBluetoothDevices));
        }
    }

    public bool HasBluetoothDevices => _bluetoothDevices.Count > 0;
    public string? BluetoothMessage { get => _bluetoothMessage; private set => SetProperty(ref _bluetoothMessage, value); }

    // ---- Ethernet ----

    public bool IsEthernetConnected => _ethernetAdapters.Any(a => a.IsUp);
    public bool HasEthernet => _ethernetAdapters.Count > 0;

    public string EthernetStatus => !HasEthernet ? "No adapter"
        : IsEthernetConnected ? (_network.Kind == NetworkKind.Ethernet && !_network.HasInternet ? "No internet" : "Connected")
        : "Unplugged";

    public IReadOnlyList<EthernetAdapter> EthernetAdapters
    {
        get => _ethernetAdapters;
        private set
        {
            if (!SetProperty(ref _ethernetAdapters, value)) return;
            OnPropertyChanged(nameof(IsEthernetConnected));
            OnPropertyChanged(nameof(HasEthernet));
            OnPropertyChanged(nameof(EthernetStatus));
        }
    }

    // ---- Sound ----

    public bool HasAudioDevice => _audio.HasDevice;
    public string OutputDeviceName => _audio.DeviceName ?? "No output device";
    public bool IsMuted => _audio.IsMuted;

    public string VolumeGlyph => !_audio.HasDevice || _audio.IsMuted ? "\uE74F"
        : _volume == 0 ? "\uE992"
        : _volume < 34 ? "\uE993"
        : _volume < 67 ? "\uE994"
        : "\uE995";

    /// <summary>0 - 100</summary>
    public double Volume
    {
        get => _volume;
        set
        {
            if (!SetProperty(ref _volume, value)) return;
            _audio.SetVolume(value / 100);
            OnPropertyChanged(nameof(VolumeGlyph));
        }
    }

    public IReadOnlyList<AudioDeviceEntry> OutputDevices { get => _outputDevices; private set => SetProperty(ref _outputDevices, value); }

    // ---- Brightness ----

    public bool HasBrightness => _brightness.IsAvailable;

    /// <summary>0 - 100</summary>
    public double Brightness
    {
        get => _brightnessLevel;
        set
        {
            if (!SetProperty(ref _brightnessLevel, value)) return;
            _brightness.SetLevel((int)Math.Round(value));
        }
    }

    // ---- Media ----

    public bool HasMedia => _media.HasSession && _media.Title is not null;
    public string MediaTitle => _media.Title ?? "Not playing";
    public string MediaArtist => _media.Artist ?? "";
    public ImageSource? MediaArtwork => _media.Artwork;
    public bool HasArtwork => _media.Artwork is not null;
    public string PlayPauseGlyph => _media.IsPlaying ? "\uE769" : "\uE768";

    // ---- lifecycle ----

    public void OnOpened()
    {
        Page = ControlCentrePage.Main;
        UpdateClock();
        _clock.Start();
        _network.Refresh();
        UpdateEthernet();
        UpdateAudio();
        OutputDevices = _audio.GetOutputDevices();
        _ = RefreshBluetoothDevicesAsync();
        _ = RefreshBrightnessAsync();
        _ = ScanWifiAsync();
    }

    public void OnClosed()
    {
        _clock.Stop();
        foreach (WifiNetworkItem item in WifiNetworks)
        {
            item.IsExpanded = false;
            item.NeedsPassword = false;
            item.Message = null;
        }

        WifiMessage = null;
        BluetoothMessage = null;
    }

    private void ShowPage(ControlCentrePage page)
    {
        Page = page;
        switch (page)
        {
            case ControlCentrePage.WiFi:
                _ = ScanWifiAsync();
                break;
            case ControlCentrePage.Bluetooth:
                _ = RefreshBluetoothDevicesAsync();
                break;
            case ControlCentrePage.Sound:
                OutputDevices = _audio.GetOutputDevices();
                break;
            case ControlCentrePage.Ethernet:
                UpdateEthernet();
                break;
        }
    }

    private void UpdateClock()
    {
        DateTime now = DateTime.Now;
        TimeText = now.ToString("t", CultureInfo.CurrentCulture);
        DateText = now.ToString("dddd d MMMM", CultureInfo.CurrentCulture);
    }

    // ---- Wi-Fi actions ----

    private async Task ScanWifiAsync()
    {
        if (IsScanning || !_wifi.IsOn) return;
        IsScanning = true;
        try
        {
            await _wifi.ScanAsync();
        }
        finally
        {
            IsScanning = false;
        }
    }

    private async Task ToggleWifiAsync()
    {
        WifiMessage = await _wifi.SetEnabledAsync(!_wifi.IsOn);
        UpdateWifi();
    }

    private async Task SelectNetworkAsync(WifiNetworkItem item)
    {
        if (item.IsBusy) return;

        if (item.IsConnected || item.NeedsPassword)
        {
            item.IsExpanded = !item.IsExpanded;
            return;
        }

        foreach (WifiNetworkItem other in WifiNetworks)
        {
            if (other != item) other.IsExpanded = false;
        }

        // Try a saved profile first; only ask for a password if Windows doesn't know this network.
        await ConnectAsync(item, null);
    }

    public async Task ConnectAsync(WifiNetworkItem item, string? password)
    {
        item.IsBusy = true;
        item.Message = null;
        WiFiConnectionStatus status = await _wifi.ConnectAsync(item.Network, password);
        item.IsBusy = false;

        if (status == WiFiConnectionStatus.Success)
        {
            item.NeedsPassword = false;
            item.IsExpanded = false;
            return;
        }

        if (item.IsSecured && status is WiFiConnectionStatus.InvalidCredential or WiFiConnectionStatus.UnspecifiedFailure)
        {
            item.NeedsPassword = true;
            item.IsExpanded = true;
            item.Message = password is null ? "Enter the password" : WifiService.Describe(status);
            return;
        }

        item.Message = WifiService.Describe(status);
    }

    private async Task DisconnectAsync(WifiNetworkItem item)
    {
        item.IsExpanded = false;
        await _wifi.DisconnectAsync();
    }

    // ---- Bluetooth / audio actions ----

    private async Task ToggleBluetoothAsync()
    {
        BluetoothMessage = await _bluetooth.SetEnabledAsync(!_bluetooth.IsOn);
        UpdateBluetooth();
    }

    private async Task RefreshBluetoothDevicesAsync()
    {
        BluetoothDevices = _bluetooth.IsAvailable
            ? await _bluetooth.GetPairedDevicesAsync()
            : Array.Empty<BluetoothDeviceEntry>();
    }

    private async Task RefreshBrightnessAsync()
    {
        await _brightness.RefreshAsync();
        _brightnessLevel = _brightness.Level;
        OnPropertyChanged(nameof(Brightness));
        OnPropertyChanged(nameof(HasBrightness));
    }

    private void SelectOutput(AudioDeviceEntry device)
    {
        if (device.IsDefault) return;
        if (!_audio.SetDefaultOutput(device.Id)) return;
        OutputDevices = OutputDevices.Select(d => d with { IsDefault = d.Id == device.Id }).ToList();
    }

    private void OpenUri(string uri)
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }
    }

    // ---- service updates ----

    private void OnNetworkChanged(object? sender, EventArgs e) => UpdateEthernet();
    private void OnWifiChanged(object? sender, EventArgs e) => UpdateWifi();
    private void OnBluetoothChanged(object? sender, EventArgs e)
    {
        UpdateBluetooth();
        _ = RefreshBluetoothDevicesAsync();
    }

    private void OnAudioChanged(object? sender, EventArgs e) => UpdateAudio();
    private void OnMediaChanged(object? sender, EventArgs e) => UpdateMedia();

    private void UpdateWifi()
    {
        var latest = _wifi.Networks;
        var bySsid = latest.ToDictionary(n => n.Ssid);

        for (int i = WifiNetworks.Count - 1; i >= 0; i--)
        {
            if (!bySsid.ContainsKey(WifiNetworks[i].Ssid)) WifiNetworks.RemoveAt(i);
        }

        for (int i = 0; i < latest.Count; i++)
        {
            WifiNetwork network = latest[i];
            int existing = IndexOf(network.Ssid);
            if (existing < 0)
            {
                WifiNetworks.Insert(i, new WifiNetworkItem(network));
                continue;
            }

            WifiNetworks[existing].Update(network);
            if (existing != i) WifiNetworks.Move(existing, i);
        }

        OnPropertyChanged(nameof(IsWifiAvailable));
        OnPropertyChanged(nameof(IsWifiOn));
        OnPropertyChanged(nameof(WifiStatus));
        OnPropertyChanged(nameof(HasWifiAccessProblem));
        OnPropertyChanged(nameof(WifiAccessProblem));

        int IndexOf(string ssid)
        {
            for (int i = 0; i < WifiNetworks.Count; i++)
            {
                if (WifiNetworks[i].Ssid == ssid) return i;
            }

            return -1;
        }
    }

    private void UpdateBluetooth()
    {
        OnPropertyChanged(nameof(IsBluetoothAvailable));
        OnPropertyChanged(nameof(IsBluetoothOn));
        OnPropertyChanged(nameof(BluetoothStatus));
    }

    private void UpdateEthernet() => EthernetAdapters = EthernetInfo.GetAdapters();

    private void UpdateAudio()
    {
        _volume = Math.Round(_audio.Volume * 100);
        OnPropertyChanged(nameof(Volume));
        OnPropertyChanged(nameof(VolumeGlyph));
        OnPropertyChanged(nameof(IsMuted));
        OnPropertyChanged(nameof(HasAudioDevice));

        // Volume notifications fire continuously while dragging; only re-enumerate when the device changes.
        if (_audio.DeviceName != _lastDeviceName || _outputDevices.Count == 0)
        {
            _lastDeviceName = _audio.DeviceName;
            OnPropertyChanged(nameof(OutputDeviceName));
            OutputDevices = _audio.GetOutputDevices();
        }
    }

    private void UpdateMedia()
    {
        OnPropertyChanged(nameof(HasMedia));
        OnPropertyChanged(nameof(MediaTitle));
        OnPropertyChanged(nameof(MediaArtist));
        OnPropertyChanged(nameof(MediaArtwork));
        OnPropertyChanged(nameof(HasArtwork));
        OnPropertyChanged(nameof(PlayPauseGlyph));
    }

    public void Dispose()
    {
        _clock.Stop();
        _network.Changed -= OnNetworkChanged;
        _wifi.Changed -= OnWifiChanged;
        _bluetooth.Changed -= OnBluetoothChanged;
        _audio.Changed -= OnAudioChanged;
        _media.Changed -= OnMediaChanged;
    }
}
