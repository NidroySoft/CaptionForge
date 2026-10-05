using System.Diagnostics;
using System.IO;
using System.Windows;
using CaptionForge.Desktop.Localization;
using CaptionForge.Desktop.Services;
using CaptionForge.Desktop.ViewModels;
using Microsoft.Win32;
namespace CaptionForge.Desktop.Views;
public partial class ApplicationSettingsWindow : Window
{
    private readonly MainViewModel _vm;
    private bool _saving;
    public ApplicationSettingsWindow(MainViewModel vm,Window owner)
    {
        InitializeComponent();_vm=vm;DataContext=vm;Owner=owner;
        DialogSizing.Fit(this,owner);
        WorkspaceBox.Text=vm.WorkspaceRoot;FfmpegBox.Text=vm.FfmpegPath;FfprobeBox.Text=vm.FfprobePath;
        Closing+=(_,e)=>{if(_saving)e.Cancel=true;};
    }
    private void LanguageDropDownOpened(object sender,EventArgs e)=>_vm.InterfaceLanguage.Reload();
    private void OpenLanguages(object sender,RoutedEventArgs e)
    {
        try{Directory.CreateDirectory(LocalizationService.Current.DirectoryPath);Process.Start(new ProcessStartInfo(LocalizationService.Current.DirectoryPath){UseShellExecute=true});}
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception){ShowError(ex);}
    }
    private void BrowseWorkspace(object sender,RoutedEventArgs e)
    {var d=new OpenFolderDialog{Title=L.T("settings.workspace")};if(Directory.Exists(WorkspaceBox.Text))d.InitialDirectory=WorkspaceBox.Text;if(d.ShowDialog(this)==true)WorkspaceBox.Text=d.FolderName;}
    private void BrowseFfmpeg(object sender,RoutedEventArgs e)=>BrowseTool(FfmpegBox,"ffmpeg.exe");
    private void BrowseFfprobe(object sender,RoutedEventArgs e)=>BrowseTool(FfprobeBox,"ffprobe.exe");
    private void BrowseTool(System.Windows.Controls.TextBox box,string executable)
    {var d=new OpenFileDialog{Title=L.F("settings.locateTool",executable),Filter=$"{executable}|{executable}",CheckFileExists=true};if(d.ShowDialog(this)==true)box.Text=d.FileName;}
    private async void SaveAndClose(object sender,RoutedEventArgs e)
    {
        if(_saving || _vm.IsBusy)return;
        try
        {
            string workspace=Path.GetFullPath(WorkspaceBox.Text.Trim()),ffmpeg=ValidateTool(FfmpegBox.Text),ffprobe=ValidateTool(FfprobeBox.Text);
            if(workspace!=_vm.WorkspaceRoot || ffmpeg!=_vm.FfmpegPath || ffprobe!=_vm.FfprobePath)
            {
                if(_vm.HasResult && MessageBox.Show(this,L.T("settings.invalidateResult"),L.T("settings.title"),MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)!=MessageBoxResult.Yes)return;
                Directory.CreateDirectory(workspace);
                _saving=true;IsEnabled=false;
                if(!await _vm.SaveApplicationConfigurationAsync(workspace,ffmpeg,ffprobe))return;
            }
            _saving=false;Close();
        }
        catch(Exception ex){ShowError(ex);}
        finally{_saving=false;IsEnabled=true;}
    }
    private static string ValidateTool(string value)
    {
        value=value.Trim();if(value.Length==0)throw new InvalidDataException(L.T("settings.emptyTool"));
        if(Path.IsPathFullyQualified(value) && !File.Exists(value))throw new FileNotFoundException(L.F("settings.toolMissing",value));
        return value;
    }
    private void ShowError(Exception ex){AppLog.Write(ex);MessageBox.Show(this,ex.Message,L.T("settings.title"),MessageBoxButton.OK,MessageBoxImage.Warning);}
    private void CloseDialog(object sender,RoutedEventArgs e)=>Close();
}
