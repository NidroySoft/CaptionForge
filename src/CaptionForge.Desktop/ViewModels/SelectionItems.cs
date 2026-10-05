using CaptionForge.Desktop.Localization;
using System.IO;
using CaptionForge.Core.Models.CapCut;
using CaptionForge.Core.Models.Media;
using CaptionForge.Core.Models.Subtitles;
using CaptionForge.Desktop.Mvvm;
namespace CaptionForge.Desktop.ViewModels;
public sealed class ProjectItem(CapCutProject model) : ObservableObject
{
 public CapCutProject Model {get;}=model;
 public string Name=>Model.Name;public string? CoverPath=>Model.CoverPath;public string Id=>Model.Id;
 public string Detail=>Model.LastModifiedAt?.ToLocalTime().ToString("dd MMM yyyy · HH:mm") ?? L.T("ui.proyectoDeCapcut");
}
public sealed class TimelineItem(CapCutTimeline model) : ObservableObject
{
 public CapCutTimeline Model {get;}=model;
 public string Name=>Model.Name;public string? CoverPath=>Model.CoverPath;public string Id=>Model.Id;
 public string Detail=>L.F("ui.0MmSs10Fps", TimeSpan.FromTicks(Model.DurationUs*10).ToString(@"mm\:ss"), Model.FrameRate.FramesPerSecond);
 public bool IsPinned=>Model.IsMain;
}
public sealed class AudioSegmentItem : ObservableObject
{
 private bool _included;private string? _override;
 public MediaSegment Model {get;}private readonly string _trackLabel;public string TrackLabel=>L.Render(_trackLabel);public string Id=>Model.Id;
 public bool CanInclude=>Model.Speed==1 && !Model.HasVariableSpeed && !Model.IsReversed && !Model.IsMuted;
 public bool Included {get=>_included;set=>Set(ref _included,value && CanInclude);}
 public string? OverridePath {get=>_override;set{if(Set(ref _override,value)){Raise(nameof(ResolvedPath));Raise(nameof(Name));Raise(nameof(Diagnostic));}}}
 public string ResolvedPath=>OverridePath ?? Model.SourcePath;
 public string Name=>Path.GetFileName(ResolvedPath);
 public string Range=>L.F("ui.origen01Timeline23", Format(Model.SourceRange.StartUs), Format(Model.SourceRange.EndUs), Format(Model.TargetRange.StartUs), Format(Model.TargetRange.EndUs));
 public string Diagnostic=>!CanInclude?L.T("ui.fragmentoSilenciadoInvertidoOConVelocidadModificada"):!File.Exists(ResolvedPath)?L.T("ui.archivoAusenteUsaLocalizar"):OverridePath is not null?L.T("ui.rutaAlternativaParaEstaEjecucion"):"";
 public AudioSegmentItem(MediaSegment model,string trackLabel){Model=model;_trackLabel=trackLabel;_included=CanInclude;}
 public static string Format(long us)=>TimeSpan.FromTicks(us*10).ToString(@"mm\:ss\.fff");
}
public sealed class CaptionItem(SubtitleCue model) : ObservableObject
{
 public SubtitleCue Model {get;}=model;
 public string Time=>L.F("ui.013", AudioSegmentItem.Format(Model.TimelineRange.StartUs), AudioSegmentItem.Format(Model.TimelineRange.EndUs));
 public string Text=>Model.Text;public int Words=>Model.Words.Count(w=>!string.IsNullOrWhiteSpace(w.Text));
}
public sealed class BackupItem(string journalPath,string label,string phase) : ObservableObject
{ public string JournalPath {get;}=journalPath;public string Label=>L.Render(label);public string Phase {get;}=phase; }
public sealed class LanguageItem(string code,string name) : ObservableObject
{ public string Code {get;}=code;public string Name=>L.T("language."+Code) is var value && value!="language."+Code?value:name; }
