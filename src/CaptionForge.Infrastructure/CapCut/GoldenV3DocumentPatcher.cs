using System.Text.Json.Nodes;
using CaptionForge.Application.Enums;
using CaptionForge.Application.Models.Writing;
using CaptionForge.Core.Models.Subtitles;
using CaptionForge.Infrastructure.Assets;
using CaptionForge.Infrastructure.Internal;

namespace CaptionForge.Infrastructure.CapCut;

internal static class GoldenV3DocumentPatcher
{
    internal static (JsonObject Document,ManagedSubtitleSet Managed,IReadOnlyList<string> Warnings) Patch(SubtitleWriteRequest request,JsonObject original,TemplateAssetPaths assets)
    {
        var result=(JsonObject)original.DeepClone();var materials=result["materials"] as JsonObject ?? throw new InvalidDataException("Faltan materiales.");
        var tracks=JsonFiles.Array(result,"tracks");var old=request.PreviouslyManaged;
        var warnings=new List<string>();
        if (tracks.Any(t=>t?["type"]?.GetValue<string>()=="text" && !(old?.Objects.Any(o=>o.Kind==SubtitleObjectKind.Track && o.Id==t?["id"]?.GetValue<string>()) ?? false)))
            warnings.Add("Se conservaron subtítulos/textos ajenos a CaptionForge; pueden verse duplicados si ocupan el mismo tiempo.");
        if ((result["canvas_config"]?["width"]?.GetValue<int>() ?? 0)>(result["canvas_config"]?["height"]?.GetValue<int>() ?? 0))
            warnings.Add("Se conserva el canvas horizontal. La posición/dimensiones visuales del molde v3 todavía requieren comprobación en CapCut.");
        var mold=JsonNode.Parse(GoldenV3Mold.Json)!.AsObject();var own=new List<ManagedSubtitleObject>();
        var reserved=CollectIds(result);string Id(SubtitleObjectKind kind,string key)
        {
            string? previous=old?.Objects.SingleOrDefault(o=>o.Kind==kind && o.LogicalKey==key)?.Id;
            string id=previous ?? Guid.NewGuid().ToString().ToUpperInvariant();
            if (previous is null && !reserved.Add(id)) throw new InvalidDataException("Colisión de ID nuevo.");
            own.Add(new(kind,id,key));return id;
        }
        if (old is not null) ValidateOwnership(result,old);
        string trackId=Id(SubtitleObjectKind.Track,"captions");
        var previousTrack=tracks.SingleOrDefault(t=>t?["id"]?.GetValue<string>()==trackId);
        var newTrack=previousTrack is JsonObject objectTrack ? (JsonObject)objectTrack.DeepClone() : new JsonObject { ["id"]=trackId,["type"]="text",["segments"]=new JsonArray(),["flag"]=1,["attribute"]=0,["name"]="",["is_default_name"]=true };
        if (previousTrack is not null) tracks.Remove(previousTrack);
        // Eliminar exclusivamente nodos registrados como propios; sus IDs se reutilizan en la misma función lógica.
        string group="caption_template_"+request.Run.CreatedAt.ToUnixTimeMilliseconds();
        if (old is not null)
        {
            foreach (var text in JsonFiles.Array(materials,"texts"))
                if (old.Objects.Any(o=>o.Kind==SubtitleObjectKind.Text && o.Id==text?["id"]?.GetValue<string>()))
                { group=text?["group_id"]?.GetValue<string>() ?? group;break; }
            foreach (string category in new[] {"texts","text_templates","material_animations","effects"})
            {
                var array=JsonFiles.Array(materials,category);
                foreach (var node in array.Where(n=>old.Objects.Any(o=>o.Id==n?["id"]?.GetValue<string>())).ToArray()) array.Remove(node);
            }
        }
        var segments=new JsonArray();newTrack["segments"]=segments;
        int renderBase=tracks.SelectMany(t=>JsonFiles.Array(t,"segments")).Select(s=>s?["render_index"]?.GetValue<int>() ?? 0).DefaultIfEmpty(0).Max();
        renderBase=checked(renderBase+1000);
        int trackRender=tracks.SelectMany(t=>JsonFiles.Array(t,"segments")).Select(s=>s?["track_render_index"]?.GetValue<int>() ?? 0).DefaultIfEmpty(0).Max();
        trackRender=checked(trackRender+1);
        var perSource=new Dictionary<string,int>(StringComparer.Ordinal);int sequence=0;
        foreach (var cue in request.Captions)
        {
            int ordinal=perSource.GetValueOrDefault(cue.SourceSegmentId);perSource[cue.SourceSegmentId]=ordinal+1;
            string key=cue.SourceSegmentId+":"+ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string segmentId=Id(SubtitleObjectKind.Segment,key),templateId=Id(SubtitleObjectKind.Template,key),textId=Id(SubtitleObjectKind.Text,key),
                attachmentId=Id(SubtitleObjectKind.Attachment,key),animationId=Id(SubtitleObjectKind.Animation,key),effectId=Id(SubtitleObjectKind.Effect,key);
            var segment=mold["segment"]!.DeepClone().AsObject();var template=mold["template"]!.DeepClone().AsObject();
            var text=mold["text_material"]!.DeepClone().AsObject();var animation=mold["animation"]!.DeepClone().AsObject();var effect=mold["effect"]!.DeepClone().AsObject();
            segment["id"]=segmentId;segment["material_id"]=templateId;segment["target_timerange"]=new JsonObject { ["start"]=cue.TimelineRange.StartUs,["duration"]=cue.TimelineRange.DurationUs };
            segment["extra_material_refs"]=new JsonArray(animationId,effectId);segment["render_index"]=checked(renderBase+sequence++);segment["track_render_index"]=trackRender;
            template["id"]=templateId;var attachment=template["text_info_resources"]![0]!;
            attachment["id"]=attachmentId;attachment["text_material_id"]=textId;attachment["extra_material_refs"]=new JsonArray(effectId,animationId);
            attachment["attach_info"]!["duration"]=cue.TimelineRange.DurationUs-1;
            text["id"]=textId;text["name"]=textId;text["group_id"]=group;
            var content=JsonNode.Parse(text["content"]!.GetValue<string>())!;content["text"]=cue.Text;
            foreach (var style in content["styles"]!.AsArray()) style!["range"]=new JsonArray(0,cue.Text.Length);
            Remap(content,mold,assets);text["content"]=content.ToJsonString();
            text["words"]=new JsonObject { ["start_time"]=new JsonArray(cue.Words.Select(w=>JsonValue.Create(w.StartTimeMs)).ToArray()),
                ["end_time"]=new JsonArray(cue.Words.Select(w=>JsonValue.Create(w.EndTimeMs)).ToArray()),["text"]=new JsonArray(cue.Words.Select(w=>JsonValue.Create(w.Text)).ToArray()) };
            animation["id"]=animationId;animation["animations"]![0]!["duration"]=cue.TimelineRange.DurationUs-1;effect["id"]=effectId;
            // Las propiedades desconocidas que CapCut añadió a nuestros nodos sobreviven a la actualización.
            var oldSegment=JsonFiles.Array(original,"tracks").SelectMany(t=>JsonFiles.Array(t,"segments")).SingleOrDefault(s=>s?["id"]?.GetValue<string>()==segmentId);
            if (oldSegment is not null) MergeUnknown(oldSegment,segment);
            foreach (var pair in new[] {("text_templates",template),("texts",text),("material_animations",animation),("effects",effect)})
            {
                var previous=JsonFiles.Array(original["materials"],pair.Item1).SingleOrDefault(n=>n?["id"]?.GetValue<string>()==pair.Item2["id"]!.GetValue<string>());
                if (previous is not null)
                {
                    MergeUnknown(previous,pair.Item2);
                    if (pair.Item1=="texts")
                    {
                        var oldContent=JsonNode.Parse(previous["content"]!.GetValue<string>())!;
                        var newContent=JsonNode.Parse(pair.Item2["content"]!.GetValue<string>())!;
                        MergeUnknown(oldContent,newContent);pair.Item2["content"]=newContent.ToJsonString();
                    }
                }
            }
            foreach (var node in new[] {template,text,animation,effect}) Remap(node,mold,assets);
            segments.Add(segment);JsonFiles.Array(materials,"texts").Add(text);JsonFiles.Array(materials,"text_templates").Add(template);
            JsonFiles.Array(materials,"material_animations").Add(animation);JsonFiles.Array(materials,"effects").Add(effect);
        }
        tracks.Add(newTrack);
        var managed=new ManagedSubtitleSet(request.Run.ProjectId,request.Run.TimelineId,own);
        Validate(result,request.Captions,managed);
        // Todo nodo externo permanece semánticamente igual; el espejo y sus backups se deciden fuera del patcher.
        return (result,managed,warnings.AsReadOnly());
    }
    private static void MergeUnknown(JsonNode previous,JsonNode replacement)
    {
        if (previous is JsonObject oldObject && replacement is JsonObject newObject)
            foreach (var property in oldObject)
            {
                if (!newObject.ContainsKey(property.Key)) newObject[property.Key]=property.Value?.DeepClone();
                else if (property.Value is not null && newObject[property.Key] is JsonNode child) MergeUnknown(property.Value,child);
            }
        else if (previous is JsonArray oldArray && replacement is JsonArray newArray)
            for (int i=0;i<Math.Min(oldArray.Count,newArray.Count);i++)
                if (oldArray[i] is JsonNode oldChild && newArray[i] is JsonNode newChild) MergeUnknown(oldChild,newChild);
    }
    private static void Remap(JsonNode node,JsonObject mold,TemplateAssetPaths assets)
    {
        var map=new Dictionary<string,string>(StringComparer.Ordinal)
        {
            [JsonFiles.String(mold["template"],"path")]=assets.TemplateDirectory.Replace('\\','/'),
            [JsonFiles.String(mold["text_material"]!["fonts"]![0],"path")]=assets.FontFile.Replace('\\','/'),
            [JsonFiles.String(mold["effect"],"path")]=assets.EffectDirectory.Replace('\\','/'),
            [JsonFiles.String(mold["animation"]!["animations"]![0],"path")]=assets.AnimationDirectory.Replace('\\','/')
        };
        void Walk(JsonNode current)
        {
            if (current is JsonObject obj)
                foreach (var pair in obj.ToArray())
                { if (pair.Value is JsonValue v && v.TryGetValue<string>(out var text) && map.TryGetValue(text,out var replacement)) obj[pair.Key]=replacement;
                  else if (pair.Value is not null) Walk(pair.Value); }
            else if (current is JsonArray array) foreach (var item in array) if (item is not null) Walk(item);
        }
        Walk(node);
    }
    private static HashSet<string> CollectIds(JsonNode root)
    {
        var result=new HashSet<string>(StringComparer.Ordinal);
        void Walk(JsonNode? node)
        {
            if (node is JsonObject o) { if (o["id"] is JsonValue id && id.TryGetValue<string>(out var value) && value.Length>0) result.Add(value);foreach (var p in o) Walk(p.Value); }
            else if (node is JsonArray a) foreach (var item in a) Walk(item);
        }
        Walk(root);return result;
    }
    private static void ValidateOwnership(JsonObject document,ManagedSubtitleSet managed)
    {
        var owned=managed.Objects.Select(o=>o.Id).ToHashSet(StringComparer.Ordinal);var tracks=JsonFiles.Array(document,"tracks");
        var ownedTracks=managed.Objects.Where(o=>o.Kind==SubtitleObjectKind.Track).ToArray();
        if (ownedTracks.Length!=1) throw new InvalidDataException("El registro necesita exactamente una pista propia.");
        var track=tracks.SingleOrDefault(t=>t?["id"]?.GetValue<string>()==ownedTracks[0].Id) ?? throw new InvalidDataException("La pista propia desapareció; no se actualizará otro texto.");
        if (track["type"]?.GetValue<string>()!="text" || JsonFiles.Array(track,"segments").Any(s=>!managed.Objects.Any(o=>o.Kind==SubtitleObjectKind.Segment && o.Id==s?["id"]?.GetValue<string>())))
            throw new InvalidDataException("La pista propia contiene segmentos ajenos: no se reemplazará.");
        foreach (var t in tracks.Where(t=>t?["id"]?.GetValue<string>()!=ownedTracks[0].Id))
            if (References(t,owned)) throw new InvalidDataException("Un objeto propio está referenciado por una pista ajena; no se eliminará.");
        var materials=document["materials"]!.AsObject();
        foreach (var category in materials)
            if (category.Value is JsonArray array)
                foreach (var material in array.Where(n=>!owned.Contains(n?["id"]?.GetValue<string>() ?? "")))
                    if (References(material,owned)) throw new InvalidDataException("Un objeto propio está compartido con materiales ajenos.");
        var ids=CollectIds(document);
        if (managed.Objects.Any(o=>!ids.Contains(o.Id))) throw new InvalidDataException("Faltan objetos administrados; revisa el proyecto antes de actualizar.");
    }
    private static bool References(JsonNode? node,HashSet<string> ids)
    {
        if (node is JsonValue v && v.TryGetValue<string>(out var s)) return ids.Contains(s);
        if (node is JsonObject o) return o.Any(p=>References(p.Value,ids));
        if (node is JsonArray a) return a.Any(p=>References(p,ids));return false;
    }
    internal static void Validate(JsonObject document,IReadOnlyList<SubtitleCue> captions,ManagedSubtitleSet managed)
    {
        var materials=document["materials"]!.AsObject();var trackId=managed.Objects.Single(o=>o.Kind==SubtitleObjectKind.Track).Id;
        var track=JsonFiles.Array(document,"tracks").Single(t=>t?["id"]?.GetValue<string>()==trackId)!;
        var segments=JsonFiles.Array(track,"segments");if (segments.Count!=captions.Count) throw new InvalidDataException("Número de captions incoherente.");
        for (int i=0;i<captions.Count;i++)
        {
            var cue=captions[i];var segment=segments[i]!;var template=JsonFiles.Array(materials,"text_templates").Single(x=>x?["id"]?.GetValue<string>()==segment["material_id"]!.GetValue<string>())!;
            if (template["resource_id"]!.GetValue<string>()!="7535399757947161873") throw new InvalidDataException("Template distinto al golden v3.");
            var attachment=template["text_info_resources"]![0]!;var text=JsonFiles.Array(materials,"texts").Single(x=>x?["id"]?.GetValue<string>()==attachment["text_material_id"]!.GetValue<string>())!;
            var content=JsonNode.Parse(text["content"]!.GetValue<string>())!;
            if (content["text"]!.GetValue<string>()!=cue.Text || JsonFiles.Long(segment["target_timerange"],"start")!=cue.TimelineRange.StartUs ||
                JsonFiles.Long(segment["target_timerange"],"duration")!=cue.TimelineRange.DurationUs || JsonFiles.Long(attachment["attach_info"],"duration")!=cue.TimelineRange.DurationUs-1)
                throw new InvalidDataException("Texto o rango del caption incoherente.");
            var words=text["words"]!;
            if (!JsonFiles.Array(words,"text").Select(x=>x!.GetValue<string>()).SequenceEqual(cue.Words.Select(w=>w.Text)) ||
                !JsonFiles.Array(words,"start_time").Select(x=>x!.GetValue<long>()).SequenceEqual(cue.Words.Select(w=>w.StartTimeMs)) ||
                !JsonFiles.Array(words,"end_time").Select(x=>x!.GetValue<long>()).SequenceEqual(cue.Words.Select(w=>w.EndTimeMs))) throw new InvalidDataException("Words no coincide con el resultado.");
            foreach (var reference in segment["extra_material_refs"]!.AsArray())
                if (!materials.SelectMany(p=>p.Value is JsonArray a?a.AsEnumerable():Enumerable.Empty<JsonNode?>()).Any(n=>n?["id"]?.GetValue<string>()==reference!.GetValue<string>())) throw new InvalidDataException("Referencia no resuelta.");
        }
        // Comprueba identidades de objetos materiales/pistas/segmentos, excluyendo IDs de recurso/font internos repetidos.
        var objectIds=new List<string>();objectIds.AddRange(JsonFiles.Array(document,"tracks").Select(t=>JsonFiles.String(t,"id")));
        objectIds.AddRange(JsonFiles.Array(document,"tracks").SelectMany(t=>JsonFiles.Array(t,"segments")).Select(t=>JsonFiles.String(t,"id")));
        foreach (var category in materials) if (category.Value is JsonArray a) objectIds.AddRange(a.Where(n=>n is JsonObject && n["id"] is not null).Select(n=>JsonFiles.String(n,"id")));
        if (objectIds.Distinct(StringComparer.Ordinal).Count()!=objectIds.Count) throw new InvalidDataException("IDs de objetos duplicados.");
    }
}
