using System.Windows;
namespace CaptionForge.Desktop.Views;
public partial class HelpWindow : Window
{
 public bool OnlyNewSteps {get;private set;}
 public HelpWindow(Window owner,bool hasNew){InitializeComponent();Owner=owner;DialogSizing.Fit(this,owner);NewButton.IsEnabled=hasNew;}
 private void StartFull(object sender,RoutedEventArgs e){OnlyNewSteps=false;DialogResult=true;}
 private void StartNew(object sender,RoutedEventArgs e){OnlyNewSteps=true;DialogResult=true;}
 private void CloseHelp(object sender,RoutedEventArgs e)=>Close();
}
