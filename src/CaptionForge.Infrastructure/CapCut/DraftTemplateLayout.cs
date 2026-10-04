using System.Text.Json.Nodes;
using CaptionForge.Application.Models.Writing;
using CaptionForge.Core.Models.Subtitles;
using CaptionForge.Infrastructure.Internal;

namespace CaptionForge.Infrastructure.CapCut;

/// <summary>Los índices son globales al bloque; las palabras de cada capa conservan sus tiempos relativos al bloque.</summary>
internal sealed class DraftTemplateLayout
{
    internal sealed record Layer(JsonObject Attachment, JsonObject Text, int Start, int End);
    internal sealed record Projection(Layer Source, int Start, int End, bool IsPartial);
    internal string Text { get; }
    internal IReadOnlyList<string> Words { get; }
    internal IReadOnlyList<Layer> Layers { get; }
    private DraftTemplateLayout(string text, string[] words, List<Layer> layers)
    { Text=text; Words=words; Layers=layers; }

    internal static DraftTemplateLayout Read(JsonObject doc, JsonObject template)
    {
        var layers=JsonFiles.Array(template,"text_info_resources").Select(a=>
        {
            var text=JsonFiles.Array(doc["materials"],"texts").Single(t=>t?["id"]?.GetValue<string>()==a?["text_material_id"]?.GetValue<string>())!.AsObject();
            return (Attachment:a!.AsObject(),Text:text,Content:JsonNode.Parse(JsonFiles.String(text,"content"))!.AsObject());
        }).ToArray();
        if(layers.Length==0) throw new InvalidDataException("El molde no contiene capas de texto.");
        // Preferir una capa completa: los metadatos almacenados por CapCut pueden estar obsoletos tras editar o mover el ejemplo.
        var reference=layers.OrderByDescending(l=>JsonFiles.String(l.Content,"text").Length).First();
        string full=JsonFiles.String(reference.Content,"text");
        var words=(reference.Text["words"]?["text"] as JsonArray)?.Select(n=>n!.GetValue<string>()).ToArray() ?? [];
        // Si todas las capas contienen el texto completo (o hay una auxiliar completa), su texto actual manda.
        // Cuando sólo existen páginas, reconstruir la referencia desde sus índices y textos actuales.
        bool sameText=layers.All(l=>JsonFiles.String(l.Content,"text")==full);
        bool hasFullAuxiliary=layers.Any(l=>l.Attachment["clip_type"]?.GetValue<string>()=="can_not_render" && JsonFiles.String(l.Content,"text")==full);
        int maxEnd=layers.Select(l=>l.Attachment["word_index"] is JsonArray { Count:2 } indices?indices[1]!.GetValue<int>():0).Max();
        var referenceIndices=reference.Attachment["word_index"] as JsonArray;
        bool hasFullReference=referenceIndices?.Count==2 && referenceIndices[0]!.GetValue<int>()==0 && referenceIndices[1]!.GetValue<int>()==maxEnd && words.Length==maxEnd;
        if(!sameText && !hasFullAuxiliary && !hasFullReference)
        {
            var pages=layers.OrderBy(l=>l.Attachment["word_index"] is JsonArray { Count:2 } indices?indices[0]!.GetValue<int>():0).ToArray();
            var assembled=new List<string>();
            foreach(var page in pages)
            {
                var indices=page.Attachment["word_index"] as JsonArray;
                var local=(page.Text["words"]?["text"] as JsonArray)?.Select(n=>n!.GetValue<string>()).ToArray() ?? [];
                if(indices?.Count!=2 || indices[0]!.GetValue<int>()!=assembled.Count || indices[1]!.GetValue<int>()!=assembled.Count+local.Length)
                    throw new InvalidDataException("El reparto de capas no permite reconstruir el texto completo.");
                assembled.AddRange(local);
            }
            words=assembled.ToArray();full=string.Concat(words);
        }
        if(words.Length>0 && (words.Length%2==0 || words.Where((w,i)=>string.IsNullOrWhiteSpace(w)!=(i%2==1)).Any()))
            throw new InvalidDataException("El molde no contiene una secuencia válida de palabras y espacios.");
        if(words.Length>0 && string.Concat(words)!=full) throw new InvalidDataException("El texto de referencia y sus palabras no coinciden.");
        var result=new List<Layer>();
        foreach(var layer in layers)
        {
            string text=JsonFiles.String(layer.Content,"text");
            var indices=layer.Attachment["word_index"] as JsonArray;
            int start=0,end=words.Length;
            if(indices?.Count>0)
            {
                if(indices.Count!=2) throw new InvalidDataException("El rango de palabras de una capa es inválido.");
                start=indices[0]!.GetValue<int>();end=indices[1]!.GetValue<int>();
                if(start<0 || end<=start || end>words.Length) throw new InvalidDataException("El rango de palabras de una capa excede el texto completo.");
            }
            if(text!=(words.Length==0?full:string.Concat(words.Skip(start).Take(end-start))))
                throw new InvalidDataException("El texto de una capa no corresponde a su rango de palabras dentro del bloque.");
            var local=(layer.Text["words"]?["text"] as JsonArray)?.Select(n=>n!.GetValue<string>()).ToArray() ?? [];
            if(local.Length>0 && !local.SequenceEqual(words.Skip(start).Take(end-start)))
                throw new InvalidDataException("Las palabras de una capa no corresponden a su rango global.");
            result.Add(new(layer.Attachment,layer.Text,start,end));
        }
        // Una referencia auxiliar no debe ocultar huecos en las capas visibles.
        if(words.Length>0)
        {
            var visible=result.Where(l=>l.Attachment["clip_type"]?.GetValue<string>()!="can_not_render").ToArray();
            if(Enumerable.Range(0,words.Length).Any(i=>!visible.Any(l=>l.Start<=i && l.End>i)))
                throw new InvalidDataException("Las capas visibles no cubren el texto completo.");
        }
        return new(full,words,result);
    }

    internal IReadOnlyList<Projection> Project(SubtitleCue cue)
    {
        if(Words.Count==0) return Layers.Select(l=>new Projection(l,0,cue.Words.Count,false)).ToArray();
        int oldCount=Words.Count(w=>!string.IsNullOrWhiteSpace(w)), newCount=(cue.Words.Count+1)/2;
        int Boundary(int index)
        {
            if(index==0) return 0;
            if(index==Words.Count) return cue.Words.Count;
            if(oldCount==0) throw new InvalidDataException("Falta el reparto de palabras del molde.");
            int before=Words.Take(index).Count(w=>!string.IsNullOrWhiteSpace(w));
            int count=(int)((long)before*newCount/oldCount);
            if(before>0) count=Math.Max(1,count);
            count=Math.Min(newCount,count);
            return count==0?0:Math.Min(cue.Words.Count,2*count-1);
        }
        return Layers.Select(l=>new Projection(l,Boundary(l.Start),Boundary(l.End),l.Start!=0 || l.End!=Words.Count))
            .Where(l=>l.End>l.Start).ToArray();
    }

    internal static SubtitleOverwriteInfo Inspect(JsonObject doc,string segmentId,bool managed)
    {
        var track=JsonFiles.Array(doc,"tracks").Single(t=>JsonFiles.Array(t,"segments").Any(s=>s?["id"]?.GetValue<string>()==segmentId))!;
        var segments=JsonFiles.Array(track,"segments");
        var segment=segments.Single(s=>s?["id"]?.GetValue<string>()==segmentId)!;
        var template=JsonFiles.Array(doc["materials"],"text_templates").Single(t=>t?["id"]?.GetValue<string>()==segment["material_id"]?.GetValue<string>())!.AsObject();
        bool example=segments.Count==1 && Read(doc,template).Text=="The quick brown fox jumps over the lazy dog" && !managed;
        bool confirm=!example;
        return new(JsonFiles.String(track,"id"),segments.Count,example,managed,confirm,
            confirm?$"Se sobrescribirán {segments.Count} bloque(s) de la pista seleccionada. Las demás pistas se conservarán.":"La pista contiene únicamente el texto de ejemplo reconocido.");
    }
}
