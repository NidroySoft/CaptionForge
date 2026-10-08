using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CaptionForge.Modules.TextToSpeech.Core;

namespace CaptionForge.Modules.TextToSpeech;

public sealed class WaveformView : FrameworkElement
{
    public static readonly DependencyProperty AudioProperty = DependencyProperty.Register(nameof(Audio), typeof(WaveformData), typeof(WaveformView), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty PositionProperty = DependencyProperty.Register(nameof(Position), typeof(double), typeof(WaveformView), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public WaveformData? Audio { get => (WaveformData?)GetValue(AudioProperty); set => SetValue(AudioProperty, value); }
    public double Position { get => (double)GetValue(PositionProperty); set => SetValue(PositionProperty, value); }
    public event Action<double>? Seek;
    public WaveformView() { Cursor = Cursors.Hand; Focusable = true; ToolTip = "Pulsa o arrastra para avanzar por el audio. Usa las flechas para mover cinco segundos."; }
    protected override void OnRender(DrawingContext drawing)
    {
        Brush Resource(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;
        drawing.DrawRoundedRectangle(Resource("InputBrush", Brushes.DarkSlateGray), null, new Rect(RenderSize), 8, 8);
        if (Audio is not { } audio) return;
        var played = Resource("AccentBrush", Brushes.CornflowerBlue);
        var pending = Resource("MutedBrush", Brushes.SlateGray);
        int bars = Math.Min(audio.Peaks.Length, Math.Max(1, (int)(ActualWidth / 5)));
        double step = ActualWidth / bars;
        for (int i = 0; i < bars; i++)
        {
            int first = i * audio.Peaks.Length / bars, last = Math.Max(first + 1, (i + 1) * audio.Peaks.Length / bars);
            float peak = 0;
            for (int p = first; p < last; p++) peak = Math.Max(peak, audio.Peaks[p]);
            double height = Math.Max(3, peak * (ActualHeight - 16));
            drawing.DrawRoundedRectangle(i * audio.DurationSeconds / bars <= Position ? played : pending, null,
                new Rect(i * step + 1, (ActualHeight - height) / 2, Math.Max(1, step - 2), height), 1, 1);
        }
        double x = Math.Clamp(Position / audio.DurationSeconds, 0, 1) * ActualWidth;
        drawing.DrawLine(new Pen(played, 2), new Point(x, 6), new Point(x, ActualHeight - 6));
    }
    private void SeekAt(Point point) { if (Audio is { } audio && ActualWidth > 0) Seek?.Invoke(Math.Clamp(point.X / ActualWidth, 0, 1) * audio.DurationSeconds); }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) { base.OnMouseLeftButtonDown(e); Focus(); CaptureMouse(); SeekAt(e.GetPosition(this)); e.Handled = true; }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (IsMouseCaptured) SeekAt(e.GetPosition(this)); }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) { base.OnMouseLeftButtonUp(e); if (IsMouseCaptured) { SeekAt(e.GetPosition(this)); ReleaseMouseCapture(); } }
    protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if (Audio is { } audio && e.Key is Key.Left or Key.Right) { Seek?.Invoke(Math.Clamp(Position + (e.Key == Key.Left ? -5 : 5), 0, audio.DurationSeconds)); e.Handled = true; } }
}
