using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using CaptionForge.Modules.MediaTools.Core;

namespace CaptionForge.Modules.MediaTools;

public sealed record MediaLanguage(string Code, string Name);
public sealed class MediaToolsViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? PreviewChanging;
    private readonly string tempRoot = Path.Combine(Path.GetTempPath(), "CaptionForge-media-tools", Guid.NewGuid().ToString("N"));
    private readonly string settingsPath;
    private CancellationTokenSource? cancellation;
    private Task? operation;
    private bool busy, loadingTracks, shuttingDown;
    private string source = "", preview = "", output = "", status = "Abre un audio o vídeo para empezar.", startTime = "00:00:00.000", endTime = "00:00:00.000";
    private AudioTrack? track;
    private AudioWaveform? waveform;
    private double start, end, progress;
    private Transcript? transcript;
    private double transcriptOffset;
    private string transcriptInfo = "", modelPath = "", ffmpeg = "ffmpeg", ffprobe = "ffprobe", format = "WAV";
    private int threads = Math.Max(1, Math.Min(6, Environment.ProcessorCount));
    private MediaLanguage language;
    public MediaToolsViewModel(string? settingsFile = null)
    {
        settingsPath = settingsFile ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CaptionForge", "modules", "media-tools", "settings.json");
        language = Languages[0];
        string appRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CaptionForge");
        ReadSettings(Path.Combine(appRoot, "desktop-tools.json"), false);
        ReadSettings(Path.Combine(appRoot, "settings.json"), false);
        ReadSettings(settingsPath, true);
    }
    public ObservableCollection<AudioTrack> Tracks { get; } = [];
    public IReadOnlyList<MediaLanguage> Languages { get; } = [new("auto", "Detectar idioma"), new("en", "English"), new("es", "Español"), new("fr", "Français"), new("pt", "Português"), new("de", "Deutsch"), new("it", "Italiano")];
    public IReadOnlyList<string> Formats { get; } = ["WAV", "MP3"];
    public string SourcePath => source;
    public string SourceName => source.Length == 0 ? "Sin archivo" : Path.GetFileName(source);
    public string PreviewPath => preview;
    public Task WhenIdleAsync() => operation ?? Task.CompletedTask;
    public string OutputPath => output;
    public string Status { get => status; private set { status = value; Changed(); } }
    public bool IsBusy { get => busy; private set { busy = value; Changed(); NotifyState(); } }
    public bool CanEdit => !IsBusy && !shuttingDown;
    public bool CanProcess => CanEdit && Waveform is not null && SelectedTrack is not null && SelectionValid;
    public bool CanSelect => CanEdit && Waveform is not null;
    public bool CanTranscribe => CanProcess && File.Exists(ModelPath);
    public bool CanPlay => CanSelect;
    public bool HasTranscript => transcript is { Segments.Count: > 0 };
    public bool CanExport => CanEdit && HasTranscript;
    public bool CanOpenOutput => CanEdit && File.Exists(output);
    public double Progress { get => progress; private set { progress = Math.Clamp(value, 0, 100); Changed(); } }
    public bool IsIndeterminate { get; private set; }
    public AudioWaveform? Waveform => waveform;
    public double Duration => waveform?.Duration ?? 0;
    public string DurationText => AudioRange.Format(Duration);
    public double RangeStart { get => start; set { start = Math.Clamp(value, 0, Duration); startTime = AudioRange.Format(start); Changed(); Changed(nameof(StartTime)); NotifyState(); } }
    public double RangeEnd { get => end; set { end = Math.Clamp(value, 0, Duration); endTime = AudioRange.Format(end); Changed(); Changed(nameof(EndTime)); NotifyState(); } }
    public string StartTime { get => startTime; set { startTime = value; if (AudioRange.TryParse(value, out double parsed)) start = parsed; Changed(); Changed(nameof(RangeStart)); NotifyState(); } }
    public string EndTime { get => endTime; set { endTime = value; if (AudioRange.TryParse(value, out double parsed)) end = parsed; Changed(); Changed(nameof(RangeEnd)); NotifyState(); } }
    public bool SelectionValid => AudioRange.TryParse(startTime, out double a) && AudioRange.TryParse(endTime, out double b) && a >= 0 && b <= Duration + 0.001 && b - a >= 0.02;
    public string SelectionText => SelectionValid ? $"Selección · {AudioRange.Format(end - start)} de {DurationText}" : "Escribe tiempos válidos dentro del audio (fin mayor que inicio).";
    public AudioRange SelectedRange { get { var range = new AudioRange(start, end); if (!SelectionValid) throw new ArgumentException(SelectionText); range.Validate(Duration); return range; } }
    public AudioTrack? SelectedTrack { get => track; set { if (Equals(track, value)) return; track = value; Changed(); if (!loadingTracks && value is not null && CanEdit) _ = LoadTrackAsync(); } }
    public string Format { get => format; set { format = value; Changed(); } }
    public string ModelPath { get => modelPath; set { modelPath = value; Changed(); Changed(nameof(CanTranscribe)); } }
    public string FfmpegPath { get => ffmpeg; set { ffmpeg = value; Changed(); } }
    public string FfprobePath { get => ffprobe; set { ffprobe = value; Changed(); } }
    public int CpuThreads { get => threads; set { threads = Math.Clamp(value, 1, Environment.ProcessorCount); Changed(); } }
    public MediaLanguage SelectedLanguage { get => language; set { language = value; Changed(); } }
    public bool OriginalTimestamps { get; set; }
    public string TranscriptText => transcript?.Text ?? "";
    public string TranscriptInfo => transcriptInfo;

    public Task LoadAsync(string path) => RunOperationAsync(async ct =>
    {
        PreviewChanging?.Invoke(); ClearResult(); waveform = null; preview = ""; NotifyMedia();
        source = path; Changed(nameof(SourcePath)); Changed(nameof(SourceName));
        loadingTracks = true;
        try
        {
            Tracks.Clear(); track = null; Changed(nameof(SelectedTrack));
            Status = "Leyendo pistas de audio…";
            var info = await Service.ProbeAsync(path, ct);
            foreach (var item in info.Tracks) Tracks.Add(item);
            track = Tracks[0]; Changed(nameof(SelectedTrack));
        }
        finally { loadingTracks = false; }
        await PreparePreviewAsync(ct);
    });
    public Task LoadTrackAsync() => RunOperationAsync(async ct =>
    {
        PreviewChanging?.Invoke(); ClearResult(); waveform = null; preview = ""; NotifyMedia();
        await PreparePreviewAsync(ct);
    });
    private MediaService Service => new(FfmpegPath, FfprobePath);
    private async Task PreparePreviewAsync(CancellationToken ct)
    {
        Status = "Preparando barras del audio…"; IsIndeterminate = true; Changed(nameof(IsIndeterminate));
        Directory.CreateDirectory(tempRoot);
        string next = Path.Combine(tempRoot, Guid.NewGuid().ToString("N") + ".wav");
        await Service.PreparePreviewAsync(source, track!.Index, next, new Progress<double>(seconds => Status = $"Preparando barras · {AudioRange.Format(seconds)} procesados…"), ct);
        var nextWave = await Task.Run(() => AudioWaveform.Read(next, ct: ct), ct);
        ct.ThrowIfCancellationRequested(); waveform = nextWave; preview = next; RangeStart = 0; RangeEnd = Duration;
        NotifyMedia(); Status = "Listo. Arrastra sobre las barras para seleccionar, ajusta los extremos o escribe los tiempos.";
        // Only this module's session files are removed; playback was released before loading.
        foreach (string old in Directory.EnumerateFiles(tempRoot, "*.wav")) if (old != next) { try { File.Delete(old); } catch (IOException) { } }
    }
    public void SelectAll() { RangeStart = 0; RangeEnd = Duration; }
    public Task ExtractAsync(string destination) => RunOperationAsync(async ct =>
    {
        var range = SelectedRange; Status = "Extrayendo audio…"; SetDeterminate();
        await Service.ExtractAsync(source, track!.Index, destination, range, new Progress<double>(p => Progress = p * 100), ct);
        output = destination; Changed(nameof(OutputPath)); Changed(nameof(CanOpenOutput));
        Status = $"Audio guardado · {AudioRange.Format(range.Duration)} · {Path.GetFileName(destination)}";
    });
    public Task TranscribeAsync() => RunOperationAsync(async ct =>
    {
        var range = SelectedRange; Status = "Preparando el fragmento para Whisper…"; SetDeterminate(); SaveSettings(false);
        string wave = Path.Combine(tempRoot, "transcribe-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            await Service.PrepareTranscriptionAsync(source, track!.Index, wave, range, new Progress<double>(p => Progress = p * 10), ct);
            Status = "Cargando Whisper en CPU…";
            var result = await new MediaTranscriber().TranscribeAsync(wave, ModelPath, language.Code, threads,
                new Progress<int>(p => { Progress = 10 + p * .9; Status = $"Transcribiendo en CPU · {p}%"; }), ct);
            ct.ThrowIfCancellationRequested(); transcript = result; transcriptOffset = range.Start;
            transcriptInfo = $"{SourceName} · {AudioRange.Format(range.Start)} → {AudioRange.Format(range.End)} · {result.Language}";
            Changed(nameof(TranscriptText)); Changed(nameof(TranscriptInfo)); NotifyState();
            Status = HasTranscript ? "Transcripción terminada. Revisa el texto y exporta el formato que necesites." : "No se detectó voz en la selección. Prueba otro tramo.";
        }
        finally { if (File.Exists(wave)) File.Delete(wave); }
    });
    public async Task ExportAsync(string destination)
    {
        if (!CanExport || transcript is null) return;
        if (Path.GetFullPath(destination).Equals(Path.GetFullPath(source), StringComparison.OrdinalIgnoreCase)) { Status = "El destino no puede ser el original."; return; }
        try
        {
            string text = transcript.Export(Path.GetExtension(destination), OriginalTimestamps ? transcriptOffset : 0);
            await File.WriteAllTextAsync(destination, text, new UTF8Encoding(false));
            Status = "Texto guardado · " + Path.GetFileName(destination);
        }
        catch (Exception ex) { Status = "No se pudo guardar: " + ex.Message; }
    }
    public void PlaybackError(string message) => Status = "No se pudo reproducir: " + message;
    public void Cancel() { if (IsBusy) { Status = "Cancelando…"; cancellation?.Cancel(); } }
    private Task RunOperationAsync(Func<CancellationToken, Task> action)
    {
        if (!CanEdit) return Task.CompletedTask;
        operation = ExecuteAsync(action); return operation;
    }
    private async Task ExecuteAsync(Func<CancellationToken, Task> action)
    {
        IsBusy = true; Progress = 0; cancellation = new(); IsIndeterminate = true; Changed(nameof(IsIndeterminate));
        try { await action(cancellation.Token); }
        catch (OperationCanceledException) { Status = "Operación cancelada. El archivo original permanece intacto."; }
        catch (Exception ex) { Status = "No se pudo completar: " + ex.Message; }
        finally { cancellation.Dispose(); cancellation = null; IsBusy = false; IsIndeterminate = false; Changed(nameof(IsIndeterminate)); }
    }
    private void SetDeterminate() { IsIndeterminate = false; Changed(nameof(IsIndeterminate)); }
    private void ClearResult() { transcript = null; transcriptInfo = ""; output = ""; Changed(nameof(TranscriptText)); Changed(nameof(TranscriptInfo)); Changed(nameof(OutputPath)); NotifyState(); }
    private void NotifyMedia() { Changed(nameof(Waveform)); Changed(nameof(PreviewPath)); Changed(nameof(Duration)); Changed(nameof(DurationText)); NotifyState(); }
    private void NotifyState() { foreach (string name in new[] { nameof(CanEdit), nameof(CanSelect), nameof(CanProcess), nameof(CanTranscribe), nameof(CanPlay), nameof(HasTranscript), nameof(CanExport), nameof(CanOpenOutput), nameof(SelectionValid), nameof(SelectionText) }) Changed(name); }
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    private void ReadSettings(string path, bool own)
    {
        if (!File.Exists(path)) return;
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(path)); var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return;
            if (root.TryGetProperty("settings", out var nested)) root = nested;
            if (root.ValueKind != JsonValueKind.Object) return;
            string? Get(string name) => root.EnumerateObject().FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Value is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;
            modelPath = Get("modelPath") ?? modelPath; ffmpeg = Get("ffmpegPath") ?? ffmpeg; ffprobe = Get("ffprobePath") ?? ffprobe;
            if (own && Get("language") is { } code) language = Languages.FirstOrDefault(l => l.Code == code) ?? language;
            if (root.TryGetProperty("cpuThreads", out var number) && number.TryGetInt32(out int value)) threads = Math.Clamp(value, 1, Environment.ProcessorCount);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { status = "No se pudieron leer algunos ajustes. Puedes configurarlos en Opciones avanzadas."; }
    }
    public void SaveSettings(bool announce = true)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
            File.WriteAllText(settingsPath + ".tmp", JsonSerializer.Serialize(new { modelPath, ffmpegPath = ffmpeg, ffprobePath = ffprobe, language = language.Code, cpuThreads = threads }, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(settingsPath + ".tmp", settingsPath, true); if (announce) Status = "Ajustes del módulo guardados.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Status = "No se pudieron guardar los ajustes: " + ex.Message; }
    }
    public async Task ShutdownAsync()
    {
        shuttingDown = true; cancellation?.Cancel(); if (operation is not null) await operation;
        string parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "CaptionForge-media-tools")) + Path.DirectorySeparatorChar;
        if (Path.GetFullPath(tempRoot).StartsWith(parent, StringComparison.OrdinalIgnoreCase) && Directory.Exists(tempRoot))
        { try { Directory.Delete(tempRoot, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
}
