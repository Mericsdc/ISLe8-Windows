using System.Text.Json;
using System.Text.Json.Serialization;
using ISLe8.Core.Diagnostics;

namespace ISLe8.Core.Notes;

public sealed record Note(Guid Id, string Text, DateTimeOffset Updated);

/// <summary>
/// Quick notes on the island (NotesModule.swift): type one in, click it to copy it. Kept in
/// notes.json in ISLe8's folder under %LOCALAPPDATA%, which only the user can read. Nothing runs
/// between edits; a few kilobytes at most, so each change is written straight away.
/// </summary>
public sealed class NotesStore
{
    private readonly string _path;
    private readonly Func<DateTimeOffset> _now;
    private List<Note> _notes = [];

    public NotesStore(string folder, Func<DateTimeOffset> now)
    {
        _path = Path.Combine(folder, "notes.json");
        _now = now;
        try
        {
            if (File.Exists(_path))
            {
                _notes = JsonSerializer.Deserialize(File.ReadAllText(_path), NotesJson.Default.ListNote) ?? [];
            }
        }
        catch (JsonException e)
        {
            Log.Error(e, "notes");
            Clipboard.ClipboardHistory.SetAside(_path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Error(e, "notes");
        }
    }

    /// <summary>Newest first.</summary>
    public IReadOnlyList<Note> Notes => _notes;

    public event Action? Changed;

    public void Add(string text)
    {
        text = Clean(text);
        if (text.Length == 0) return;
        _notes.Insert(0, new Note(Guid.NewGuid(), text, _now()));
        Save();
    }

    /// <summary>Trimmed, every line ending in \n: the Windows text box breaks lines with \r.</summary>
    public static string Clean(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();

    /// <summary>Emptying a note deletes it.</summary>
    public void Update(Guid id, string text)
    {
        var index = _notes.FindIndex(note => note.Id == id);
        if (index < 0) return;
        text = Clean(text);
        if (text.Length == 0) _notes.RemoveAt(index);
        else _notes[index] = _notes[index] with { Text = text, Updated = _now() };
        Save();
    }

    public void Delete(Guid id)
    {
        if (_notes.RemoveAll(note => note.Id == id) > 0) Save();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(_notes, NotesJson.Default.ListNote));
            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Error(e, "notes save");
        }
        Changed?.Invoke();
    }
}

/// <summary>
/// "şimdi", "5 dakika önce", "dün", "3 hafta önce": the Mac's relative date (.relative(presentation:
/// .named)), which .NET has no formatter for. Gives the unit and the count; the app words it.
/// </summary>
public static class RelativeTime
{
    public enum Unit
    {
        Now,
        Minutes,
        Hours,
        Yesterday,
        Days,
        Weeks,
        Months,
        Years,
    }

    public static (Unit Unit, int Count) Of(DateTimeOffset then, DateTimeOffset now)
    {
        var ago = now - then;
        if (ago < TimeSpan.FromMinutes(1)) return (Unit.Now, 0);
        if (ago < TimeSpan.FromHours(1)) return (Unit.Minutes, (int)ago.TotalMinutes);
        var days = (DateOnly.FromDateTime(now.LocalDateTime).DayNumber - DateOnly.FromDateTime(then.LocalDateTime).DayNumber);
        if (days == 0) return (Unit.Hours, (int)ago.TotalHours);
        if (days == 1) return (Unit.Yesterday, 1);
        if (days < 7) return (Unit.Days, days);
        if (days < 30) return (Unit.Weeks, days / 7);
        if (days < 365) return (Unit.Months, days / 30);
        return (Unit.Years, days / 365);
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(List<Note>))]
internal sealed partial class NotesJson : JsonSerializerContext;
