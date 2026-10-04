using System.Text.Json.Nodes;
using CaptionForge.Application.Enums;
using CaptionForge.Application.Models.Writing;
using CaptionForge.Infrastructure.Internal;
using CaptionForge.Infrastructure.Workspace;

namespace CaptionForge.Infrastructure.CapCut;

/// <summary>Registro por pista; conserva «managed» para lectores anteriores y migra sus registros al siguiente commit.</summary>
internal static class DraftTemplateRegistry
{
    internal static string TrackId(ManagedSubtitleSet set)=>set.Objects.Single(o=>o.Kind==SubtitleObjectKind.Track).Id;
    internal static JsonArray Entries(JsonObject? registry)
    {
        if(registry is null) return [];
        if(JsonFiles.Long(registry,"schemaVersion")!=1) throw new InvalidDataException("Registro de IDs de otra versión.");
        var entries=registry["tracks"] is JsonArray tracks?tracks.DeepClone().AsArray():new JsonArray(new JsonObject { ["managed"]=registry["managed"]!.DeepClone() });
        var sets=entries.Select(e=>JsonWorkspaceStore.ReadManaged(e!["managed"]!)).ToArray();
        if(sets.Select(TrackId).Distinct(StringComparer.Ordinal).Count()!=sets.Length ||
            sets.SelectMany(s=>s.Objects).Select(o=>o.Id).Distinct(StringComparer.Ordinal).Count()!=sets.Sum(s=>s.Objects.Count))
            throw new InvalidDataException("El registro por pistas contiene identidades repetidas.");
        var latest=JsonWorkspaceStore.ReadManaged(registry["managed"]!);
        if(sets.Any(s=>s.ProjectId!=latest.ProjectId || s.TimelineId!=latest.TimelineId) ||
            !sets.Any(s=>s.Objects.SequenceEqual(latest.Objects))) throw new InvalidDataException("El registro por pistas no coincide con la última ejecución.");
        return entries;
    }

    internal static JsonObject SeedGraph(JsonObject doc,string segmentId)
    {
        var track=JsonFiles.Array(doc,"tracks").Single(t=>JsonFiles.Array(t,"segments").Any(s=>s?["id"]?.GetValue<string>()==segmentId))!;
        var segment=JsonFiles.Array(track,"segments").Single(s=>s?["id"]?.GetValue<string>()==segmentId)!;
        var template=JsonFiles.Array(doc["materials"],"text_templates").Single(t=>t?["id"]?.GetValue<string>()==segment["material_id"]?.GetValue<string>())!;
        var ids=new HashSet<string>(StringComparer.Ordinal){JsonFiles.String(template,"id")};
        foreach(var layer in JsonFiles.Array(template,"text_info_resources"))
        {
            ids.Add(JsonFiles.String(layer,"text_material_id"));
            foreach(var id in JsonFiles.Array(layer,"extra_material_refs")) ids.Add(id!.GetValue<string>());
        }
        foreach(var id in JsonFiles.Array(segment,"extra_material_refs")) ids.Add(id!.GetValue<string>());
        var materials=new JsonObject();
        foreach(var category in doc["materials"]!.AsObject()) if(category.Value is JsonArray items)
            materials[category.Key]=new JsonArray(items.Where(n=>ids.Contains(n?["id"]?.GetValue<string>() ?? "")).Select(n=>n!.DeepClone()).ToArray());
        var seedTrack=track.DeepClone();seedTrack["segments"]=new JsonArray(segment.DeepClone());
        return new JsonObject { ["tracks"]=new JsonArray(seedTrack),["materials"]=materials };
    }
    internal static string Fingerprint(JsonObject doc,string segmentId)=>JsonFiles.Hash(JsonFiles.Bytes(SeedGraph(doc,segmentId)));
}
