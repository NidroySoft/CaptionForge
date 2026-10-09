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
                Require(!vm.RemoveOriginalSubtitleTrack, "Subtitle source removal must be opt-in");
                var importView = new CaptionForge.Desktop.Views.ConfigurationView { DataContext = vm, Foreground = (Brush)app.FindResource("TextBrush") };
                foreach (var sourceId in new[] { "CapCutTrack", "SubtitleFile", "Audio" })
                {
                    vm.SelectedCaptionSource = vm.CaptionSources.Single(s => s.Id == sourceId);
                    await Dispatcher.Yield(DispatcherPriority.ContextIdle);
                    importView.Measure(new Size(1000, 550));
                    importView.Arrange(new Rect(0, 0, 1000, 550));
                    importView.UpdateLayout();
                    var sourceSelector = (ComboBox)importView.FindName("CaptionSourceSelector");
                    Require(sourceSelector.Items.Count == 3 && Equals(sourceSelector.SelectedItem, vm.SelectedCaptionSource), "Subtitle source selector did not bind");
                    Require(vm.UsesAudioSource == (sourceId == "Audio") && vm.UsesTrackSource == (sourceId == "CapCutTrack") && vm.UsesFileSource == (sourceId == "SubtitleFile"), "Subtitle source controls do not match selection");
                    var bitmap = new RenderTargetBitmap(1000, 550, 96, 96, PixelFormats.Pbgra32);
                    var background = new DrawingVisual();
                    using (var drawing = background.RenderOpen()) drawing.DrawRectangle((Brush)app.FindResource("BackgroundBrush"), null, new Rect(0, 0, 1000, 550));
                    bitmap.Render(background); bitmap.Render(importView);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = File.Create(Path.Combine(directory, $"subtitle-source-{sourceId}.png")); encoder.Save(stream);
                }
                Console.WriteLine("PASS: subtitle origins, default source retention and WPF rendering.");
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
                selector.SelectedItem = selector.Items.Cast<CaptionForge.Modularity.ModuleDefinition>().Single(m => m.Id == "text-to-speech");
                Require(host.Content is SpeechView, "Speech module did not load");
                var voiceView = (SpeechView)host.Content;
                var tabs = (TabControl)window.FindName("ModuleTabs");
                Require(tabs.Items.Count == 2, "Expected subtitle and speech tabs");
                selector.SelectedItem = selector.Items.Cast<CaptionForge.Modularity.ModuleDefinition>().Single(m => m.Id == "text-to-speech");
                Require(tabs.Items.Count == 2 && ReferenceEquals(host.Content, voiceView), "Opening a module duplicated its tab");
                await Dispatcher.Yield(DispatcherPriority.ContextIdle);
                Render(window, Path.Combine(directory, "empty-result.png"));
                var optionsPanel = (FrameworkElement)voiceView.FindName("OptionsPanel");
                var resultPanel = (FrameworkElement)voiceView.FindName("ResultPanel");
                Require(Math.Abs(optionsPanel.ActualWidth - resultPanel.ActualWidth) < 1, "Generation and result columns are unbalanced");
                Require(resultPanel.TranslatePoint(new Point(), voiceView).X > optionsPanel.TranslatePoint(new Point(optionsPanel.ActualWidth, 0), voiceView).X, "Result overlaps generation options");
                Require(((FrameworkElement)voiceView.FindName("ResultPanel")).Visibility == Visibility.Visible, "Empty result panel disappeared");
                Require(((TextBlock)voiceView.FindName("ResultEmptyMessage")).Visibility == Visibility.Visible, "Empty result guidance missing");
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
                var sample = Directory.EnumerateFiles(Path.Combine(Environment.CurrentDirectory, "artifacts", "module-checks"), "*.wav").FirstOrDefault(path => !Path.GetFileName(path).Contains("-nivel-"));
                if (sample is not null)
                {
                    voiceView.Model.SelectedEngine = voiceView.Model.Engines.Single(e => e.Engine == SpeechEngine.Nano);
                    voiceView.Model.ReferencePath = sample;
                    await Dispatcher.Yield(DispatcherPriority.ContextIdle);
                    var reference = (ReferenceAudioView)voiceView.FindName("ReferencePlayer");
                    await reference.Loading;
                    Require(reference.Audio is { DurationSeconds: > 0 }, "Reference waveform did not load");
                    Render(window, Path.Combine(directory, "reference-audio.png"));
                    await voiceView.Model.LoadPreviewAsync(sample);
                    await Dispatcher.Yield(DispatcherPriority.ContextIdle);
                    Require(((TextBlock)voiceView.FindName("ResultEmptyMessage")).Visibility == Visibility.Collapsed, "Empty guidance remained after generation");
                    await voiceView.Model.AdjustAudioAsync(normalize: true);
                    Require(voiceView.Model.Status.Contains("Sin saturación"), "Normalization failed: " + voiceView.Model.Status);
                    var firstPeaks = voiceView.Model.Waveform!.Peaks.ToArray();
                    await voiceView.Model.AdjustAudioAsync(normalize: true);
                    Require(firstPeaks.SequenceEqual(voiceView.Model.Waveform!.Peaks), "Repeated normalization accumulated gain");
                    voiceView.Model.GainDb = -6;
                    await voiceView.Model.AdjustAudioAsync(normalize: false);
                    Require(voiceView.Model.Status.Contains("Ganancia aplicada"), "Manual gain failed");
                    await voiceView.Model.AdjustAudioAsync(normalize: false, restore: true);
                    Require(voiceView.Model.OutputPath == sample, "Original audio was not restored");
                    await Dispatcher.Yield(DispatcherPriority.ContextIdle);
                    Require(((ComboBox)voiceView.FindName("LanguageSelector")).SelectedItem is LanguageChoice { Code: "en" }, "Language selection was cleared by operation state changes");
                    Console.WriteLine("PASS: normalization, manual gain and original restoration.");
                }
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
                var languageDisplay = (ComboBox)voiceView.FindName("LanguageSelector");
                Require(Descendants(languageDisplay).OfType<TextBlock>().Any(text => text.Text == voiceView.Model.SelectedLanguage.Name), "Selected language label was not rendered");
                Render(window, Path.Combine(directory, "compact.png"), 820, 510);
                var expert = Descendants(voiceView).OfType<Expander>().Single(expander => Equals(expander.Header, "Ganancia manual · experto"));
                expert.IsExpanded = true;
                Render(window, Path.Combine(directory, "expert.png"));
                expert.IsExpanded = false;
                // Render the official light palette without writing appearance preferences.
                using (var paletteStream = System.Windows.Application.GetResourceStream(new Uri("Resources/Palettes.json", UriKind.Relative))!.Stream)
                {
                    var light = CaptionForge.Desktop.Appearance.PaletteCatalog.Load(paletteStream).Resolve(null, CaptionForge.Desktop.Appearance.AppearanceMode.Light).Variants[CaptionForge.Desktop.Appearance.AppearanceMode.Light];
                    foreach (var property in light.GetType().GetProperties())
                        app.Resources[property.Name + "Brush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString((string)property.GetValue(light)!)!);
                    Render(window, Path.Combine(directory, "light.png"));
                }
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
                selector.SelectedItem = selector.Items.Cast<CaptionForge.Modularity.ModuleDefinition>().Single(m => m.Id == "text-to-speech");
                Require(ReferenceEquals(host.Content, voiceView) && voiceView.Model.Text == preservedText, "Speech state was replaced");
                Console.WriteLine("PASS: WPF views, language controls, local voices and navigation retain state.");
                window.CloseModuleTab("text-to-speech");
                Require(tabs.Items.Count == 1 && ReferenceEquals(host.Content, subtitles), "Closing a tab failed");
                selector.SelectedItem = selector.Items.Cast<CaptionForge.Modularity.ModuleDefinition>().Single(m => m.Id == "text-to-speech");
                Require(tabs.Items.Count == 2 && ReferenceEquals(host.Content, voiceView), "Reopening a tab duplicated its instance");
                window.CloseModuleTab("subtitles"); window.CloseModuleTab("text-to-speech");
                Require(tabs.Items.Count == 0 && host.Content is null, "Closing the last tab failed");
                selector.SelectedIndex = 0;
                Require(tabs.Items.Count == 1 && ReferenceEquals(host.Content, subtitles), "Reopening subtitles failed");
                await voiceView.ShutdownAsync();
                Console.WriteLine("PASS: unique tabs, close/reopen, empty state and reference waveform.");

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

    private static void Render(MainWindow window, string path, int width = 1040, int height = 660)
    {
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(width, height));
        content.Arrange(new Rect(0, 0, width, height));
        content.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var drawing = background.RenderOpen()) drawing.DrawRectangle(window.Background, null, new Rect(0, 0, width, height));
        bitmap.Render(background);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
