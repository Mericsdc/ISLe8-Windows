using ISLe8.Core.Devices;
using ISLe8.Core.Settings;
using ISLe8.Core.Status;
using ISLe8.Devices;
using Microsoft.UI.Dispatching;

namespace ISLe8.Status;

/// <summary>
/// This computer's battery, its cable and its volume, as the Mac's SystemModule and HUDModule see
/// them: what is news (plugged in, a cable out, a key changing the volume) and what the header and
/// Home's volume slider show. Everything is event-driven; the charge rate is read only while open.
/// </summary>
internal sealed class SystemStatus : IDisposable
{
    private readonly AppSettings _settings;
    private readonly DeviceService _devices;
    private readonly BatteryAnnouncer _battery = new();
    private readonly WiredWatch _wired = new();
    private readonly VolumeFilter _volumeFilter = new();

    public SystemStatus(AppSettings settings, DeviceService devices, DispatcherQueue queue)
    {
        _settings = settings;
        _devices = devices;
        Volume = new VolumeMonitor(queue);
        Network = new NetworkMonitor(queue);
        Watts = new ChargeWatts(queue);
        Brightness = new BrightnessMonitor(queue);
    }

    public VolumeMonitor Volume { get; }
    public NetworkMonitor Network { get; }
    public ChargeWatts Watts { get; }
    public BrightnessMonitor Brightness { get; }

    /// <summary>This computer's battery; null on a desktop.</summary>
    public PcBattery? Battery => _devices.Battery;

    /// <summary>The output volume, 0–1 (0 when muted), for the slider; null with no output device.</summary>
    public double? OutputVolume { get; private set; }

    /// <summary>The volume or the brightness changed in a way worth a HUD.</summary>
    public event Action<HudValue>? Hud;

    /// <summary>Plugged in or out, or the battery at 20 % or 10 %, with Settings › System › Battery on.</summary>
    public event Action<BatteryEvent>? BatteryEvent;

    /// <summary>The cable went in (true) or came out (false); the link is what the connection is now.</summary>
    public event Action<bool, NetworkLink>? WiredChanged;

    /// <summary>Something the header's chips or the volume slider show changed.</summary>
    public event Action? Changed;

    public void Start()
    {
        _battery.Changed(Battery, _settings.ShowBatteryAlerts);
        _devices.BatteryChanged += OnBatteryChanged;
        Network.Changed += OnNetworkChanged;
        Network.Start();
        Watts.Changed += () => Changed?.Invoke();
        // Followed even with the volume HUD off, for the slider; Core Audio only calls back on a change.
        Volume.Changed += OnVolumeChanged;
        Volume.DeviceChanged += ReadVolume;
        Volume.Start();
        ReadVolume();
        Brightness.Changed += OnBrightnessChanged;
        ApplySettings();
    }

    /// <summary>Settings › System › HUDs: brightness is only followed while its HUD is on.</summary>
    public void ApplySettings() => Brightness.SetEnabled(_settings.ShowBrightnessHud);

    /// <summary>The island opened or closed: the charge rate is read only while it is open.</summary>
    public void SetVisible(bool open) => Watts.SetVisible(open);

    /// <summary>The slider on the open island; dragging up from zero also unmutes (HUDModule.swift, setOutputVolume).</summary>
    public void SetOutputVolume(double value)
    {
        var level = (float)Math.Clamp(value, 0, 1);
        if (!Volume.SetVolume(level)) return;
        if (level > 0 && Volume.IsMuted) Volume.SetMuted(false);
        OutputVolume = level;
    }

    public void Dispose()
    {
        _devices.BatteryChanged -= OnBatteryChanged;
        Network.Dispose();
        Watts.SetVisible(false);
        Volume.Dispose();
        Brightness.Dispose();
    }

    private void OnBatteryChanged()
    {
        if (_battery.Changed(Battery, _settings.ShowBatteryAlerts) is { } news) BatteryEvent?.Invoke(news);
        Changed?.Invoke();
    }

    private void OnNetworkChanged()
    {
        if (_wired.Changed(Network.Link) is { } connected) WiredChanged?.Invoke(connected, Network.Link);
        Changed?.Invoke();
    }

    /// <summary>A new default device: its level is where the slider stands, not news for the HUD.</summary>
    private void ReadVolume()
    {
        var level = Volume.Volume;
        var muted = Volume.IsMuted;
        _volumeFilter.Reset(level, muted);
        OutputVolume = level is { } value ? muted ? 0 : value : null;
        Changed?.Invoke();
    }

    /// <summary>The endpoint repeats itself; only real changes move the slider and bring up the HUD.</summary>
    private void OnVolumeChanged(float level, bool muted)
    {
        if (!_volumeFilter.IsChange(level, muted)) return;
        OutputVolume = muted ? 0 : level;
        Changed?.Invoke();
        if (_settings.ShowVolumeHud) Hud?.Invoke(new HudValue(HudKind.Volume, level, muted));
    }

    private void OnBrightnessChanged(double level)
    {
        if (_settings.ShowBrightnessHud && BrightnessKeys.ShowsHud(DateTimeOffset.Now, Brightness.LastKey)) Hud?.Invoke(new HudValue(HudKind.Brightness, level));
    }
}
