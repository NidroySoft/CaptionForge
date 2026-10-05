using System.IO;
using System.Text.Json;
namespace CaptionForge.Desktop.Localization;
public sealed record InterfacePreferences
{
    public string LanguageCode { get; init; }="es";
    public bool TutorialOffered { get; init; }
    public string[] CompletedTutorialSteps { get; init; }=[];
    public string[] KnownTutorialSteps {get;init;}=[];
}
public sealed class InterfacePreferencesStore(string path)
{
    public InterfacePreferences Load()=>File.Exists(path)?JsonSerializer.Deserialize<InterfacePreferences>(File.ReadAllBytes(path)) ?? new():new();
    public void Save(InterfacePreferences value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try { File.WriteAllBytes(temp,JsonSerializer.SerializeToUtf8Bytes(value,new JsonSerializerOptions{WriteIndented=true}));File.Move(temp,path,true); }
        finally { if(File.Exists(temp))File.Delete(temp); }
    }
}
