using System;
using System.Windows.Threading;
using Windows.Devices.Power;
using Windows.System.Power;

namespace SolDesktop.Services.Status;

public sealed class BatteryStatusService : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly Battery _battery = Battery.AggregateBattery;

    public bool IsPresent { get; private set; }
    public int Percent { get; private set; }
    public bool IsCharging { get; private set; }

    public event EventHandler? Changed;

    public BatteryStatusService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _battery.ReportUpdated += OnReportUpdated;
        Refresh();
    }

    private void OnReportUpdated(Battery sender, object args) => _dispatcher.InvokeAsync(Refresh);

    public void Refresh()
    {
        try
        {
            BatteryReport report = _battery.GetReport();
            IsPresent = report.Status != BatteryStatus.NotPresent;
            IsCharging = report.Status == BatteryStatus.Charging;

            int? full = report.FullChargeCapacityInMilliwattHours;
            int? remaining = report.RemainingCapacityInMilliwattHours;
            Percent = full is > 0 && remaining is not null
                ? Math.Clamp((int)Math.Round(remaining.Value * 100.0 / full.Value), 0, 100)
                : 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            IsPresent = false;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => _battery.ReportUpdated -= OnReportUpdated;
}
