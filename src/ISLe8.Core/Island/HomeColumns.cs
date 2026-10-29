using ISLe8.Core.Settings;

namespace ISLe8.Core.Island;

/// <summary>
/// Home's columns side by side at full height (HomeView.swift). On the dashboard each column is a
/// card and calendar, media and weather share the width about evenly, the timer narrow. The compact
/// and minimal strips are flat, split by hairlines, with the player in the middle (<see cref="HomeStrip"/>).
/// </summary>
public static class HomeColumns
{
    /// <summary>The Mac's ids (SettingsStore.swift, allHomeTiles), in the order "add column" offers them.</summary>
    private static readonly string[] Ids = ["calendar", "media", "weather", "timer"];

    public static IReadOnlyList<string> AllIds => Ids;

    /// <summary>A card gap on the dashboard; a hairline with room on both sides in the strips.</summary>
    public static double Spacing(HomeStyle style) => style == HomeStyle.Dashboard ? 10 : HomeStrip.Gap;

    /// <summary>The dashboard's share of the width for each column.</summary>
    public static double Weight(string id) => id switch
    {
        "media" => 1.1,
        "timer" => 0.62,
        _ => 1,
    };

    /// <summary>Each column's width in <paramref name="width"/>, after the gaps between them.</summary>
    public static IReadOnlyList<double> Widths(HomeStyle style, IReadOnlyList<string> columns, double width)
    {
        if (columns.Count == 0) return [];
        if (style != HomeStyle.Dashboard) return HomeStrip.Widths(columns, style, width);
        var total = columns.Sum(Weight);
        var free = Math.Max(width - (columns.Count - 1) * Spacing(style), 0);
        return columns.Select(id => free * Weight(id) / total).ToList();
    }

    /// <summary>Columns not on Home yet, for "add column".</summary>
    public static IReadOnlyList<string> Missing(AppSettings settings) => Ids.Where(id => !settings.HomeTiles.Contains(id)).ToList();
}

/// <summary>
/// How the compact and minimal strips share their width (HomeView.swift, HomeStrip). Each column
/// gets what it needs; the player sits in the middle, under the pill, and the columns either side
/// of it get equal halves. When the two sides would be very uneven (the timer on one of them), the
/// columns just go side by side.
/// </summary>
public static class HomeStrip
{
    /// <summary>Between two columns: a 1-wide hairline with 10 on either side.</summary>
    public const double Gap = 21;

    /// <summary>The island is this much wider than its strip (the Mac's shoulders and margins).</summary>
    public const double SideRoom = 48;

    /// <summary>The sides may differ by this much and the player still sits in the middle.</summary>
    private const double MostUneven = 80;

    public static double Need(HomeStyle style, string id) => (style, id) switch
    {
        (HomeStyle.Compact, "calendar") => 270,
        (HomeStyle.Compact, "media") => 240,
        (HomeStyle.Compact, "weather") => 270,
        (HomeStyle.Compact, "timer") => 120,
        (HomeStyle.Minimal, "calendar") => 170,
        (HomeStyle.Minimal, "media") => 250,
        (HomeStyle.Minimal, "weather") => 190,
        (HomeStyle.Minimal, "timer") => 130,
        _ => 150,
    };

    /// <summary>The width the columns need, gaps included.</summary>
    public static double Width(IReadOnlyList<string> columns, HomeStyle style)
    {
        if (columns.Count == 0) return 0;
        if (Sides(columns, style) is var (left, right))
        {
            return Need(style, "media") + 2 * Math.Max(Span(left, style), Span(right, style));
        }
        return columns.Sum(id => Need(style, id)) + (columns.Count - 1) * Gap;
    }

    /// <summary>Each column's width in a strip <paramref name="total"/> wide.</summary>
    public static IReadOnlyList<double> Widths(IReadOnlyList<string> columns, HomeStyle style, double total)
    {
        if (columns.Count == 0) return [];
        if (Sides(columns, style) is var (left, right))
        {
            var media = Need(style, "media");
            var half = (total - media) / 2;
            return [.. Share(left, half - left.Count * Gap, style), media, .. Share(right, half - right.Count * Gap, style)];
        }
        return Share(columns, total - (columns.Count - 1) * Gap, style);
    }

    /// <summary>The columns left and right of the player, when it can sit in the middle.</summary>
    private static (IReadOnlyList<string> Left, IReadOnlyList<string> Right)? Sides(IReadOnlyList<string> columns, HomeStyle style)
    {
        var index = columns.IndexOf("media");
        if (index <= 0 || index >= columns.Count - 1) return null;
        var left = columns.Take(index).ToList();
        var right = columns.Skip(index + 1).ToList();
        return Math.Abs(Span(left, style) - Span(right, style)) <= MostUneven ? (left, right) : null;
    }

    /// <summary>One side of the player: its columns, each with the gap that separates it.</summary>
    private static double Span(IEnumerable<string> columns, HomeStyle style) => columns.Sum(id => Need(style, id) + Gap);

    /// <summary><paramref name="room"/> split in proportion to what each column needs.</summary>
    private static List<double> Share(IReadOnlyList<string> columns, double room, HomeStyle style)
    {
        var total = columns.Sum(id => Need(style, id));
        return columns.Select(id => Need(style, id) / Math.Max(total, 1) * room).ToList();
    }

    private static int IndexOf(this IReadOnlyList<string> list, string item)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] == item) return i;
        }
        return -1;
    }
}
