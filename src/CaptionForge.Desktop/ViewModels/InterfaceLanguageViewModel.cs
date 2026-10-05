using System.Windows;
using CaptionForge.Desktop.Localization;
using CaptionForge.Desktop.Mvvm;
namespace CaptionForge.Desktop.ViewModels;
public sealed class InterfaceLanguageViewModel : ObservableObject
{
    private readonly LocalizationService _service=LocalizationService.Current;
    private bool _refreshing;
    public IReadOnlyList<LanguageDocument> AvailableLanguages=>_service.Languages;
    public LanguageDocument? SelectedLanguage
    {
        get=>AvailableLanguages.FirstOrDefault(d=>d.Code==_service.SelectedCode);
        set{if(!_refreshing && value is not null && value.Code!=_service.SelectedCode)_service.Select(value.Code);}
    }
    public string Notice=>_service.Notice;
    public string LanguageFolder=>_service.DirectoryPath;
    public InterfaceLanguageViewModel()
    { WeakEventManager<LocalizationService,EventArgs>.AddHandler(_service,nameof(LocalizationService.LanguageChanged),OnChanged); }
    public void Reload()=>_service.Reload();
    private void OnChanged(object? sender,EventArgs e)
    {
        _refreshing=true;
        try{Raise(nameof(AvailableLanguages));Raise(nameof(SelectedLanguage));Raise(nameof(Notice));}
        finally{_refreshing=false;}
    }
}
