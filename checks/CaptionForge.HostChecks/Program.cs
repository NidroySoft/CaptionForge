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
     Console.WriteLine("PASS: independent discovery, dependencies and WPF view: " + definition.DisplayName);
    }
    await registry.ShutdownAsync(); result = 0;
   }
   catch (Exception e) { Console.Error.WriteLine(e); }
   finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
  }
  app.Dispatcher.BeginInvoke(Check); Dispatcher.Run(); return result;
 }
}
