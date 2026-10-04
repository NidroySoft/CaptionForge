using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;
namespace CaptionForge.Desktop.Services;
public sealed class DesktopDialogs : IDesktopDialogs
{
 public string? Folder(string title,string? initial=null)
 { var d=new OpenFolderDialog { Title=title,Multiselect=false };if(Directory.Exists(initial))d.InitialDirectory=initial;return d.ShowDialog()==true?d.FolderName:null; }
 public string? File(string title,string filter)
 {var d=new OpenFileDialog {Title=title,Filter=filter,CheckFileExists=true,Multiselect=false};return d.ShowDialog()==true?d.FileName:null;}
 public string? SaveFile(string title,string filter,string suggested)
 {var d=new SaveFileDialog {Title=title,Filter=filter,FileName=suggested,AddExtension=true,OverwritePrompt=true};return d.ShowDialog()==true?d.FileName:null;}
 public bool Confirm(string title,string message)=>MessageBox.Show(message,title,MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)==MessageBoxResult.Yes;
 public void OpenFolder(string path)
 {if(!Directory.Exists(path))throw new DirectoryNotFoundException(path);Process.Start(new ProcessStartInfo(path){UseShellExecute=true});}
}
