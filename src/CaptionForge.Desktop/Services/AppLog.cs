using System.IO;
namespace CaptionForge.Desktop.Services;
internal static class AppLog
{
 public static void Write(Exception ex)
 {
  try { string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CaptionForge","Logs");Directory.CreateDirectory(folder);File.AppendAllText(Path.Combine(folder,"desktop.log"),$"{DateTimeOffset.Now:O} {ex}\n"); }
  catch(IOException){} catch(UnauthorizedAccessException){}
 }
}
