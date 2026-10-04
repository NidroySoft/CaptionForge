using System.IO;
using System.Windows.Media;
using System.Windows.Threading;
using CaptionForge.Desktop.Mvvm;
namespace CaptionForge.Desktop.Services;
/// <summary>Reproduce exclusivamente el recorte de origen. No modifica ni extrae el medio.</summary>
public sealed class AudioPreviewService : ObservableObject,IDisposable
{
 private readonly MediaPlayer _player=new();
 private readonly DispatcherTimer _timer=new(){Interval=TimeSpan.FromMilliseconds(100)};
 private TimeSpan _start,_length;private bool _opening,_playing,_updating;private double _position;
 private string _label="Selecciona un fragmento para escucharlo",_error="";
 public string Label {get=>_label;private set=>Set(ref _label,value);}
 public string Error {get=>_error;private set=>Set(ref _error,value);}
 public bool IsPlaying {get=>_playing;private set {if(Set(ref _playing,value))Raise(nameof(PlayLabel));}}
 public string PlayLabel=>IsPlaying?"Pausar":"Reproducir";
 public double DurationSeconds=>_length.TotalSeconds;
 public double PositionSeconds {get=>_position;set {if(Set(ref _position,Math.Clamp(value,0,DurationSeconds)) && !_updating && !_opening)_player.Position=_start+TimeSpan.FromSeconds(_position);Raise(nameof(TimeLabel));}}
 public string TimeLabel=>$"{TimeSpan.FromSeconds(_position):mm\\:ss} / {_length:mm\\:ss}";
 public AudioPreviewService()
 {
  _player.MediaOpened+=(_,_)=>{if(!_opening)return;_opening=false;_player.Position=_start;_player.Play();IsPlaying=true;_timer.Start();};
  _player.MediaFailed+=(_,e)=>{Stop();Error="No se pudo reproducir este medio con los códecs de Windows: "+e.ErrorException.Message;};
  _player.MediaEnded+=(_,_)=>Stop();
  _timer.Tick+=(_,_)=>{if(!IsPlaying)return;if(_player.Position>=_start+_length){Stop();return;}_updating=true;PositionSeconds=(_player.Position-_start).TotalSeconds;_updating=false;};
 }
 public void Play(string path,long startUs,long durationUs,string label)
 {
  Stop();if(!File.Exists(path))throw new FileNotFoundException("Localiza el archivo del fragmento.",path);
  _start=TimeSpan.FromTicks(checked(startUs*10));_length=TimeSpan.FromTicks(checked(durationUs*10));Label=label;Error="";
  Raise(nameof(DurationSeconds));Raise(nameof(TimeLabel));_opening=true;_player.Open(new Uri(Path.GetFullPath(path))); 
 }
 public void Toggle(){if(_opening)return;if(IsPlaying){_player.Pause();IsPlaying=false;_timer.Stop();}else if(_player.Source is not null){_player.Play();IsPlaying=true;_timer.Start();}}
 public void Stop(){_opening=false;_timer.Stop();_player.Stop();_player.Close();IsPlaying=false;_updating=true;PositionSeconds=0;_updating=false;}
 public void Dispose(){Stop();_timer.Stop();}
}
