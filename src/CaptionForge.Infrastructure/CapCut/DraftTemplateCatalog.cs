using System.Text.Json.Nodes;
using CaptionForge.Application.Models.CapCut;
using CaptionForge.Application.Models.Writing;
using CaptionForge.Infrastructure.Internal;

namespace CaptionForge.Infrastructure.CapCut;

public sealed record DraftTemplateCandidate(string SegmentId, string TrackId, int TrackNumber, string Name,
    string ResourceId, string SampleText, long StartUs, int TextLayerCount, string? UnsupportedReason)
{
    public SubtitleOverwriteInfo? OverwriteInfo { get; init; }
    public bool RequiresOverwriteConfirmation => OverwriteInfo?.RequiresConfirmation==true;
    public bool IsSupported => UnsupportedReason is null;
    public string DisplayName => $"{Name} · pista {TrackNumber} · {SampleText}";
}

public static class DraftTemplateCatalog
{
    public static async Task ValidateResourcesAsync(TimelineSnapshot snapshot, string segmentId, CancellationToken cancellationToken = default)
    {
        await JsonFiles.VerifyAsync(snapshot.SourceFiles, cancellationToken).ConfigureAwait(false);
        var doc = await JsonFiles.ReadObjectAsync(snapshot.Timeline.DraftContentPath, cancellationToken).ConfigureAwait(false);
        DraftTemplateDocumentPatcher.ValidateAssets(DraftTemplateDocumentPatcher.AssetPaths(doc, segmentId));
        await JsonFiles.VerifyAsync(snapshot.SourceFiles, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<IReadOnlyList<DraftTemplateCandidate>> ReadAsync(TimelineSnapshot snapshot,
        IEnumerable<string>? excludedSegmentIds = null, CancellationToken cancellationToken = default)
    {
        await JsonFiles.VerifyAsync(snapshot.SourceFiles, cancellationToken).ConfigureAwait(false);
        var doc = await JsonFiles.ReadObjectAsync(snapshot.Timeline.DraftContentPath, cancellationToken).ConfigureAwait(false);
        var excluded = (excludedSegmentIds ?? []).ToHashSet(StringComparer.Ordinal);
        var results = new List<DraftTemplateCandidate>(); int number = 0;
        foreach (var track in JsonFiles.Array(doc, "tracks"))
        {
            number++;
            if (track?["type"]?.GetValue<string>() != "text") continue;
            foreach (var segment in JsonFiles.Array(track, "segments"))
            {
                string id = JsonFiles.String(segment, "id"); if (excluded.Contains(id)) continue;
                var template = JsonFiles.Array(doc["materials"], "text_templates")
                    .SingleOrDefault(m => m?["id"]?.GetValue<string>() == segment?["material_id"]?.GetValue<string>());
                if (template is null || template["type"]?.GetValue<string>() != "text_template_subtitle") continue;
                string sample = ""; string? reason = null; SubtitleOverwriteInfo? overwrite=null;
                try
                {
                    DraftTemplateDocumentPatcher.ValidateSeed(doc, id);
                    sample = DraftTemplateLayout.Read(doc, template.AsObject()).Text;
                    overwrite=DraftTemplateLayout.Inspect(doc,id,false);
                }
                catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or System.Text.Json.JsonException)
                { reason = ex.Message; }
                results.Add(new(id, JsonFiles.String(track, "id"), number, template["name"]?.GetValue<string>() ?? "Plantilla",
                    template["resource_id"]?.GetValue<string>() ?? "", sample,
                    JsonFiles.Long(segment?["target_timerange"], "start"), (template["text_info_resources"] as JsonArray)?.Count ?? 0, reason) { OverwriteInfo=overwrite });
            }
        }
        await JsonFiles.VerifyAsync(snapshot.SourceFiles, cancellationToken).ConfigureAwait(false);
        return results.GroupBy(c=>(c.TrackId,c.ResourceId)).Select(g=>g.FirstOrDefault(c=>c.IsSupported) ?? g.First()).ToList().AsReadOnly();
    }
}
