using System.Runtime.InteropServices;
using ISLe8.Core.Geometry;
using ISLe8.Interop;

namespace ISLe8.Shell;

/// <summary>
/// Screenshot tools and Game Bar cover the screen with a window of their own while you pick an area
/// (Win+Shift+S, Print Screen, ShareX…). That is not a full-screen app or a game leaving: the island stays
/// as it is, so it can be in the picture.
/// </summary>
internal static unsafe partial class CaptureTools
{
    private static readonly HashSet<string> Programs = new(StringComparer.OrdinalIgnoreCase)
    {
        "ScreenClippingHost", "SnippingTool", "ScreenSketch", "ShareX", "Lightshot", "Greenshot", "PicPick", "Snagit32",
        "SnagitCapture", "SnagitEditor", "Flameshot", "Gyazo", "GyazoWin", "GameBar", "GameBarFTServer", "XboxGameBarWidgets",
        "PowerToys.PowerOCR", "PowerToys.MeasureToolUI", "PowerToys.ScreenRuler",
    };

    public static bool Owns(nint window) =>
        window != 0 && GameWatch.Program(window) is { } program && Programs.Contains(Path.GetFileNameWithoutExtension(program));

    /// <summary>
    /// Whether the uppermost window covering <paramref name="bounds"/> (besides the island) is a capture tool's. The shell's
    /// full-screen notice can come before the tool's window has the focus.
    /// </summary>
    public static bool CoverScreen(nint island, PixelRect bounds)
    {
        for (var window = GetTopWindow(0); window != 0; window = GetWindow(window, 2 /* GW_HWNDNEXT */))
        {
            if (window == island || !Win32.IsWindowVisible(window) || Cloaked(window) || !GetWindowRect(window, out var rect)) continue;
            if (rect.Left <= bounds.X && rect.Top <= bounds.Y && rect.Right >= bounds.Right && rect.Bottom >= bounds.Bottom) return Owns(window);
        }
        return false;
    }

    /// <summary>A window Windows keeps but doesn't draw (a suspended app's, another desktop's).</summary>
    private static bool Cloaked(nint window)
    {
        int cloaked;
        return DwmGetWindowAttribute(window, 14 /* DWMWA_CLOAKED */, &cloaked, sizeof(int)) == 0 && cloaked != 0;
    }

    [LibraryImport("user32.dll")]
    private static partial nint GetTopWindow(nint parent);

    [LibraryImport("user32.dll")]
    private static partial nint GetWindow(nint window, uint command);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint window, out RECT rect);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmGetWindowAttribute(nint window, uint attribute, void* value, uint size);
}
