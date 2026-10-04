using CaptionForge.Application.Internal;
using CaptionForge.Application.Models.Workspace;
using CaptionForge.Core.Models.Media;
namespace CaptionForge.Application.Models.Media;

/// <summary>Recorte del medio original; el WAV resultante usa el tiempo del destino, con origen local cero.</summary>
public sealed record AudioPreparationRequest
{
    public RunContext Run { get; }
    public MediaSegment Segment { get; }
    public string ResolvedSourcePath { get; }

    public AudioPreparationRequest(RunContext run, MediaSegment segment, string resolvedSourcePath)
    {

        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(segment);
        Guard.Text(resolvedSourcePath, nameof(resolvedSourcePath));
        Run = run;
        Segment = segment;
        ResolvedSourcePath = resolvedSourcePath;
    }

    public int SampleRate => 16000;
    public int Channels => 1;
    public int BitsPerSample => 16;
    public long RequiredDurationUs => Segment.TargetRange.DurationUs;

}
