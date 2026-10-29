using ISLe8.Core.Diagnostics;
using Microsoft.UI.Dispatching;
using Windows.Devices.Power;

namespace ISLe8.Status;

/// <summary>
/// The power going into the battery while it charges, for the header's "⚡ 61 W" (PORTING.md §3.3).
/// Windows gives no adapter figure, as the Mac does; this is the battery's charge rate, so it is
/// there only while the battery really charges. Read only while the island is open.
/// </summary>
internal sealed class ChargeWatts
{
    private readonly DispatcherQueue _queue;
    private Battery? _battery;

    public ChargeWatts(DispatcherQueue queue) => _queue = queue;

    /// <summary>Whole watts going in; null on battery, when full, or with no battery at all.</summary>
    public int? Watts { get; private set; }

    /// <summary><see cref="Watts"/> changed, on the UI thread.</summary>
    public event Action? Changed;

    /// <summary>Open: read now and follow the reports. Closed: let go, and forget the figure.</summary>
    public void SetVisible(bool visible)
    {
        if (visible == (_battery is not null)) return;
        if (visible)
        {
            try
            {
                _battery = Battery.AggregateBattery;
                _battery.ReportUpdated += OnReportUpdated;
            }
            catch (Exception e)
            {
                Log.Error(e, "charge rate");
                _battery = null;
                return;
            }
            Read();
        }
        else
        {
            _battery!.ReportUpdated -= OnReportUpdated;
            _battery = null;
            Set(null);
        }
    }

    private void OnReportUpdated(Battery sender, object args) => _queue.TryEnqueue(() =>
    {
        if (_battery is not null) Read();
    });

    private void Read()
    {
        try
        {
            var report = _battery!.GetReport();
            Set(report.Status == Windows.System.Power.BatteryStatus.Charging && report.ChargeRateInMilliwatts is int rate && rate > 0
                ? (int)Math.Round(rate / 1000.0)
                : null);
        }
        catch (Exception e)
        {
            Log.Error(e, "charge rate");
            Set(null);
        }
    }

    private void Set(int? watts)
    {
        // Under half a watt rounds to nothing worth showing.
        if (watts == 0) watts = null;
        if (watts == Watts) return;
        Watts = watts;
        Changed?.Invoke();
    }
}
