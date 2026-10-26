using ISLe8.Core.Geometry;

namespace ISLe8.Core.Island;

/// <summary>
/// Where the open island takes the hover, with the Mac's slack (IslandPanel.swift, setIslandSize):
/// when the island gets shorter (minimal Home after a taller tab), the pointer that clicked the
/// dock is suddenly below it. Until the pointer moves into the new outline, the old one still
/// counts, so the island doesn't close under it. Leaving both ends the slack too.
/// </summary>
public sealed class HoverOutline
{
    public PixelRect Current { get; private set; }

    /// <summary>The previous, bigger outline while it still counts.</summary>
    public PixelRect? Slack { get; private set; }

    /// <summary>The island changed size. Shrinking away from a pointer that was inside keeps the old outline as slack.</summary>
    public void Resize(PixelRect next, int pointerX, int pointerY)
    {
        var shrinking = next.Width * next.Height < Current.Width * Current.Height;
        Slack = shrinking && Current.Contains(pointerX, pointerY) && !next.Contains(pointerX, pointerY) ? Current : null;
        Current = next;
    }

    /// <summary>Starts over without slack: the island opened, closed or moved.</summary>
    public void Reset(PixelRect outline)
    {
        Current = outline;
        Slack = null;
    }

    /// <summary>Whether the pointer at (<paramref name="x"/>, <paramref name="y"/>) is on the island; settles the slack as it goes.</summary>
    public bool Contains(int x, int y)
    {
        if (Current.Contains(x, y))
        {
            Slack = null;
            return true;
        }
        if (Slack is { } slack && slack.Contains(x, y)) return true;
        Slack = null;
        return false;
    }
}
