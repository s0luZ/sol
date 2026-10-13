using System;
using System.Runtime.InteropServices;

namespace SolDesktop.Interop;

/// <summary>
/// Switching the default playback device has no public API; this is the undocumented
/// IPolicyConfig interface that the Windows Sound settings (and EarTrumpet, SoundSwitch) use.
/// </summary>
public static class AudioPolicy
{
    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        // Only SetDefaultEndpoint is called; the others reserve their vtable slots.
        [PreserveSig] int GetMixFormat();
        [PreserveSig] int GetDeviceFormat();
        [PreserveSig] int ResetDeviceFormat();
        [PreserveSig] int SetDeviceFormat();
        [PreserveSig] int GetProcessingPeriod();
        [PreserveSig] int SetProcessingPeriod();
        [PreserveSig] int GetShareMode();
        [PreserveSig] int SetShareMode();
        [PreserveSig] int GetPropertyValue();
        [PreserveSig] int SetPropertyValue();
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int role);
    }

    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    private class PolicyConfigClient
    {
    }

    /// <summary>Makes the endpoint the default for console, multimedia and communications.</summary>
    public static void SetDefaultEndpoint(string deviceId)
    {
        var config = (IPolicyConfig)new PolicyConfigClient();
        try
        {
            for (int role = 0; role < 3; role++)
            {
                Marshal.ThrowExceptionForHR(config.SetDefaultEndpoint(deviceId, role));
            }
        }
        finally
        {
            Marshal.ReleaseComObject(config);
        }
    }
}
