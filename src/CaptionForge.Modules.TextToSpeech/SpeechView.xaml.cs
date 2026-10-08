using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.ComponentModel;
using Microsoft.Win32;

namespace CaptionForge.Modules.TextToSpeech;

public partial class SpeechView : UserControl
{
    private readonly MediaPlayer _player = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private bool _opened, _playing, _playAfterOpen, _updating;
    private double _pendingPosition;
    public SpeechViewModel Model { get; } = new();
    public SpeechView()
    {
        InitializeComponent(); DataContext = Model;
        _player.MediaOpened += (_, _) => { _opened = true; _player.Position = TimeSpan.FromSeconds(_pendingPosition); if (_playAfterOpen) { _player.Play(); _playing = true; _timer.Start(); } UpdatePosition(); };
        _player.MediaEnded += (_, _) => StopPlayback();
        _player.MediaFailed += (_, e) => { StopPlayback(); MessageBox.Show(e.ErrorException.Message, "Audio", MessageBoxButton.OK, MessageBoxImage.Error); };
        Unloaded += (_, _) => StopPlayback();
        Model.PropertyChanged += ModelChanged;
        _timer.Tick += (_, _) => UpdatePosition();
        Waveform.Seek += Seek;
    }
    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(Model.Waveform)) return;
        StopPlayback();
        if (Model.Waveform is { } audio) { _updating = true; AudioPosition.Maximum = audio.DurationSeconds; _updating = false; }
        UpdatePosition();
    }
    public void StopPlayback() { _timer.Stop(); _player.Stop(); _player.Close(); _opened = _playing = _playAfterOpen = false; _pendingPosition = 0; if (AudioPosition is not null) UpdatePosition(); }
    private void UpdatePosition()
    {
        double position = _opened ? _player.Position.TotalSeconds : _pendingPosition;
        _updating = true; AudioPosition.Value = position; _updating = false;
        Waveform.Position = position;
        TimeLabel.Text = $"{TimeSpan.FromSeconds(position):mm\\:ss} / {TimeSpan.FromSeconds(Model.Waveform?.DurationSeconds ?? 0):mm\\:ss}";
        PlayButton.Content = _playing ? "Ⅱ Pausar" : "▶ Reproducir";
    }
    private void Seek(double seconds)
    {
        if (!Model.HasOutput) return;
        _pendingPosition = seconds;
        if (_opened) _player.Position = TimeSpan.FromSeconds(seconds);
        else { _playAfterOpen = false; _player.Open(new Uri(Model.OutputPath)); }
        UpdatePosition();
    }
    private void PositionChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (!_updating && Model is not null && Model.HasOutput) Seek(e.NewValue); }
    private void BrowseReference(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Audio de referencia", Filter = "Audio|*.wav;*.mp3;*.flac;*.ogg;*.m4a", CheckFileExists = true };
        if (dialog.ShowDialog() == true) Model.ReferencePath = dialog.FileName;
    }
    private void BrowsePython(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Python del entorno instalado", Filter = "Python|python.exe", CheckFileExists = true };
        if (dialog.ShowDialog() == true) Model.PythonExecutable = dialog.FileName;
    }
    private string? Folder(string title, string initial)
    {
        var dialog = new OpenFolderDialog { Title = title };
        if (Directory.Exists(initial)) dialog.InitialDirectory = initial;
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
    private void BrowseModels(object sender, RoutedEventArgs e) { if (Folder("Carpeta del modelo", Model.ModelDirectory) is { } folder) Model.ModelDirectory = folder; }
    private void BrowseOutput(object sender, RoutedEventArgs e) { if (Folder("Carpeta de salida", Model.OutputDirectory) is { } folder) Model.OutputDirectory = folder; }
    private void RefreshVoices(object sender, RoutedEventArgs e) => Model.RefreshVoices();
    private void SaveSettings(object sender, RoutedEventArgs e) => Model.SaveSettings();
    private void DetectInstallations(object sender, RoutedEventArgs e) => Model.DetectInstallations();
    private async void InstallEngine(object sender, RoutedEventArgs e) { StopPlayback(); await Model.InstallAsync(); }
    private async void Generate(object sender, RoutedEventArgs e) { StopPlayback(); await Model.GenerateAsync(); }
    private void Cancel(object sender, RoutedEventArgs e) => Model.Cancel();
    private void Play(object sender, RoutedEventArgs e)
    {
        if (!Model.HasOutput) return;
        if (_playing) { _player.Pause(); _playing = false; _timer.Stop(); }
        else if (_opened) { _player.Play(); _playing = true; _timer.Start(); }
        else { _playAfterOpen = true; _player.Open(new Uri(Model.OutputPath)); }
        UpdatePosition();
    }
    private void Stop(object sender, RoutedEventArgs e) => StopPlayback();
    private async void NormalizeAudio(object sender, RoutedEventArgs e) { StopPlayback(); await Model.AdjustAudioAsync(normalize: true); }
    private async void ApplyGain(object sender, RoutedEventArgs e) { StopPlayback(); await Model.AdjustAudioAsync(normalize: false); }
    private async void RestoreAudio(object sender, RoutedEventArgs e) { StopPlayback(); await Model.AdjustAudioAsync(normalize: false, restore: true); }
    private void SaveAudio(object sender, RoutedEventArgs e)
    {
        if (!Model.HasOutput) return;
        var dialog = new SaveFileDialog { Title = "Guardar audio", Filter = "Audio WAV|*.wav", FileName = Path.GetFileName(Model.OutputPath), AddExtension = true, OverwritePrompt = true };
        if (dialog.ShowDialog() == true && !string.Equals(Path.GetFullPath(dialog.FileName), Path.GetFullPath(Model.OutputPath), StringComparison.OrdinalIgnoreCase))
            Try(() => File.Copy(Model.OutputPath, dialog.FileName, overwrite: true));
    }
    private void OpenOutputFolder(object sender, RoutedEventArgs e) { if (Model.HasOutput) Try(() => Process.Start(new ProcessStartInfo(Path.GetDirectoryName(Model.OutputPath)!) { UseShellExecute = true })); }
    private static void Try(Action action) { try { action(); } catch (Exception e) { MessageBox.Show(e.Message, "Texto a voz", MessageBoxButton.OK, MessageBoxImage.Error); } }
}
