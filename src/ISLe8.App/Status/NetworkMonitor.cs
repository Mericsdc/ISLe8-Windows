using ISLe8.Core.Diagnostics;
using ISLe8.Core.Status;
using ISLe8.UI;
using Microsoft.UI.Dispatching;
using Windows.Networking.Connectivity;

namespace ISLe8.Status;

/// <summary>
/// The way out to the internet (SystemModule.swift, NWPathMonitor): Windows says when the network
/// changes, and the internet connection's adapter says whether it is a cable. Nothing polls.
/// </summary>
internal sealed class NetworkMonitor : IDisposable
{
    private readonly DispatcherQueue _queue;
    private readonly Lock _lock = new();
    private bool _readQueued;
    private bool _started;

    public NetworkMonitor(DispatcherQueue queue) => _queue = queue;

    public NetworkLink Link { get; private set; } = NetworkLink.Offline;

    /// <summary><see cref="Link"/> was read again, on the UI thread; it may be the same as before.</summary>
    public event Action? Changed;

    public void Start()
    {
        if (_started) return;
        _started = true;
        NetworkInformation.NetworkStatusChanged += OnStatusChanged;
        Read();
    }

    public void Dispose()
    {
        if (!_started) return;
        _started = false;
        NetworkInformation.NetworkStatusChanged -= OnStatusChanged;
    }

    /// <summary>A change often comes as several events in a row; they are read once.</summary>
    private void OnStatusChanged(object sender)
    {
        lock (_lock)
        {
            if (_readQueued) return;
            _readQueued = true;
        }
        _queue.Post(() =>
        {
            lock (_lock) _readQueued = false;
            if (!_started) return;
            Read();
        });
    }

    private void Read()
    {
        Link = Current();
        Changed?.Invoke();
    }

    private static NetworkLink Current()
    {
        try
        {
            var profile = NetworkInformation.GetInternetConnectionProfile();
            if (profile is null || profile.GetNetworkConnectivityLevel() == NetworkConnectivityLevel.None) return NetworkLink.Offline;
            // An adapter Windows doesn't describe is "something else", not "offline".
            return NetworkLinks.FromInterfaceType(profile.NetworkAdapter?.IanaInterfaceType ?? 1);
        }
        catch (Exception e)
        {
            Log.Error(e, "network");
            return NetworkLink.Offline;
        }
    }
}
