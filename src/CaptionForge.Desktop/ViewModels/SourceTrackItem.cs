using System.ComponentModel;
using System.IO;
using CaptionForge.Core.Models.Media;
using CaptionForge.Desktop.Localization;
using CaptionForge.Desktop.Mvvm;

namespace CaptionForge.Desktop.ViewModels;

/// <summary>Una pista de origen y sus clips; la selección se conserva al cambiar de pista.</summary>
public sealed class SourceTrackItem : ObservableObject
{
    public MediaTrack Model { get; }
    public string Id => Model.Id;
    public int Number { get; }
    public IReadOnlyList<AudioSegmentItem> Segments { get; }
    public string TypeLabel => L.F("source.trackTitle", L.T("tracks." + Model.Type), Number);
    // CapCut may leave the track name empty. Use one source filename as a
    // readable fallback, rather than listing every chunk in the selector.
    public string Name => !string.IsNullOrWhiteSpace(Model.Name) ? Model.Name
        : Model.Segments.OrderBy(s => s.TargetRange.StartUs).ThenBy(s => s.Id, StringComparer.Ordinal)
            .Select(s => Path.GetFileName(s.SourcePath.Replace('\\', '/')))
            .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? TypeLabel;
    public string TrackLabel => L.F("source.namedTrackTitle", L.T("tracks." + Model.Type), Number, Name);
    public string DisplayName => L.F("source.trackLabel", TrackLabel, MediaSummary, Segments.Count);
    public string MediaSummary => string.Join(", ", Segments.Select(s => Path.GetFileName(s.ResolvedPath))
        .Distinct(StringComparer.OrdinalIgnoreCase));

    public SourceTrackItem(MediaTrack model, int number)
    {
        Model = model;
        Number = number;
        Segments = Array.AsReadOnly(model.Segments.OrderBy(s => s.TargetRange.StartUs)
            .ThenBy(s => s.Id, StringComparer.Ordinal).Select(s => new AudioSegmentItem(s, TrackLabel)).ToArray());
        foreach (var segment in Segments) segment.PropertyChanged += SegmentChanged;
    }

    private void SegmentChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AudioSegmentItem.OverridePath)) return;
        Raise(nameof(MediaSummary));
        Raise(nameof(DisplayName));
    }

    public override string ToString() => Name;
}
