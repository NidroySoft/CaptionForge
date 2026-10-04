namespace CaptionForge.Application.Models.Writing;

/// <summary>Detección del backend; la interfaz debe pedir confirmación antes de sobrescribir contenido real.</summary>
public sealed record SubtitleOverwriteInfo(string TrackId, int ExistingBlockCount, bool IsRecognizedExample,
    bool IsManagedTrack, bool RequiresConfirmation, string Message);
