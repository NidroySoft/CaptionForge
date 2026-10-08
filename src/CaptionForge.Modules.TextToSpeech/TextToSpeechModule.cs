using CaptionForge.Modularity;
namespace CaptionForge.Modules.TextToSpeech;

public sealed class TextToSpeechModule : IApplicationModule
{
    private readonly SpeechView _view = new();
    public object View => _view;
    public bool IsBusy => _view.Model.IsBusy;
    public void Deactivate() => _view.StopPlayback();
    public async Task ShutdownAsync()
    {
        _view.StopPlayback();
        await _view.ShutdownAsync();
    }
}
