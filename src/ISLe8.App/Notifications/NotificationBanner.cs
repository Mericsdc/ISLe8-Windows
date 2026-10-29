using ISLe8.Core.Notifications;
using ISLe8.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace ISLe8.Notifications;

/// <summary>
/// A notification under the pill's row (NotificationViews.swift, NotificationAlertView): the app's
/// icon, its name and how many more wait, the title and up to two lines. A click opens the app.
/// </summary>
internal sealed class NotificationBanner
{
    private readonly NotificationService _service;
    private readonly AppIcon _icon = new(34, Ui.White(0x1A), Ui.White(0x99));
    private readonly TextBlock _app = Ui.Text(10, alpha: 0x8C, semibold: true);
    private readonly TextBlock _count = Ui.Text(10, semibold: true);
    private readonly Border _countBadge;
    private readonly TextBlock _title = Ui.Text(12, semibold: true);
    private readonly TextBlock _detail = Ui.Text(11, alpha: 0xB3);

    public NotificationBanner(NotificationService service)
    {
        _service = service;
        _detail.TextWrapping = TextWrapping.Wrap;
        _detail.MaxLines = 2;
        _count.VerticalAlignment = VerticalAlignment.Center;
        _countBadge = new Border
        {
            Height = 16,
            Padding = new Thickness(6, 0, 6, 0),
            CornerRadius = new CornerRadius(8),
            Background = Ui.White(0x24),
            Child = _count,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var top = new Grid
        {
            ColumnSpacing = 6,
            ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition { Width = GridLength.Auto } },
            Children = { _app, _countBadge },
        };
        Grid.SetColumn(_countBadge, 1);
        var text = new StackPanel { Spacing = 1, Children = { top, _title, _detail } };
        _icon.Root.VerticalAlignment = VerticalAlignment.Top;
        var row = new Grid
        {
            ColumnSpacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
            ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition() },
            Children = { _icon.Root, text },
        };
        Grid.SetColumn(text, 1);
        Root = row;
    }

    public FrameworkElement Root { get; }

    public void Show(AppNotification notification, int pending)
    {
        _app.Text = notification.AppName;
        _title.Text = notification.Title;
        _title.Visibility = notification.Title.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _detail.Text = notification.Detail;
        _detail.Visibility = notification.Detail.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        SetPending(pending);
        _icon.Show(_service.LogoFor(notification.AppId));
    }

    /// <summary>"+N": notifications waiting behind this one.</summary>
    public void SetPending(int count)
    {
        _countBadge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _count.Text = $"+{count}";
    }
}

/// <summary>An app's logo, or a bell on a rounded square while there is none (NotificationViews.swift, AppIconView).</summary>
internal sealed class AppIcon
{
    private readonly Image _image;
    private readonly Border _placeholder;
    private int _version;

    public AppIcon(double size, Brush background, Brush glyph)
    {
        _image = new Image { Width = size, Height = size, Stretch = Stretch.Uniform };
        _placeholder = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size * 0.22),
            Background = background,
            Child = new FontIcon { Glyph = Glyphs.Bell, FontSize = size * 0.4, Foreground = glyph },
        };
        Root = new Grid { Width = size, Height = size, Children = { _placeholder, _image } };
    }

    public FrameworkElement Root { get; }

    public async void Show(RandomAccessStreamReference? logo)
    {
        var version = ++_version;
        _image.Source = null;
        _placeholder.Visibility = Visibility.Visible;
        if (logo is null) return;
        try
        {
            using var stream = await logo.OpenReadAsync();
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream);
            if (version != _version) return;
            _image.Source = bitmap;
            _placeholder.Visibility = Visibility.Collapsed;
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or IOException or UnauthorizedAccessException or ArgumentException)
        {
        }
    }
}
