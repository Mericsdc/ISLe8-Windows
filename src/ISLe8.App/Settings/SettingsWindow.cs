using ISLe8.Core.Settings;
using ISLe8.Interop;
using ISLe8.Island;
using ISLe8.Localization;
using ISLe8.UI;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.UI.ViewManagement;

namespace ISLe8.Settings;

/// <summary>
/// ISLe8's settings: an ordinary window with a menu on the left, as on the Mac
/// (SettingsWindowController.swift), drawn with Mica and in Windows' light or dark theme, whatever
/// the island's. Built when first opened and dropped when closed, so it costs nothing in between.
/// Sections arrive with the steps of W1 that need them (PORTING.md §3.17).
/// </summary>
internal sealed class SettingsWindow(IslandController island, SettingsStore store)
{
    private Window? _window;
    private UISettings? _systemColors;

    public void Show()
    {
        _window ??= Build();
        _window.Activate();
    }

    private Window Build()
    {
        var window = new Window
        {
            Title = L.Get("Settings_Title"),
            SystemBackdrop = new MicaBackdrop(),
            ExtendsContentIntoTitleBar = true,
        };
        window.AppWindow.SetIcon(Asset("AppIcon.ico"));
        var palette = new SettingsPalette();
        var context = new SettingsContext(island, store, window, palette);

        var titleBar = new Grid
        {
            Height = 48,
            Padding = new Thickness(16, 0, 0, 0),
            ColumnSpacing = 12,
            ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition() },
        };
        titleBar.Children.Add(new Image
        {
            Source = new BitmapImage(new Uri(Asset("Square44x44Logo.png"))),
            Width = 16,
            Height = 16,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var title = new TextBlock { Text = L.Get("Settings_Title"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(title, 1);
        titleBar.Children.Add(title);

        var navigation = new NavigationView
        {
            PaneDisplayMode = NavigationViewPaneDisplayMode.Left,
            IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed,
            IsSettingsVisible = false,
            IsPaneToggleButtonVisible = false,
            IsTitleBarAutoPaddingEnabled = false,
            OpenPaneLength = 240,
        };
        var general = Section("Settings_General", Glyphs.Settings, GeneralPage.Build);
        navigation.MenuItems.Add(general);
        navigation.MenuItems.Add(Section("Settings_Appearance", Glyphs.Palette, AppearancePage.Build));
        navigation.MenuItems.Add(Section("Settings_Layout", Glyphs.Layout, LayoutPage.Build));
        navigation.SelectionChanged += (_, args) =>
        {
            if (args.SelectedItem is NavigationViewItem { Tag: Func<SettingsContext, FrameworkElement> build }) navigation.Content = Page(build(context));
        };

        var root = new Grid { RowDefinitions = { new RowDefinition { Height = GridLength.Auto }, new RowDefinition() } };
        root.Children.Add(titleBar);
        Grid.SetRow(navigation, 1);
        root.Children.Add(navigation);
        window.Content = root;
        window.SetTitleBar(titleBar);
        navigation.SelectedItem = general;

        ApplyTheme(window, root, palette);
        var queue = DispatcherQueue.GetForCurrentThread();
        _systemColors = new UISettings();
        _systemColors.ColorValuesChanged += (_, _) => queue.TryEnqueue(() =>
        {
            if (_window == window) ApplyTheme(window, root, palette);
        });
        window.Closed += (_, _) =>
        {
            _window = null;
            _systemColors = null;
        };

        Center(window, width: 880, height: 680);
        return window;
    }

    /// <summary>A menu entry that builds its page when chosen; pages are not kept.</summary>
    private static NavigationViewItem Section(string title, string glyph, Func<SettingsContext, FrameworkElement> build) =>
        new() { Content = L.Get(title), Icon = new FontIcon { Glyph = glyph }, Tag = build };

    private static FrameworkElement Page(FrameworkElement content) => new ScrollViewer
    {
        Content = new Border { Padding = new Thickness(36, 16, 36, 36), Child = content },
    };

    /// <summary>Windows' own light or dark mode: the island is always dark, its settings are not.</summary>
    private static void ApplyTheme(Window window, FrameworkElement root, SettingsPalette palette)
    {
        var background = new UISettings().GetColorValue(UIColorType.Background);
        var theme = background.R + background.G + background.B < 3 * 128 ? ElementTheme.Dark : ElementTheme.Light;
        root.RequestedTheme = theme;
        palette.Apply(theme);
        window.AppWindow.TitleBar.PreferredTheme = theme == ElementTheme.Dark ? TitleBarTheme.Dark : TitleBarTheme.Light;
    }

    /// <summary>Sized in DIPs for the screen it opens on, in the middle of that screen's work area.</summary>
    private static void Center(Window window, int width, int height)
    {
        var appWindow = window.AppWindow;
        var scale = Win32.GetDpiForWindow(Win32Interop.GetWindowFromWindowId(appWindow.Id)) / 96.0;
        var size = new SizeInt32((int)(width * scale), (int)(height * scale));
        var area = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        appWindow.MoveAndResize(new RectInt32(
            area.X + Math.Max((area.Width - size.Width) / 2, 0),
            area.Y + Math.Max((area.Height - size.Height) / 2, 0),
            Math.Min(size.Width, area.Width),
            Math.Min(size.Height, area.Height)));
    }

    private static string Asset(string name) => Path.Combine(AppContext.BaseDirectory, "Assets", name);
}
