using System.Windows;
using System.Windows.Threading;
using CaptionForge.Modularity;

internal static class Program
{
 [STAThread]
 private static int Main()
 {
  var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
  app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/CaptionForge;component/Resources/Theme.xaml") });
  SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
  int result = 1;
  async void Check()
  {
   try
   {
    CheckLanguages();
    var registry = new ModuleRegistry();
    var discovery = new ModuleDiscovery();
    var errors = discovery.RegisterFrom(System.IO.Path.Combine(AppContext.BaseDirectory, "Modules"), registry);
    if (errors.Count > 0 || registry.Definitions.Count == 0) throw new InvalidOperationException("No modules discovered: " + string.Join("; ", errors));
    foreach (var definition in registry.Definitions)
    {
     var module = registry.Get(definition.Id);
     if (module.View is not FrameworkElement view) throw new InvalidOperationException("Invalid view");
     if (!view.GetType().Assembly.Location.StartsWith(System.IO.Path.Combine(AppContext.BaseDirectory, "Modules"), StringComparison.OrdinalIgnoreCase))
      throw new InvalidOperationException("Module was linked into the host instead of loaded from its folder");
     await Dispatcher.Yield(DispatcherPriority.ContextIdle);
     view.Measure(new Size(1040, 660)); view.Arrange(new Rect(0, 0, 1040, 660)); view.UpdateLayout();
     var testWindow = new Window { Content = view };
     var model = view.DataContext!;
     var selected = model.GetType().GetProperty("SelectedLanguage");
     var before = selected?.GetValue(model);
     foreach (var code in new[] { "en", "es", "en", "es" })
     {
      InterfaceLanguage.Select(code);
      await Dispatcher.Yield(DispatcherPriority.ContextIdle);
      view.Measure(new Size(1040, 660)); view.Arrange(new Rect(0,0,1040,660)); view.UpdateLayout();
      if (selected is not null && !ReferenceEquals(before,selected.GetValue(model))) throw new Exception("UI language reset the content language");
      var labels = Descendants(view).OfType<System.Windows.Controls.TextBlock>().Select(t=>t.Text).ToArray();
      var expected = definition.Id == "media-tools" ? (code == "en" ? "Audio and transcription" : "Audio y transcripción") : (code == "en" ? "Generate audio" : "Generar audio");
      if(code=="en")
      {
       var output=System.IO.Path.GetFullPath("artifacts/language-checks");System.IO.Directory.CreateDirectory(output);
       var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap(1040,660,96,96,System.Windows.Media.PixelFormats.Pbgra32);
       bitmap.Render(view);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
       using var image=System.IO.File.Create(System.IO.Path.Combine(output,definition.Id+"-en.png"));encoder.Save(image);
      }
      if (!labels.Contains(expected)) throw new Exception("Missing live module translation: " + expected + " labels=" + string.Join(" | ",labels) + " resource=" + app.Resources["MediaTools.t0"] + " code=" + InterfaceLanguage.Code);
     }
     testWindow.Content = null; testWindow.Close();
     Console.WriteLine("PASS: independent discovery, dependencies and WPF view: " + definition.DisplayName);
    }
    await registry.ShutdownAsync(); result = 0;
   }
   catch (Exception e) { Console.Error.WriteLine(e); }
   finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
  }
  app.Dispatcher.BeginInvoke(Check); Dispatcher.Run(); return result;
 }
 private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
 {
  for (int i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);i++)
  {
   var child=System.Windows.Media.VisualTreeHelper.GetChild(parent,i); yield return child;
   foreach(var nested in Descendants(child)) yield return nested;
  }
 }
 private static void CheckLanguages()
 {
  var folder=System.IO.Path.Combine(AppContext.BaseDirectory,"Languages");
  using var stream=System.IO.File.OpenRead(System.IO.Path.Combine(folder,"es.json"));
  var fallback=CaptionForge.Desktop.Localization.LanguageCatalog.Parse(stream,"es");
  var scan=CaptionForge.Desktop.Localization.LanguageCatalog.Scan(folder,fallback);
  if(scan.RejectedFiles.Count!=0 || !scan.Languages.Any(l=>l.Code=="en")) throw new Exception("English catalog missing or invalid");
  using var englishStream=System.IO.File.OpenRead(System.IO.Path.Combine(folder,"en.json"));
  var english=CaptionForge.Desktop.Localization.LanguageCatalog.Parse(englishStream,"en");
  if(!fallback.Translations.Keys.ToHashSet().SetEquals(english.Translations.Keys)) throw new Exception("Translation keys differ");
  string temp=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"CaptionForge-language-"+Guid.NewGuid().ToString("N"));
  System.IO.Directory.CreateDirectory(temp);
  try
  {
   var store=new CaptionForge.Desktop.Localization.InterfacePreferencesStore(System.IO.Path.Combine(temp,"interface.json"));
   CaptionForge.Desktop.Localization.LocalizationService Create()=>new(folder,store,()=>System.IO.File.OpenRead(System.IO.Path.Combine(folder,"es.json")),_=>{});
   var service=Create();service.Initialize();service.Select("en");
   if(service.T("shell.0")!="Tools")throw new Exception("English selection failed");
   var fresh=Create();fresh.Initialize();if(fresh.SelectedCode!="en")throw new Exception("Language was not persisted");
   fresh.Select("es");if(fresh.T("shell.0")!="Herramientas")throw new Exception("Spanish restore failed");
   Console.WriteLine("PASS: complete English catalog, placeholder validation, live switch, and isolated preference persistence");
  }
  finally { System.IO.Directory.Delete(temp,true); }
 }

}
