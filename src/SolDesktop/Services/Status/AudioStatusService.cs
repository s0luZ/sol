using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace SolDesktop.Services.Status;

/// <summary>Tracks the default playback device and its volume/mute state.</summary>
public sealed class AudioStatusService : IMMNotificationClient, IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly MMDeviceEnumerator _enumerator = new();
    private MMDevice? _device;

    public bool HasDevice => _device is not null;
    public string? DeviceName { get; private set; }

    /// <summary>Master volume, 0.0 - 1.0.</summary>
    public float Volume { get; private set; }

    public bool IsMuted { get; private set; }

    public event EventHandler? Changed;

    public AudioStatusService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _enumerator.RegisterEndpointNotificationCallback(this);
        AttachDefaultDevice();
    }

    private void AttachDefaultDevice()
    {
        DetachDevice();
        try
        {
            if (_enumerator.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
            {
                _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                DeviceName = _device.FriendlyName;
                _device.AudioEndpointVolume.OnVolumeNotification += OnVolumeNotification;
            }
        }
        catch (COMException ex)
        {
            Log.Error(ex);
            DetachDevice();
        }

        Refresh();
    }

    private void DetachDevice()
    {
        if (_device is null) return;
        _device.AudioEndpointVolume.OnVolumeNotification -= OnVolumeNotification;
        _device.Dispose();
        _device = null;
        DeviceName = null;
    }

    private void Refresh()
    {
        if (_device is not null)
        {
            try
            {
                Volume = _device.AudioEndpointVolume.MasterVolumeLevelScalar;
                IsMuted = _device.AudioEndpointVolume.Mute;
            }
            catch (COMException ex)
            {
                Log.Error(ex);
            }
        }
        else
        {
            Volume = 0;
            IsMuted = false;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnVolumeNotification(AudioVolumeNotificationData data) => _dispatcher.InvokeAsync(Refresh);

    // IMMNotificationClient callbacks arrive on a COM worker thread; re-acquire the device on the UI thread.
    void IMMNotificationClient.OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        if (flow == DataFlow.Render && role == Role.Multimedia)
        {
            _dispatcher.InvokeAsync(AttachDefaultDevice);
        }
    }

    void IMMNotificationClient.OnDeviceStateChanged(string deviceId, DeviceState newState) { }
    void IMMNotificationClient.OnDeviceAdded(string pwstrDeviceId) { }
    void IMMNotificationClient.OnDeviceRemoved(string deviceId) { }
    void IMMNotificationClient.OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }

    public void Dispose()
    {
        _enumerator.UnregisterEndpointNotificationCallback(this);
        DetachDevice();
        _enumerator.Dispose();
    }
}
