using System.Windows;
using CaptionForge.Desktop.Services;
using CaptionForge.Desktop.ViewModels;
namespace CaptionForge.Desktop;
public partial class App : System.Windows.Application
{
 protected override void OnStartup(StartupEventArgs e)
 {
  base.OnStartup(e);
  DispatcherUnhandledException += (_, args) => { AppLog.Write(args.Exception); MessageBox.Show(args.Exception.Message,"CaptionForge",MessageBoxButton.OK,MessageBoxImage.Error); args.Handled=true; };
  CaptionForge.Desktop.Localization.LocalizationService.Current.Initialize();
  CaptionForge.Modularity.InterfaceLanguage.Select(Localization.LocalizationService.Current.SelectedCode);
  Localization.LocalizationService.Current.LanguageChanged += (_, _) => CaptionForge.Modularity.InterfaceLanguage.Select(Localization.LocalizationService.Current.SelectedCode);
  var vm=new MainViewModel(new DesktopDialogs(),new AudioPreviewService());
  var window=new MainWindow { DataContext=vm }; MainWindow=window;window.Show();
 }
}
