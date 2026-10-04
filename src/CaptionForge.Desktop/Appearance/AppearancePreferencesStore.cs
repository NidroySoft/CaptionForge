using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CaptionForge.Desktop.Appearance;

internal sealed class AppearancePreferencesStore(string path)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    public AppearancePreferences Load()
    {
        if (!File.Exists(path)) return new();
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<AppearancePreferences>(stream, Options) ?? new();
    }
    public void Save(AppearancePreferences preferences)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string staged = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(staged, JsonSerializer.Serialize(preferences, Options));
            File.Move(staged, path, overwrite: true);
        }
        finally { if (File.Exists(staged)) File.Delete(staged); }
    }
}
