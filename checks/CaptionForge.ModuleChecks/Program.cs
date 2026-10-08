using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CaptionForge.Application.Models.Speech;
using CaptionForge.Desktop;
using CaptionForge.Desktop.Localization;
using CaptionForge.Desktop.Services;
using CaptionForge.Desktop.ViewModels;
using CaptionForge.Infrastructure.Speech;
using CaptionForge.Modules.TextToSpeech;

// Native WPF component smoke check, without opening windows or interacting with user projects.
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/CaptionForge;component/Resources/Theme.xaml")
        });
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        int exitCode = 1;
        async void Check()
        {
            try
            {
                var directory = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/module-checks");
                Directory.CreateDirectory(directory);
                LocalizationService.Current.Initialize();
                var vm = new MainViewModel(new DesktopDialogs(), new AudioPreviewService());
                var window = new MainWindow { DataContext = vm };
                await Dispatcher.Yield(DispatcherPriority.ContextIdle);
                var selector = (ComboBox)window.FindName("ModuleSelector");
                var host = (ContentControl)window.FindName("ModuleHost");
                var subtitles = host.Content;
                Require(subtitles is FrameworkElement, "Subtitles did not load");
                selector.SelectedIndex = 1;
                Require(host.Content is SpeechView, "Speech module did not load");
                var voiceView = (SpeechView)host.Content;
                voiceView.Model.Text = "Module state survives navigation.";
                voiceView.Model.SelectedEngine = voiceView.Model.Engines.Single(e => e.Engine == SpeechEngine.Nano);
                Require(voiceView.Model.Languages.Count == 1 && voiceView.Model.UsesReference, "Nano language/reference controls");
                voiceView.Model.SelectedEngine = voiceView.Model.Engines.Single(e => e.Engine == SpeechEngine.Kokoro);
                voiceView.Model.SelectedLanguage = voiceView.Model.Languages.Single(l => l.Code == "es");
                Require(voiceView.Model.Voices.All(v => v.StartsWith('e')), "Spanish voices were not filtered");
                await Dispatcher.Yield(DispatcherPriority.ContextIdle);
                Render(window, Path.Combine(directory, "text-to-speech.png"));
                selector.SelectedIndex = 0;
                Require(ReferenceEquals(host.Content, subtitles), "Subtitle state was replaced");
                await Dispatcher.Yield(DispatcherPriority.ContextIdle);
                Render(window, Path.Combine(directory, "subtitles.png"));
                var subtitleView = (FrameworkElement)subtitles!;
                Require(ReferenceEquals(subtitleView.DataContext, vm), "Subtitle DataContext changed");
                var views = Descendants(subtitleView).OfType<CaptionForge.Desktop.Views.ProjectsView>().ToArray();
                Require(views.Length == 1 && ReferenceEquals(views[0].DataContext, vm) && views[0].Visibility == Visibility.Visible,
                    "Subtitle project view bindings did not load");
                Require(Descendants(subtitleView).OfType<CaptionForge.Desktop.Views.ResultsView>().Single().Visibility == Visibility.Collapsed,
                    "Inactive subtitle view remained visible");
                selector.SelectedIndex = 1;
                Require(ReferenceEquals(host.Content, voiceView) && voiceView.Model.Text == "Module state survives navigation.", "Speech state was replaced");
                Console.WriteLine("PASS: WPF views, language controls, local voices and navigation retain state.");

                if (args.Contains("--real-speech"))
                {
                    var defaults = SpeechSettings.Defaults();
                    var service = new PythonSpeechSynthesisService(Path.Combine(AppContext.BaseDirectory, "Modules", "TextToSpeech", "tts_worker.py"));
                    // Sequential CPU jobs; validates the actual .NET -> Python -> local weights -> WAV chain.
                    foreach (var (engine, language, voice, text) in new[]
                    {
                        (SpeechEngine.Kokoro, "en", "af_heart", "Your familiar voice is a private mix. The recording is the public version."),
                        (SpeechEngine.Kokoro, "es", "ef_dora", "¿Por qué tu voz grabada suena diferente? Escucha esta explicación."),
                        (SpeechEngine.Pocket, "en", "alba", "A clear voice can make a complicated idea easier to understand."),
                        (SpeechEngine.Pocket, "es", "lola", "Una voz clara puede hacer que una idea complicada sea más fácil de entender.")
                    })
                    {
                        var runtime = defaults.Engines[engine];
                        var result = await service.GenerateAsync(new(engine, language, text, voice, null, runtime.PythonExecutable, runtime.ModelDirectory, directory),
                            new Progress<SpeechProgress>(p => Console.WriteLine(p.Message)), CancellationToken.None);
                        Require(result.SampleRate == 24000 && result.DurationSeconds > 1, "Invalid real WAV");
                        Console.WriteLine($"PASS: {engine}/{language}, {result.DurationSeconds:F2}s WAV, {result.ElapsedSeconds:F2}s generation; {result.AudioPath}");
                    }
                }
                await vm.ShutdownAsync();
                await voiceView.Model.ShutdownAsync();
                exitCode = 0;
            }
            catch (Exception e) { Console.Error.WriteLine(e); }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }
        app.Dispatcher.BeginInvoke(Check);
        Dispatcher.Run();
        return exitCode;
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    private static void Render(MainWindow window, string path)
    {
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(1040, 660));
        content.Arrange(new Rect(0, 0, 1040, 660));
        content.UpdateLayout();
        var bitmap = new RenderTargetBitmap(1040, 660, 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var drawing = background.RenderOpen()) drawing.DrawRectangle(window.Background, null, new Rect(0, 0, 1040, 660));
        bitmap.Render(background);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
