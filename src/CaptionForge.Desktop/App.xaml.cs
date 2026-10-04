using CaptionForge.Desktop.Services;
using CaptionForge.Desktop.ViewModels;
using System.Windows;
namespace CaptionForge.Desktop;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            AppLog.Write(args.Exception);
            MessageBox.Show(args.Exception.Message, "CaptionForge", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        var vm = new MainViewModel(new DesktopDialogs(), new AudioPreviewService());
        var window = new MainWindow { DataContext = vm };
        MainWindow = window;
        window.Show();
    }
}
