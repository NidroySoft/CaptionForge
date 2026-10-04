using CaptionForge.Application.Models.CapCut;
using CaptionForge.Core.Models.CapCut;

namespace CaptionForge.Application.Abstractions;

public interface ICapCutCatalog
{
    /// <summary>Valida la raíz y lista proyectos. Una raíz inválida produce un diagnóstico, no un proyecto inventado.</summary>
    Task<IReadOnlyList<CapCutProject>> FindProjectsAsync(string projectsRoot, CancellationToken cancellationToken = default);
    /// <summary>Lee Timelines/project.json, ignora timelines borradas y conserva IDs/nombres/covers reales.</summary>
    Task<IReadOnlyList<CapCutTimeline>> GetTimelinesAsync(CapCutProject project, CancellationToken cancellationToken = default);
    /// <summary>Lectura coherente de documentos/huellas; resuelve medios mediante material_id y registra archivos ausentes.</summary>
    Task<TimelineSnapshot> ReadTimelineAsync(CapCutProject project, CapCutTimeline timeline, CancellationToken cancellationToken = default);
}
