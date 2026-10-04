using CaptionForge.Application.Abstractions;
using CaptionForge.Application.Models.Settings;
using CaptionForge.Infrastructure.Configuration;
using CaptionForge.Infrastructure.Internal;

namespace CaptionForge.Infrastructure.Settings;

public sealed class JsonApplicationSettingsStore : IApplicationSettingsStore
{
    private readonly string _path;
    public JsonApplicationSettingsStore(string? path = null) => _path=PathSafety.Full(path ?? InfrastructurePaths.DefaultSettings);
    public async Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(_path)) return new();
        var doc=await JsonFiles.ReadObjectAsync(_path,cancellationToken).ConfigureAwait(false);
        if (JsonFiles.Long(doc,"schemaVersion") != 1) throw new InvalidDataException("Versión de ajustes no admitida.");
        return JsonFiles.Read<ApplicationSettings>(doc["settings"]!);
    }
    public async Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        using var lease=await FileLease.AcquireAsync(_path+".lock",cancellationToken).ConfigureAwait(false);
        await JsonFiles.WriteAsync(_path,new System.Text.Json.Nodes.JsonObject { ["schemaVersion"]=1,["settings"]=JsonFiles.Node(settings) },cancellationToken).ConfigureAwait(false);
    }
}
