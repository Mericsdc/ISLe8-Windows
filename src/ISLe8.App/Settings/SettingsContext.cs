using ISLe8.Core.Settings;
using ISLe8.UI;
using Microsoft.UI.Xaml;

namespace ISLe8.Settings;

/// <summary>
/// What a settings page works with: the island's own settings object, so a change shows at once;
/// <see cref="PlaceIsland"/> moves the island to the screen the settings now ask for.
/// </summary>
internal sealed record SettingsContext(AppSettings Settings, SettingsStore Store, Action PlaceIsland, Window Owner, SettingsPalette Palette)
{
    public void Save() => Store.Save(Settings);
}
