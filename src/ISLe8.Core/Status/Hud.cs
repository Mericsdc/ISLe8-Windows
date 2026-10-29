namespace ISLe8.Core.Status;

public enum HudKind
{
    Volume,
    Brightness,
}

/// <summary>What a volume or brightness HUD shows (HUDModule.swift, HUDValue).</summary>
public readonly record struct HudValue(HudKind Kind, double Level, bool IsMuted = false)
{
    /// <summary>The bar's fill, 0–1: empty while muted.</summary>
    public double Shown => IsMuted ? 0 : Math.Clamp(Level, 0, 1);
}

public enum SpeakerLevel
{
    Muted,
    Low,
    Medium,
    High,
}

/// <summary>The HUD's symbols and figure (SystemViews.swift, HUDAlertView).</summary>
public static class HudLook
{
    /// <summary>speaker.slash / wave.1 / wave.2 / wave.3: muted or silent, under a third, under two thirds, above.</summary>
    public static SpeakerLevel Speaker(double level, bool muted) =>
        muted || level <= 0 ? SpeakerLevel.Muted
        : level < 0.34 ? SpeakerLevel.Low
        : level < 0.67 ? SpeakerLevel.Medium
        : SpeakerLevel.High;

    /// <summary>sun.min under half, sun.max from half up.</summary>
    public static bool IsDim(double level) => level < 0.5;

    /// <summary>The HUD's percentage, as Foundation's percent format with no fraction rounds it (halves to even).</summary>
    public static int Percent(double shown) => (int)Math.Round(Math.Clamp(shown, 0, 1) * 100, MidpointRounding.ToEven);

    /// <summary>The volume slider's figure, rounded as Swift's rounded() does (halves away from zero).</summary>
    public static int SliderPercent(double volume) => (int)Math.Round(Math.Clamp(volume, 0, 1) * 100, MidpointRounding.AwayFromZero);
}

/// <summary>
/// The volume's last known value. The audio endpoint reports the same value more than once; only a
/// real change brings up the HUD (HUDModule.swift, volumeChanged).
/// </summary>
public sealed class VolumeFilter
{
    public const double Threshold = 0.001;

    private (double Level, bool Muted)? _last;

    /// <summary>The value when listening starts, or the default device changed: not a change of its own.</summary>
    public void Reset(double? level, bool muted) => _last = level is { } value ? (value, muted) : null;

    /// <summary>Whether this is a real change; it becomes the last known value if so.</summary>
    public bool IsChange(double level, bool muted)
    {
        if (_last is { } last && Math.Abs(last.Level - level) < Threshold && last.Muted == muted) return false;
        _last = (level, muted);
        return true;
    }
}

/// <summary>
/// Only the brightness keys bring up the brightness HUD (HUDModule.swift, brightnessChanged).
/// Automatic brightness, dimming when idle and waking the screen change the level too, but with no
/// key pressed just before.
/// </summary>
public static class BrightnessKeys
{
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(1.2);

    public static bool ShowsHud(DateTimeOffset now, DateTimeOffset? lastKey) =>
        lastKey is { } key && now >= key && now - key < Window;
}
