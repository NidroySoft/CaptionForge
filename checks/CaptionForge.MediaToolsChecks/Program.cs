using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CaptionForge.Desktop;
using CaptionForge.Desktop.Localization;
using CaptionForge.Desktop.Services;
using CaptionForge.Desktop.ViewModels;
using CaptionForge.Modularity;
using CaptionForge.Modules.MediaTools;
using CaptionForge.Modules.MediaTools.Core;

internal static class Program
{
 [STAThread]
 private static int Main(string[] args)
 {
  var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
  app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/CaptionForge;component/Resources/Theme.xaml") });
  SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext()); int result = 1;
  async void Check()
  {
   MediaToolsView? view = null; MainViewModel? hostModel = null;
   try
   {
    string root = Path.GetFullPath("artifacts/media-tools-checks"); Directory.CreateDirectory(root);
    view = new(new MediaToolsViewModel(Path.Combine(root, "test-settings.json"))) { PreviewVolume = 0 };
    Require(!view.Model.CanProcess && !view.Model.CanExport, "Empty controls must be disabled");
    Render(view, Path.Combine(root, "empty.png"), 1040, 660);
    Require(((FrameworkElement)view.FindName("ResultEmptyMessage")).Visibility == Visibility.Visible, "Empty result guidance missing");
    var options = (FrameworkElement)view.FindName("OptionsPanel"); var results = (FrameworkElement)view.FindName("ResultPanel");
    Require(Math.Abs(options.ActualWidth - results.ActualWidth) < 1, "Columns differ in width");
    Require(results.TranslatePoint(new Point(), view).X > options.TranslatePoint(new Point(options.ActualWidth, 0), view).X, "Panels overlap");
    string source = Path.Combine(root, "vídeo de prueba.mkv");
    await MediaService.RunAsync("ffmpeg", ["-nostdin", "-v", "error", "-f", "lavfi", "-i", "color=s=64x64:d=4", "-f", "lavfi", "-i", "sine=frequency=440:duration=4", "-f", "lavfi", "-i", "sine=frequency=880:duration=4", "-map", "0:v", "-map", "1:a", "-map", "2:a", "-c:v", "mpeg4", "-c:a", "pcm_s16le", "-metadata:s:a:0", "language=eng", "-metadata:s:a:1", "language=spa", "-y", source], null, default);
    await view.Model.LoadAsync(source);
    Require(view.Model.CanProcess && view.Model.Tracks.Count == 2, "Video source/pistas did not load: " + view.Model.Status);
    view.Model.SelectedTrack = view.Model.Tracks[1]; await view.Model.WhenIdleAsync();
    Require(view.Model.CanProcess && view.Model.SelectedTrack.Index == 2, "Second audio track did not load");
    await Dispatcher.Yield(DispatcherPriority.ContextIdle);
    var waveform = (SelectionWaveform)view.FindName("Waveform"); waveform.SelectRange(2.7, 1.2);
    Require(Math.Abs(view.Model.RangeStart - 1.2) < .001 && Math.Abs(view.Model.RangeEnd - 2.7) < .001, "Selection did not update bound time fields");
    view.Model.StartTime = "invalid"; Require(!view.Model.CanProcess && view.Model.CanSelect, "Invalid fields should disable processing but keep selection usable");
    view.Model.SelectAll(); Require(view.Model.CanProcess, "Select all did not restore valid fields");
    view.Model.StartTime = "00:00:01.200"; view.Model.EndTime = "00:00:02.700";
    string output = Path.Combine(root, "selected.wav"); await view.Model.ExtractAsync(output);
    Require(File.Exists(output) && view.Model.CanOpenOutput, "Extraction failed: " + view.Model.Status);
    Render(view, Path.Combine(root, "selection-dark.png"), 1040, 660);
    Render(view, Path.Combine(root, "selection-compact.png"), 820, 510);
    // Exercise real native playback silently, including stopping at the selection end.
    if (args.Contains("--playback") || args.Contains("--real-transcription"))
    {
     ((Button)FindButton(view, "▶ Selección")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
     await Task.Delay(2000);
     Require(waveform.Position <= 2.8 && waveform.Position >= 1.2, "Selection playback exceeded its end or failed to start: " + view.Model.Status);
     view.StopPlayback(); Console.WriteLine("PASS: silent native playback stops at the selected end.");
    }
    if (args.Contains("--real-transcription"))
    {
     string voice = Directory.EnumerateFiles("artifacts/module-checks", "kokoro-*.wav").First(p => !Path.GetFileName(p).Contains("-nivel-"));
     await view.Model.LoadAsync(voice);
     view.Model.ModelPath = @"C:\Proyectos\CaptionForge\Models\ggml-medium.en.bin";
     view.Model.SelectedLanguage = view.Model.Languages.Single(l => l.Code == "en");
     await view.Model.TranscribeAsync();
     Require(view.Model.HasTranscript && view.Model.TranscriptText.Contains("voice", StringComparison.OrdinalIgnoreCase), "Real Whisper transcription failed: " + view.Model.Status + " / " + view.Model.TranscriptText);
     foreach (string ext in new[] { "txt", "srt", "vtt" }) await view.Model.ExportAsync(Path.Combine(root, "transcript." + ext));
     Render(view, Path.Combine(root, "transcript-dark.png"), 1040, 660);
     Console.WriteLine("PASS: Real GGML medium.en CPU transcription and TXT/SRT/VTT exports: " + view.Model.TranscriptText);
    }
    // A palette check never writes user preferences.
    foreach (var item in new Dictionary<string, string> { ["BackgroundBrush"]="#F3F7F8", ["SurfaceBrush"]="#FFFFFF", ["RaisedBrush"]="#E8EFF2", ["InputBrush"]="#F5F8FA", ["BorderBrush"]="#BECED5", ["TextBrush"]="#142630", ["MutedBrush"]="#45616F", ["AccentBrush"]="#007F6A", ["AccentTextBrush"]="#FFFFFF", ["SelectionBrush"]="#D6EFE8" }) app.Resources[item.Key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(item.Value));
    // Unattached rendered views are outside Application.Windows; create a fresh resource target.
    var lightView = new MediaToolsView(new MediaToolsViewModel(Path.Combine(root, "test-settings.json")));
    try
    {
     await lightView.Model.LoadAsync(source); lightView.Model.SelectedTrack = lightView.Model.Tracks[1]; await lightView.Model.WhenIdleAsync();
     lightView.Model.RangeStart = 1.2; lightView.Model.RangeEnd = 2.7;
     Render(lightView, Path.Combine(root, "selection-light.png"), 1040, 660);
    }
    finally { await lightView.ShutdownAsync(); }
    LocalizationService.Current.Initialize(); hostModel = new MainViewModel(new DesktopDialogs(), new AudioPreviewService());
    var window = new MainWindow { DataContext = hostModel }; await Dispatcher.Yield(DispatcherPriority.ContextIdle);
    var selector = (ComboBox)window.FindName("ModuleSelector"); var host = (ContentControl)window.FindName("ModuleHost"); var tabs = (TabControl)window.FindName("ModuleTabs");
    var definition = selector.Items.Cast<ModuleDefinition>().Single(d => d.Id == "media-tools");
    selector.SelectedItem = definition; var firstView = host.Content; Require(firstView is MediaToolsView, "Generic host did not discover media module");
    selector.SelectedItem = definition; Require(tabs.Items.Count == 2 && ReferenceEquals(firstView, host.Content), "Duplicate media tabs");
    window.CloseModuleTab("media-tools"); selector.SelectedItem = definition; Require(ReferenceEquals(firstView, host.Content), "Reopening lost module instance");
    Console.WriteLine("PASS: waveform, track selection, exact time fields, extraction, dark/light/compact layouts and single-instance host tabs.");
    result = 0;
   }
   catch (Exception e) { Console.Error.WriteLine(e); }
   finally { if (view is not null) await view.ShutdownAsync(); if (hostModel is not null) await hostModel.ShutdownAsync(); Dispatcher.CurrentDispatcher.InvokeShutdown(); }
  }
  app.Dispatcher.BeginInvoke(Check); Dispatcher.Run(); return result;
 }
 private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
 private static DependencyObject FindButton(DependencyObject parent, string label)
 {
  if (parent is Button { Content: string text } && text == label) return parent;
  for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) { var found = FindButtonOrNull(VisualTreeHelper.GetChild(parent, i), label); if (found is not null) return found; }
  throw new InvalidOperationException("Button not found: " + label);
 }
 private static DependencyObject? FindButtonOrNull(DependencyObject parent, string label)
 { if (parent is Button { Content: string text } && text == label) return parent; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) { var found = FindButtonOrNull(VisualTreeHelper.GetChild(parent, i), label); if (found is not null) return found; } return null; }
 private static void Render(FrameworkElement view, string path, int width, int height)
 {
  view.Measure(new Size(width, height)); view.Arrange(new Rect(0, 0, width, height)); view.UpdateLayout();
  var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(view);
  var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(path); encoder.Save(file);
 }
}
