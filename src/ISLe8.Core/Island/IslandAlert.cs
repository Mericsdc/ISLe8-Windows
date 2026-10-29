namespace ISLe8.Core.Island;

/// <summary>
/// A short-lived banner on the island (IslandModel.swift, IslandAlert). Each kind arrives with its
/// module: the song now, battery and timer later in W1, notifications and agents after that.
/// </summary>
public abstract record IslandAlert;

/// <summary>A new song started playing.</summary>
public sealed record NowPlayingAlert : IslandAlert;

/// <summary>How long each banner stays, from the Mac (AppDelegate.swift; PORTING.md §2).</summary>
public static class AlertDurations
{
    public static readonly TimeSpan NowPlaying = TimeSpan.FromSeconds(2.5);
    public static readonly TimeSpan Battery = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan Timer = TimeSpan.FromSeconds(4);
    public static readonly TimeSpan Meeting = TimeSpan.FromSeconds(10);
}
