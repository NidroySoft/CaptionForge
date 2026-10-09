using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CaptionForge.Modules.MediaTools.Core;

namespace CaptionForge.Modules.MediaTools;

public sealed class SelectionWaveform : FrameworkElement
{
    public static readonly DependencyProperty AudioProperty = DependencyProperty.Register(nameof(Audio), typeof(AudioWaveform), typeof(SelectionWaveform), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty StartProperty = DependencyProperty.Register(nameof(Start), typeof(double), typeof(SelectionWaveform), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
    public static readonly DependencyProperty EndProperty = DependencyProperty.Register(nameof(End), typeof(double), typeof(SelectionWaveform), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
    public static readonly DependencyProperty PositionProperty = DependencyProperty.Register(nameof(Position), typeof(double), typeof(SelectionWaveform), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public AudioWaveform? Audio { get => (AudioWaveform?)GetValue(AudioProperty); set => SetValue(AudioProperty, value); }
    public double Start { get => (double)GetValue(StartProperty); set => SetCurrentValue(StartProperty, value); }
    public double End { get => (double)GetValue(EndProperty); set => SetCurrentValue(EndProperty, value); }
    public double Position { get => (double)GetValue(PositionProperty); set => SetValue(PositionProperty, value); }
    public event Action<double>? SeekRequested;
    private int handle;
    private double anchorX, anchorTime;
    private bool dragged;
    public SelectionWaveform() { Focusable = true; ClipToBounds = true; Cursor = Cursors.Cross; ToolTip = "Arrastra para seleccionar · ajusta los extremos · haz clic para escuchar desde un punto"; }
    private Brush Brush(string key, Color fallback) => TryFindResource(key) as Brush ?? new SolidColorBrush(fallback);
    private double X(double seconds) => Math.Clamp(seconds / Math.Max(.001, Audio?.Duration ?? 1), 0, 1) * ActualWidth;
    private double Time(double x) => Math.Clamp(x / Math.Max(1, ActualWidth), 0, 1) * (Audio?.Duration ?? 0);
    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRoundedRectangle(Brush("InputBrush", Colors.Black), null, new(0, 0, ActualWidth, ActualHeight), 6, 6);
        var muted = Brush("MutedBrush", Colors.Gray); var accent = Brush("AccentBrush", Colors.Turquoise);
        dc.DrawLine(new(muted, .5), new(0, ActualHeight / 2), new(ActualWidth, ActualHeight / 2));
        if (Audio is not { } audio) return;
        double left = X(Start), right = X(End);
        dc.DrawRectangle(Brush("SelectionBrush", Colors.DarkSlateGray), null, new(left, 0, Math.Max(0, right - left), ActualHeight));
        int bars = Math.Max(1, (int)(ActualWidth / 3));
        for (int bar = 0; bar < bars; bar++)
        {
            int a = bar * audio.Peaks.Length / bars, b = Math.Max(a + 1, (bar + 1) * audio.Peaks.Length / bars);
            float peak = 0; for (int i = a; i < Math.Min(b, audio.Peaks.Length); i++) peak = Math.Max(peak, audio.Peaks[i]);
            double x = (bar + .5) * ActualWidth / bars, height = Math.Max(1, peak * (ActualHeight - 24));
            dc.DrawLine(new(x >= left && x <= right ? accent : muted, 2), new(x, (ActualHeight - height) / 2), new(x, (ActualHeight + height) / 2));
        }
        foreach (double edge in new[] { left, right }) { dc.DrawLine(new(accent, 2), new(edge, 0), new(edge, ActualHeight)); dc.DrawRoundedRectangle(accent, null, new(Math.Clamp(edge - 3, 0, Math.Max(0, ActualWidth - 6)), 0, 6, 13), 2, 2); }
        dc.DrawLine(new(Brush("TextBrush", Colors.White), 1.5), new(X(Position), 14), new(X(Position), ActualHeight));
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (Audio is null || !IsEnabled) return;
        Focus(); anchorX = e.GetPosition(this).X; anchorTime = Time(anchorX); dragged = false;
        handle = Math.Abs(anchorX - X(Start)) <= 8 ? 1 : Math.Abs(anchorX - X(End)) <= 8 ? 2 : 3;
        CaptureMouse(); e.Handled = true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!IsMouseCaptured || Audio is null) return;
        double x = e.GetPosition(this).X;
        if (Math.Abs(x - anchorX) >= 3) dragged = true;
        if (!dragged) return;
        double time = Time(x);
        if (handle == 1) Start = Math.Clamp(time, 0, Math.Max(0, End - .02));
        else if (handle == 2) End = Math.Clamp(time, Math.Min(Audio.Duration, Start + .02), Audio.Duration);
        else SelectRange(anchorTime, time);
        e.Handled = true;
    }
    public void SelectRange(double a, double b)
    {
        if (Audio is null) return;
        double first = Math.Clamp(Math.Min(a, b), 0, Math.Max(0, Audio.Duration - .02));
        double last = Math.Clamp(Math.Max(a, b), first + Math.Min(.02, Audio.Duration), Audio.Duration);
        Start = first; End = last;
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    { if (!IsMouseCaptured) return; if (!dragged) SeekRequested?.Invoke(Time(e.GetPosition(this).X)); ReleaseMouseCapture(); handle = 0; e.Handled = true; }
    protected override void OnLostMouseCapture(MouseEventArgs e) { handle = 0; base.OnLostMouseCapture(e); }
    protected override void OnKeyDown(KeyEventArgs e)
    { if (Audio is not null && e.Key is Key.Left or Key.Right) { SeekRequested?.Invoke(Math.Clamp(Position + (e.Key == Key.Left ? -5 : 5), 0, Audio.Duration)); e.Handled = true; } base.OnKeyDown(e); }
}
