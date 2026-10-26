using ISLe8.Core.Settings;

namespace ISLe8.Core.Island;

/// <summary>The open island's tabs, in the dock at its bottom. Stored by their Mac names.</summary>
public enum IslandTab
{
    Home,
    Shelf,
    Clipboard,
    Agents,
    Devices,
}

public static class IslandTabs
{
    /// <summary>The ids are the Mac's (SettingsStore.swift, allTabs), in the default order.</summary>
    private static readonly string[] Ids = ["home", "shelf", "clipboard", "agents", "devices"];

    public static IReadOnlyList<string> AllIds => Ids;

    public static string Id(IslandTab tab) => Ids[(int)tab];

    public static IslandTab? Parse(string? id) => Array.IndexOf(Ids, id) is var index and >= 0 ? (IslandTab)index : null;

    /// <summary>The tabs in the dock, in the user's order, without hidden ones; never none.</summary>
    public static IReadOnlyList<IslandTab> Visible(AppSettings settings)
    {
        var tabs = settings.TabOrder
            .Where(id => !settings.HiddenTabs.Contains(id))
            .Select(Parse)
            .OfType<IslandTab>()
            .ToList();
        return tabs.Count > 0 ? tabs : [IslandTab.Home];
    }
}
