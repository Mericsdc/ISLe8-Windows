using System.Text.Json;
using System.Text.Json.Serialization;
using ISLe8.Core.Diagnostics;

namespace ISLe8.Core.Shelf;

/// <summary>A file on the shelf (ShelfStore.swift, ShelfItem).</summary>
/// <param name="FileId">The file's NTFS identity, so it is found again after a move or rename (the Mac's bookmark).</param>
/// <param name="IsCopy">The file is a copy in ISLe8's own folder and is deleted together with the item.</param>
public sealed record ShelfItem(Guid Id, string Path, DateTimeOffset AddedAt, bool IsCopy, string? FileId = null)
{
    private static readonly string[] Images = [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".tif", ".tiff", ".webp", ".heic", ".heif", ".ico"];

    [JsonIgnore]
    public string Name => System.IO.Path.GetFileName(Path.TrimEnd('\\', '/'));

    [JsonIgnore]
    public bool IsImage => Images.Contains(System.IO.Path.GetExtension(Path), StringComparer.OrdinalIgnoreCase);
}

/// <summary>Finds files by their identity; the app does it with Win32, tests with a dictionary.</summary>
public interface IFileLocator
{
    /// <summary>The file's identity (volume and file ID), or null when it can't be read.</summary>
    string? IdOf(string path);

    /// <summary>Where the file with <paramref name="id"/> is now, or null when it is gone.</summary>
    string? PathOf(string id);
}

/// <summary>
/// The files on the shelf (ShelfStore.swift). Items point at the files themselves and follow them
/// through renames and moves on the same drive (by NTFS file ID, as the Mac does by bookmark). With
/// "copy" on, a dropped file is copied into ISLe8's folder first. Kept in shelf.json.
/// </summary>
public sealed class ShelfStore
{
    private readonly string _index;
    private readonly IFileLocator _locator;
    private readonly Func<DateTimeOffset> _now;
    private List<ShelfItem> _items = [];

    public ShelfStore(string folder, IFileLocator locator, Func<DateTimeOffset> now)
    {
        _index = Path.Combine(folder, "shelf.json");
        FilesFolder = Path.Combine(folder, "Shelf");
        _locator = locator;
        _now = now;
        Load();
    }

    /// <summary>Where copies (and zips or conversions of files whose folder is read-only) are kept.</summary>
    public string FilesFolder { get; }

    /// <summary>Newest first.</summary>
    public IReadOnlyList<ShelfItem> Items => _items;

    public event Action? Changed;

    /// <summary>Adds files to the front of the shelf. A file already on it just moves to the front.</summary>
    public void Add(IEnumerable<string> paths, bool copy)
    {
        foreach (var path in paths)
        {
            var target = path;
            if (copy && MakeCopy(path) is { } copied) target = copied;
            else if (copy) continue;
            var index = _items.FindIndex(item => string.Equals(item.Path, target, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                var existing = _items[index];
                _items.RemoveAt(index);
                _items.Insert(0, existing);
            }
            else
            {
                _items.Insert(0, new ShelfItem(Guid.NewGuid(), target, _now(), copy, _locator.IdOf(target)));
            }
        }
        Save();
    }

    /// <summary>
    /// Files the app already copied into a folder from <see cref="NewCopyFolder"/>, because ISLe8 could
    /// not open their own place (another packaged app's private folder): kept, and deleted, as copies.
    /// </summary>
    public void AddCopies(IEnumerable<string> copies)
    {
        foreach (var path in copies) _items.Insert(0, new ShelfItem(Guid.NewGuid(), path, _now(), true, _locator.IdOf(path)));
        Save();
    }

    /// <summary>A new, empty folder for one copy, as <see cref="Add"/> makes its own.</summary>
    public string NewCopyFolder()
    {
        var folder = Path.Combine(FilesFolder, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    public void Remove(Guid id)
    {
        var doomed = _items.Where(item => item.Id == id).ToList();
        if (doomed.Count == 0) return;
        DeleteCopies(doomed);
        _items.RemoveAll(item => item.Id == id);
        Save();
    }

    public void RemoveAll()
    {
        DeleteCopies(_items);
        _items.Clear();
        Save();
    }

    /// <summary>"name.ext", or "name 2.ext", "name 3.ext"… if that is taken (ShelfActions.swift, uniqueURL).</summary>
    public static string UniquePath(string folder, string name, string extension)
    {
        var candidate = Path.Combine(folder, $"{name}.{extension}");
        for (var counter = 2; File.Exists(candidate) || Directory.Exists(candidate); counter++)
        {
            candidate = Path.Combine(folder, $"{name} {counter}.{extension}");
        }
        return candidate;
    }

    /// <summary>Next to the file when that folder can be written to, else the shelf's own folder.</summary>
    public string WritableFolderNext(string path)
    {
        var folder = Path.GetDirectoryName(path.TrimEnd('\\', '/')) ?? FilesFolder;
        try
        {
            var probe = Path.Combine(folder, $".isle8-{Guid.NewGuid():N}");
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
            return folder;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Directory.CreateDirectory(FilesFolder);
            return FilesFolder;
        }
    }

    private string? MakeCopy(string path)
    {
        var folder = Path.Combine(FilesFolder, Guid.NewGuid().ToString("N"));
        var destination = Path.Combine(folder, Path.GetFileName(path.TrimEnd('\\', '/')));
        try
        {
            Directory.CreateDirectory(folder);
            if (Directory.Exists(path)) CopyFolder(path, destination);
            else File.Copy(path, destination);
            return destination;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Error(e, "shelf copy");
            return null;
        }
    }

    private static void CopyFolder(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (var folder in Directory.GetDirectories(source)) CopyFolder(folder, Path.Combine(destination, Path.GetFileName(folder)));
    }

    private static void DeleteCopies(IEnumerable<ShelfItem> items)
    {
        foreach (var item in items.Where(item => item.IsCopy))
        {
            try
            {
                if (Path.GetDirectoryName(item.Path) is { } folder && Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Error(e, "shelf delete");
            }
        }
    }

    /// <summary>A file that moved is followed by its identity; one that is gone leaves the shelf.</summary>
    private void Load()
    {
        try
        {
            if (!File.Exists(_index)) return;
            var saved = JsonSerializer.Deserialize(File.ReadAllText(_index), ShelfJson.Default.ListShelfItem) ?? [];
            foreach (var item in saved)
            {
                if (File.Exists(item.Path) || Directory.Exists(item.Path))
                {
                    _items.Add(item);
                }
                else if (item.FileId is { } id && _locator.PathOf(id) is { } moved)
                {
                    _items.Add(item with { Path = moved });
                }
            }
        }
        catch (JsonException e)
        {
            Log.Error(e, "shelf");
            Clipboard.ClipboardHistory.SetAside(_index);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Error(e, "shelf");
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_index)!);
            var temporary = _index + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(_items, ShelfJson.Default.ListShelfItem));
            File.Move(temporary, _index, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Error(e, "shelf save");
        }
        Changed?.Invoke();
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(List<ShelfItem>))]
internal sealed partial class ShelfJson : JsonSerializerContext;
