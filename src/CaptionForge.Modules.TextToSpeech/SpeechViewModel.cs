using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using CaptionForge.Modules.TextToSpeech.Core;

namespace CaptionForge.Modules.TextToSpeech;

public sealed record EngineChoice(SpeechEngine Engine, string Name) { public override string ToString() => Name; }
public sealed record LanguageChoice(string Code, string Name) { public override string ToString() => Name; }

public sealed class SpeechViewModel : INotifyPropertyChanged
{
    private readonly SpeechSettings _settings;
    public static string ModuleDirectory => Path.GetDirectoryName(typeof(SpeechViewModel).Assembly.Location)!;
    private readonly PythonSpeechSynthesisService _service = new(Path.Combine(ModuleDirectory, "Backend", "tts_worker.py"));
    private CancellationTokenSource? _cts;
    private Task? _running;
    private EngineChoice _engine;
    private LanguageChoice _language;
    private string _text = "Ever hear your recorded voice and wonder why it sounds like a stranger?", _voice = "", _status = "Listo", _output = "";
    private bool _busy;
    private double _progress, _speed = 1, _exaggeration = 0.5;
    private int _seed = 42;
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    private void NotifyAll() => PropertyChanged?.Invoke(this, new(null));
    public IReadOnlyList<EngineChoice> Engines { get; } = [new(SpeechEngine.Kokoro, "Kokoro"), new(SpeechEngine.Pocket, "Pocket TTS (sin clonación)"), new(SpeechEngine.Nano, "Chatterbox Nano"), new(SpeechEngine.ChatterboxMultilingual, "Chatterbox Multilingual V3")];
    private static readonly IReadOnlyList<LanguageChoice> Bilingual = [new("en", "Inglés"), new("es", "Español")];
    private static readonly IReadOnlyList<LanguageChoice> English = [Bilingual[0]];
    public IReadOnlyList<LanguageChoice> Languages => SelectedEngine.Engine == SpeechEngine.Nano ? English : Bilingual;
    public ObservableCollection<string> Voices { get; } = [];
    public SpeechViewModel()
    {
        try { _settings = SpeechSettings.Load(); }
        catch (Exception e) when (e is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
        { _settings = SpeechSettings.Defaults(); _status = "No se pudo leer la configuración de voz: " + e.Message; }
        _engine = Engines.Single(e => e.Engine == _settings.SelectedEngine);
        _language = Languages.FirstOrDefault(l => l.Code == _settings.Language) ?? Languages[0];
        RefreshVoices();
    }
    public EngineChoice SelectedEngine
    {
        get => _engine;
        set
        {
            if (value is null || value == _engine || IsBusy) return;
            _engine = value; _settings.SelectedEngine = value.Engine;
            _language = Languages.FirstOrDefault(l => l.Code == _language.Code) ?? Languages[0];
            _settings.Language = _language.Code; RefreshVoices(); NotifyAll();
        }
    }
    public LanguageChoice SelectedLanguage
    {
        get => _language;
        set { if (value is null || value.Code == _language.Code || IsBusy) return; _language = value; _settings.Language = value.Code; RefreshVoices(); NotifyAll(); }
    }
    private SpeechRuntimeSettings Runtime => _settings.Engines[SelectedEngine.Engine];
    public string PythonExecutable { get => Runtime.PythonExecutable; set { Runtime.PythonExecutable = value; NotifyAll(); } }
    public string ModelDirectory { get => Runtime.ModelDirectory; set { Runtime.ModelDirectory = value; RefreshVoices(); NotifyAll(); } }
    public string OutputDirectory { get => _settings.OutputDirectory; set { _settings.OutputDirectory = value; Notify(); } }
    public string ReferencePath { get => _settings.ReferencePath; set { _settings.ReferencePath = value; NotifyAll(); } }
    public string Text { get => _text; set { _text = value; Notify(); } }
    public string SelectedVoice { get => _voice; set { _voice = value ?? ""; _settings.Voices[VoiceKey] = _voice; Notify(); } }
    private string VoiceKey => $"{SelectedEngine.Engine}:{SelectedLanguage.Code}";
    public int Seed { get => _seed; set { _seed = value; Notify(); } }
    public double Speed { get => _speed; set { _speed = value; Notify(); } }
    public double Exaggeration { get => _exaggeration; set { _exaggeration = value; Notify(); } }
    public bool UsesReference => SpeechEngineCatalog.UsesReference(SelectedEngine.Engine);
    public bool UsesPreset => !UsesReference;
    public bool IsKokoro => SelectedEngine.Engine == SpeechEngine.Kokoro;
    public bool IsMultilingual => SelectedEngine.Engine == SpeechEngine.ChatterboxMultilingual;
    public bool IsBusy { get => _busy; private set { _busy = value; NotifyAll(); } }
    public bool CanEdit => !IsBusy;
    public bool CanGenerate => !IsBusy && File.Exists(PythonExecutable) && Directory.Exists(ModelDirectory) && (UsesReference ? File.Exists(ReferencePath) : Voices.Count > 0);
    public string InstallationStatus => !File.Exists(PythonExecutable) ? "Este motor necesita preparación. Pulsa «Instalar motor»." : !Directory.Exists(ModelDirectory) ? "No se encuentra el modelo. Instálalo o selecciona su carpeta." : UsesPreset && Voices.Count == 0 ? "No hay voces del idioma seleccionado en esta carpeta. Usa «Detectar instalaciones» o instala el motor." : UsesPreset ? $"{Voices.Count} voces locales disponibles · {ModelDirectory}" : "Motor local disponible. Selecciona una referencia de voz.";
    private WaveformData? _waveform;
    public WaveformData? Waveform { get => _waveform; private set { _waveform = value; Notify(); } }
    public bool HasOutput => File.Exists(OutputPath);
    public string OutputPath { get => _output; private set { _output = value; NotifyAll(); } }
    public string Status { get => _status; private set { _status = value; Notify(); } }
    public double Progress { get => _progress; private set { _progress = value; Notify(); } }
    public string Hint => SelectedEngine.Engine switch
    {
        SpeechEngine.Kokoro => "Voces predefinidas. Usa texto limpio. Las voces af/am son americanas y bf/bm británicas.",
        SpeechEngine.Pocket => "Versión pública sin clonación. Usa texto limpio y una voz predefinida del idioma seleccionado.",
        SpeechEngine.Nano => "Solo inglés. Audio de referencia limpio de 6–15 segundos. Puedes probar [chuckle], [sigh] o [gasp].",
        _ => "Inglés y español con referencia de voz. Este modelo puede tardar varios minutos en CPU."
    };
    public void RefreshVoices()
    {
        var remembered = _settings.Voices.GetValueOrDefault(VoiceKey);
        Voices.Clear();
        try { foreach (var voice in SpeechEngineCatalog.GetVoices(SelectedEngine.Engine, SelectedLanguage.Code, ModelDirectory)) Voices.Add(voice); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { Status = "No se pudieron leer las voces: " + e.Message; }
        string preferred = SelectedEngine.Engine == SpeechEngine.Kokoro ? (SelectedLanguage.Code == "es" ? "ef_dora" : "af_heart") : (SelectedLanguage.Code == "es" ? "lola" : "alba");
        SelectedVoice = remembered is not null && Voices.Contains(remembered) ? remembered : Voices.Contains(preferred) ? preferred : Voices.FirstOrDefault() ?? "";
        Notify(nameof(InstallationStatus)); Notify(nameof(CanGenerate));
    }
    public void DetectInstallations()
    {
        if (IsBusy) return;
        var defaults = SpeechSettings.Defaults();
        foreach (var (engine, runtime) in defaults.Engines)
            if (File.Exists(runtime.PythonExecutable) && Directory.Exists(runtime.ModelDirectory)) _settings.Engines[engine] = runtime;
        RefreshVoices(); NotifyAll(); Status = "Instalaciones locales revisadas.";
    }
    public Task InstallAsync()
    {
        if (IsBusy) return Task.CompletedTask;
        _running = InstallCoreAsync(); return _running;
    }
    private async Task InstallCoreAsync()
    {
        IsBusy = true; Progress = 0;
        using var cts = new CancellationTokenSource(); _cts = cts;
        try
        {
            var installer = new EngineInstaller(Path.Combine(ModuleDirectory, "Backend"), SpeechSettings.InstallationRoot);
            var installed = await installer.InstallAsync(SelectedEngine.Engine, new Progress<SpeechProgress>(p => { Status = p.Message; Progress = p.Fraction * 100; }), cts.Token);
            Runtime.PythonExecutable = installed.PythonExecutable; Runtime.ModelDirectory = installed.ModelDirectory;
            RefreshVoices(); _settings.Save(); Status = "Motor instalado. Ya puedes generar voz.";
        }
        catch (OperationCanceledException) { Status = "Instalación cancelada. Puedes volver a intentarlo."; }
        catch (Exception e) { Status = "No se pudo preparar el motor: " + e.Message; }
        finally { _cts = null; IsBusy = false; }
    }
    public void SaveSettings()
    {
        try { _settings.Save(); Status = "Configuración de este módulo guardada."; }
        catch (Exception e) { Status = "No se pudo guardar la configuración: " + e.Message; }
    }
    public Task GenerateAsync()
    {
        if (IsBusy) return Task.CompletedTask;
        _running = GenerateCoreAsync();
        return _running;
    }
    private async Task GenerateCoreAsync()
    {
        IsBusy = true; Progress = 0;
        using var cts = new CancellationTokenSource(); _cts = cts;
        try
        {
            var request = new SpeechSynthesisRequest(SelectedEngine.Engine, SelectedLanguage.Code, Text, SelectedVoice, ReferencePath,
                PythonExecutable, ModelDirectory, OutputDirectory, Seed, Speed, Exaggeration, Math.Min(4, Environment.ProcessorCount));
            SpeechEngineCatalog.Validate(request);
            _settings.Save();
            var result = await _service.GenerateAsync(request, new Progress<SpeechProgress>(p => { Status = p.Message; Progress = p.Fraction * 100; }), cts.Token);
            OutputPath = result.AudioPath;
            Waveform = await Task.Run(() => WaveformData.Read(result.AudioPath), cts.Token);
            Status = $"Audio listo: {result.DurationSeconds:F1} s · Generación y carga: {result.ElapsedSeconds:F1} s";
        }
        catch (OperationCanceledException) { Status = "Generación cancelada."; }
        catch (Exception e) { Status = "No se pudo generar el audio: " + e.Message; }
        finally { _cts = null; IsBusy = false; }
    }
    public void Cancel() => _cts?.Cancel();
    public async Task LoadPreviewAsync(string path) { Waveform = await Task.Run(() => WaveformData.Read(path)); OutputPath = path; }
    public async Task ShutdownAsync() { Cancel(); if (_running is not null) await _running; }
}
