using System.Windows;
using CaptionForge.Desktop.ViewModels;
using CaptionForge.Modularity;

namespace CaptionForge.Desktop.Modules;

/// <summary>Adapter for the existing, stateful subtitle workflow.</summary>
internal sealed class SubtitleModule(MainViewModel model, FrameworkElement view) : IApplicationModule
{
    public object View => view;
    public bool IsBusy => model.IsBusy;
    public void Deactivate() => model.Preview.Stop();
    public Task ShutdownAsync() => model.ShutdownAsync();
}
