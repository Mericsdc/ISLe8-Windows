using ISLe8.Core.Shelf;
using ISLe8.Core.Theming;
using ISLe8.Localization;
using ISLe8.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.UI;

namespace ISLe8.ShelfTab;

/// <summary>
/// The Shelf tab (ShelfViews.swift, ShelfTab): the files in a row of tiles. A tile can be dragged
/// anywhere, opened with a double click, and has the Mac's menu; Share sends them all.
/// </summary>
internal sealed class ShelfTabView
{
    private readonly ShelfStore _store;
    private readonly Func<bool> _copy;
    private readonly nint _window;
    private readonly TextBlock _count = Ui.Text(11, alpha: 0x80);
    private readonly StackPanel _buttons = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
    private readonly StackPanel _tiles = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
    private readonly ScrollViewer _scroll;
    private readonly StackPanel _empty;

    /// <param name="copy">Whether dropped files are copied (Settings › Shelf).</param>
    /// <param name="window">The island's window, which the share window belongs to.</param>
    public ShelfTabView(ShelfStore store, Func<bool> copy, nint window)
    {
        _store = store;
        _copy = copy;
        _window = window;

        var title = Ui.Text(13, semibold: true);
        title.Text = L.Get("Shelf_Title");
        _count.VerticalAlignment = title.VerticalAlignment = VerticalAlignment.Center;
        _buttons.Children.Add(Capsule(L.Get("Shelf_ShareZone"), Glyphs.Share, () => ShelfActions.Share(store.Items.Select(i => i.Path).ToList(), window, L.Get("Shelf_Title"))));
        _buttons.Children.Add(Capsule(L.Get("Shelf_Clear"), Glyphs.Delete, store.RemoveAll));
        var header = new Grid { ColumnSpacing = 6, ColumnDefinitions = { Auto(), Auto(), new ColumnDefinition(), Auto() } };
        header.Children.Add(title);
        Grid.SetColumn(_count, 1);
        header.Children.Add(_count);
        Grid.SetColumn(_buttons, 3);
        header.Children.Add(_buttons);

        var emptyTitle = Ui.Text(12, semibold: true);
        emptyTitle.Text = L.Get("Shelf_EmptyTitle");
        var emptyDetail = Ui.Text(11, alpha: 0x80);
        emptyDetail.Text = L.Get("Shelf_EmptyDetail");
        emptyDetail.TextWrapping = TextWrapping.Wrap;
        emptyDetail.TextAlignment = TextAlignment.Center;
        emptyTitle.HorizontalAlignment = HorizontalAlignment.Center;
        _empty = new StackPanel
        {
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { new FontIcon { Glyph = Glyphs.Shelf, FontSize = 22, Foreground = Ui.White(0x73) }, emptyTitle, emptyDetail },
        };
        _scroll = new ScrollViewer
        {
            Content = _tiles,
            HorizontalScrollMode = ScrollMode.Enabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalAlignment = VerticalAlignment.Center,
        };

        Root = new Grid { RowSpacing = 8, RowDefinitions = { new RowDefinition { Height = GridLength.Auto }, new RowDefinition() }, Children = { header } };
        Grid.SetRow(_empty, 1);
        Grid.SetRow(_scroll, 1);
        Root.Children.Add(_empty);
        Root.Children.Add(_scroll);

        store.Changed += Fill;
        Fill();
    }

    public Grid Root { get; }

    public void Detach() => _store.Changed -= Fill;

    private void Fill()
    {
        var items = _store.Items;
        _count.Text = items.Count == 0 ? "" : L.Format(items.Count == 1 ? "Shelf_CountOne" : "Shelf_Count", items.Count);
        _buttons.Visibility = _scroll.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _empty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _tiles.Children.Clear();
        foreach (var item in items) _tiles.Children.Add(Tile(item));
    }

    private FrameworkElement Tile(ShelfItem item)
    {
        var thumbnail = new Image { Width = 48, Height = 48, Stretch = Stretch.Uniform };
        _ = LoadThumbnail(item.Path, thumbnail);
        var name = Ui.Text(10);
        name.Text = item.Name;
        name.TextWrapping = TextWrapping.Wrap;
        name.MaxLines = 2;
        name.TextAlignment = TextAlignment.Center;
        name.Height = 26;
        var stack = new StackPanel { Spacing = 4, Children = { thumbnail, name } };
        var tile = new Border
        {
            Width = 74,
            Padding = new Thickness(0, 6, 0, 6),
            CornerRadius = new CornerRadius(12),
            Background = Ui.White(0x0A),
            Child = stack,
            CanDrag = true,
        };
        var remove = Ui.IconButton(Glyphs.CloseCircle, 18, 13, L.Get("Shelf_Remove"), () => _store.Remove(item.Id), out _);
        remove.HorizontalAlignment = HorizontalAlignment.Right;
        remove.VerticalAlignment = VerticalAlignment.Top;
        remove.Margin = new Thickness(0, -4, -4, 0);
        remove.Visibility = Visibility.Collapsed;
        var cell = new Grid { Children = { tile, remove } };

        cell.PointerEntered += (_, _) =>
        {
            tile.Background = Ui.White(0x1A);
            remove.Visibility = Visibility.Visible;
        };
        cell.PointerExited += (_, _) =>
        {
            tile.Background = Ui.White(0x0A);
            remove.Visibility = Visibility.Collapsed;
        };
        tile.DoubleTapped += (_, _) => ShelfActions.Open(item.Path);
        // Dragged out like a file from Explorer.
        tile.DragStarting += async (_, e) =>
        {
            var deferral = e.GetDeferral();
            try
            {
                IStorageItem storage = Directory.Exists(item.Path) ? await StorageFolder.GetFolderFromPathAsync(item.Path) : await StorageFile.GetFileFromPathAsync(item.Path);
                e.Data.SetStorageItems([storage]);
                e.Data.RequestedOperation = DataPackageOperation.Copy;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
            {
                e.Cancel = true;
            }
            finally
            {
                deferral.Complete();
            }
        };
        ToolTipService.SetToolTip(cell, item.Path);
        cell.ContextFlyout = Menu(item);
        return cell;
    }

    private MenuFlyout Menu(ShelfItem item)
    {
        var menu = new MenuFlyout();
        MenuFlyoutItem Item(string key, Action action)
        {
            var entry = new MenuFlyoutItem { Text = L.Get(key) };
            entry.Click += (_, _) => action();
            return entry;
        }
        menu.Items.Add(Item("Shelf_Open", () => ShelfActions.Open(item.Path)));
        menu.Items.Add(Item("Shelf_ShowInFolder", () => ShelfActions.Reveal(item.Path)));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(Item("Shelf_Share", () => ShelfActions.Share([item.Path], _window, item.Name)));
        menu.Items.Add(Item("Shelf_Zip", async () =>
        {
            if (await ShelfActions.Zip(item.Path, _store) is { } zip) _store.Add([zip], copy: false);
        }));
        if (item.IsImage)
        {
            var convert = new MenuFlyoutSubItem { Text = L.Get("Shelf_Convert") };
            foreach (var (label, jpeg) in new[] { ("PNG", false), ("JPEG", true) })
            {
                var entry = new MenuFlyoutItem { Text = label };
                entry.Click += async (_, _) =>
                {
                    if (await ShelfActions.Convert(item.Path, jpeg, _store) is { } converted) _store.Add([converted], copy: false);
                };
                convert.Items.Add(entry);
            }
            menu.Items.Add(convert);
        }
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(Item("Shelf_Remove", () => _store.Remove(item.Id)));
        return menu;
    }

    /// <summary>The file's own thumbnail (a picture's preview, a document's icon), as Explorer shows it.</summary>
    private static async Task LoadThumbnail(string path, Image image)
    {
        try
        {
            IStorageItemProperties storage = Directory.Exists(path) ? await StorageFolder.GetFolderFromPathAsync(path) : await StorageFile.GetFileFromPathAsync(path);
            using var thumbnail = await storage.GetThumbnailAsync(ThumbnailMode.SingleItem, 96, ThumbnailOptions.UseCurrentScale);
            if (thumbnail is null) return;
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(thumbnail);
            image.Source = bitmap;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or ArgumentException)
        {
            Core.Diagnostics.Log.Error(e, "shelf thumbnail");
        }
    }

    public static Button Capsule(string title, string glyph, Action click)
    {
        var text = Ui.Text(11);
        text.Text = title;
        var button = new Button
        {
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { new FontIcon { Glyph = glyph, FontSize = 11 }, text } },
            Height = 22,
            Padding = new Thickness(9, 0, 9, 0),
            CornerRadius = new CornerRadius(11),
            BorderThickness = new Thickness(0),
            Background = Ui.White(0x14),
        };
        button.Resources["ButtonBackgroundPointerOver"] = Ui.White(0x29);
        button.Resources["ButtonBackgroundPressed"] = Ui.White(0x1F);
        button.Click += (_, _) => click();
        return button;
    }

    private static ColumnDefinition Auto() => new() { Width = GridLength.Auto };
}

/// <summary>
/// What the island shows while files are dragged onto it (ShelfViews.swift, DropTargetView): under
/// the empty pill row, "Rafa bırak" and "Paylaş" (the Mac's AirDrop) side by side.
/// </summary>
internal sealed class DropTargetView
{
    private static readonly Color Blue = Color.FromArgb(255, 0x0A, 0x84, 0xFF);

    public DropTargetView(Func<IReadOnlyList<IStorageItem>, Task> toShelf, Func<IReadOnlyList<IStorageItem>, Task> toShare)
    {
        Shelf = new Zone(L.Get("Shelf_DropHere"), Glyphs.ShelfIn, Color.FromArgb(255, 255, 255, 255), toShelf);
        Share = new Zone(L.Get("Shelf_ShareZone"), Glyphs.Share, Blue, toShare);
        var zones = new Grid { ColumnSpacing = 10, Margin = new Thickness(14, 6, 14, 14), ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition() } };
        zones.Children.Add(Shelf.Root);
        Grid.SetColumn(Share.Root, 1);
        zones.Children.Add(Share.Root);
        Root = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Visibility = Visibility.Collapsed,
            RowDefinitions = { new RowDefinition { Height = new GridLength(Core.Geometry.IslandSizes.HeaderHeight) }, new RowDefinition() },
        };
        Grid.SetRow(zones, 1);
        Root.Children.Add(zones);
    }

    public Grid Root { get; }
    public Zone Shelf { get; }
    public Zone Share { get; }

    /// <summary>A dashed outline that fills with its tint while the drag is over it (DropZone).</summary>
    internal sealed class Zone
    {
        private readonly Color _tint;
        private readonly Rectangle _outline;
        private readonly FontIcon _icon;
        private readonly TextBlock _title = Ui.Text(12, semibold: true);

        public Zone(string title, string glyph, Color tint, Func<IReadOnlyList<IStorageItem>, Task> dropped)
        {
            _tint = tint;
            _title.Text = title;
            _title.HorizontalAlignment = HorizontalAlignment.Center;
            _icon = new FontIcon { Glyph = glyph, FontSize = 20 };
            _outline = new Rectangle { RadiusX = 16, RadiusY = 16, StrokeThickness = 1.5 };
            Root = new Grid
            {
                AllowDrop = true,
                Children =
                {
                    _outline,
                    new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Children = { _icon, _title } },
                },
            };
            Root.DragEnter += (_, e) =>
            {
                if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
                e.AcceptedOperation = DataPackageOperation.Copy;
                Highlight(true);
            };
            Root.DragOver += (_, e) =>
            {
                if (e.DataView.Contains(StandardDataFormats.StorageItems)) e.AcceptedOperation = DataPackageOperation.Copy;
            };
            Root.DragLeave += (_, _) => Highlight(false);
            Root.Drop += async (_, e) =>
            {
                // Handled here, so the island under the zone doesn't close the drop target first.
                e.Handled = true;
                Highlight(false);
                if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
                var deferral = e.GetDeferral();
                try
                {
                    // The items themselves, not only their paths: one from another app's own folder can
                    // only be read through them, while the drop lasts.
                    await dropped(await e.DataView.GetStorageItemsAsync());
                }
                finally
                {
                    deferral.Complete();
                }
            };
            Highlight(false);
        }

        public Grid Root { get; }

        public void Highlight(bool on)
        {
            var white = Color.FromArgb(0xBF, 255, 255, 255);
            _icon.Foreground = _title.Foreground = new SolidColorBrush(on ? _tint : white);
            _outline.Fill = new SolidColorBrush(Color.FromArgb(on ? (byte)0x2E : (byte)0x0A, _tint.R, _tint.G, _tint.B));
            _outline.Stroke = new SolidColorBrush(Color.FromArgb(on ? (byte)0xE6 : (byte)0x4D, _tint.R, _tint.G, _tint.B));
            _outline.StrokeDashArray = on ? null : new DoubleCollection { 5 / 1.5, 4 / 1.5 };
        }
    }
}
