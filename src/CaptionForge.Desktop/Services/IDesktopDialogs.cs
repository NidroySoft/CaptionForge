namespace CaptionForge.Desktop.Services;
public interface IDesktopDialogs
{
 string? Folder(string title,string? initial=null);
 string? File(string title,string filter);
 string? SaveFile(string title,string filter,string suggested);
 bool Confirm(string title,string message);
 void OpenFolder(string path);
}
