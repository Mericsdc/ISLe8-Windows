using ISLe8.Core.Island;
using ISLe8.Core.Theming;
using ISLe8.Localization;
using ISLe8.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ISLe8.Island;

/// <summary>
/// The tabs, as icons in a small capsule at the bottom of the open island (HeaderBar.swift,
/// TabDock). The chosen tab is filled with the theme's accent. The buttons are plain borders, not
/// Buttons: the island never takes focus, and their look depends on the selection, which a
/// Button's visual states would fight.
/// </summary>
internal sealed class TabDock
{
    public const double Height = 36;
    private const double ButtonWidth = 48;

    private readonly IslandTheme _theme;
    private readonly StackPanel _buttons;
    private readonly Dictionary<IslandTab, DockButton> _items = [];
    private IslandTab _selected;

    public TabDock(IslandTheme theme)
    {
        _theme = theme;
        _buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        Root = new Border
        {
            Height = Height,
            Padding = new Thickness(3),
            CornerRadius = new CornerRadius(Height / 2),
            Background = theme.Surface.ToBrush(),
            BorderBrush = theme.Border.IsClear ? null : theme.Border.ToBrush(),
            BorderThickness = new Thickness(theme.Border.IsClear ? 0 : 1),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = _buttons,
        };
    }

    public Border Root { get; }

    /// <summary>A tab was clicked.</summary>
    public event Action<IslandTab>? Chosen;

    public static string Title(IslandTab tab) => L.Get(tab switch
    {
        IslandTab.Shelf => "Tab_Shelf",
        IslandTab.Clipboard => "Tab_Clipboard",
        IslandTab.Notes => "Tab_Notes",
        IslandTab.System => "Tab_System",
        IslandTab.Agents => "Tab_Agents",
        IslandTab.Devices => "Tab_Devices",
        _ => "Tab_Home",
    });

    public static string Glyph(IslandTab tab) => tab switch
    {
        IslandTab.Shelf => Glyphs.Shelf,
        IslandTab.Clipboard => Glyphs.Clipboard,
        IslandTab.Notes => Glyphs.Notes,
        IslandTab.System => Glyphs.System,
        IslandTab.Agents => Glyphs.Agents,
        IslandTab.Devices => Glyphs.Devices,
        _ => Glyphs.Home,
    };

    /// <summary>The tabs in the dock, in order; <paramref name="selected"/> is filled.</summary>
    public void SetTabs(IReadOnlyList<IslandTab> tabs, IslandTab selected)
    {
        _buttons.Children.Clear();
        _items.Clear();
        foreach (var tab in tabs)
        {
            var item = new DockButton(tab, _theme);
            item.Root.Tapped += (_, e) =>
            {
                e.Handled = true;
                Chosen?.Invoke(tab);
            };
            _items[tab] = item;
            _buttons.Children.Add(item.Root);
        }
        _selected = selected;
        Update();
    }

    public void SetSelected(IslandTab tab)
    {
        _selected = tab;
        Update();
    }

    private void Update()
    {
        foreach (var (tab, item) in _items) item.SetSelected(tab == _selected);
    }

    private sealed class DockButton
    {
        private readonly IslandTheme _theme;
        private readonly FontIcon _icon;
        private bool _selected;
        private bool _hovering;

        public DockButton(IslandTab tab, IslandTheme theme)
        {
            _theme = theme;
            _icon = new FontIcon { Glyph = Glyph(tab), FontSize = 14, FontWeight = FontWeights.SemiBold };
            Root = new Border
            {
                Width = ButtonWidth,
                Height = Height - 6,
                CornerRadius = new CornerRadius((Height - 6) / 2),
                Child = _icon,
            };
            AutomationProperties.SetName(Root, Title(tab));
            ToolTipService.SetToolTip(Root, Title(tab));
            Root.PointerEntered += (_, _) =>
            {
                _hovering = true;
                Paint();
            };
            Root.PointerExited += (_, _) =>
            {
                _hovering = false;
                Paint();
            };
        }

        public Border Root { get; }

        public void SetSelected(bool selected)
        {
            _selected = selected;
            Paint();
        }

        /// <summary>Accent fill and icon when chosen; otherwise a dim icon that brightens under the pointer.</summary>
        private void Paint()
        {
            Root.Background = _selected
                ? _theme.Accent.WithAlpha(0x33).ToBrush()
                : new SolidColorBrush(Windows.UI.Color.FromArgb(_hovering ? (byte)0x14 : (byte)0, 255, 255, 255));
            _icon.Foreground = _selected ? _theme.Accent.ToBrush() : Ui.White(_hovering ? (byte)0xE6 : (byte)0x8C);
        }
    }
}
