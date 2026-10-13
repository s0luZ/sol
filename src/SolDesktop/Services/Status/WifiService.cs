using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using Windows.Devices.Radios;
using Windows.Devices.WiFi;
using Windows.Networking.Connectivity;
using Windows.Security.Credentials;

namespace SolDesktop.Services.Status;

public sealed record WifiNetwork(string Ssid, int SignalBars, bool IsSecured, bool IsConnected, WiFiAvailableNetwork Native);

/// <summary>Wi-Fi radio, scanning, and connecting through the WinRT Wi-Fi API.</summary>
public sealed class WifiService : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private WiFiAdapter? _adapter;
    private Radio? _radio;

    public bool IsAvailable => _adapter is not null || _radio is not null;
    public bool IsOn => _radio?.State == RadioState.On;

    /// <summary>Set when Windows refuses scan access (Windows 11 ties it to location permission).</summary>
    public string? AccessProblem { get; private set; }

    public string? ConnectedSsid { get; private set; }
    public IReadOnlyList<WifiNetwork> Networks { get; private set; } = Array.Empty<WifiNetwork>();

    public event EventHandler? Changed;

    public WifiService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public async Task InitializeAsync()
    {
        try
        {
            var radios = await Radio.GetRadiosAsync();
            _radio = radios.FirstOrDefault(r => r.Kind == RadioKind.WiFi);
            if (_radio is not null) _radio.StateChanged += OnRadioStateChanged;

            WiFiAccessStatus access = await WiFiAdapter.RequestAccessAsync();
            if (access != WiFiAccessStatus.Allowed)
            {
                AccessProblem = "Windows blocks Wi-Fi scanning for Sol. Turn on location access for desktop apps in Settings to see networks.";
            }

            var adapters = await WiFiAdapter.FindAllAdaptersAsync();
            _adapter = adapters.FirstOrDefault();
            if (_adapter is not null) _adapter.AvailableNetworksChanged += OnAvailableNetworksChanged;
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            AccessProblem ??= "Windows blocks Wi-Fi scanning for Sol. Turn on location access for desktop apps in Settings to see networks.";
        }

        await RebuildAsync();
    }

    public async Task ScanAsync()
    {
        if (_adapter is null || !IsOn) return;
        try
        {
            await _adapter.ScanAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }

        await RebuildAsync();
    }

    public async Task<string?> SetEnabledAsync(bool on)
    {
        if (_radio is null) return "No Wi-Fi adapter found.";
        try
        {
            if (await Radio.RequestAccessAsync() != RadioAccessStatus.Allowed)
            {
                return "Windows didn't allow Sol to change Wi-Fi.";
            }

            RadioAccessStatus result = await _radio.SetStateAsync(on ? RadioState.On : RadioState.Off);
            return result == RadioAccessStatus.Allowed ? null : "Windows didn't allow Sol to change Wi-Fi.";
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            return "Couldn't change Wi-Fi.";
        }
    }

    /// <summary>Connects using a saved profile, or with the given password.</summary>
    public async Task<WiFiConnectionStatus> ConnectAsync(WifiNetwork network, string? password)
    {
        if (_adapter is null) return WiFiConnectionStatus.NetworkNotAvailable;
        try
        {
            WiFiConnectionResult result = password is null
                ? await _adapter.ConnectAsync(network.Native, WiFiReconnectionKind.Automatic)
                : await _adapter.ConnectAsync(network.Native, WiFiReconnectionKind.Automatic, new PasswordCredential { Password = password });
            await RebuildAsync();
            return result.ConnectionStatus;
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            return WiFiConnectionStatus.UnspecifiedFailure;
        }
    }

    public async Task DisconnectAsync()
    {
        try
        {
            _adapter?.Disconnect();
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }

        await RebuildAsync();
    }

    public static string Describe(WiFiConnectionStatus status) => status switch
    {
        WiFiConnectionStatus.Success => "Connected",
        WiFiConnectionStatus.InvalidCredential => "Wrong password",
        WiFiConnectionStatus.NetworkNotAvailable => "Network not available",
        WiFiConnectionStatus.Timeout => "Timed out",
        WiFiConnectionStatus.AccessRevoked => "Access denied",
        WiFiConnectionStatus.UnsupportedAuthenticationProtocol => "Use Windows settings for this network",
        _ => "Couldn't connect",
    };

    private async Task RebuildAsync()
    {
        string? connected = null;
        var networks = new List<WifiNetwork>();

        try
        {
            if (_adapter is not null)
            {
                ConnectionProfile? profile = await _adapter.NetworkAdapter.GetConnectedProfileAsync();
                if (profile?.IsWlanConnectionProfile == true) connected = profile.WlanConnectionProfileDetails.GetConnectedSsid();

                if (IsOn)
                {
                    networks = _adapter.NetworkReport.AvailableNetworks
                        .Where(n => !string.IsNullOrWhiteSpace(n.Ssid))
                        .GroupBy(n => n.Ssid)
                        .Select(g => g.OrderByDescending(n => n.NetworkRssiInDecibelMilliwatts).First())
                        .Select(n => new WifiNetwork(
                            n.Ssid,
                            n.SignalBars,
                            n.SecuritySettings.NetworkAuthenticationType is not (NetworkAuthenticationType.Open80211 or NetworkAuthenticationType.None),
                            n.Ssid == connected,
                            n))
                        .OrderByDescending(n => n.IsConnected)
                        .ThenByDescending(n => n.SignalBars)
                        .ThenBy(n => n.Ssid, StringComparer.CurrentCultureIgnoreCase)
                        .ToList();
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }

        ConnectedSsid = connected;
        Networks = networks;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnAvailableNetworksChanged(WiFiAdapter sender, object args) =>
        _dispatcher.InvokeAsync(RebuildAsync);

    private void OnRadioStateChanged(Radio sender, object args) =>
        _dispatcher.InvokeAsync(RebuildAsync);

    public void Dispose()
    {
        if (_adapter is not null) _adapter.AvailableNetworksChanged -= OnAvailableNetworksChanged;
        if (_radio is not null) _radio.StateChanged -= OnRadioStateChanged;
    }
}
