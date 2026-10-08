using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CaptionForge.Modules.TextToSpeech.Core;
using CaptionForge.Desktop;
using CaptionForge.Desktop.Localization;
using CaptionForge.Desktop.Services;
using CaptionForge.Desktop.ViewModels;
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
                if (args.Contains("--no-modules"))
                {
                    Require(selector.Items.Count == 1, "The host retained an optional module");
                    Console.WriteLine("PASS: CaptionForge works with no optional modules installed.");
                    await vm.ShutdownAsync(); exitCode = 0; return;
                }
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
                var voices = (ComboBox)voiceView.FindName("VoiceSelector");
                Require(voices.Items.Count == voiceView.Model.Voices.Count && voices.SelectedItem is not null == (voices.Items.Count > 0), "Voice selector did not load or select its items");
                voices.ApplyTemplate();
                for (int index = 0; index < voices.Items.Count; index++)
                {
                    // Generate each dropdown container, not just the collection behind the view.
                    var container = new ComboBoxItem { Content = voices.Items[index] };
                    container.ApplyTemplate(); container.Measure(new Size(400, 100));
                    Require(container.DesiredSize.Height > 0, "A voice dropdown row is invisible");
                }
                if (args.Contains("--install-kokoro"))
                {
                    var installer = new EngineInstaller(Path.Combine(SpeechViewModel.ModuleDirectory, "Backend"), SpeechSettings.InstallationRoot);
                    var installed = await installer.InstallAsync(SpeechEngine.Kokoro, new Progress<SpeechProgress>(p => Console.WriteLine(p.Message)), CancellationToken.None);
                    voiceView.Model.PythonExecutable = installed.PythonExecutable; voiceView.Model.ModelDirectory = installed.ModelDirectory;
                    voiceView.Model.SelectedLanguage = voiceView.Model.Languages.Single(l => l.Code == "en");
                    voiceView.Model.Text = "A clear voice makes a complicated idea easier to understand.";
                    await voiceView.Model.GenerateAsync();
                    Require(voiceView.Model.HasOutput && voiceView.Model.Waveform is not null, "Fresh installation did not generate audio");
                    Console.WriteLine("PASS: Automatic installation and real generation without system Python.");
                }
                var sample = Directory.EnumerateFiles(Path.Combine(Environment.CurrentDirectory, "artifacts", "module-checks"), "*.wav").FirstOrDefault();
                if (sample is not null) await voiceView.Model.LoadPreviewAsync(sample);
                if (sample is not null && args.Contains("--playback"))
                {
                    var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var player = new MediaPlayer { Volume = 0 };
                    player.MediaOpened += (_, _) => ready.TrySetResult();
                    player.MediaFailed += (_, e) => ready.TrySetException(e.ErrorException);
                    try
                    {
                        player.Open(new Uri(Path.GetFullPath(sample)));
                        await ready.Task.WaitAsync(TimeSpan.FromSeconds(15));
                        Require(player.NaturalDuration.HasTimeSpan && player.NaturalDuration.TimeSpan.TotalSeconds > 1, "Native player did not decode the WAV");
                        player.Play(); await Task.Delay(250); player.Pause();
                        player.Position = TimeSpan.FromSeconds(1);
                        Require(Math.Abs(player.Position.TotalSeconds - 1) < 0.2, "Native audio seeking failed");
                        Console.WriteLine("PASS: Native WAV decode, playback, pause and seek (muted).");
                    }
                    finally { player.Close(); }
                }
                await Dispatcher.Yield(DispatcherPriority.ContextIdle);
                Render(window, Path.Combine(directory, "text-to-speech.png"));
                string preservedText = voiceView.Model.Text;
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
                Require(ReferenceEquals(host.Content, voiceView) && voiceView.Model.Text == preservedText, "Speech state was replaced");
                Console.WriteLine("PASS: WPF views, language controls, local voices and navigation retain state.");

                if (args.Contains("--real-speech"))
                {
                    var defaults = SpeechSettings.Defaults();
                    var service = new PythonSpeechSynthesisService(Path.Combine(SpeechViewModel.ModuleDirectory, "Backend", "tts_worker.py"));
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
