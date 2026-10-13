using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;

namespace SolDesktop.Services.Status;

public sealed record BluetoothDeviceEntry(string Name, bool IsConnected)
{
    public string Detail => IsConnected ? "Connected" : "Not connected";
}

public sealed class BluetoothStatusService : IDisposable
{
    private const string IsConnectedProperty = "System.Devices.Aep.IsConnected";

    private readonly Dispatcher _dispatcher;
    private Radio? _radio;

    public bool IsAvailable => _radio is not null;
    public bool IsOn => _radio?.State == RadioState.On;

    public event EventHandler? Changed;

    public BluetoothStatusService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public async Task InitializeAsync()
    {
        try
        {
            var radios = await Radio.GetRadiosAsync();
            _radio = radios.FirstOrDefault(r => r.Kind == RadioKind.Bluetooth);
            if (_radio is not null)
            {
                _radio.StateChanged += OnRadioStateChanged;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Turns the Bluetooth radio on or off. Returns an error message, or null on success.</summary>
    public async Task<string?> SetEnabledAsync(bool on)
    {
        if (_radio is null) return "No Bluetooth adapter found.";
        try
        {
            if (await Radio.RequestAccessAsync() != RadioAccessStatus.Allowed)
            {
                return "Windows didn't allow Sol to change Bluetooth.";
            }

            RadioAccessStatus result = await _radio.SetStateAsync(on ? RadioState.On : RadioState.Off);
            return result == RadioAccessStatus.Allowed ? null : "Windows didn't allow Sol to change Bluetooth.";
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            return "Couldn't change Bluetooth.";
        }
    }

    public async Task<IReadOnlyList<BluetoothDeviceEntry>> GetPairedDevicesAsync()
    {
        var result = new List<BluetoothDeviceEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string[] properties = { IsConnectedProperty };

        try
        {
            foreach (string selector in new[]
                     {
                         BluetoothDevice.GetDeviceSelectorFromPairingState(true),
                         BluetoothLEDevice.GetDeviceSelectorFromPairingState(true),
                     })
            {
                var devices = await DeviceInformation.FindAllAsync(selector, properties, DeviceInformationKind.AssociationEndpoint);
                foreach (DeviceInformation device in devices)
                {
                    if (string.IsNullOrWhiteSpace(device.Name) || !seen.Add(device.Name)) continue;
                    bool connected = device.Properties.TryGetValue(IsConnectedProperty, out object? value) && value is true;
                    result.Add(new BluetoothDeviceEntry(device.Name, connected));
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }

        return result.OrderByDescending(d => d.IsConnected).ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private void OnRadioStateChanged(Radio sender, object args) =>
        _dispatcher.InvokeAsync(() => Changed?.Invoke(this, EventArgs.Empty));

    public void Dispose()
    {
        if (_radio is not null) _radio.StateChanged -= OnRadioStateChanged;
    }
}
