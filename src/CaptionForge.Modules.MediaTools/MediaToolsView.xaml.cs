using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CaptionForge.Modules.MediaTools.Core;
using Microsoft.Win32;

namespace CaptionForge.Modules.MediaTools;

public partial class MediaToolsView : UserControl
{
    public MediaToolsViewModel Model { get; }
    private MediaPlayer? player;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private bool playing, opened, selectionPlayback;
    private string loadedPath = "";
    private double pendingPosition, stopAt;
    public MediaToolsView() : this(new MediaToolsViewModel()) { }
    public MediaToolsView(MediaToolsViewModel model)
    {
        Model = model; InitializeComponent(); DataContext = Model;
        Model.PreviewChanging += ReleasePlayer;
        Model.PropertyChanged += Changed;
        Waveform.SeekRequested += Seek;
        timer.Tick += (_, _) =>
        {
            if (player is null || !opened) return;
            if (playing && player.Position.TotalSeconds >= stopAt) { StopPlayback(); pendingPosition = stopAt; player.Position = TimeSpan.FromSeconds(stopAt); }
            UpdatePosition(player.Position.TotalSeconds);
        };
        Unloaded += (_, _) => StopPlayback();
    }
    private void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Model.IsBusy) && Model.IsBusy) StopPlayback();
        if (e.PropertyName == nameof(Model.PreviewPath)) { ReleasePlayer(); UpdatePosition(0); }
        if (selectionPlayback && e.PropertyName is nameof(Model.RangeStart) or nameof(Model.RangeEnd)) StopPlayback();
    }
    private void EnsurePlayer()
    {
        if (loadedPath == Model.PreviewPath && player is not null) return;
        ReleasePlayer(); if (!File.Exists(Model.PreviewPath)) return;
        player = new MediaPlayer(); loadedPath = Model.PreviewPath;
        player.MediaOpened += (_, _) => { opened = true; player.Position = TimeSpan.FromSeconds(pendingPosition); if (playing) player.Play(); };
        player.MediaEnded += (_, _) => StopPlayback();
        player.MediaFailed += (_, e) => { StopPlayback(); Model.PlaybackError(e.ErrorException.Message); };
        player.Open(new Uri(loadedPath, UriKind.Absolute));
    }
    public double PreviewVolume { get; set; } = 1;
    private void BeginPlayback(bool selection)
    {
        if (!Model.CanPlay) return;
        if (selection && !Model.SelectionValid) return;
        if (!selection && playing) { player?.Pause(); playing = false; timer.Stop(); PlayButton.Content = "▶ Escuchar"; return; }
        double desired = selection ? Model.RangeStart : Waveform.Position;
        if (desired >= Model.Duration - .01) desired = 0;
        EnsurePlayer(); if (player is null) return;
        selectionPlayback = selection; pendingPosition = desired; stopAt = selection ? Model.RangeEnd : Model.Duration;
        player.Volume = PreviewVolume; playing = true;
        if (opened) { player.Position = TimeSpan.FromSeconds(desired); player.Play(); }
        timer.Start(); PlayButton.Content = "❚❚ Pausar";
    }
    private void UpdatePosition(double position) { Waveform.Position = position; TimeLabel.Text = $"{AudioRange.Format(position)} / {Model.DurationText}"; }
    private void Seek(double position)
    {
        if (!Model.CanPlay) return;
        pendingPosition = position; selectionPlayback = false; stopAt = Model.Duration;
        if (opened && player is not null) player.Position = TimeSpan.FromSeconds(position);
        UpdatePosition(position);
    }
    public void StopPlayback()
    { player?.Pause(); playing = false; selectionPlayback = false; timer.Stop(); if (PlayButton is not null) PlayButton.Content = "▶ Escuchar"; }
    private void ReleasePlayer() { StopPlayback(); player?.Close(); player = null; opened = false; loadedPath = ""; pendingPosition = 0; }
    public async Task ShutdownAsync() { ReleasePlayer(); await Model.ShutdownAsync(); }
    private async void BrowseSource(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Abrir audio o vídeo", Filter = "Audio y vídeo|*.mp4;*.mkv;*.mov;*.avi;*.webm;*.wav;*.mp3;*.m4a;*.aac;*.flac;*.ogg;*.wma;*.opus|Todos los archivos|*.*" };
        if (dialog.ShowDialog() == true) await Model.LoadAsync(dialog.FileName);
    }
    private void BrowseModel(object sender, RoutedEventArgs e)
    { var dialog = new OpenFileDialog { Title = "Modelo GGML de Whisper", Filter = "Modelo Whisper|*.bin|Todos los archivos|*.*" }; if (dialog.ShowDialog() == true) Model.ModelPath = dialog.FileName; }
    private void Play(object sender, RoutedEventArgs e) => BeginPlayback(false);
    private void PlaySelection(object sender, RoutedEventArgs e) => BeginPlayback(true);
    private void Stop(object sender, RoutedEventArgs e) { StopPlayback(); Seek(0); }
    private void SelectAll(object sender, RoutedEventArgs e) => Model.SelectAll();
    private void Cancel(object sender, RoutedEventArgs e) => Model.Cancel();
    private void SaveSettings(object sender, RoutedEventArgs e) => Model.SaveSettings();
    private async void ExtractAudio(object sender, RoutedEventArgs e)
    {
        string ext = Model.Format.ToLowerInvariant();
        var dialog = new SaveFileDialog { Title = "Extraer audio seleccionado", Filter = ext.ToUpperInvariant() + "|*." + ext, DefaultExt = ext, FileName = SafeStem() + "-audio." + ext, OverwritePrompt = true };
        if (dialog.ShowDialog() == true) await Model.ExtractAsync(dialog.FileName);
    }
    private async void Transcribe(object sender, RoutedEventArgs e) => await Model.TranscribeAsync();
    private async void ExportText(object sender, RoutedEventArgs e)
    {
        string ext = (string)((Button)sender).Tag;
        var dialog = new SaveFileDialog { Title = "Guardar transcripción", Filter = ext.ToUpperInvariant() + "|*." + ext, DefaultExt = ext, FileName = SafeStem() + "." + ext, OverwritePrompt = true };
        if (dialog.ShowDialog() == true) await Model.ExportAsync(dialog.FileName);
    }
    private string SafeStem() { string stem = Path.GetFileNameWithoutExtension(Model.SourcePath); return stem[..Math.Min(stem.Length, 100)]; }
    private void OpenFolder(object sender, RoutedEventArgs e)
    { try { Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true, ArgumentList = { "/select,", Model.OutputPath } }); } catch (Exception ex) { Model.PlaybackError(ex.Message); } }
}
