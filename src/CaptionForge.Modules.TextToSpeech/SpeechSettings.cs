using System.IO;
using System.Text.Json;
using CaptionForge.Application.Models.Speech;

namespace CaptionForge.Modules.TextToSpeech;

public sealed class SpeechRuntimeSettings
{
    public string PythonExecutable { get; set; } = "";
    public string ModelDirectory { get; set; } = "";
}

public sealed class SpeechSettings
{
    public Dictionary<SpeechEngine, SpeechRuntimeSettings> Engines { get; set; } = [];
    public string OutputDirectory { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CaptionForge", "Speech", "Audio");
    public string ReferencePath { get; set; } = "";
    public SpeechEngine SelectedEngine { get; set; } = SpeechEngine.Kokoro;
    public string Language { get; set; } = "en";
    public Dictionary<string, string> Voices { get; set; } = [];
    public static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CaptionForge", "modules", "text-to-speech", "settings.json");

    public static SpeechSettings Defaults()
    {
        var settings = new SpeechSettings();
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Wondecode");
        foreach (var engine in Enum.GetValues<SpeechEngine>())
        {
            string runtime = engine == SpeechEngine.Pocket ? "PocketTTS" : "ChatterboxNano";
            string models = engine switch { SpeechEngine.Kokoro => "Kokoro", SpeechEngine.Pocket => "PocketTTS", SpeechEngine.Nano => "ChatterboxNano", _ => "ChatterboxMultilingual" };
            settings.Engines[engine] = new()
            {
                PythonExecutable = Path.Combine(root, runtime, ".venv", "Scripts", "python.exe"),
                ModelDirectory = Path.Combine(root, models, engine == SpeechEngine.Nano ? "modelo" : "paquete")
            };
        }
        return settings;
    }

    public static SpeechSettings Load()
    {
        var defaults = Defaults();
        if (!File.Exists(SettingsPath)) return defaults;
        var loaded = JsonSerializer.Deserialize<SpeechSettings>(File.ReadAllText(SettingsPath)) ?? defaults;
        loaded.Engines ??= [];
        loaded.Voices ??= [];
        foreach (var (key, value) in defaults.Engines)
            if (!loaded.Engines.TryGetValue(key, out var runtime) || runtime is null) loaded.Engines[key] = value;
        if (!Enum.IsDefined(loaded.SelectedEngine)) loaded.SelectedEngine = SpeechEngine.Kokoro;
        if (loaded.Language is not ("en" or "es")) loaded.Language = "en";
        return loaded;
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        string temporary = SettingsPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, SettingsPath, overwrite: true);
    }
}
