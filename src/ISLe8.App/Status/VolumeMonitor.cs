using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ISLe8.Core.Diagnostics;
using ISLe8.UI;
using Microsoft.UI.Dispatching;

namespace ISLe8.Status;

/// <summary>
/// The default output device's volume and mute state (VolumeMonitor.swift). Core Audio calls back
/// when either changes (IAudioEndpointVolumeCallback), and when the default device itself changes
/// (IMMNotificationClient); nothing polls. The COM calls are made by hand, through the vtables, as
/// elsewhere in ISLe8, so Native AOT needs no generated interop.
/// </summary>
internal sealed unsafe partial class VolumeMonitor : IDisposable
{
    private static readonly Guid EnumeratorClass = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid EnumeratorId = new("A95664D2-9614-4F35-A746-DE8DB63617E6");
    private static readonly Guid EndpointVolumeId = new("5CDF2C82-841E-4546-9722-0CF74078229A");
    private static readonly Guid VolumeCallbackId = new("657804FA-D6AD-4496-8A60-352752AF4F89");
    private static readonly Guid NotificationClientId = new("7991EEC9-7E89-4D85-8390-6C703CEC60C0");
    private static readonly Guid UnknownId = new("00000000-0000-0000-C000-000000000046");

    private const int Render = 0; // eRender
    private const int Console = 0; // eConsole: what the volume keys and the taskbar's slider change
    private const uint ClsctxAll = 0x17;

    private readonly DispatcherQueue _queue;
    private GCHandle _self;
    private nint _enumerator;
    private nint _volume;
    private volatile bool _adjustable;
    private Callback* _volumeCallback;
    private Callback* _deviceCallback;
    private int _attachQueued;

    public VolumeMonitor(DispatcherQueue queue) => _queue = queue;

    /// <summary>The level (0–1) or mute changed, on the UI thread.</summary>
    public event Action<float, bool>? Changed;

    /// <summary>Another output device became the default, on the UI thread. Its level is not a change of its own.</summary>
    public event Action? DeviceChanged;

    public void Start()
    {
        if (_enumerator != 0) return;
        _self = GCHandle.Alloc(this);
        try
        {
            var clsid = EnumeratorClass;
            var iid = EnumeratorId;
            nint enumerator;
            Marshal.ThrowExceptionForHR(CoCreateInstance(&clsid, 0, ClsctxAll, &iid, &enumerator));
            _enumerator = enumerator;
            _volumeCallback = Callback.Create(VolumeVtable, VolumeCallbackId, _self);
            _deviceCallback = Callback.Create(DeviceVtable, NotificationClientId, _self);
            var register = (delegate* unmanaged[Stdcall]<nint, Callback*, int>)Slot(_enumerator, 6);
            register(_enumerator, _deviceCallback);
            Attach();
        }
        catch (Exception e)
        {
            Log.Error(e, "volume");
        }
    }

    /// <summary>There is an output device. Safe to ask from any thread.</summary>
    public bool HasDevice => _volume != 0;

    /// <summary>Its volume can be changed (<see cref="SetVolume"/>). Safe to ask from any thread.</summary>
    public bool IsAdjustable => _volume != 0 && _adjustable;

    /// <summary>The default device's level, 0–1; null when there is no output device.</summary>
    public float? Volume
    {
        get
        {
            if (_volume == 0) return null;
            float level;
            var get = (delegate* unmanaged[Stdcall]<nint, float*, int>)Slot(_volume, 9);
            return get(_volume, &level) >= 0 ? level : null;
        }
    }

    public bool IsMuted
    {
        get
        {
            if (_volume == 0) return false;
            int muted;
            var get = (delegate* unmanaged[Stdcall]<nint, int*, int>)Slot(_volume, 15);
            return get(_volume, &muted) >= 0 && muted != 0;
        }
    }

    /// <summary>
    /// Returns false when there is no output device, or its volume is fixed (a headset that sets its
    /// own, digital outputs): its range is a single level, as the Mac's "not settable".
    /// </summary>
    public bool SetVolume(float level)
    {
        if (_volume == 0 || !_adjustable) return false;
        var set = (delegate* unmanaged[Stdcall]<nint, float, Guid*, int>)Slot(_volume, 7);
        return set(_volume, Math.Clamp(level, 0, 1), null) >= 0;
    }

    public bool SetMuted(bool muted)
    {
        if (_volume == 0) return false;
        var set = (delegate* unmanaged[Stdcall]<nint, int, Guid*, int>)Slot(_volume, 14);
        return set(_volume, muted ? 1 : 0, null) >= 0;
    }

    public void Dispose()
    {
        Detach();
        if (_enumerator != 0)
        {
            var unregister = (delegate* unmanaged[Stdcall]<nint, Callback*, int>)Slot(_enumerator, 7);
            unregister(_enumerator, _deviceCallback);
            Marshal.Release(_enumerator);
            _enumerator = 0;
        }
        // The callbacks stay allocated: Core Audio may still be on its way into one. They no longer
        // reach this monitor once their owner is cleared.
        if (_volumeCallback is not null) _volumeCallback->Owner = 0;
        if (_deviceCallback is not null) _deviceCallback->Owner = 0;
        if (_self.IsAllocated) _self.Free();
    }

    /// <summary>Follows the default output device: its volume control, with this monitor's callback on it.</summary>
    private void Attach()
    {
        Volatile.Write(ref _attachQueued, 0);
        Detach();
        nint device;
        var getDefault = (delegate* unmanaged[Stdcall]<nint, int, int, nint*, int>)Slot(_enumerator, 4);
        // No output device at all (E_NOTFOUND) is not an error: the HUD and the slider just wait.
        var found = getDefault(_enumerator, Render, Console, &device);
        if (found < 0)
        {
            Log.Info($"volume: no default output device (0x{found:X8})");
            return;
        }
        try
        {
            var iid = EndpointVolumeId;
            nint volume;
            var activate = (delegate* unmanaged[Stdcall]<nint, Guid*, uint, void*, nint*, int>)Slot(device, 3);
            var activated = activate(device, &iid, ClsctxAll, null, &volume);
            if (activated < 0)
            {
                Log.Info($"volume: no volume control (0x{activated:X8})");
                return;
            }
            var register = (delegate* unmanaged[Stdcall]<nint, Callback*, int>)Slot(volume, 3);
            var registered = register(volume, _volumeCallback);
            if (registered < 0)
            {
                Log.Info($"volume: callback refused (0x{registered:X8})");
                Marshal.Release(volume);
                return;
            }
            _volume = volume;
            float lowest, highest, step;
            var range = (delegate* unmanaged[Stdcall]<nint, float*, float*, float*, int>)Slot(volume, 20);
            _adjustable = range(volume, &lowest, &highest, &step) >= 0 && highest > lowest;
        }
        finally
        {
            Marshal.Release(device);
        }
    }

    private void Detach()
    {
        if (_volume == 0) return;
        var unregister = (delegate* unmanaged[Stdcall]<nint, Callback*, int>)Slot(_volume, 4);
        unregister(_volume, _volumeCallback);
        Marshal.Release(_volume);
        _volume = 0;
    }

    private static nint Slot(nint instance, int index) => (*(nint**)instance)[index];

    // MARK: Callbacks, on Core Audio's threads

    private void Notified(float level, bool muted) => _queue.Post(() => Changed?.Invoke(level, muted));

    private void DefaultChanged()
    {
        if (Interlocked.Exchange(ref _attachQueued, 1) == 1) return;
        _queue.Post(() =>
        {
            if (_enumerator == 0) return;
            Attach();
            DeviceChanged?.Invoke();
        });
    }

    /// <summary>A COM object with one interface besides IUnknown. It lives as long as ISLe8.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct Callback
    {
        public nint* Vtable;
        public nint Owner;
        public Guid Interface;
        public int References;

        public static Callback* Create(nint* vtable, Guid iid, GCHandle owner)
        {
            var callback = (Callback*)NativeMemory.AllocZeroed((nuint)sizeof(Callback));
            callback->Vtable = vtable;
            callback->Owner = GCHandle.ToIntPtr(owner);
            callback->Interface = iid;
            callback->References = 1;
            return callback;
        }

        public static VolumeMonitor? OwnerOf(Callback* self)
        {
            var owner = Volatile.Read(ref self->Owner);
            return owner == 0 ? null : GCHandle.FromIntPtr(owner).Target as VolumeMonitor;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct VolumeNotification
    {
        public Guid EventContext;
        public int Muted;
        public float MasterVolume;
        public uint Channels;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid FormatId;
        public uint PropertyId;
    }

    private static readonly nint* VolumeVtable = Vtable(
        (nint)(delegate* unmanaged[Stdcall]<Callback*, Guid*, nint*, int>)&QueryInterface,
        (nint)(delegate* unmanaged[Stdcall]<Callback*, uint>)&AddRef,
        (nint)(delegate* unmanaged[Stdcall]<Callback*, uint>)&Release,
        (nint)(delegate* unmanaged[Stdcall]<Callback*, VolumeNotification*, int>)&OnNotify);

    private static readonly nint* DeviceVtable = Vtable(
        (nint)(delegate* unmanaged[Stdcall]<Callback*, Guid*, nint*, int>)&QueryInterface,
        (nint)(delegate* unmanaged[Stdcall]<Callback*, uint>)&AddRef,
        (nint)(delegate* unmanaged[Stdcall]<Callback*, uint>)&Release,
        (nint)(delegate* unmanaged[Stdcall]<Callback*, char*, uint, int>)&OnDeviceStateChanged,
        (nint)(delegate* unmanaged[Stdcall]<Callback*, char*, int>)&OnDeviceAdded,
        (nint)(delegate* unmanaged[Stdcall]<Callback*, char*, int>)&OnDeviceRemoved,
        (nint)(delegate* unmanaged[Stdcall]<Callback*, int, int, char*, int>)&OnDefaultDeviceChanged,
        (nint)(delegate* unmanaged[Stdcall]<Callback*, char*, PropertyKey, int>)&OnPropertyValueChanged);

    private static nint* Vtable(params ReadOnlySpan<nint> slots)
    {
        var table = (nint*)NativeMemory.Alloc((nuint)(slots.Length * sizeof(nint)));
        for (var i = 0; i < slots.Length; i++) table[i] = slots[i];
        return table;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int QueryInterface(Callback* self, Guid* iid, nint* result)
    {
        if (*iid == UnknownId || *iid == self->Interface)
        {
            Interlocked.Increment(ref self->References);
            *result = (nint)self;
            return 0;
        }
        *result = 0;
        return unchecked((int)0x80004002); // E_NOINTERFACE
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint AddRef(Callback* self) => (uint)Interlocked.Increment(ref self->References);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint Release(Callback* self) => (uint)Math.Max(Interlocked.Decrement(ref self->References), 1);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int OnNotify(Callback* self, VolumeNotification* data)
    {
        try
        {
            Callback.OwnerOf(self)?.Notified(data->MasterVolume, data->Muted != 0);
        }
        catch (Exception e)
        {
            Log.Error(e, "volume notification");
        }
        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int OnDefaultDeviceChanged(Callback* self, int flow, int role, char* id)
    {
        try
        {
            if (flow == Render && role == Console) Callback.OwnerOf(self)?.DefaultChanged();
        }
        catch (Exception e)
        {
            Log.Error(e, "default device");
        }
        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int OnDeviceStateChanged(Callback* self, char* id, uint state) => 0;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int OnDeviceAdded(Callback* self, char* id) => 0;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int OnDeviceRemoved(Callback* self, char* id) => 0;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int OnPropertyValueChanged(Callback* self, char* id, PropertyKey key) => 0;

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(Guid* clsid, nint outer, uint context, Guid* iid, nint* result);
}
