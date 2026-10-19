using System.Text.Json;
using Akses.Core;

namespace Akses.App;

/// <summary>The few things Akses remembers, in %LOCALAPPDATA%\Akses\settings.json.</summary>
internal sealed class Settings
{
    public static readonly string DataFolder = CreateDataFolder();

    private static readonly string FilePath = Path.Combine(DataFolder, "settings.json");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static Settings Current { get; } = Load();

    /// <summary>Fixed output folder; null = next to the original file.</summary>
    public string? OutputFolder { get; set; }
    public Quality Quality { get; set; } = Quality.High;
    /// <summary>Longest side for images and videos in the convert screen; null = original size.</summary>
    public int? MaxSide { get; set; }

    public ConversionOptions ToOptions() => new() { Quality = Quality, MaxSide = MaxSide, OutputFolder = OutputFolder };

    private static string CreateDataFolder()
    {
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Akses");
        try { Directory.CreateDirectory(folder); } catch { }
        return folder;
    }

    private static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), Json) ?? new Settings();
        }
        catch (Exception ex)
        {
            AppLog.Write($"Impostazioni illeggibili, uso quelle predefinite: {ex.Message}");
        }
        return new Settings();
    }

    public void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json)); }
        catch (Exception ex) { AppLog.Write($"Salvataggio impostazioni fallito: {ex.Message}"); }
    }
}
