using System.Text.Json.Nodes;
using CaptionForge.Application.Enums;
using CaptionForge.Application.Exceptions;
using CaptionForge.Application.Models.Writing;
using CaptionForge.Core.Models.Subtitles;
using CaptionForge.Core.ValueObjects;
using CaptionForge.Infrastructure.Internal;

namespace CaptionForge.Infrastructure.CapCut;

internal static class DraftTemplateDocumentPatcher
{
    private static JsonArray Array(JsonNode? node,string key)=>node?[key] as JsonArray ?? new JsonArray();
    private static JsonObject Segment(JsonObject doc, string id) => Array(doc,"tracks")
        .Where(t=>t?["type"]?.GetValue<string>()=="text").SelectMany(t=>Array(t,"segments"))
        .SingleOrDefault(s=>s?["id"]?.GetValue<string>()==id)?.AsObject()
        ?? throw new InvalidDataException("El subtítulo elegido como molde ya no existe.");
    private static JsonObject Material(JsonObject doc,string category,string id) => Array(doc["materials"],category)
        .SingleOrDefault(n=>n?["id"]?.GetValue<string>()==id)?.AsObject()
        ?? throw new InvalidDataException($"Referencia de molde no resuelta: {category}/{id}.");

    internal static void ValidateSeed(JsonObject doc,string id)
    {
        var segment=Segment(doc,id); var template=Material(doc,"text_templates",JsonFiles.String(segment,"material_id"));
        if (template["type"]?.GetValue<string>()!="text_template_subtitle") throw new InvalidDataException("Selecciona una plantilla de subtítulos.");
        long duration=JsonFiles.Long(segment["target_timerange"],"duration");
        if (duration<=0) throw new InvalidDataException("El molde no tiene una duración válida.");
        if (Array(template,"non_text_info_resources").Count!=0 || template["is_dynamic_build"]?.GetValue<bool>()==true ||
            template["is_ai_emoji"]?.GetValue<bool>()==true || template["is_lyric_effect"]?.GetValue<bool>()==true)
            throw new InvalidDataException("Este molde necesita capas gráficas o generación dinámica aún no admitidas.");
        var layers=Array(template,"text_info_resources");
        if (layers.Count==0) throw new InvalidDataException("El molde no contiene capas de texto.");
        _ = DraftTemplateLayout.Read(doc,template);
        foreach(var a in layers)
        {
            if (a is not JsonObject || Array(a,"lyric_keyframes").Count!=0) throw new InvalidDataException("El molde contiene keyframes de letras aún no admitidos.");
            var text=Material(doc,"texts",JsonFiles.String(a,"text_material_id"));
            var content=JsonNode.Parse(JsonFiles.String(text,"content"))?.AsObject() ?? throw new InvalidDataException("Contenido de texto inválido.");
            string sample=JsonFiles.String(content,"text");
            var styles=Array(content,"styles");
            if (styles.Count!=1 || Array(styles[0],"range").Count!=2 ||
                styles[0]?["range"]?[0]?.GetValue<int>()!=0 || styles[0]?["range"]?[1]?.GetValue<int>()!=sample.Length)
                throw new InvalidDataException("Este molde tiene estilos por rangos parciales aún no admitidos.");
            long start=JsonFiles.Long(a?["attach_info"],"start_time"), d=JsonFiles.Long(a?["attach_info"],"duration");
            if(start<0 || d<=0 || start>duration-d) throw new InvalidDataException("La capa contiene tiempos internos inválidos.");
            if (Array(text["subtitle_keywords"],"range").Count!=0)
                throw new InvalidDataException("El molde tiene palabras clave fijadas al texto de ejemplo; elimina esa selección antes de usarlo.");
        }
        foreach(string field in new[]{"origin_word_info","current_word_info"})
            if (Array(template[field],"keyword_ranges").Count!=0)
                throw new InvalidDataException("El molde tiene rangos de palabras clave específicos del ejemplo.");
        foreach(var (_,dependency) in Dependencies(doc,segment,template))
            foreach(var animation in Array(dependency,"animations"))
                if(JsonFiles.Long(animation,"start")<0 || JsonFiles.Long(animation,"duration")<=0 || JsonFiles.Long(animation,"start")>duration-JsonFiles.Long(animation,"duration"))
                    throw new InvalidDataException("La animación usa fases internas aún no admitidas.");
    }

    private static List<(string Category,JsonObject Node)> Dependencies(JsonObject doc,JsonObject segment,JsonObject template)
    {
        var ids=Array(segment,"extra_material_refs").Concat(Array(template,"text_info_resources")
            .SelectMany(a=>Array(a,"extra_material_refs"))).Select(n=>n!.GetValue<string>()).Distinct(StringComparer.Ordinal);
        var result=new List<(string,JsonObject)>();
        foreach(string id in ids)
        {
            var found=doc["materials"]!.AsObject().Where(p=>p.Value is JsonArray)
                .SelectMany(p=>((JsonArray)p.Value!).Where(n=>n?["id"]?.GetValue<string>()==id).Select(n=>(p.Key,n!.AsObject()))).ToArray();
            if(found.Length!=1 || found[0].Key is not ("material_animations" or "effects"))
                throw new InvalidDataException("El molde contiene dependencias de un tipo aún no admitido.");
            result.Add((found[0].Key,found[0].Item2));
        }
        return result;
    }

    internal static string[] AssetPaths(JsonObject doc,string segmentId)
    {
        ValidateSeed(doc,segmentId);var segment=Segment(doc,segmentId);var template=Material(doc,"text_templates",JsonFiles.String(segment,"material_id"));
        var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Walk(JsonNode? node)
        {
            if(node is JsonObject o) foreach(var p in o)
            {
                if(p.Key=="path" && p.Value is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s)) paths.Add(s);
                else if(p.Key=="content" && p.Value is JsonValue c && c.TryGetValue<string>(out var value) && value.StartsWith('{')) Walk(JsonNode.Parse(value));
                else Walk(p.Value);
            }
            else if(node is JsonArray a) foreach(var n in a) Walk(n);
        }
        Walk(template);foreach(var a in Array(template,"text_info_resources")) Walk(Material(doc,"texts",JsonFiles.String(a,"text_material_id")));
        foreach(var (_,n) in Dependencies(doc,segment,template)) Walk(n);
        return paths.ToArray();
    }
    internal static void ValidateAssets(IEnumerable<string> paths)
    {
        foreach(string path in paths)
        {
            PathSafety.RejectLinks(path);
            if(!File.Exists(path) && !Directory.Exists(path)) throw new CaptionForgeOperationException(OperationErrorCode.ResourceUnavailable,
                $"Falta un recurso de la plantilla. Abre CapCut, descarga/aplica la plantilla y vuelve a cargar la timeline: {path}");
        }
    }

    internal static (JsonObject Document,ManagedSubtitleSet Managed,IReadOnlyList<string> Warnings) Patch(SubtitleWriteRequest request,JsonObject original,string seedId,JsonObject? prototype=null)
    {
        var sourceDocument=prototype ?? original;
        ValidateSeed(sourceDocument,seedId);var seed=Segment(sourceDocument,seedId);var seedTemplate=Material(sourceDocument,"text_templates",JsonFiles.String(seed,"material_id"));
        var layout=DraftTemplateLayout.Read(sourceDocument,seedTemplate);
        var dependencies=Dependencies(sourceDocument,seed,seedTemplate);
        var warnings=new List<string>();
        var old=request.PreviouslyManaged;
        if(old is not null && !Array(original,"tracks").Any(t=>t?["id"]?.GetValue<string>()==old.Objects.Single(o=>o.Kind==SubtitleObjectKind.Track).Id))
        {
            warnings.Add("La pista utilizada anteriormente fue eliminada. Se usará el ejemplo seleccionado y se conservarán los materiales anteriores que todavía existan.");
            old=null;
        }
        var result=original.DeepClone().AsObject();var tracks=Array(result,"tracks");var materials=result["materials"]!.AsObject();
        var seedTrack=Array(original,"tracks").Single(t=>Array(t,"segments").Any(s=>s?["id"]?.GetValue<string>()==seedId))!;
        string trackId=JsonFiles.String(seedTrack,"id");
        if(old is not null && old.Objects.Single(o=>o.Kind==SubtitleObjectKind.Track).Id!=trackId) old=null;
        if(old is not null) ValidateManaged(original,old);
        bool reusePrevious=old?.Objects.Any(o=>o.Kind==SubtitleObjectKind.Segment && o.Id==seedId)==true;
        var seedMaterialIds=new HashSet<string>(StringComparer.Ordinal) {JsonFiles.String(seedTemplate,"id")};
        foreach(var a in Array(seedTemplate,"text_info_resources")) seedMaterialIds.Add(JsonFiles.String(a,"text_material_id"));
        foreach(var (_,dependency) in dependencies) seedMaterialIds.Add(JsonFiles.String(dependency,"id"));
        // La pista seleccionada es la unidad de sustitución. La autoría de otras pistas no participa en esta limpieza.
        var selectedSegments=Array(seedTrack,"segments").Select(s=>JsonFiles.String(s,"id")).ToHashSet(StringComparer.Ordinal);
        foreach(var selected in Array(seedTrack,"segments"))
        {
            string materialId=JsonFiles.String(selected,"material_id");
            var template=Array(original["materials"],"text_templates").SingleOrDefault(t=>t?["id"]?.GetValue<string>()==materialId);
            seedMaterialIds.Add(materialId);
            foreach(var id in Array(selected,"extra_material_refs")) seedMaterialIds.Add(id!.GetValue<string>());
            if(template is not null) foreach(var layer in Array(template,"text_info_resources"))
            {
                seedMaterialIds.Add(JsonFiles.String(layer,"text_material_id"));
                foreach(var id in Array(layer,"extra_material_refs")) seedMaterialIds.Add(id!.GetValue<string>());
            }
        }
        RejectSharedReferences(original,seedMaterialIds,selectedSegments);
        var reserved=GoldenV3DocumentPatcher.CollectIds(original);var own=new List<ManagedSubtitleObject>();
        bool first=true;
        string Id(SubtitleObjectKind kind,string key,string seedIdentity)
        {
            string? previous=reusePrevious?old?.Objects.SingleOrDefault(o=>o.Kind==kind && o.LogicalKey==key)?.Id:null;
            string id=first?seedIdentity:previous ?? "";
            if(string.IsNullOrEmpty(id) || own.Any(o=>o.Id==id))
            {
                do { id=Guid.NewGuid().ToString().ToUpperInvariant(); } while(!reserved.Add(id));
            }
            own.Add(new(kind,id,key));return id;
        }
        own.Add(new(SubtitleObjectKind.Track,trackId,"captions"));
        var removedSegments=old?.Objects.Where(o=>o.Kind==SubtitleObjectKind.Segment).Select(o=>o.Id).ToHashSet(StringComparer.Ordinal) ?? new HashSet<string>(StringComparer.Ordinal);
        removedSegments.UnionWith(selectedSegments);
        int insertion=Array(seedTrack,"segments").TakeWhile(s=>JsonFiles.String(s,"id")!=seedId).Count(s=>!removedSegments.Contains(JsonFiles.String(s,"id")));
        foreach(var track in tracks)
            foreach(var s in Array(track,"segments").Where(s=>removedSegments.Contains(JsonFiles.String(s,"id"))).ToArray()) Array(track,"segments").Remove(s);
        var removedMaterials=old?.Objects.Where(o=>o.Kind is not (SubtitleObjectKind.Track or SubtitleObjectKind.Segment or SubtitleObjectKind.Attachment)).Select(o=>o.Id).ToHashSet(StringComparer.Ordinal) ?? new HashSet<string>(StringComparer.Ordinal);
        removedMaterials.UnionWith(seedMaterialIds);
        foreach(var p in materials) if(p.Value is JsonArray a)
            foreach(var n in a.Where(n=>removedMaterials.Contains(n?["id"]?.GetValue<string>() ?? "")).ToArray()) a.Remove(n);
        var targetTrack=tracks.Single(t=>JsonFiles.String(t,"id")==trackId)!;
        var segments=Array(targetTrack,"segments");
        var usedRender=tracks.SelectMany(t=>Array(t,"segments")).Select(s=>s?["render_index"]?.GetValue<int>() ?? 0).ToHashSet();
        int render=seed["render_index"]?.GetValue<int>() ?? 14000;
        long sourceDuration=JsonFiles.Long(seed["target_timerange"],"duration");var ordinals=new Dictionary<string,int>();
        foreach(var cue in request.Captions)
        {
            int ordinal=ordinals.GetValueOrDefault(cue.SourceSegmentId);ordinals[cue.SourceSegmentId]=ordinal+1;
            string key=cue.SourceSegmentId+":"+ordinal;
            var segment=seed.DeepClone().AsObject();var template=seedTemplate.DeepClone().AsObject();
            segment["id"]=Id(SubtitleObjectKind.Segment,key,JsonFiles.String(seed,"id"));template["id"]=Id(SubtitleObjectKind.Template,key,JsonFiles.String(seedTemplate,"id"));segment["material_id"]=template["id"]!.DeepClone();
            segment["target_timerange"]=new JsonObject { ["start"]=cue.TimelineRange.StartUs,["duration"]=cue.TimelineRange.DurationUs };
            if(!first) while(usedRender.Contains(render)) render=checked(render+1);
            segment["render_index"]=render;usedRender.Add(render);render=checked(render+1);
            var projections=layout.Project(cue);
            var layers=Array(template,"text_info_resources");layers.Clear();
            var layerRefs=layout.Layers.SelectMany(l=>Array(l.Attachment,"extra_material_refs")).Select(n=>n!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
            var activeRefs=projections.SelectMany(l=>Array(l.Source.Attachment,"extra_material_refs")).Select(n=>n!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
            var remap=new Dictionary<string,string>(StringComparer.Ordinal);int depIndex=0;
            foreach(var (category,source) in dependencies)
            {
                int index=depIndex++;string originalId=JsonFiles.String(source,"id");
                if(layerRefs.Contains(originalId) && !activeRefs.Contains(originalId)) continue;
                var node=source.DeepClone().AsObject();string id=Id(category=="effects"?SubtitleObjectKind.Effect:SubtitleObjectKind.Animation,key+":dependency:"+index,originalId);
                remap.Add(originalId,id);node["id"]=id;
                var windows=projections.Where(l=>Array(l.Source.Attachment,"extra_material_refs").Any(n=>n!.GetValue<string>()==originalId))
                    .Select(l=>Window(l,cue,sourceDuration)).Distinct().ToArray();
                foreach(var animation in Array(node,"animations"))
                {
                    var window=windows.Length==1?windows[0]:(Start:0L,Duration:cue.TimelineRange.DurationUs);
                    long from=windows.Length==1?JsonFiles.Long(projections.First(l=>Array(l.Source.Attachment,"extra_material_refs").Any(n=>n!.GetValue<string>()==originalId)).Source.Attachment["attach_info"],"duration"):sourceDuration;
                    animation!["start"]=ScaleStart(JsonFiles.Long(animation,"start"),from,window.Duration);
                    animation["duration"]=Math.Min(window.Duration-JsonFiles.Long(animation,"start"),Resize(JsonFiles.Long(animation,"duration"),from,window.Duration));
                }
                Add(category,node);
            }
            foreach(var projection in projections)
            {
                int layerIndex=layout.Layers.ToList().IndexOf(projection.Source);
                string layerKey=key+":layer:"+layerIndex;var source=projection.Source.Text;var text=source.DeepClone().AsObject();
                var a=projection.Source.Attachment.DeepClone().AsObject();layers.Add(a);
                string id=Id(SubtitleObjectKind.Text,layerKey,JsonFiles.String(source,"id"));text["id"]=id;
                a["id"]=Id(SubtitleObjectKind.Attachment,layerKey,JsonFiles.String(a,"id"));a["text_material_id"]=id;
                var window=Window(projection,cue,sourceDuration);
                a["attach_info"]!["start_time"]=window.Start;a["attach_info"]!["duration"]=window.Duration;
                if(Array(a,"word_index").Count!=0) a["word_index"]=new JsonArray(projection.Start,projection.End);
                var content=JsonNode.Parse(JsonFiles.String(text,"content"))!;
                string value=string.Concat(cue.Words.Skip(projection.Start).Take(projection.End-projection.Start).Select(w=>w.Text));
                content["text"]=value;content["styles"]![0]!["range"]=new JsonArray(0,value.Length);text["content"]=content.ToJsonString();
                text["words"]=Words(cue,projection.Start,projection.End);if(Array(text["current_words"],"text").Count!=0) text["current_words"]=Words(cue,projection.Start,projection.End);
                Add("texts",text);
            }
            foreach(string field in new[]{"origin_word_info","current_word_info"})
                if(template[field] is JsonObject info && (!string.IsNullOrEmpty(info["text"]?.GetValue<string>()) || Array(info,"words").Count!=0))
                {
                    info["text"]=cue.Text;info["start_time"]=TimeRangeUs.RoundToMilliseconds(cue.TimelineRange.StartUs);info["end_time"]=TimeRangeUs.RoundToMilliseconds(cue.TimelineRange.EndUs);
                    info["words"]=new JsonArray(cue.Words.Select(w=>(JsonNode)new JsonObject { ["text"]=w.Text,["start_time"]=w.StartTimeMs,["end_time"]=w.EndTimeMs }).ToArray());
                }
            RemapRefs(segment,remap);RemapRefs(template,remap);segments.Insert(insertion++,segment);Add("text_templates",template);first=false;
        }
        var managed=new ManagedSubtitleSet(request.Run.ProjectId,request.Run.TimelineId,own);
        Validate(result,request.Captions,managed,JsonFiles.String(seedTemplate,"resource_id"));
        warnings.Add("El ejemplo seleccionado se sustituyó por los subtítulos, conservando su pista, los IDs del primer bloque y la configuración de la plantilla. Las demás pistas se conservaron.");
        return(result,managed,warnings.AsReadOnly());
        void Add(string category,JsonObject node)
        {
            if(materials[category] is not JsonArray) materials[category]=new JsonArray();
            // Campos desconocidos añadidos por CapCut a un objeto propio sobreviven a la actualización.
            var previous=Array(original["materials"],category).SingleOrDefault(n=>n?["id"]?.GetValue<string>()==node["id"]?.GetValue<string>());
            if(previous is not null) GoldenV3DocumentPatcher.MergeUnknown(previous,node);
            materials[category]!.AsArray().Add(node);
        }
    }
    private static void ValidateManaged(JsonObject document,ManagedSubtitleSet managed)
    {
        var ownedTracks=managed.Objects.Where(o=>o.Kind==SubtitleObjectKind.Track).ToArray();
        if(ownedTracks.Length!=1) throw new InvalidDataException("El registro necesita exactamente una pista de destino.");
        var track=Array(document,"tracks").Single(t=>JsonFiles.String(t,"id")==ownedTracks[0].Id)!;
        var segmentIds=managed.Objects.Where(o=>o.Kind==SubtitleObjectKind.Segment).Select(o=>o.Id).ToHashSet(StringComparer.Ordinal);
        if(track["type"]?.GetValue<string>()!="text" || segmentIds.Any(id=>!Array(track,"segments").Any(s=>JsonFiles.String(s,"id")==id)))
            throw new InvalidDataException("Los bloques administrados desaparecieron o cambiaron de pista; revisa el proyecto.");
        var objectIds=managed.Objects.Where(o=>o.Kind is not (SubtitleObjectKind.Track or SubtitleObjectKind.Segment or SubtitleObjectKind.Attachment)).Select(o=>o.Id).ToHashSet(StringComparer.Ordinal);
        RejectSharedReferences(document,objectIds,segmentIds);
        var allIds=GoldenV3DocumentPatcher.CollectIds(document);
        if(managed.Objects.Any(o=>!allIds.Contains(o.Id))) throw new InvalidDataException("Faltan objetos administrados; revisa el proyecto antes de actualizar.");
    }
    private static void RejectSharedReferences(JsonObject document,HashSet<string> materialIds,HashSet<string> allowedSegments)
    {
        bool References(JsonNode? node)
        {
            if(node is JsonValue v && v.TryGetValue<string>(out var value)) return materialIds.Contains(value);
            if(node is JsonObject o) return o.Any(p=>References(p.Value));
            if(node is JsonArray a) return a.Any(References);
            return false;
        }
        foreach(var segment in Array(document,"tracks").SelectMany(t=>Array(t,"segments")))
            if(!allowedSegments.Contains(JsonFiles.String(segment,"id")) && References(segment))
                throw new InvalidDataException("La plantilla o un material administrado está compartido por otro segmento; no se modificará ese contenido.");
        foreach(var category in document["materials"]!.AsObject()) if(category.Value is JsonArray array)
            foreach(var material in array)
                if(!materialIds.Contains(material?["id"]?.GetValue<string>() ?? "") && References(material))
                    throw new InvalidDataException("La plantilla o un material administrado está compartido con materiales ajenos.");
    }
    private static long Resize(long value,long from,long to) => value==from?to:value==from-1?Math.Max(1,to-1):Math.Clamp((long)Math.Round((decimal)value*to/from),1,to);
    private static long ScaleStart(long value,long from,long to)=>Math.Clamp((long)Math.Round((decimal)value*to/from),0,Math.Max(0,to-1));
    private static (long Start,long Duration) Window(DraftTemplateLayout.Projection layer,SubtitleCue cue,long sourceDuration)
    {
        if(!layer.IsPartial)
        {
            long begin=ScaleStart(JsonFiles.Long(layer.Source.Attachment["attach_info"],"start_time"),sourceDuration,cue.TimelineRange.DurationUs);
            return (begin,Math.Min(cue.TimelineRange.DurationUs-begin,Resize(JsonFiles.Long(layer.Source.Attachment["attach_info"],"duration"),sourceDuration,cue.TimelineRange.DurationUs)));
        }
        long start=cue.Words[layer.Start].StartTimeMs*1000;
        long end=cue.Words[layer.End-1].EndTimeMs*1000;
        if(layer.End==cue.Words.Count) end=cue.TimelineRange.DurationUs;
        end=Math.Min(end,cue.TimelineRange.DurationUs);
        return (start,Math.Max(1,end-start));
    }
    private static JsonObject Words(SubtitleCue cue,int start=0,int? end=null)
    {
        var words=cue.Words.Skip(start).Take((end ?? cue.Words.Count)-start).ToArray();
        return new() { ["start_time"]=new JsonArray(words.Select(w=>JsonValue.Create(w.StartTimeMs)).ToArray()),
            ["end_time"]=new JsonArray(words.Select(w=>JsonValue.Create(w.EndTimeMs)).ToArray()),["text"]=new JsonArray(words.Select(w=>JsonValue.Create(w.Text)).ToArray()) };
    }
    private static void RemapRefs(JsonNode node,Dictionary<string,string> map)
    {
        if(node is JsonObject o) foreach(var p in o.ToArray())
        { if(p.Key=="extra_material_refs" && p.Value is JsonArray a) { var refs=a.Select(n=>n!.GetValue<string>()).Where(map.ContainsKey).Select(id=>map[id]).ToArray();a.Clear();foreach(string id in refs) a.Add(id); } else if(p.Value is not null) RemapRefs(p.Value,map); }
        else if(node is JsonArray a) foreach(var n in a) if(n is not null) RemapRefs(n,map);
    }
    private static void Validate(JsonObject doc,IReadOnlyList<SubtitleCue> cues,ManagedSubtitleSet managed,string resourceId)
    {
        var materialIds=doc["materials"]!.AsObject().Where(p=>p.Value is JsonArray).SelectMany(p=>(JsonArray)p.Value!).Where(n=>n?["id"] is not null).Select(n=>JsonFiles.String(n,"id")).ToArray();
        var objectIds=materialIds.Concat(Array(doc,"tracks").Select(t=>JsonFiles.String(t,"id")))
            .Concat(Array(doc,"tracks").SelectMany(t=>Array(t,"segments")).Select(s=>JsonFiles.String(s,"id"))).ToArray();
        if(objectIds.Distinct(StringComparer.Ordinal).Count()!=objectIds.Length) throw new InvalidDataException("IDs de objetos duplicados.");
        var track=Array(doc,"tracks").Single(t=>t?["id"]?.GetValue<string>()==managed.Objects.Single(o=>o.Kind==SubtitleObjectKind.Track).Id)!;
        var segmentIds=managed.Objects.Where(o=>o.Kind==SubtitleObjectKind.Segment).Select(o=>o.Id).ToHashSet(StringComparer.Ordinal);
        var segments=Array(track,"segments").Where(s=>segmentIds.Contains(JsonFiles.String(s,"id"))).ToArray();if(segments.Length!=cues.Count) throw new InvalidDataException("Número de subtítulos incoherente.");
        for(int i=0;i<cues.Count;i++)
        {
            var s=segments[i]!;var cue=cues[i];var template=Material(doc,"text_templates",JsonFiles.String(s,"material_id"));
            if(JsonFiles.String(template,"resource_id")!=resourceId || JsonFiles.Long(s["target_timerange"],"start")!=cue.TimelineRange.StartUs ||
                JsonFiles.Long(s["target_timerange"],"duration")!=cue.TimelineRange.DurationUs) throw new InvalidDataException("El molde o los tiempos cambiaron.");
            var layout=DraftTemplateLayout.Read(doc,template);
            if(layout.Text!=cue.Text) throw new InvalidDataException("La plantilla no conserva el texto completo.");
            foreach(var layer in layout.Layers)
            {
                if(!JsonNode.DeepEquals(layer.Text["words"],Words(cue,layer.Start,layer.End)))
                    throw new InvalidDataException("Una capa no conserva los tiempos de Whisper relativos al bloque.");
                long start=JsonFiles.Long(layer.Attachment["attach_info"],"start_time"),duration=JsonFiles.Long(layer.Attachment["attach_info"],"duration");
                if(start<0 || duration<=0 || start>cue.TimelineRange.DurationUs-duration)
                    throw new InvalidDataException("Los tiempos de una capa exceden el bloque.");
            }
            foreach(var id in Array(s,"extra_material_refs").Concat(Array(template,"text_info_resources").SelectMany(a=>Array(a,"extra_material_refs"))))
                if(!materialIds.Contains(id!.GetValue<string>(),StringComparer.Ordinal)) throw new InvalidDataException("Referencia generada no resuelta.");
        }
    }
}
