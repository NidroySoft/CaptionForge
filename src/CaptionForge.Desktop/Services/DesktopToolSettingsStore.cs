using System.IO;
using System.Text.Json;
namespace CaptionForge.Desktop.Services;
public sealed record DesktopToolSettings(string FfmpegPath,string FfprobePath);
/// <summary>Preferencias específicas de escritorio sin cambiar el contrato de ApplicationSettings.</summary>
internal static class DesktopToolSettingsStore
{
    private static string PathName=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CaptionForge","desktop-tools.json");
    public static async Task<DesktopToolSettings?> LoadAsync(CancellationToken ct)
    {
        if(!File.Exists(PathName))return null;
        await using var stream=File.OpenRead(PathName);return await JsonSerializer.DeserializeAsync<DesktopToolSettings>(stream,cancellationToken:ct);
    }
    public static async Task SaveAsync(DesktopToolSettings settings,CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);string staging=PathName+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{await File.WriteAllTextAsync(staging,JsonSerializer.Serialize(settings),ct);ct.ThrowIfCancellationRequested();File.Move(staging,PathName,overwrite:true);}
        finally{if(File.Exists(staging))File.Delete(staging);}
    }
}
