using ISLe8.Core.Settings;
using ISLe8.Island;
using ISLe8.UI;
using Microsoft.UI.Xaml;

namespace ISLe8.Settings;

/// <summary>
/// What a settings page works with: the island's own settings object, so a change shows at once,
/// and the island to tell (move, rebuild, resize).
/// </summary>
internal sealed record SettingsContext(IslandController Island, SettingsStore Store, Window Owner, SettingsPalette Palette)
{
    public AppSettings Settings => Island.Settings;

    public void Save() => Store.Save(Settings);
}
