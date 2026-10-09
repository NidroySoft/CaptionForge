using CaptionForge.Modularity;
namespace CaptionForge.Modules.MediaTools;

public sealed class MediaToolsModule : IApplicationModule
{
    private readonly MediaToolsView view = new();
    public object View => view;
    public bool IsBusy => view.Model.IsBusy;
    public void Deactivate() => view.StopPlayback();
    public Task ShutdownAsync() => view.ShutdownAsync();
}
