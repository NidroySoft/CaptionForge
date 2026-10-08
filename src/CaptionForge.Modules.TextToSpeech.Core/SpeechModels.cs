namespace CaptionForge.Modules.TextToSpeech.Core;

public enum SpeechEngine { Kokoro, Pocket, Nano, ChatterboxMultilingual }
public sealed record SpeechSynthesisRequest(
    SpeechEngine Engine, string Language, string Text, string? Voice, string? ReferencePath,
    string PythonExecutable, string ModelDirectory, string OutputDirectory,
    int Seed = 42, double Speed = 1, double Exaggeration = 0.5, int CpuThreads = 4);
public sealed record SpeechProgress(string Message, double Fraction = 0);
public sealed record SpeechSynthesisResult(string AudioPath, double DurationSeconds, double ElapsedSeconds, int SampleRate);

public static class SpeechEngineCatalog
{
    public static bool UsesReference(SpeechEngine engine) => engine is SpeechEngine.Nano or SpeechEngine.ChatterboxMultilingual;
    public static IReadOnlyList<string> GetVoices(SpeechEngine engine, string language, string directory)
    {
        if (!Directory.Exists(directory) || UsesReference(engine)) return [];
        string path = engine == SpeechEngine.Kokoro ? Path.Combine(directory, "voices") :
            Path.Combine(directory, "languages", language == "es" ? "spanish" : "english", "embeddings");
        if (!Directory.Exists(path)) return [];
        return Directory.EnumerateFiles(path, engine == SpeechEngine.Kokoro ? "*.pt" : "*.safetensors")
            .Select(Path.GetFileNameWithoutExtension).OfType<string>()
            .Where(v => engine != SpeechEngine.Kokoro || (language == "es" ? v.StartsWith('e') : v.StartsWith('a') || v.StartsWith('b')))
            .OrderBy(v => v, StringComparer.Ordinal).ToArray();
    }

    public static void Validate(SpeechSynthesisRequest request)
    {
        if (!Enum.IsDefined(request.Engine)) throw new ArgumentException("Motor desconocido.");
        if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > 6000)
            throw new ArgumentException("Escribe un texto de entre 1 y 6.000 caracteres.");
        if (request.Language is not ("en" or "es") || (request.Engine == SpeechEngine.Nano && request.Language != "en"))
            throw new ArgumentException("Este motor no admite el idioma seleccionado.");
        if (!File.Exists(request.PythonExecutable)) throw new FileNotFoundException("Selecciona el Python del entorno de este motor.", request.PythonExecutable);
        if (!Directory.Exists(request.ModelDirectory)) throw new DirectoryNotFoundException("Selecciona la carpeta de los modelos descargados.");
        if (UsesReference(request.Engine) && !File.Exists(request.ReferencePath))
            throw new ArgumentException("Selecciona un audio de referencia limpio de más de cinco segundos.");
        if (!UsesReference(request.Engine) && !GetVoices(request.Engine, request.Language, request.ModelDirectory).Contains(request.Voice))
            throw new ArgumentException("Selecciona una voz instalada para este idioma.");
        if (!double.IsFinite(request.Speed) || request.Speed is < 0.7 or > 1.3 ||
            !double.IsFinite(request.Exaggeration) || request.Exaggeration is < 0 or > 1 || request.CpuThreads < 1)
            throw new ArgumentException("Revisa los parámetros de generación.");
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputDirectory);
    }
}
