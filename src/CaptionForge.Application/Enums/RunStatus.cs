namespace CaptionForge.Application.Enums;

public enum RunStatus
{
    Created, PreparingAudio, Transcribing, PreparingSubtitles, ReadyToApply,
    Applying, Completed, Cancelled, Failed, RecoveryRequired
}
