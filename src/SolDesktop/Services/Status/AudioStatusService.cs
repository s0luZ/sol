using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using SolDesktop.Interop;

namespace SolDesktop.Services.Status;

public sealed record AudioDeviceEntry(string Id, string Name, bool IsDefault);

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

    /// <param name="volume">0.0 - 1.0</param>
    public void SetVolume(double volume)
    {
        if (_device is null) return;
        try
        {
            _device.AudioEndpointVolume.MasterVolumeLevelScalar = (float)Math.Clamp(volume, 0, 1);
            if (volume > 0 && _device.AudioEndpointVolume.Mute) _device.AudioEndpointVolume.Mute = false;
        }
        catch (COMException ex)
        {
            Log.Error(ex);
        }
    }

    public void SetMuted(bool muted)
    {
        if (_device is null) return;
        try
        {
            _device.AudioEndpointVolume.Mute = muted;
        }
        catch (COMException ex)
        {
            Log.Error(ex);
        }
    }

    public IReadOnlyList<AudioDeviceEntry> GetOutputDevices()
    {
        var result = new List<AudioDeviceEntry>();
        string? defaultId = _device?.ID;
        try
        {
            foreach (MMDevice device in _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                using (device)
                {
                    result.Add(new AudioDeviceEntry(device.ID, device.FriendlyName, device.ID == defaultId));
                }
            }
        }
        catch (COMException ex)
        {
            Log.Error(ex);
        }

        return result;
    }

    public bool SetDefaultOutput(string deviceId)
    {
        try
        {
            AudioPolicy.SetDefaultEndpoint(deviceId);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            return false;
        }
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
