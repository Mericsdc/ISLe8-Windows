using ISLe8.Core.Settings;

namespace ISLe8.Core.Geometry;

/// <summary>A connected display as far as choosing one goes.</summary>
public readonly record struct DisplaySummary(string Device, bool IsPrimary);

/// <summary>Which connected display the island lives on (Settings › General › Screen).</summary>
public static class DisplayChoice
{
    /// <summary>
    /// The index of the display <paramref name="settings"/> asks for: the named one while it is
    /// connected, the active one (<paramref name="active"/>, -1 for none yet) in active mode, else
    /// the main one, else the first. -1 only when there is no display at all.
    /// </summary>
    public static int Pick(IReadOnlyList<DisplaySummary> displays, AppSettings settings, int active = -1)
    {
        if (displays.Count == 0) return -1;
        switch (settings.Monitor)
        {
            case MonitorChoice.Named:
                for (var i = 0; i < displays.Count; i++)
                {
                    if (displays[i].Device == settings.MonitorDevice) return i;
                }
                break;
            case MonitorChoice.Active when active >= 0 && active < displays.Count:
                return active;
        }
        for (var i = 0; i < displays.Count; i++)
        {
            if (displays[i].IsPrimary) return i;
        }
        return 0;
    }
}
