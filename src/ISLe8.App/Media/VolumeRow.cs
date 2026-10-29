using ISLe8.Core.Status;
using ISLe8.Core.Theming;
using ISLe8.Localization;
using ISLe8.Status;
using ISLe8.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace ISLe8.Media;

/// <summary>
/// The wide player's last row (MediaViews.swift): a speaker, the output volume's slider in the
/// theme's accent, and the level. Dragging sets the volume as it goes; from zero it also unmutes.
/// It follows the volume only while Home is on screen.
/// </summary>
internal sealed class VolumeRow
{
    private const double Track = 4;
    private const double Knob = 14;

    private readonly SystemStatus _status;
    private readonly FontIcon _speaker = new() { FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Ui.White(0xB3), Width = 18, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _value = Ui.Text(11, alpha: 0x99);
    private readonly Grid _bar;
    private readonly Border _fill;
    private readonly Border _knob;
    private double _level;
    private bool _dragging;
    private bool _active;

    public VolumeRow(SystemStatus status, IslandTheme theme)
    {
        _status = status;
        _value.FontWeight = FontWeights.Medium;
        _value.Width = 24;
        _value.TextAlignment = TextAlignment.Right;
        _value.VerticalAlignment = VerticalAlignment.Center;
        StatusGlyphs.Tabular(_value);

        var track = new Border { Height = Track, CornerRadius = new CornerRadius(Track / 2), Background = Ui.White(0x33), VerticalAlignment = VerticalAlignment.Center };
        _fill = new Border { Height = Track, CornerRadius = new CornerRadius(Track / 2), Background = theme.Accent.ToBrush(), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        _knob = new Border { Width = Knob, Height = Knob, CornerRadius = new CornerRadius(Knob / 2), Background = Ui.White(0xFF), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        // A transparent background, so the knob's whole height takes the pointer, not just the track.
        _bar = new Grid { Height = Knob, Background = Ui.Clear(), Children = { track, _fill, _knob } };
        AutomationProperties.SetName(_bar, L.Get("Media_Volume"));
        _bar.SizeChanged += (_, _) => Draw();
        _bar.PointerPressed += (_, e) =>
        {
            if (!_bar.CapturePointer(e.Pointer)) return;
            e.Handled = true;
            _dragging = true;
            Drag(e.GetCurrentPoint(_bar).Position.X);
        };
        _bar.PointerMoved += (_, e) =>
        {
            if (_dragging) Drag(e.GetCurrentPoint(_bar).Position.X);
        };
        _bar.PointerReleased += (_, e) =>
        {
            if (!_dragging) return;
            _bar.ReleasePointerCapture(e.Pointer);
            _dragging = false;
        };
        _bar.PointerCaptureLost += (_, _) => _dragging = false;

        var row = new Grid
        {
            ColumnSpacing = 8,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition(),
                new ColumnDefinition { Width = GridLength.Auto },
            },
            Children = { _speaker, _bar, _value },
        };
        Grid.SetColumn(_bar, 1);
        Grid.SetColumn(_value, 2);
        Root = row;
    }

    public FrameworkElement Root { get; }

    public void SetActive(bool active)
    {
        if (active == _active) return;
        _active = active;
        if (active)
        {
            _status.Changed += Refresh;
            Refresh();
        }
        else
        {
            _status.Changed -= Refresh;
        }
    }

    /// <summary>With no output device the row goes.</summary>
    private void Refresh()
    {
        Root.Visibility = _status.OutputVolume is null ? Visibility.Collapsed : Visibility.Visible;
        if (_dragging || _status.OutputVolume is not { } volume) return;
        _level = volume;
        Draw();
    }

    private void Drag(double x)
    {
        var travel = Math.Max(_bar.ActualWidth - Knob, 1);
        _status.SetOutputVolume(Math.Clamp((x - Knob / 2) / travel, 0, 1));
        // A fixed volume doesn't follow the knob, so the knob stays where the volume is.
        _level = _status.OutputVolume ?? _level;
        Draw();
    }

    private void Draw()
    {
        var travel = Math.Max(_bar.ActualWidth - Knob, 0);
        _knob.Margin = new Thickness(travel * _level, 0, 0, 0);
        _fill.Width = travel * _level + Knob / 2;
        _speaker.Glyph = StatusGlyphs.Speaker(HudLook.Speaker(_level, muted: false));
        _value.Text = HudLook.SliderPercent(_level).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
