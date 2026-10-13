using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using Windows.Devices.Radios;

namespace SolDesktop.Services.Status;

public sealed class BluetoothStatusService : IDisposable
{
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

    private void OnRadioStateChanged(Radio sender, object args) =>
        _dispatcher.InvokeAsync(() => Changed?.Invoke(this, EventArgs.Empty));

    public void Dispose()
    {
        if (_radio is not null) _radio.StateChanged -= OnRadioStateChanged;
    }
}
