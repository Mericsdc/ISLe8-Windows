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
            if (!File.Exists(FilePath)) return new AppSettings();
            var settings = JsonSerializer.Deserialize(File.ReadAllText(FilePath), IsleJsonContext.Default.AppSettings)
                ?? new AppSettings();
            settings.Normalize();
            return settings;
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
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, IsleJsonContext.Default.AppSettings));
            File.Move(temporary, FilePath, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Error(e, "settings");
        }
    }
}
