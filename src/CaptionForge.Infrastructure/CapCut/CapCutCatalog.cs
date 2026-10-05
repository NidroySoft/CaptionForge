using System.Globalization;
using System.Text.Json.Nodes;
using CaptionForge.Application.Abstractions;
using CaptionForge.Application.Models.CapCut;
using CaptionForge.Core.Enums;
using CaptionForge.Core.Models.CapCut;
using CaptionForge.Core.Models.Media;
using CaptionForge.Core.ValueObjects;
using CaptionForge.Infrastructure.Internal;

namespace CaptionForge.Infrastructure.CapCut;

/// <summary>Lector del formato observado en CapCut 9.5; no interpreta otros formatos como si fueran equivalentes.</summary>
public sealed class CapCutCatalog : ICapCutCatalog
{
    public IReadOnlyList<string> LastWarnings { get; private set; } = Array.Empty<string>();
    public async Task<IReadOnlyList<CapCutProject>> FindProjectsAsync(string projectsRoot,CancellationToken cancellationToken = default)
    {
        string root=PathSafety.Full(projectsRoot);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("No existe la carpeta de proyectos indicada.");
        var projects=new List<CapCutProject>();var warnings=new List<string>();
        foreach (string directory in Directory.EnumerateDirectories(root).Order(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();string meta=Path.Combine(directory,"draft_meta_info.json");
            if (!File.Exists(meta)) continue;
            try
            {
                var doc=await JsonFiles.ReadObjectAsync(meta,cancellationToken).ConfigureAwait(false);
                string id=JsonFiles.String(doc,"draft_id");PathSafety.Part(id);
                string registryPath=Path.Combine(directory,"Timelines","project.json");
                if (!File.Exists(registryPath)) { warnings.Add($"{directory}: falta el registro de timelines del formato soportado.");continue; }
                var registry=await JsonFiles.ReadObjectAsync(registryPath,cancellationToken).ConfigureAwait(false);
                var entries=JsonFiles.Array(registry,"timelines");
                if (!entries.Any(t => t?["is_marked_delete"]?.GetValue<bool>() != true && File.Exists(Path.Combine(directory,"Timelines",PathSafety.Part(JsonFiles.String(t,"id")),"draft_content.json"))))
                { warnings.Add($"{directory}: no hay una timeline válida.");continue; }
                string name=doc["draft_name"]?.GetValue<string>() ?? Path.GetFileName(directory);
                string cover=doc["draft_cover"]?.GetValue<string>() ?? "draft_cover.jpg";
                string coverPath=Path.IsPathRooted(cover) ? cover : Path.Combine(directory,cover);
                projects.Add(new(id,string.IsNullOrWhiteSpace(name)?Path.GetFileName(directory):name,directory,
                    File.Exists(coverPath)?coverPath:null,new DateTimeOffset(File.GetLastWriteTimeUtc(meta)),JsonFiles.String(registry,"id")));
            }
            catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException or ArgumentException or IOException or UnauthorizedAccessException)
            { warnings.Add($"{directory}: {ex.Message}"); }
        }
        LastWarnings=warnings.AsReadOnly();
        var duplicated=projects.GroupBy(p=>p.Id,StringComparer.Ordinal).Where(g=>g.Count()>1).Select(g=>g.Key).ToHashSet(StringComparer.Ordinal);
        foreach (string id in duplicated) warnings.Add($"El draft_id {id} aparece en varias carpetas; resuelve la duplicación antes de usar su espacio propio.");
        LastWarnings=warnings.AsReadOnly();
        return projects.Where(p=>!duplicated.Contains(p.Id)).OrderByDescending(p=>p.LastModifiedAt).ThenBy(p=>p.Name,StringComparer.OrdinalIgnoreCase).ToList().AsReadOnly();
    }
    public async Task<IReadOnlyList<CapCutTimeline>> GetTimelinesAsync(CapCutProject project,CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);var warnings=new List<string>();var result=new List<CapCutTimeline>();
        var meta=await JsonFiles.ReadObjectAsync(Path.Combine(project.DirectoryPath,"draft_meta_info.json"),cancellationToken).ConfigureAwait(false);
        if (JsonFiles.String(meta,"draft_id")!=project.Id) throw new InvalidDataException("El ID del proyecto cambió.");
        var registry=await JsonFiles.ReadObjectAsync(Path.Combine(project.DirectoryPath,"Timelines","project.json"),cancellationToken).ConfigureAwait(false);
        if (project.TimelineRegistryId is not null && JsonFiles.String(registry,"id")!=project.TimelineRegistryId) throw new InvalidDataException("El registro pertenece a otro proyecto.");
        var ids=new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in JsonFiles.Array(registry,"timelines"))
        {
            if (entry?["is_marked_delete"]?.GetValue<bool>()==true) continue;
            string id=JsonFiles.String(entry,"id");if (!ids.Add(id)) throw new InvalidDataException("ID de timeline duplicado.");
            try
            {
                string directory=Path.Combine(project.DirectoryPath,"Timelines",PathSafety.Part(id));string path=Path.Combine(directory,"draft_content.json");
                var doc=await JsonFiles.ReadObjectAsync(path,cancellationToken).ConfigureAwait(false);
                result.Add(Timeline(project,entry!,doc,directory,path,registry["main_timeline_id"]?.GetValue<string>()==id));
            }
            catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException or IOException or ArgumentException)
            { warnings.Add($"Timeline {id}: {ex.Message}"); }
        }
        LastWarnings=warnings.AsReadOnly();return result.AsReadOnly();
    }
    public async Task<TimelineSnapshot> ReadTimelineAsync(CapCutProject project,CapCutTimeline timeline,CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);ArgumentNullException.ThrowIfNull(timeline);
        if (project.Id!=timeline.ProjectId) throw new InvalidDataException("Timeline de otro proyecto.");
        string directory=Path.Combine(PathSafety.Full(project.DirectoryPath),"Timelines",PathSafety.Part(timeline.Id));
        string path=Path.Combine(directory,"draft_content.json");
        if (!PathSafety.Full(timeline.DraftContentPath).Equals(path,PathSafety.Comparison)) throw new InvalidDataException("La ruta no corresponde a la timeline.");
        var paths=new[] {path,path+".bak",Path.Combine(project.DirectoryPath,"draft_content.json"),Path.Combine(project.DirectoryPath,"draft_content.json.bak"),
            Path.Combine(project.DirectoryPath,"draft_meta_info.json"),Path.Combine(project.DirectoryPath,"Timelines","project.json")};
        var bytes=new Dictionary<string,byte[]>();var stamps=new List<SourceFileStamp>();
        foreach (string file in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(file)) { var data=await File.ReadAllBytesAsync(file,cancellationToken).ConfigureAwait(false);bytes[file]=data;stamps.Add(new(file,true,JsonFiles.Hash(data))); }
            else stamps.Add(new(file,false));
        }
        await JsonFiles.VerifyAsync(stamps,cancellationToken).ConfigureAwait(false);
        var meta=JsonFiles.Parse(bytes[Path.Combine(project.DirectoryPath,"draft_meta_info.json")]);
        if (JsonFiles.String(meta,"draft_id")!=project.Id) throw new InvalidDataException("El proyecto cambió durante la lectura.");
        var registry=JsonFiles.Parse(bytes[Path.Combine(project.DirectoryPath,"Timelines","project.json")]);
        if (project.TimelineRegistryId is not null && JsonFiles.String(registry,"id")!=project.TimelineRegistryId) throw new InvalidDataException("El registro cambió de identidad.");
        var entry=JsonFiles.Array(registry,"timelines").SingleOrDefault(x=>x?["id"]?.GetValue<string>()==timeline.Id && x?["is_marked_delete"]?.GetValue<bool>()!=true)
            ?? throw new InvalidDataException("La timeline ya no está en el registro.");
        var draft=JsonFiles.Parse(bytes[path]);
        var current=Timeline(project,entry,draft,directory,path,registry["main_timeline_id"]?.GetValue<string>()==timeline.Id);
        var tracks=new List<MediaTrack>();var warnings=new List<string>();
        var materials=draft["materials"] as JsonObject ?? throw new InvalidDataException("Faltan materiales.");
        foreach (var track in JsonFiles.Array(draft,"tracks"))
        {
            string type=JsonFiles.String(track,"type"),id=JsonFiles.String(track,"id");string name=track?["name"]?.GetValue<string>() ?? "";
            if (type=="text") { tracks.Add(new(id,name,MediaTrackType.Text));continue; }
            if (type is not ("audio" or "video")) { warnings.Add($"Pista {id}: tipo {type} aún no soportado.");continue; }
            var segments=new List<MediaSegment>();
            foreach (var s in JsonFiles.Array(track,"segments"))
            {
                string sid=JsonFiles.String(s,"id");
                try
                {
                    string materialId=JsonFiles.String(s,"material_id");
                    var material=JsonFiles.Array(materials,type=="audio"?"audios":"videos").SingleOrDefault(m=>m?["id"]?.GetValue<string>()==materialId)
                        ?? throw new InvalidDataException("No se resuelve el material del fragmento; puede ser compuesto.");
                    // Un vídeo sin audio no es una fuente de transcripción. Si el
                    // campo falta, FFprobe mantiene la comprobación del medio real.
                    if(type=="video" && material?["has_audio"]?.GetValue<bool>()==false)continue;
                    string source=JsonFiles.String(material,"path");if (string.IsNullOrWhiteSpace(source)) throw new InvalidDataException("Material sin ruta de medio local.");
                    if (material?["type"]?.GetValue<string>() is "draft" or "combination") throw new InvalidDataException("Material compuesto pendiente de soporte.");
                    double speed=s?["speed"]?.GetValue<double>() ?? 1;bool variable=false;
                    var refs=s?["extra_material_refs"] as JsonArray ?? new JsonArray();
                    foreach (var sp in (materials["speeds"] as JsonArray ?? new JsonArray()).Where(sp=>refs.Any(r=>r?.GetValue<string>()==sp?["id"]?.GetValue<string>())))
                    {
                        variable |= sp?["curve_speed"] is not null || (sp?["mode"]?.GetValue<int>() ?? 0)!=0;
                        if (!variable && sp?["speed"] is not null && Math.Abs(sp["speed"]!.GetValue<double>()-speed)>1e-9) throw new InvalidDataException("Velocidades inconsistentes entre segmento y material.");
                    }
                    bool muted=(s?["volume"]?.GetValue<double>() ?? 1)<=0 || (type=="video" && draft["config"]?["video_mute"]?.GetValue<bool>()==true);
                    segments.Add(new(sid,id,materialId,source,Range(s?["source_timerange"]),Range(s?["target_timerange"]),speed,variable,s?["reverse"]?.GetValue<bool>() ?? false,muted));
                    if (!File.Exists(source)) warnings.Add($"Fragmento {sid}: medio ausente; se puede localizar para esta ejecución.");
                }
                catch (Exception ex) when (ex is InvalidDataException or ArgumentException or InvalidOperationException or OverflowException)
                { warnings.Add($"Fragmento {sid}: {ex.Message}"); }
            }
            tracks.Add(new(id,name,type=="audio"?MediaTrackType.Audio:MediaTrackType.Video,segments));
        }
        LastWarnings=warnings.AsReadOnly();return new(project,current,tracks,stamps);
    }
    private static TimeRangeUs Range(JsonNode? value) => new(JsonFiles.Long(value,"start"),JsonFiles.Long(value,"duration"));
    private static CapCutTimeline Timeline(CapCutProject project,JsonNode entry,JsonObject draft,string directory,string path,bool main)
    {
        if (JsonFiles.Long(draft,"version")!=360000) throw new InvalidDataException("El esquema de este proyecto no coincide con el formato CapCut comprobado (version 360000).");
        string id=JsonFiles.String(entry,"id");if (JsonFiles.String(draft,"id")!=id) throw new InvalidDataException("ID interno distinto al registro de timeline.");
        decimal value=draft["fps"]!.GetValue<decimal>();if (value<=0 || value>1000) throw new InvalidDataException("FPS fuera del rango soportado.");
        int denominator=1;while (decimal.Truncate(value)!=value && denominator<1_000_000) { value*=10;denominator*=10; }
        if (decimal.Truncate(value)!=value) throw new InvalidDataException("FPS necesita una razón explícita; no se redondeará silenciosamente.");
        var rate=new FrameRate(checked((int)value),denominator);
        string name=entry["name"]?.GetValue<string>() ?? id;string cover=Path.Combine(directory,"draft_cover.jpg");
        return new(id,project.Id,string.IsNullOrWhiteSpace(name)?id:name,directory,path,rate,JsonFiles.Long(draft,"duration"),main,File.Exists(cover)?cover:null);
    }
}
