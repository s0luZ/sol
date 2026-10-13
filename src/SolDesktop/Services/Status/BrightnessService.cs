using System;
using System.Collections.Generic;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using static SolDesktop.Interop.NativeMethods;

namespace SolDesktop.Services.Status;

/// <summary>
/// Screen brightness: WMI for built-in laptop panels, DDC/CI for external monitors that
/// support it. Both are slow, so all calls run off the UI thread.
/// </summary>
public sealed class BrightnessService : IDisposable
{
    private readonly object _sync = new();
    private readonly List<(PHYSICAL_MONITOR[] Array, IntPtr Handle, uint Min, uint Max)> _ddc = new();
    private bool _probed;
    private bool _useWmi;
    private int? _pending;
    private bool _writing;

    public bool IsAvailable { get; private set; }

    /// <summary>0 - 100</summary>
    public int Level { get; private set; }

    public async Task RefreshAsync()
    {
        (bool available, int level) = await Task.Run(Read);
        IsAvailable = available;
        if (available) Level = level;
    }

    public void SetLevel(int level)
    {
        level = Math.Clamp(level, 0, 100);
        Level = level;
        lock (_sync)
        {
            _pending = level;
            if (_writing) return;
            _writing = true;
        }

        ThreadPool.QueueUserWorkItem(_ => WriteLoop());
    }

    private void WriteLoop()
    {
        while (true)
        {
            int level;
            lock (_sync)
            {
                if (_pending is not int next)
                {
                    _writing = false;
                    return;
                }

                level = next;
                _pending = null;
            }

            try
            {
                Write(level);
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
        }
    }

    private (bool, int) Read()
    {
        lock (_sync)
        {
            Probe();
        }

        try
        {
            if (_useWmi)
            {
                using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
                foreach (ManagementBaseObject item in searcher.Get())
                {
                    using (item) return (true, Convert.ToInt32(item["CurrentBrightness"]));
                }
            }

            foreach (var monitor in _ddc)
            {
                if (GetMonitorBrightness(monitor.Handle, out uint min, out uint current, out uint max) && max > min)
                {
                    return (true, (int)Math.Round((current - min) * 100.0 / (max - min)));
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }

        return (false, 0);
    }

    private void Write(int level)
    {
        if (_useWmi)
        {
            using var methods = new ManagementClass(@"root\WMI", "WmiMonitorBrightnessMethods", null);
            foreach (ManagementObject instance in methods.GetInstances())
            {
                using (instance) instance.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)level });
            }
        }

        foreach (var monitor in _ddc)
        {
            SetMonitorBrightness(monitor.Handle, (uint)Math.Round(monitor.Min + (monitor.Max - monitor.Min) * level / 100.0));
        }
    }

    private void Probe()
    {
        if (_probed) return;
        _probed = true;

        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
            using ManagementObjectCollection results = searcher.Get();
            _useWmi = results.Count > 0;
        }
        catch (ManagementException)
        {
            _useWmi = false;
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }

        var monitors = new List<IntPtr>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr _, ref RECT _, IntPtr _) =>
        {
            monitors.Add(hMonitor);
            return true;
        }, IntPtr.Zero);

        foreach (IntPtr hMonitor in monitors)
        {
            if (!GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out uint count) || count == 0) continue;
            var array = new PHYSICAL_MONITOR[count];
            if (!GetPhysicalMonitorsFromHMONITOR(hMonitor, count, array)) continue;

            bool kept = false;
            foreach (PHYSICAL_MONITOR physical in array)
            {
                if (GetMonitorBrightness(physical.hPhysicalMonitor, out uint min, out _, out uint max) && max > min)
                {
                    _ddc.Add((array, physical.hPhysicalMonitor, min, max));
                    kept = true;
                }
            }

            if (!kept) DestroyPhysicalMonitors(count, array);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            var destroyed = new HashSet<PHYSICAL_MONITOR[]>();
            foreach (var monitor in _ddc)
            {
                if (destroyed.Add(monitor.Array)) DestroyPhysicalMonitors((uint)monitor.Array.Length, monitor.Array);
            }

            _ddc.Clear();
        }
    }
}
