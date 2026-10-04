using CaptionForge.Application.Internal;
namespace CaptionForge.Application.Models.Workspace;

/// <summary>Carpeta propia del proyecto y ejecución única. Nunca se usa el ID de timeline como ID de proyecto.</summary>
public sealed record RunContext
{
    public string RunId { get; }
    public string ProjectId { get; }
    public string TimelineId { get; }
    public string ProjectDirectory { get; }
    public string RunDirectory { get; }
    public DateTimeOffset CreatedAt { get; }

    public RunContext(string runId, string projectId, string timelineId, string projectDirectory, string runDirectory, DateTimeOffset createdAt)
    {
        Guard.Text(runId, nameof(runId));
        Guard.Text(projectId, nameof(projectId));
        Guard.Text(timelineId, nameof(timelineId));
        Guard.Text(projectDirectory, nameof(projectDirectory));
        Guard.Text(runDirectory, nameof(runDirectory));
        RunId = runId;
        ProjectId = projectId;
        TimelineId = timelineId;
        ProjectDirectory = projectDirectory;
        RunDirectory = runDirectory;
        CreatedAt = createdAt;
    }

}
