using System.IO;
using System.Text.Json;
using CaptionForge.Desktop.Services;
namespace CaptionForge.Desktop.Localization;

/// <summary>Textos y preferencias de interfaz; no accede a opciones ni resultados de Whisper.</summary>
public sealed partial class LocalizationService : System.ComponentModel.INotifyPropertyChanged
{
    private readonly InterfacePreferencesStore _store;
    private readonly Func<Stream> _fallbackStream;
    private readonly Action<IReadOnlyDictionary<string,string>> _publish;
    private LanguageDocument _fallback=null!,_active=null!;
    private InterfacePreferences _preferences=new();
    private bool _initialized;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string,(string Key,object?[] Arguments)> _rendered=new(StringComparer.Ordinal);
    private readonly ThreadLocal<HashSet<string>> _rendering=new(()=>new(StringComparer.Ordinal));
    private string _notice="";
    private bool _preferencesUnreadable;
    public string DirectoryPath {get;}
    public LocalizationService(string directory,InterfacePreferencesStore store,Func<Stream> fallbackStream,Action<IReadOnlyDictionary<string,string>> publish)
    {DirectoryPath=directory;_store=store;_fallbackStream=fallbackStream;_publish=publish;}
    public IReadOnlyList<LanguageDocument> Languages { get; private set; }=[];
    public string SelectedCode=>_preferences.LanguageCode;
    public string Notice=>Render(_notice);
    public bool TutorialOffered=>_preferences.TutorialOffered;
    public IReadOnlySet<string> KnownSteps=>new HashSet<string>(_preferences.KnownTutorialSteps ?? [],StringComparer.Ordinal);
    public IReadOnlySet<string> CompletedSteps=>new HashSet<string>(_preferences.CompletedTutorialSteps ?? [],StringComparer.Ordinal);
    public event EventHandler<EventArgs>? LanguageChanged;
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private void Notify(){PropertyChanged?.Invoke(this,new(null));LanguageChanged?.Invoke(this,EventArgs.Empty);}
    public void Initialize()
    {
        if(_initialized)return;
        using var stream=_fallbackStream();
        _fallback=LanguageCatalog.Parse(stream,"es");
        _active=_fallback;_initialized=true;
        try { _preferences=_store.Load(); }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or JsonException)
        { AppLog.Write(ex);_preferencesUnreadable=true; }
        Reload();
    }
    public string T(string key)
    {
        string value=_active?.Translations.GetValueOrDefault(key) ?? _fallback?.Translations.GetValueOrDefault(key) ?? key;
        if(value!=key && !value.Contains('{'))Remember(value,key,[]);
        return value;
    }
    private void Remember(string text,string key,object?[] arguments)
    { if(_rendered.Count>4096)_rendered.Clear();_rendered[text]=(key,arguments); }
    public string Render(string value)
    {
        var rendering=_rendering.Value!;
        if(!rendering.Add(value))return value;
        try{return _rendered.TryGetValue(value,out var source)?source.Arguments.Length==0?T(source.Key):F(source.Key,source.Arguments):value;}
        finally{rendering.Remove(value);}
    }
    public string F(string key,params object?[] args)
    {
        try
        {
            var current=args.Select(a=>a is string text?Render(text):a).ToArray();
            string value=string.Format(System.Globalization.CultureInfo.CurrentCulture,T(key),current);
            Remember(value,key,args);return value;
        }
        catch(FormatException ex) { AppLog.Write(ex);return string.Format(_fallback.Translations.GetValueOrDefault(key) ?? key,args); }
    }
    public void Reload()
    {
        if(!_initialized){Initialize();return;}
        var scan=LanguageCatalog.Scan(DirectoryPath,_fallback);Languages=scan.Languages;
        var selected=Languages.FirstOrDefault(d=>d.Code==SelectedCode);
        bool missing=selected is null;
        _active=selected ?? Languages.Single(d=>d.Code=="es");
        if(missing)_preferences=_preferences with{LanguageCode="es"};
        ApplyResources();
        _notice=string.Join("\n",new[]{_preferencesUnreadable?T("language.preferencesFailed"):"",missing?T("language.missing"):"",scan.RejectedFiles.Count>0?F("language.rejected",string.Join(", ",scan.RejectedFiles)):""}.Where(s=>s.Length>0));
        if(missing)Save();
        Notify();
    }
    public void Select(string code)
    {
        var next=Languages.SingleOrDefault(d=>d.Code==code);if(next is null)return;
        _active=next;_preferences=_preferences with{LanguageCode=code};ApplyResources();Save();Notify();
    }
    private void ApplyResources()
    { _publish(_active.Translations); }
    public void MarkOffered(IEnumerable<string>? knownSteps=null){_preferences=_preferences with{TutorialOffered=true,KnownTutorialSteps=KnownSteps.Union(knownSteps ?? []).Order(StringComparer.Ordinal).ToArray()};Save();}
    public void CompleteStep(string id)
    { if(CompletedSteps.Contains(id))return;_preferences=_preferences with{CompletedTutorialSteps=CompletedSteps.Append(id).Order(StringComparer.Ordinal).ToArray()};Save(); }
    private void Save()
    {
        try{_store.Save(_preferences);_preferencesUnreadable=false;}
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){AppLog.Write(ex);_notice=T("language.saveFailed");}
    }
}
public static class L
{
    public static string T(string key)=>LocalizationService.Current.T(key);
    public static string Render(string value)=>LocalizationService.Current.Render(value);
    public static string F(string key,params object?[] args)=>LocalizationService.Current.F(key,args);
}
