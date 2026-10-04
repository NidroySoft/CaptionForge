namespace CaptionForge.Application.Enums;

public enum OperationErrorCode
{
    InvalidSelection, UnsupportedMedia, OverlappingSegments, InvalidAdapterResult,
    InvalidWordTiming, NoCaptions, RunStateConflict, SourceChanged,
    CapCutOpen, ResourceUnavailable, PersistenceFailure, RecoveryRequired
}
