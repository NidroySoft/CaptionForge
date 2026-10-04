using CaptionForge.Application.Models.Settings;

namespace CaptionForge.Application.Abstractions;

public interface IApplicationSettingsStore
{
    Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken = default);
}
