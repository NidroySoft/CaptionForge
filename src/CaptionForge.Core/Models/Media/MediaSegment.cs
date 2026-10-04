using CaptionForge.Core.ValueObjects;

namespace CaptionForge.Core.Models.Media;

/// <summary>Fragmento de audio/vídeo existente, con rangos de origen y destino separados.</summary>
public sealed record MediaSegment
{
    public string Id { get; }
    public string TrackId { get; }
    public string MaterialId { get; }
    public string SourcePath { get; }
    public TimeRangeUs SourceRange { get; }
    public TimeRangeUs TargetRange { get; }
    public double Speed { get; }
    public bool HasVariableSpeed { get; }
    public bool IsReversed { get; }
    public bool IsMuted { get; }

    public MediaSegment(string id, string trackId, string materialId, string sourcePath,
        TimeRangeUs sourceRange, TimeRangeUs targetRange, double speed = 1.0,
        bool hasVariableSpeed = false, bool isReversed = false, bool isMuted = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(trackId);
        ArgumentException.ThrowIfNullOrWhiteSpace(materialId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        if (sourceRange.IsEmpty)
            throw new ArgumentException("El recorte de origen debe tener duración positiva.", nameof(sourceRange));
        if (targetRange.IsEmpty)
            throw new ArgumentException("El rango de destino debe tener duración positiva.", nameof(targetRange));
        if (!double.IsFinite(speed) || speed <= 0)
            throw new ArgumentOutOfRangeException(nameof(speed), "La velocidad debe ser finita y positiva.");
        Id = id;
        TrackId = trackId;
        MaterialId = materialId;
        SourcePath = sourcePath;
        SourceRange = sourceRange;
        TargetRange = targetRange;
        Speed = speed;
        HasVariableSpeed = hasVariableSpeed;
        IsReversed = isReversed;
        IsMuted = isMuted;
    }
}
