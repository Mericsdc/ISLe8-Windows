namespace ISLe8.Core.Status;

/// <summary>The way out to the internet: an Ethernet cable, Wi-Fi, something else, or none (SystemModule.swift, NetworkLink).</summary>
public enum NetworkLink
{
    Wired,
    Wifi,
    Cellular,
    Other,
    Offline,
}

public static class NetworkLinks
{
    /// <summary>The internet connection's adapter type (IANA ifType): 6 Ethernet, 71 Wi-Fi, 243/244 mobile broadband.</summary>
    public static NetworkLink FromInterfaceType(uint? ianaType) => ianaType switch
    {
        null => NetworkLink.Offline,
        6 => NetworkLink.Wired,
        71 => NetworkLink.Wifi,
        243 or 244 => NetworkLink.Cellular,
        _ => NetworkLink.Other,
    };
}

/// <summary>
/// Whether the cable came or went (SystemModule.swift, networkChanged). The first reading only sets
/// the scene: already on a cable when ISLe8 starts says nothing.
/// </summary>
public sealed class WiredWatch
{
    private bool _hasReading;

    public NetworkLink Link { get; private set; } = NetworkLink.Offline;

    /// <summary>True when the cable was plugged in, false when it was taken out, null when neither.</summary>
    public bool? Changed(NetworkLink link)
    {
        var old = Link;
        Link = link;
        bool? change = _hasReading && (link == NetworkLink.Wired) != (old == NetworkLink.Wired) ? link == NetworkLink.Wired : null;
        _hasReading = true;
        return change;
    }
}
