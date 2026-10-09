using System.Text.Json.Nodes;
using CaptionForge.Application.Models.CapCut;
using CaptionForge.Application.Services;
using CaptionForge.Core.Models.Subtitles;
using CaptionForge.Core.ValueObjects;
using CaptionForge.Infrastructure.Internal;
namespace CaptionForge.Infrastructure.CapCut;

public sealed record ExistingSubtitleTrack(string Id, int Number, string Name, IReadOnlyList<SubtitleCue> Captions, bool ApproximateWordTimings, string? UnsupportedReason = null)
{
    public bool IsSupported => UnsupportedReason is null && Captions.Count > 0;
    public string DisplayName => $"Pista {Number} · {(Name.Length > 0 ? Name : "Subtítulos")} · {Captions.Count} bloques" + (UnsupportedReason is null ? "" : " · No compatible");
}
public static class ExistingSubtitleCatalog
{
    public static async Task<IReadOnlyList<ExistingSubtitleTrack>> ReadAsync(TimelineSnapshot snapshot, CancellationToken ct = default)
    {
        await JsonFiles.VerifyAsync(snapshot.SourceFiles, ct).ConfigureAwait(false);
        var doc = await JsonFiles.ReadObjectAsync(snapshot.Timeline.DraftContentPath, ct).ConfigureAwait(false);
        var results = new List<ExistingSubtitleTrack>(); int number = 0;
        foreach (var track in JsonFiles.Array(doc, "tracks"))
        {
            ct.ThrowIfCancellationRequested(); number++;
            if (track?["type"]?.GetValue<string>() != "text" || JsonFiles.Array(track, "segments").Count == 0) continue;
            List<SubtitleCue> captions = []; bool approximate = false; string? reason = null;
            try
            {
                foreach (var segment in JsonFiles.Array(track, "segments"))
                {
                    string materialId = JsonFiles.String(segment, "material_id"), text; JsonNode? textMaterial = null;
                    var plain = JsonFiles.Array(doc["materials"], "texts").SingleOrDefault(m => m?["id"]?.GetValue<string>() == materialId);
                    if (plain is not null)
                    {
                        if (plain["type"]?.GetValue<string>() != "subtitle") throw new InvalidDataException("La pista contiene texto normal, no solo subtítulos.");
                        text = JsonFiles.String(JsonNode.Parse(JsonFiles.String(plain, "content")), "text"); textMaterial = plain;
                    }
                    else
                    {
                        var template = JsonFiles.Array(doc["materials"], "text_templates").SingleOrDefault(m => m?["id"]?.GetValue<string>() == materialId);
                        if (template?["type"]?.GetValue<string>() != "text_template_subtitle") throw new InvalidDataException("La pista contiene un bloque que no se reconoce como subtítulo.");
                        var layout = DraftTemplateLayout.Read(doc, template.AsObject()); text = layout.Text;
                        textMaterial = layout.Layers.FirstOrDefault(l => JsonFiles.String(JsonNode.Parse(JsonFiles.String(l.Text, "content")), "text").Trim() == text.Trim())?.Text;
                    }
                    var range = new TimeRangeUs(JsonFiles.Long(segment?["target_timerange"], "start"), JsonFiles.Long(segment?["target_timerange"], "duration"));
                    var cue = ExistingSubtitleCueBuilder.Build(JsonFiles.String(segment, "id"), text, range);
                    try
                    {
                        var words = textMaterial?["words"]; var labels = JsonFiles.Array(words, "text"); var starts = JsonFiles.Array(words, "start_time"); var ends = JsonFiles.Array(words, "end_time");
                        if (labels.Count == 0 || starts.Count != labels.Count || ends.Count != labels.Count) throw new InvalidDataException();
                        var timed = labels.Select((label, i) => new TimedWord(label!.GetValue<string>(), starts[i]!.GetValue<long>(), ends[i]!.GetValue<long>())).ToArray();
                        var exact = new SubtitleCue(cue.SourceSegmentId, range, timed);
                        if (exact.Text != cue.Text) throw new InvalidDataException();
                        cue = exact;
                    }
                    catch (Exception ex) when (ex is ArgumentException or InvalidDataException or InvalidOperationException or System.Text.Json.JsonException) { approximate = true; }
                    captions.Add(cue);
                }
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidDataException or InvalidOperationException or System.Text.Json.JsonException) { captions.Clear(); reason = ex.Message; }
            results.Add(new(JsonFiles.String(track, "id"), number, track?["name"]?.GetValue<string>() ?? "", captions.OrderBy(c => c.TimelineRange.StartUs).ToArray(), approximate, reason));
        }
        await JsonFiles.VerifyAsync(snapshot.SourceFiles, ct).ConfigureAwait(false); return results;
    }
}
