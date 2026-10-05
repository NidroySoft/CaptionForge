using System.IO;
namespace CaptionForge.Desktop.Localization;
/// <summary>Adaptador WPF: el catálogo, la selección y las preferencias se pueden probar sin una ventana.</summary>
public sealed partial class LocalizationService
{
 public static LocalizationService Current {get;}=new(
   Path.Combine(AppContext.BaseDirectory,"Languages"),
   new InterfacePreferencesStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CaptionForge","interface.json")),
   ()=>EmbeddedLanguageResource.Open(typeof(LocalizationService).Assembly),
   texts=>{foreach(var pair in texts)System.Windows.Application.Current.Resources["Loc."+pair.Key]=pair.Value;});
}
