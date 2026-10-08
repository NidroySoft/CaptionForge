using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CaptionForge.Modules.TextToSpeech.Core;
using NAudio.Wave;

namespace CaptionForge.Modules.TextToSpeech;

public partial class ReferenceAudioView : UserControl
{
    public static readonly DependencyProperty AudioPathProperty = DependencyProperty.Register(nameof(AudioPath), typeof(string), typeof(ReferenceAudioView), new PropertyMetadata("", Changed));
    public string AudioPath { get => (string)GetValue(AudioPathProperty); set => SetValue(AudioPathProperty, value); }
    private readonly MediaPlayer _player = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private bool _opened, _playing, _afterOpen, _updating;
    private double _pending;
    private string? _temporary;
    private Task _loading = Task.CompletedTask;
    private int _version;
    public WaveformData? Audio => Bars.Audio;
    public Task Loading => _loading;
    public event Action? PlaybackStarted;
    public ReferenceAudioView()
    {
        InitializeComponent();
        Bars.Seek += Seek;
        _player.MediaOpened += (_, _) => { _opened = true; _player.Position = TimeSpan.FromSeconds(_pending); if (_afterOpen) { _player.Play(); _playing = true; _timer.Start(); } Update(); };
        _player.MediaEnded += (_, _) => StopPlayback();
        _player.MediaFailed += (_, e) => { StopPlayback(); Info.Text = "No se pudo reproducir la referencia: " + e.ErrorException.Message; };
        _timer.Tick += (_, _) => Update(); Unloaded += (_, _) => StopPlayback();
    }
    private static void Changed(DependencyObject d, DependencyPropertyChangedEventArgs e)
    { var view = (ReferenceAudioView)d; view._loading = view.LoadAsync((string?)e.NewValue ?? ""); }
    private async Task LoadAsync(string path)
    {
        int version = ++_version; StopPlayback(); DeleteTemporary(); Bars.Audio = null; Controls.IsEnabled = false;
        Info.Text = "Selecciona una muestra para ver sus barras y escucharla.";
        if (!File.Exists(path)) return;
        string decoded = Path.Combine(Path.GetTempPath(), "CaptionForge-reference-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            Info.Text = "Preparando muestra…";
            var waveform = await Task.Run(() =>
            {
                using var reader = new AudioFileReader(path);
                if (reader.TotalTime.TotalMinutes > 10) throw new InvalidDataException("Selecciona una muestra de hasta 10 minutos; para clonar recomendamos 6–15 segundos.");
                WaveFileWriter.CreateWaveFile16(decoded, reader);
                return WaveformData.Read(decoded);
            });
            if (version != _version) { File.Delete(decoded); return; }
            _temporary = decoded; Bars.Audio = waveform; Position.Maximum = waveform.DurationSeconds; Controls.IsEnabled = true; Update();
        }
        catch (Exception e)
        {
            if (File.Exists(decoded)) File.Delete(decoded);
            if (version == _version) Info.Text = "No se pudo leer la muestra. Prueba con WAV o MP3. " + e.Message;
        }
    }
    private void Update()
    {
        double seconds = _opened ? _player.Position.TotalSeconds : _pending;
        _updating = true; Position.Value = seconds; _updating = false; Bars.Position = seconds;
        PlayButton.Content = _playing ? "Ⅱ" : "▶";
        if (Bars.Audio is { } audio) Info.Text = $"{TimeSpan.FromSeconds(seconds):mm\\:ss} / {TimeSpan.FromSeconds(audio.DurationSeconds):mm\\:ss} · {Path.GetFileName(AudioPath)}";
    }
    public void StopPlayback() { _timer.Stop(); _player.Close(); _opened = _playing = _afterOpen = false; _pending = 0; if (Position is not null) Update(); }
    private void Play(object sender, RoutedEventArgs e)
    {
        if (_temporary is null) return;
        if (_playing) { _player.Pause(); _playing = false; _timer.Stop(); }
        else { PlaybackStarted?.Invoke(); if (_opened) { _player.Play(); _playing = true; _timer.Start(); } else { _afterOpen = true; _player.Open(new Uri(_temporary)); } }
        Update();
    }
    private void Seek(double seconds) { if (_temporary is null) return; _pending = seconds; if (_opened) _player.Position = TimeSpan.FromSeconds(seconds); else { _afterOpen = false; _player.Open(new Uri(_temporary)); } Update(); }
    private void SeekSlider(object sender, RoutedPropertyChangedEventArgs<double> e) { if (!_updating) Seek(e.NewValue); }
    private void Stop(object sender, RoutedEventArgs e) => StopPlayback();
    private void DeleteTemporary() { if (_temporary is not null) { try { File.Delete(_temporary); } catch (IOException) { } _temporary = null; } }
    public async Task ShutdownAsync() { ++_version; StopPlayback(); await _loading; DeleteTemporary(); }
}
