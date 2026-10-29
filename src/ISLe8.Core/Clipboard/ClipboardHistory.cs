using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ISLe8.Core.Diagnostics;

namespace ISLe8.Core.Clipboard;

[JsonConverter(typeof(Theming.LenientEnumConverter<ClipboardKind>))]
public enum ClipboardKind
{
    Text,
    RichText,
    Url,
    Image,
    Files,
}

/// <summary>What was read off the clipboard, ready to be stored (ClipboardItem.swift, ClipboardCapture).</summary>
public sealed record ClipboardCapture(ClipboardKind Kind, string Text, string? Rtf, byte[]? ImagePng, string Hash)
{
    /// <summary>Identifies the content, so copying the same thing again only moves it to the top.</summary>
    public static string HashOf(string kind, ReadOnlySpan<byte> data)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        sha.AppendData(Encoding.UTF8.GetBytes(kind));
        sha.AppendData(data);
        return Convert.ToHexStringLower(sha.GetHashAndReset());
    }

    public static ClipboardCapture ForText(string text, string? rtf) =>
        new(ClipboardRules.IsWebUrl(text) ? ClipboardKind.Url : rtf is null ? ClipboardKind.Text : ClipboardKind.RichText,
            text, ClipboardRules.IsWebUrl(text) ? null : rtf, null, HashOf("text", Encoding.UTF8.GetBytes(text)));

    public static ClipboardCapture ForFiles(IReadOnlyList<string> paths)
    {
        var text = string.Join("\n", paths);
        return new(ClipboardKind.Files, text, null, null, HashOf("files", Encoding.UTF8.GetBytes(text)));
    }

    public static ClipboardCapture ForImage(byte[] png) => new(ClipboardKind.Image, "", null, png, HashOf("image", png));
}

/// <summary>One entry of the history (ClipboardItem.swift).</summary>
public sealed record ClipboardEntry
{
    public required long Id { get; init; }
    public required ClipboardKind Kind { get; init; }
    /// <summary>The text, or the file paths one per line. Empty for images.</summary>
    public required string Text { get; init; }
    public string? Rtf { get; init; }
    public string? ImageName { get; init; }
    public required string Hash { get; init; }
    /// <summary>The program it was copied from, as its .exe name.</summary>
    public string? SourceApp { get; init; }
    public required DateTimeOffset CopiedAt { get; init; }
    public bool IsPinned { get; init; }

    /// <summary>One line for the list: whitespace runs collapsed, file paths reduced to names. Null for an image ("Görsel").</summary>
    [JsonIgnore]
    public string? Title => Kind switch
    {
        ClipboardKind.Image => null,
        ClipboardKind.Files => string.Join(", ", Text.Split('\n').Select(Path.GetFileName)),
        _ => string.Join(' ', Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)),
    };

    /// <summary>The colour, when the whole text is a hex colour such as #FF9F0A.</summary>
    [JsonIgnore]
    public (byte R, byte G, byte B)? Color
    {
        get
        {
            if (Kind != ClipboardKind.Text) return null;
            var hex = Text.Trim().TrimStart('#');
            if (hex.Length != 6 || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)) return null;
            return ((byte)(value >> 16), (byte)(value >> 8), (byte)value);
        }
    }

    [JsonIgnore]
    public string? EmailAddress => Kind == ClipboardKind.Text && ClipboardRules.IsEmail(Text.Trim()) ? Text.Trim() : null;
}

public static partial class ClipboardRules
{
    public const int MaxTextLength = 1_000_000;
    public const int MaxRtfSize = 2_000_000;
    public const int MaxImageSize = 25_000_000;

    /// <summary>
    /// Clipboard formats that mean "don't record this" (PORTING.md §3.9): password managers set them,
    /// and so does Windows' own clipboard history for content it must not keep.
    /// </summary>
    public static readonly string[] Excluding = ["ExcludeClipboardContentFromMonitorProcessing", "Clipboard Viewer Ignore"];

    /// <summary>Formats whose DWORD value 0 means "don't record".</summary>
    public static readonly string[] AllowFlags = ["CanIncludeInClipboardHistory", "CanUploadToCloudClipboard"];

    /// <summary>The Mac's defaults (password managers) by their Windows program names.</summary>
    public static IReadOnlyList<string> DefaultExcludedApps { get; } = ["1Password.exe", "Bitwarden.exe", "KeePass.exe", "KeePassXC.exe", "Dashlane.exe"];

    public static bool IsWebUrl(string text)
    {
        var trimmed = text.Trim();
        return !trimmed.Any(char.IsWhiteSpace)
            && Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https";
    }

    public static bool IsEmail(string text) => Email().IsMatch(text);

    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]+$")]
    private static partial Regex Email();

    /// <summary>
    /// Folds for search: case and accents go (ş → s, İ → i), and the dotless ı becomes i, which
    /// SQLite's tokenizer doesn't do on the Mac either; there a separate folded column does it.
    /// </summary>
    public static string Fold(string text)
    {
        var lowered = text.Replace('ı', 'i').Replace('I', 'i').Replace('İ', 'i').ToLowerInvariant();
        var decomposed = lowered.Normalize(NormalizationForm.FormD);
        var folded = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) folded.Append(c);
        }
        return folded.ToString();
    }

    /// <summary>Every word of the query starts a word of the text (FTS5's "term"* match).</summary>
    public static bool Matches(string foldedText, IReadOnlyList<string> foldedTerms)
    {
        if (foldedTerms.Count == 0) return true;
        var words = Words().Matches(foldedText).Select(m => m.Value).ToList();
        return foldedTerms.All(term => words.Any(word => word.StartsWith(term, StringComparison.Ordinal)));
    }

    public static IReadOnlyList<string> Terms(string query) =>
        Words().Matches(Fold(query)).Select(m => m.Value).ToList();

    /// <summary>Letters and digits, as FTS5's default tokenizer splits.</summary>
    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex Words();
}

/// <summary>
/// The clipboard history on disk (ClipboardStore.swift): clipboard.json beside a folder of PNG
/// images. The Mac keeps it in SQLite with an FTS5 index; for at most a thousand entries a JSON
/// file and a search in memory are as quick, and need no native library under Native AOT.
/// </summary>
public sealed class ClipboardHistory
{
    private readonly string _file;
    private List<ClipboardEntry> _entries = [];
    private long _nextId = 1;

    public ClipboardHistory(string folder)
    {
        _file = Path.Combine(folder, "clipboard.json");
        ImagesFolder = Path.Combine(folder, "Clipboard");
        try
        {
            Directory.CreateDirectory(ImagesFolder);
            IsAvailable = true;
            if (File.Exists(_file)) _entries = JsonSerializer.Deserialize(File.ReadAllText(_file), ClipboardJson.Default.ListClipboardEntry) ?? [];
        }
        catch (JsonException e)
        {
            // A damaged file is set aside, as a damaged settings file is, and the history starts again.
            Log.Error(e, "clipboard history");
            SetAside(_file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Error(e, "clipboard history");
            IsAvailable = false;
        }
        _nextId = _entries.Count == 0 ? 1 : _entries.Max(entry => entry.Id) + 1;
    }

    /// <summary>False when the folder or file couldn't be opened ("Pano geçmişi açılamadı").</summary>
    public bool IsAvailable { get; }

    /// <summary>Keeps a damaged file as .broken beside it, for a look later, so the next save can start clean.</summary>
    public static void SetAside(string file)
    {
        try
        {
            File.Copy(file, file + ".broken", overwrite: true);
            File.Delete(file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Error(e, "set aside");
        }
    }

    public string ImagesFolder { get; }

    public int Count => _entries.Count;

    /// <summary>Pinned on top, then newest first; <paramref name="query"/> matches word prefixes, ignoring case and accents.</summary>
    public IReadOnlyList<ClipboardEntry> Items(string query, int limit)
    {
        var terms = ClipboardRules.Terms(query);
        return _entries
            .Where(entry => terms.Count == 0 || entry.Kind != ClipboardKind.Image && ClipboardRules.Matches(ClipboardRules.Fold(entry.Text), terms))
            .OrderByDescending(entry => entry.IsPinned)
            .ThenByDescending(entry => entry.CopiedAt)
            .Take(limit)
            .ToList();
    }

    public ClipboardEntry? Find(long id) => _entries.FirstOrDefault(entry => entry.Id == id);

    /// <summary>
    /// Records a copy. The same content again only moves to the top with the new time and app.
    /// Then the history is trimmed to <paramref name="limit"/> and <paramref name="maxAge"/>; pinned entries stay.
    /// </summary>
    public void Record(ClipboardCapture capture, string? app, DateTimeOffset now, int limit, TimeSpan maxAge)
    {
        var index = _entries.FindIndex(entry => entry.Hash == capture.Hash);
        if (index >= 0)
        {
            _entries[index] = _entries[index] with { CopiedAt = now, SourceApp = app ?? _entries[index].SourceApp };
        }
        else
        {
            string? image = null;
            if (capture.ImagePng is { } png)
            {
                image = Guid.NewGuid().ToString("N") + ".png";
                try
                {
                    File.WriteAllBytes(Path.Combine(ImagesFolder, image), png);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    Log.Error(e, "clipboard image");
                    return;
                }
            }
            _entries.Add(new ClipboardEntry
            {
                Id = _nextId++,
                Kind = capture.Kind,
                Text = capture.Text,
                Rtf = capture.Rtf,
                ImageName = image,
                Hash = capture.Hash,
                SourceApp = app,
                CopiedAt = now,
            });
        }
        Prune(limit, maxAge, now);
        Save();
    }

    /// <summary>Copied back from the history: it moves to the top.</summary>
    public void Touch(long id, DateTimeOffset now) => Change(id, entry => entry with { CopiedAt = now });

    public void SetPinned(long id, bool pinned) => Change(id, entry => entry with { IsPinned = pinned });

    public void Delete(long id)
    {
        var removed = _entries.Where(entry => entry.Id == id).ToList();
        if (removed.Count == 0) return;
        Remove(removed);
        Save();
    }

    /// <summary>"Geçmişi temizle": everything but the pinned.</summary>
    public void Clear()
    {
        Remove(_entries.Where(entry => !entry.IsPinned).ToList());
        Save();
    }

    public void Prune(int limit, TimeSpan maxAge, DateTimeOffset now)
    {
        var cutoff = now - maxAge;
        var old = _entries.Where(entry => !entry.IsPinned && entry.CopiedAt < cutoff).ToList();
        var extra = _entries.Where(entry => !entry.IsPinned).Except(old).OrderByDescending(entry => entry.CopiedAt).Skip(limit).ToList();
        Remove([.. old, .. extra]);
    }

    private void Change(long id, Func<ClipboardEntry, ClipboardEntry> change)
    {
        var index = _entries.FindIndex(entry => entry.Id == id);
        if (index < 0) return;
        _entries[index] = change(_entries[index]);
        Save();
    }

    private void Remove(List<ClipboardEntry> doomed)
    {
        foreach (var entry in doomed)
        {
            _entries.Remove(entry);
            if (entry.ImageName is { } image)
            {
                try
                {
                    File.Delete(Path.Combine(ImagesFolder, image));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    Log.Error(e, "clipboard image delete");
                }
            }
        }
    }

    private void Save()
    {
        if (!IsAvailable) return;
        try
        {
            var temporary = _file + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(_entries, ClipboardJson.Default.ListClipboardEntry));
            File.Move(temporary, _file, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Error(e, "clipboard save");
        }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(List<ClipboardEntry>))]
internal sealed partial class ClipboardJson : JsonSerializerContext;
