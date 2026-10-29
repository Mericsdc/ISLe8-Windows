using ISLe8.Core.Devices;
using ISLe8.Core.Diagnostics;
using ISLe8.Core.Island;
using Microsoft.UI.Dispatching;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;
using Windows.System.Power;
using DeviceClass = ISLe8.Core.Devices.DeviceClass;

namespace ISLe8.Devices;

/// <summary>
/// Batteries of connected wireless devices, and of this computer (DevicesModule.swift). The Mac
/// reads macOS's Bluetooth report; Windows keeps the same numbers its Settings show on the device's
/// nodes (<see cref="BatteryKey"/>), and Bluetooth LE devices also answer the GATT Battery Service.
/// AirPods' buds and case are only in Apple's advertisements, listened to while the Devices tab is
/// on screen. Nothing polls without a reason: Windows says when a paired device connects or goes,
/// the list is read 2 and 10 s later, when the island opens after a minute, and every ten minutes
/// while a device is connected.
/// </summary>
internal sealed class DeviceService : IDisposable
{
    /// <summary>The battery level Windows Settings shows for a Bluetooth device, on one of its device nodes.</summary>
    private const string BatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";
    private const string IsConnectedKey = "System.Devices.Aep.IsConnected";
    private const string ContainerKey = "System.Devices.Aep.ContainerId";
    private const string AddressKey = "System.Devices.Aep.DeviceAddress";
    private const string CodMajorKey = "System.Devices.Aep.Bluetooth.Cod.Major";
    private const string CodMinorKey = "System.Devices.Aep.Bluetooth.Cod.Minor";
    private const string AppearanceKey = "System.Devices.Aep.Bluetooth.Le.Appearance.Category";
    private const string AppearanceSubKey = "System.Devices.Aep.Bluetooth.Le.Appearance.Subcategory";
    private static readonly string[] EndpointProperties = [IsConnectedKey, ContainerKey, AddressKey, CodMajorKey, CodMinorKey, AppearanceKey, AppearanceSubKey];
    private static readonly string[] NodeProperties = [BatteryKey, "System.Devices.HardwareIds"];

    private static readonly TimeSpan Periodic = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan Stale = TimeSpan.FromSeconds(60);
    /// <summary>A bud reading older than this no longer counts.</summary>
    private static readonly TimeSpan BudsKeep = TimeSpan.FromMinutes(10);

    private readonly DispatcherQueue _queue;
    private readonly IIslandClock _clock;
    private readonly Dictionary<string, Endpoint> _endpoints = new(StringComparer.Ordinal);
    private readonly DeviceAlerts _alerts = new();
    /// <summary>The latest advertisement of each Apple model, with its signal and time.</summary>
    private readonly Dictionary<int, (AirPodsReading Reading, short Signal, DateTimeOffset At)> _buds = [];
    private readonly List<DeviceWatcher> _watchers = [];
    private int _enumerated;
    private IDisposable? _soon;
    private IDisposable? _periodic;
    private bool _reading;
    private bool _readAgain;
    private DateTimeOffset _lastRead = DateTimeOffset.MinValue;
    private IReadOnlyList<Reading> _read = [];
    private BluetoothLEAdvertisementWatcher? _advertisements;
    private bool _tabShown;

    public DeviceService(DispatcherQueue queue, IIslandClock clock)
    {
        _queue = queue;
        _clock = clock;
    }

    /// <summary>Connected devices that report a battery, merged by name and sorted.</summary>
    public IReadOnlyList<WirelessDevice> Devices { get; private set; } = [];

    /// <summary>This computer's battery; null on a desktop.</summary>
    public PcBattery? Battery { get; private set; }

    /// <summary><see cref="Devices"/> or <see cref="Battery"/> changed, on the UI thread.</summary>
    public event Action? Changed;

    /// <summary>A device connected (false) or ran low (true): worth a banner.</summary>
    public event Action<WirelessDevice, bool>? Alert;

    public void Start()
    {
        ReadBattery();
        PowerManager.RemainingChargePercentChanged += OnPowerChanged;
        PowerManager.BatteryStatusChanged += OnPowerChanged;
        PowerManager.PowerSupplyStatusChanged += OnPowerChanged;
        try
        {
            // A receiver plugged in or out: no Bluetooth event says so.
            var receivers = DeviceInformation.CreateWatcher(HidBatteries.WatchSelector);
            receivers.Added += (_, _) => _queue.TryEnqueue(() =>
            {
                if (_enumerated >= _watchers.Count) SomethingConnected();
            });
            receivers.Removed += (_, _) => _queue.TryEnqueue(SomethingConnected);
            receivers.EnumerationCompleted += (_, _) => _queue.TryEnqueue(EnumerationCompleted);
            _watchers.Add(receivers);
            receivers.Start();
        }
        catch (Exception e)
        {
            Log.Error(e, "receiver watcher");
        }
        foreach (var selector in new[] { BluetoothDevice.GetDeviceSelectorFromPairingState(true), BluetoothLEDevice.GetDeviceSelectorFromPairingState(true) })
        {
            try
            {
                var watcher = DeviceInformation.CreateWatcher(selector, EndpointProperties, DeviceInformationKind.AssociationEndpoint);
                watcher.Added += (_, info) => _queue.TryEnqueue(() => Added(info));
                watcher.Updated += (_, update) => _queue.TryEnqueue(() => Updated(update));
                watcher.Removed += (_, update) => _queue.TryEnqueue(() => Removed(update.Id));
                watcher.EnumerationCompleted += (_, _) => _queue.TryEnqueue(EnumerationCompleted);
                watcher.Start();
                _watchers.Add(watcher);
            }
            catch (Exception e)
            {
                Log.Error(e, "device watcher");
            }
        }
    }

    /// <summary>The island opened: numbers older than a minute are read again.</summary>
    public void SetVisible(bool open)
    {
        if (open && _clock.Now - _lastRead > Stale) _ = RefreshAsync();
    }

    /// <summary>The Devices tab is on screen: AirPods' advertisements are listened to, if an Apple device is connected.</summary>
    public void SetTabShown(bool shown)
    {
        _tabShown = shown;
        UpdateAdvertisements();
    }

    public void Dispose()
    {
        PowerManager.RemainingChargePercentChanged -= OnPowerChanged;
        PowerManager.BatteryStatusChanged -= OnPowerChanged;
        PowerManager.PowerSupplyStatusChanged -= OnPowerChanged;
        foreach (var watcher in _watchers)
        {
            try
            {
                if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted) watcher.Stop();
            }
            catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException)
            {
            }
        }
        _watchers.Clear();
        _tabShown = false;
        UpdateAdvertisements();
        _soon?.Dispose();
        _periodic?.Dispose();
    }

    // MARK: This computer

    private void OnPowerChanged(object? sender, object e) => _queue.TryEnqueue(() =>
    {
        var before = Battery;
        ReadBattery();
        if (Battery != before) Changed?.Invoke();
    });

    private void ReadBattery()
    {
        try
        {
            Battery = PowerManager.BatteryStatus == BatteryStatus.NotPresent
                ? null
                : new PcBattery(
                    Math.Clamp(PowerManager.RemainingChargePercent, 0, 100),
                    PowerManager.BatteryStatus == BatteryStatus.Charging,
                    PowerManager.PowerSupplyStatus != PowerSupplyStatus.NotPresent);
        }
        catch (Exception e)
        {
            Log.Error(e, "battery");
            Battery = null;
        }
    }

    // MARK: Paired devices

    private sealed class Endpoint
    {
        public required string Id { get; init; }
        public required bool IsLe { get; init; }
        public string Name { get; set; } = "";
        public bool IsConnected { get; set; }
        public Guid? Container { get; set; }
        public string Address { get; set; } = "";
        public DeviceClass Class { get; set; }
    }

    private void Added(DeviceInformation info)
    {
        var endpoint = new Endpoint { Id = info.Id, IsLe = info.Id.StartsWith("BluetoothLE", StringComparison.OrdinalIgnoreCase), Name = info.Name };
        Apply(endpoint, info.Properties);
        _endpoints[info.Id] = endpoint;
        if (endpoint.IsConnected && _enumerated >= _watchers.Count) SomethingConnected();
    }

    private void Updated(DeviceInformationUpdate update)
    {
        if (!_endpoints.TryGetValue(update.Id, out var endpoint)) return;
        var wasConnected = endpoint.IsConnected;
        Apply(endpoint, update.Properties);
        if (update.Properties.TryGetValue("System.ItemNameDisplay", out var name) && name is string text) endpoint.Name = text;
        if (endpoint.IsConnected != wasConnected) SomethingConnected();
    }

    private void Removed(string id)
    {
        if (_endpoints.Remove(id, out var endpoint) && endpoint.IsConnected) SomethingConnected();
    }

    private void EnumerationCompleted()
    {
        // The first read, once both lists are in (DevicesModule.start).
        if (++_enumerated == _watchers.Count) _ = RefreshAsync();
    }

    private static void Apply(Endpoint endpoint, IReadOnlyDictionary<string, object> properties)
    {
        if (properties.TryGetValue(IsConnectedKey, out var connected) && connected is bool isConnected) endpoint.IsConnected = isConnected;
        if (properties.TryGetValue(ContainerKey, out var container) && container is Guid guid) endpoint.Container = guid;
        if (properties.TryGetValue(AddressKey, out var address) && address is string text) endpoint.Address = text;
        endpoint.Class = new DeviceClass(
            Number(properties, CodMajorKey) ?? endpoint.Class.CodMajor,
            Number(properties, CodMinorKey) ?? endpoint.Class.CodMinor,
            Number(properties, AppearanceKey) ?? endpoint.Class.AppearanceCategory,
            Number(properties, AppearanceSubKey) ?? endpoint.Class.AppearanceSubcategory);
    }

    private static int? Number(IReadOnlyDictionary<string, object> properties, string key) =>
        properties.TryGetValue(key, out var value) && value is not null && value is not string
            ? Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture)
            : null;

    /// <summary>Read twice: right after connecting, buds often report before the case does (DevicesModule.swift).</summary>
    private void SomethingConnected()
    {
        _soon?.Dispose();
        _soon = _clock.After(TimeSpan.FromSeconds(2), () =>
        {
            _ = RefreshAsync();
            _soon = _clock.After(TimeSpan.FromSeconds(8), () =>
            {
                _soon = null;
                _ = RefreshAsync();
            });
        });
    }

    // MARK: Reading

    /// <summary>One connected device as read: its battery and what Apple model it is, if any.</summary>
    private sealed record Reading(string Id, string Name, DeviceKind Kind, int? Level, int? AppleModel, bool IsCharging = false, int? Spare = null);

    private async Task RefreshAsync()
    {
        if (_reading)
        {
            _readAgain = true;
            return;
        }
        _reading = true;
        var connected = _endpoints.Values
            .Where(endpoint => endpoint.IsConnected)
            .Select(endpoint => (endpoint.Id, endpoint.IsLe, endpoint.Name, endpoint.Container, endpoint.Address, endpoint.Class))
            .ToList();
        try
        {
            _read = await Task.Run(async () =>
            {
                var readings = new List<Reading>();
                foreach (var endpoint in connected)
                {
                    if (await ReadAsync(endpoint.Id, endpoint.IsLe, endpoint.Name, endpoint.Container, endpoint.Address, endpoint.Class) is { } reading)
                    {
                        readings.Add(reading);
                    }
                }
                // Receivers of Razer and SteelSeries devices: their makers' own protocols.
                readings.AddRange(HidBatteries.Read().Select(hid => new Reading(hid.Id, hid.Name, hid.Kind, hid.Level, null, hid.IsCharging, hid.Spare)));
                return (IReadOnlyList<Reading>)readings;
            });
        }
        catch (Exception e)
        {
            // A failed read keeps what was there.
            Log.Error(e, "devices");
        }
        _reading = false;
        _lastRead = _clock.Now;
        Publish(alert: true);
        SchedulePeriodic();
        UpdateAdvertisements();
        if (_readAgain)
        {
            _readAgain = false;
            _ = RefreshAsync();
        }
    }

    private static async Task<Reading?> ReadAsync(string id, bool isLe, string name, Guid? container, string address, DeviceClass type)
    {
        int? level = null;
        int? model = null;
        if (container is { } guid)
        {
            try
            {
                var nodes = await DeviceInformation.FindAllAsync($"System.Devices.ContainerId:=\"{{{guid}}}\"", NodeProperties, DeviceInformationKind.Device);
                foreach (var node in nodes)
                {
                    if (level is null && node.Properties.TryGetValue(BatteryKey, out var battery) && battery is not null && battery is not string)
                    {
                        level = Math.Clamp(Convert.ToInt32(battery, System.Globalization.CultureInfo.InvariantCulture), 0, 100);
                    }
                    if (model is null && node.Properties.TryGetValue("System.Devices.HardwareIds", out var ids) && ids is string[] hardwareIds)
                    {
                        model = DeviceRules.AppleModel(hardwareIds);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "device nodes");
            }
        }
        if (level is null && isLe) level = await ReadGattBatteryAsync(id);
        var kind = DeviceRules.Kind(name, model, type);
        return level is null && model is null ? null : new Reading(address.Length > 0 ? address : id, name, kind, level, model);
    }

    /// <summary>The Battery Service's level characteristic, for an LE device Windows keeps no number for.</summary>
    private static async Task<int?> ReadGattBatteryAsync(string id)
    {
        try
        {
            using var device = await BluetoothLEDevice.FromIdAsync(id);
            if (device is null) return null;
            var services = await device.GetGattServicesForUuidAsync(GattServiceUuids.Battery, BluetoothCacheMode.Cached);
            if (services.Status != GattCommunicationStatus.Success) return null;
            int? level = null;
            foreach (var service in services.Services)
            {
                using (service)
                {
                    if (level is not null) continue;
                    var characteristics = await service.GetCharacteristicsForUuidAsync(GattCharacteristicUuids.BatteryLevel, BluetoothCacheMode.Cached);
                    if (characteristics.Status != GattCommunicationStatus.Success) continue;
                    foreach (var characteristic in characteristics.Characteristics)
                    {
                        var value = await characteristic.ReadValueAsync(BluetoothCacheMode.Uncached);
                        if (value.Status != GattCommunicationStatus.Success || value.Value is not { Length: >= 1 } buffer) continue;
                        using var reader = DataReader.FromBuffer(buffer);
                        level = Math.Clamp((int)reader.ReadByte(), 0, 100);
                        break;
                    }
                }
            }
            return level;
        }
        catch (Exception e)
        {
            Log.Error(e, "gatt battery");
            return null;
        }
    }

    /// <summary>Puts the last read and the buds' advertisements together; banners only after a read.</summary>
    private void Publish(bool alert)
    {
        var now = _clock.Now;
        var devices = new List<WirelessDevice>();
        foreach (var reading in _read)
        {
            if (reading.AppleModel is { } model && _buds.TryGetValue(model, out var buds) && now - buds.At < BudsKeep)
            {
                var pods = buds.Reading;
                // Each bud and the case say more than the one number Windows keeps for the pair.
                if (pods.Left is not null || pods.Right is not null || pods.Case is not null)
                {
                    devices.Add(new WirelessDevice(reading.Id, reading.Name, reading.Kind, null, pods.Left, pods.Right, pods.Case));
                    continue;
                }
            }
            if (reading.Level is { } level) devices.Add(new WirelessDevice(reading.Id, reading.Name, reading.Kind, level, IsCharging: reading.IsCharging, Spare: reading.Spare));
        }
        var merged = DeviceRules.Merge(devices);
        var changed = !merged.SequenceEqual(Devices);
        Devices = merged;
        if (alert)
        {
            foreach (var (device, isLow) in _alerts.Read(merged)) Alert?.Invoke(device, isLow);
        }
        if (changed) Changed?.Invoke();
    }

    /// <summary>Batteries change slowly; ten minutes is plenty, and nothing runs with no device.</summary>
    private void SchedulePeriodic()
    {
        _periodic?.Dispose();
        _periodic = Devices.Count == 0 ? null : _clock.After(Periodic, () =>
        {
            _periodic = null;
            _ = RefreshAsync();
        });
    }

    // MARK: AirPods

    /// <summary>Listens only while the Devices tab shows a connected Apple device; passive, so nothing is sent.</summary>
    private void UpdateAdvertisements()
    {
        var wanted = _tabShown && _read.Any(reading => reading.AppleModel is not null);
        if (wanted == (_advertisements is not null)) return;
        if (!wanted)
        {
            try
            {
                _advertisements!.Stop();
            }
            catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException)
            {
            }
            _advertisements!.Received -= OnAdvertisement;
            _advertisements = null;
            return;
        }
        try
        {
            var watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Passive };
            using var prefix = new DataWriter();
            // Apple's proximity-pairing message; Apple sends many other kinds under the same company id.
            prefix.WriteByte(0x07);
            watcher.AdvertisementFilter.Advertisement.ManufacturerData.Add(new BluetoothLEManufacturerData((ushort)DeviceRules.AppleVendor, prefix.DetachBuffer()));
            watcher.Received += OnAdvertisement;
            watcher.Start();
            _advertisements = watcher;
        }
        catch (Exception e)
        {
            Log.Error(e, "advertisements");
        }
    }

    private void OnAdvertisement(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
    {
        foreach (var data in args.Advertisement.GetManufacturerDataByCompanyId((ushort)DeviceRules.AppleVendor))
        {
            var bytes = new byte[data.Data.Length];
            using (var reader = DataReader.FromBuffer(data.Data)) reader.ReadBytes(bytes);
            if (AirPodsReading.Parse(bytes) is not { } reading) continue;
            var signal = args.RawSignalStrengthInDBm;
            _queue.TryEnqueue(() => Heard(reading, signal));
        }
    }

    /// <summary>
    /// Someone else's AirPods nearby advertise too: only a model that is connected here counts, and
    /// of several, the closest (the strongest signal) in the last half minute.
    /// </summary>
    private void Heard(AirPodsReading reading, short signal)
    {
        if (!_read.Any(device => device.AppleModel == reading.Model)) return;
        var now = _clock.Now;
        if (_buds.TryGetValue(reading.Model, out var known) && now - known.At < TimeSpan.FromSeconds(30) && known.Signal > signal + 6) return;
        var changed = known.Reading != reading;
        _buds[reading.Model] = (reading, signal, now);
        if (changed) Publish(alert: false);
    }
}
