using System.ComponentModel;
using System.Runtime.CompilerServices;
namespace CaptionForge.Desktop.Mvvm;
public abstract class ObservableObject : INotifyPropertyChanged
{
 protected ObservableObject() => System.Windows.WeakEventManager<CaptionForge.Desktop.Localization.LocalizationService,EventArgs>.AddHandler(CaptionForge.Desktop.Localization.LocalizationService.Current,nameof(CaptionForge.Desktop.Localization.LocalizationService.LanguageChanged),OnLanguageChanged);
 private void OnLanguageChanged(object? sender,EventArgs e)=>Raise(null);
 public event PropertyChangedEventHandler? PropertyChanged;
 protected bool Set<T>(ref T field,T value,[CallerMemberName] string? name=null)
 { if(EqualityComparer<T>.Default.Equals(field,value))return false;field=value;Raise(name);return true; }
 protected void Raise([CallerMemberName] string? name=null)=>PropertyChanged?.Invoke(this,new(name));
}
