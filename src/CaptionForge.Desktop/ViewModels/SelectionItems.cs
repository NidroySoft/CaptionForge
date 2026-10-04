using System.IO;
using CaptionForge.Core.Models.CapCut;
using CaptionForge.Core.Models.Media;
using CaptionForge.Core.Models.Subtitles;
using CaptionForge.Desktop.Mvvm;
namespace CaptionForge.Desktop.ViewModels;
public sealed record ProjectItem(CapCutProject Model)
{
 public string Name=>Model.Name;public string? CoverPath=>Model.CoverPath;public string Id=>Model.Id;
 public string Detail=>Model.LastModifiedAt?.ToLocalTime().ToString("dd MMM yyyy · HH:mm") ?? "Proyecto de CapCut";
}
public sealed record TimelineItem(CapCutTimeline Model)
{
 public string Name=>Model.Name;public string? CoverPath=>Model.CoverPath;public string Id=>Model.Id;
 public string Detail=>$"{TimeSpan.FromTicks(Model.DurationUs*10):mm\\:ss} · {Model.FrameRate.FramesPerSecond:0.##} fps";
 public bool IsPinned=>Model.IsMain;
}
public sealed class AudioSegmentItem : ObservableObject
{
 private bool _included;private string? _override;
 public MediaSegment Model {get;}public string TrackLabel {get;}public string Id=>Model.Id;
 public bool CanInclude=>Model.Speed==1 && !Model.HasVariableSpeed && !Model.IsReversed && !Model.IsMuted;
 public bool Included {get=>_included;set=>Set(ref _included,value && CanInclude);}
 public string? OverridePath {get=>_override;set{if(Set(ref _override,value)){Raise(nameof(ResolvedPath));Raise(nameof(Name));Raise(nameof(Diagnostic));}}}
 public string ResolvedPath=>OverridePath ?? Model.SourcePath;
 public string Name=>Path.GetFileName(ResolvedPath);
 public string Range=>$"Origen {Format(Model.SourceRange.StartUs)}–{Format(Model.SourceRange.EndUs)} · timeline {Format(Model.TargetRange.StartUs)}–{Format(Model.TargetRange.EndUs)}";
 public string Diagnostic=>!CanInclude?"Fragmento silenciado, invertido o con velocidad modificada":!File.Exists(ResolvedPath)?"Archivo ausente: usa Localizar":OverridePath is not null?"Ruta alternativa para esta ejecución":"";
 public AudioSegmentItem(MediaSegment model,string trackLabel){Model=model;TrackLabel=trackLabel;_included=CanInclude;}
 public static string Format(long us)=>TimeSpan.FromTicks(us*10).ToString(@"mm\:ss\.fff");
}
public sealed record CaptionItem(SubtitleCue Model)
{
 public string Time=>$"{AudioSegmentItem.Format(Model.TimelineRange.StartUs)} → {AudioSegmentItem.Format(Model.TimelineRange.EndUs)}";
 public string Text=>Model.Text;public int Words=>Model.Words.Count(w=>!string.IsNullOrWhiteSpace(w.Text));
}
public sealed record BackupItem(string JournalPath,string Label,string Phase);
public sealed record LanguageItem(string Code,string Name);
