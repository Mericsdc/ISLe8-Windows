using System.Text.Json;
using ISLe8.Core.Diagnostics;

namespace ISLe8.Core.Settings;

/// <summary>Settings as one JSON file. A broken file is set aside and ISLe8 starts with defaults.</summary>
public sealed class SettingsStore(string filePath)
{
    public string FilePath { get; } = filePath;

    public AppSettings Load()
    {
        try
        {
            return File.Exists(FilePath) ? Import(File.ReadAllText(FilePath)) : new AppSettings();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Log.Error(e, "settings");
            try
            {
                File.Copy(FilePath, FilePath + ".broken", overwrite: true);
            }
            catch (Exception copy) when (copy is IOException or UnauthorizedAccessException)
            {
            }
            return new AppSettings();
        }
    }

    /// <summary>Writes a temporary file and moves it over, so a crash never leaves half a file.</summary>
    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var temporary = FilePath + ".tmp";
            File.WriteAllText(temporary, Export(settings));
            File.Move(temporary, FilePath, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Error(e, "settings");
        }
    }

    /// <summary>Every setting as one JSON document: what settings.json holds and what Settings › Back up writes.</summary>
    public static string Export(AppSettings settings) => JsonSerializer.Serialize(settings, IsleJsonContext.Default.AppSettings);

    /// <summary>
    /// Reads settings written by <see cref="Export"/>, from this or an older version, brought back into
    /// range. Throws <see cref="JsonException"/> (or <see cref="NotSupportedException"/>) when the text is
    /// not a settings object.
    /// </summary>
    public static AppSettings Import(string json)
    {
        var settings = JsonSerializer.Deserialize(json, IsleJsonContext.Default.AppSettings)
            ?? throw new JsonException("Not ISLe8 settings");
        settings.Normalize();
        return settings;
    }
}
