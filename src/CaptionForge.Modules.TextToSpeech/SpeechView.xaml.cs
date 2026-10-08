using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace CaptionForge.Modules.TextToSpeech;

public partial class SpeechView : UserControl
{
    private readonly MediaPlayer _player = new();
    public SpeechViewModel Model { get; } = new();
    public SpeechView()
    {
        InitializeComponent(); DataContext = Model;
        _player.MediaOpened += (_, _) => _player.Play();
        _player.MediaEnded += (_, _) => StopPlayback();
        _player.MediaFailed += (_, e) => { StopPlayback(); MessageBox.Show(e.ErrorException.Message, "Audio", MessageBoxButton.OK, MessageBoxImage.Error); };
        Unloaded += (_, _) => StopPlayback();
    }
    public void StopPlayback() { _player.Stop(); _player.Close(); }
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
    private async void Generate(object sender, RoutedEventArgs e) { StopPlayback(); await Model.GenerateAsync(); }
    private void Cancel(object sender, RoutedEventArgs e) => Model.Cancel();
    private void Play(object sender, RoutedEventArgs e) { if (Model.HasOutput) { StopPlayback(); _player.Open(new Uri(Model.OutputPath)); } }
    private void Stop(object sender, RoutedEventArgs e) => StopPlayback();
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
