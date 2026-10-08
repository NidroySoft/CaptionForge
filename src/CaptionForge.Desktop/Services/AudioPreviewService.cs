using CaptionForge.Desktop.Localization;
using System.IO;
using System.Windows.Media;
using System.Windows.Threading;
using CaptionForge.Desktop.Mvvm;
namespace CaptionForge.Desktop.Services;
/// <summary>Prepara y reproduce exclusivamente el recorte de origen.</summary>
public sealed class AudioPreviewService : ObservableObject,IDisposable
{
 private readonly MediaPlayer _player=new();
 private readonly DispatcherTimer _timer=new(){Interval=TimeSpan.FromMilliseconds(100)};
 private TimeSpan _start,_length;private bool _opening,_playWhenOpened,_playing,_updating;private double _position;
 private string? _path;
 private string _label=L.T("ui.seleccionaUnFragmentoParaEscucharlo"),_detail="",_error="";
 public string Label {get=>L.Render(_label);private set=>Set(ref _label,value);}
 public string Detail {get=>L.Render(_detail);private set=>Set(ref _detail,value);}
 public string Error {get=>L.Render(_error);private set=>Set(ref _error,value);}
 public bool HasClip=>_path is not null;
 public bool CanPlay=>HasClip && File.Exists(_path);
 public bool IsPlaying {get=>_playing;private set {if(Set(ref _playing,value))Raise(nameof(PlayLabel));}}
 public string PlayLabel=>IsPlaying || (_opening && _playWhenOpened)?L.T("ui.pausar"):L.T("ui.reproducir");
 public double DurationSeconds=>_length.TotalSeconds;
 public double PositionSeconds {get=>_position;set {if(Set(ref _position,Math.Clamp(value,0,DurationSeconds)) && !_updating && !_opening && _player.Source is not null)_player.Position=_start+TimeSpan.FromSeconds(_position);Raise(nameof(TimeLabel));}}
 public string TimeLabel=>L.F("ui.0MmSs1MmSs",TimeSpan.FromSeconds(_position).ToString(@"mm\:ss"),_length.ToString(@"mm\:ss"));
 public AudioPreviewService()
 {
  _player.MediaOpened+=(_,_)=>
  {
   if(!_opening)return;_opening=false;_player.Position=_start+TimeSpan.FromSeconds(_position);
   if(_playWhenOpened){_player.Play();IsPlaying=true;_timer.Start();}
   else{_player.Pause();IsPlaying=false;}
   Raise(nameof(PlayLabel));
  };
  _player.MediaFailed+=(_,e)=>{Stop();Error=L.F("preview.failed",e.ErrorException.Message);};
  _player.MediaEnded+=(_,_)=>Stop();
  _timer.Tick+=(_,_)=>{if(!IsPlaying)return;if(_player.Position>=_start+_length){Stop();return;}_updating=true;PositionSeconds=(_player.Position-_start).TotalSeconds;_updating=false;};
 }
 // Preparing a clip does not start playback or open a decoder. The main Play
 // button opens this prepared source on demand, including after Stop/Ended.
 public void Load(string path,long startUs,long durationUs,string label,string detail="")
 {
  if(startUs<0 || durationUs<=0)throw new ArgumentOutOfRangeException(nameof(durationUs));
  var start=TimeSpan.FromTicks(checked(startUs*10));var length=TimeSpan.FromTicks(checked(durationUs*10));
  string fullPath=Path.GetFullPath(path);
  Stop();_path=fullPath;_start=start;_length=length;Label=label;Detail=detail;
  Error=File.Exists(fullPath)?"":L.F("preview.missing",path);
  Raise(nameof(HasClip));Raise(nameof(CanPlay));Raise(nameof(DurationSeconds));Raise(nameof(TimeLabel));
 }
 public void Play(string path,long startUs,long durationUs,string label)
 { Load(path,startUs,durationUs,label);Toggle(); }
 public void Toggle()
 {
  if(!HasClip)return;
  if(!File.Exists(_path)){Stop();Error=L.F("preview.missing",_path);Raise(nameof(CanPlay));return;}
  Error="";
  if(_opening){_playWhenOpened=!_playWhenOpened;Raise(nameof(PlayLabel));return;}
  if(IsPlaying){_player.Pause();IsPlaying=false;_timer.Stop();return;}
  if(PositionSeconds>=DurationSeconds)PositionSeconds=0;
  if(_player.Source is null)
  {
   _opening=true;_playWhenOpened=true;Raise(nameof(PlayLabel));
   try{_player.Open(new Uri(_path!));}
   catch{_opening=false;_playWhenOpened=false;Raise(nameof(PlayLabel));throw;}
  }
  else{_player.Play();IsPlaying=true;_timer.Start();}
 }
 public void Stop(){_opening=false;_playWhenOpened=false;_timer.Stop();_player.Stop();_player.Close();IsPlaying=false;_updating=true;PositionSeconds=0;_updating=false;Raise(nameof(PlayLabel));}
 public void Clear()
 {
  Stop();_path=null;_start=TimeSpan.Zero;_length=TimeSpan.Zero;Label=L.T("ui.seleccionaUnFragmentoParaEscucharlo");Detail="";Error="";
  Raise(nameof(HasClip));Raise(nameof(CanPlay));Raise(nameof(DurationSeconds));Raise(nameof(TimeLabel));
 }
 public void Dispose(){Clear();_timer.Stop();}
}
