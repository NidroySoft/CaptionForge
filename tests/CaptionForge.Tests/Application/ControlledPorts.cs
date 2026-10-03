using System.Text.Json;
using CaptionForge.Application.Abstractions;
using CaptionForge.Application.Enums;
using CaptionForge.Application.Exceptions;
using CaptionForge.Application.Models.CapCut;
using CaptionForge.Application.Models.Generation;
using CaptionForge.Application.Models.Media;
using CaptionForge.Application.Models.Settings;
using CaptionForge.Application.Models.Workspace;
using CaptionForge.Application.Models.Writing;
using CaptionForge.Application.Services;
using CaptionForge.Core.Enums;
using CaptionForge.Core.Models.CapCut;
using CaptionForge.Core.Models.Media;
using CaptionForge.Core.Models.Subtitles;
using CaptionForge.Core.Models.Transcription;
using CaptionForge.Core.ValueObjects;

using Xunit;
using Xunit.Abstractions;
using CaptionForge.Tests.Support;

namespace CaptionForge.Tests.Application;

sealed class InlineProgress(Action<GenerationProgress> action) : IProgress<GenerationProgress>
{ public void Report(GenerationProgress value) => action(value); }

sealed class Fixture
{
    public TimelineSnapshot Snapshot { get; }
    public List<string> Events { get; } = new();
    public FakeAudio Audio { get; }
    public FakeTranscription Transcriber { get; }
    public FakeWriter Writer { get; }
    public FakeWorkspace Store { get; }
    public CaptionGenerationService Service { get; }
    public Fixture(MediaSegment? replacement = null)
    {
        var project = new CapCutProject("project", "1003", "C:/CapCut/1003", timelineRegistryId: "registry");
        var timeline = new CapCutTimeline("timeline", project.Id, "Timeline", "C:/CapCut/1003/Timelines/timeline", "C:/CapCut/1003/Timelines/timeline/draft_content.json", new FrameRate(30), 40_000_000);
        var segments = new[] { replacement?.Id == "a" ? replacement : Segment("a"), replacement?.Id == "b" ? replacement : Segment("b", targetStart: 30_000_000) };
        Snapshot = new TimelineSnapshot(project, timeline, new[] { new MediaTrack("audio", "", MediaTrackType.Audio, segments) },
            new[] { new SourceFileStamp(timeline.DraftContentPath, true, new string('a', 64)), new SourceFileStamp(timeline.DraftContentPath + ".bak", false) });
        Audio = new(Events); Transcriber = new(Events); Writer = new(Events); Store = new(Events);
        Service = new(Audio, Transcriber, Writer, Store);
    }
    public GenerateCaptionsRequest Request(IEnumerable<string>? selected = null, IEnumerable<SourcePathOverride>? overrides = null, TranscriptionOptions? options = null) =>
        new(Snapshot, selected ?? new[] { "b", "a" }, options ?? new("test", "C:/model.bin", "en", 6, 8), "C:/CaptionForge", overrides);
    public static MediaSegment Segment(string id, long targetStart = 20_000_000, double speed = 1, bool variable = false, bool reversed = false, bool muted = false) =>
        new(id, "audio", "material-" + id, "C:/source-" + id + ".mp3", new(10_000_000, 1_000_000), new(targetStart, 1_000_000), speed, variable, reversed, muted);
    public static TranscriptionToken Token(string text, long start, long end) => new(text, new(start, end - start));
    public static TranscriptionSegment Phrase(long start = 0, long end = 1_000_000) => new(" Hello", new(start, end - start), new[] { Token(" Hello", start, end) });
    public static TranscriptionResult Transcription(MediaSegment s) => new(s.Id, "test", "en", s.TargetRange.DurationUs, new[] { Phrase() });
}
sealed class FakeAudio(List<string> events) : IAudioPreparationService
{
    public List<AudioPreparationRequest> Requests { get; } = new();
    public Action? Before { get; set; }
    public bool WrongIdentity { get; set; }
    public bool WrongDuration { get; set; }
    public Task<PreparedAudio> PrepareAsync(AudioPreparationRequest request, CancellationToken cancellationToken = default)
    {
        events.Add("audio:" + request.Segment.Id); Requests.Add(request); Before?.Invoke();
        long d = request.RequiredDurationUs + (WrongDuration ? 1000 : 0);
        return Task.FromResult(new PreparedAudio(WrongIdentity ? "wrong" : request.Segment.Id, request.Run.RunDirectory + "/audio/" + request.Segment.Id + ".wav", d, d));
    }
}
sealed class FakeTranscription(List<string> events) : ITranscriptionService
{
    public int Calls { get; private set; }
    public Action? Before { get; set; }
    public string? Mismatch { get; set; }
    public bool ControlOnly { get; set; }
    public bool InvalidWords { get; set; }
    public Task<TranscriptionResult> TranscribeAsync(RunContext run, PreparedAudio audio, TranscriptionOptions options, CancellationToken cancellationToken = default)
    {
        events.Add("transcribe:" + audio.SourceSegmentId); Calls++; Before?.Invoke();
        var phrase = ControlOnly ? new TranscriptionSegment("", new(0, 1_000_000), new[] { new TranscriptionToken("[_BEG_]", new(0,0), true) })
            : InvalidWords ? new TranscriptionSegment(" First second", new(0,1_000_000), new[] { Fixture.Token(" First", 0,700_000), Fixture.Token(" second",500_000,900_000) }) : Fixture.Phrase();
        return Task.FromResult(new TranscriptionResult(Mismatch == "id" ? "wrong" : audio.SourceSegmentId, Mismatch == "model" ? "wrong" : options.ModelName,
            Mismatch == "language" ? "es" : "en", audio.DurationUs + (Mismatch == "duration" ? 1000 : 0), new[] { phrase }));
    }
}
sealed class FakeWriter(List<string> events) : ICapCutSubtitleWriter
{
    public SubtitleWriteRequest? LastRequest { get; private set; }
    public int ApplyCalls { get; private set; }
    public string? PlanFlaw { get; set; }
    public string? ReceiptFlaw { get; set; }
    public Exception? ApplyFailure { get; set; }
    public Action? BeforeApply { get; set; }
    public Action? AfterCommit { get; set; }
    public TaskCompletionSource? Entered { get; set; }
    public TaskCompletionSource? Release { get; set; }
    public Task<PreparedSubtitlePlan> PrepareAsync(SubtitleWriteRequest request, CancellationToken cancellationToken = default)
    {
        events.Add("prepare-plan"); LastRequest = request;
        IEnumerable<SourceFileStamp> files = request.Snapshot.SourceFiles;
        if (PlanFlaw == "hash") files = new[] { new SourceFileStamp(request.Snapshot.Timeline.DraftContentPath, true, new string('f',64)), request.Snapshot.SourceFiles[1] };
        if (PlanFlaw == "path") files = new[] { new SourceFileStamp("C:/foreign/draft_content.json", true, new string('a',64)) };
        if (PlanFlaw == "no-draft") files = request.Snapshot.SourceFiles.Skip(1);
        if (PlanFlaw == "no-bak") files = request.Snapshot.SourceFiles.Take(1);
        var managed = new ManagedSubtitleSet(request.Run.ProjectId, request.Run.TimelineId, new[] { new ManagedSubtitleObject(SubtitleObjectKind.Track, "owned-track", "captions") });
        return Task.FromResult(new PreparedSubtitlePlan(request.Run, request.Run.RunDirectory + "/result/plan.json", new string('c',64), request.Captions.Count + (PlanFlaw == "count" ? 1 : 0), files, managed));
    }
    public async Task<SubtitleApplyResult> ApplyAsync(PreparedSubtitlePlan plan, CancellationToken cancellationToken = default)
    {
        events.Add("apply-plan"); ApplyCalls++; BeforeApply?.Invoke();
        if (Entered is not null) { Entered.SetResult(); await Release!.Task; }
        if (ApplyFailure is not null) throw ApplyFailure;
        var files = plan.ExpectedFiles.Select(f => new AppliedFile(ReceiptFlaw == "hash" && f.Exists ? new SourceFileStamp(f.Path, true, new string('f',64)) : f, new string(ReceiptFlaw == "stale-bak" && f.Path.EndsWith(".bak", StringComparison.Ordinal) ? 'e' : 'b',64), f.Exists ? plan.Run.RunDirectory + "/backup/" + Path.GetFileName(f.Path) : null)).ToArray();
        if (ReceiptFlaw == "count") files = files.Take(1).ToArray();
        var receipt = new SubtitleApplyResult(ReceiptFlaw == "run" ? "wrong-run" : plan.Run.RunId, plan.Run.RunDirectory + "/journal.json", files);
        AfterCommit?.Invoke(); return receipt;
    }
}
sealed class FakeWorkspace(List<string> events) : IWorkspaceStore
{
    private readonly object _gate = new();
    public RunStatus Status { get; private set; } = RunStatus.Created;
    public int CreateCalls { get; private set; }
    public ManagedSubtitleSet? Managed { get; set; }
    public GenerationResult? Saved { get; private set; }
    public bool FailSave { get; set; }
    public bool FailTerminal { get; set; }
    public bool FailComplete { get; set; }
    public CancellationToken CompletionToken { get; private set; }
    public Task<RunContext> CreateRunAsync(GenerateCaptionsRequest request, CancellationToken cancellationToken = default)
    {
        events.Add("create"); CreateCalls++;
        return Task.FromResult(new RunContext("run", request.Snapshot.Project.Id, request.Snapshot.Timeline.Id, "C:/CaptionForge/project", "C:/CaptionForge/project/runs/run", DateTimeOffset.UnixEpoch));
    }
    public Task<ManagedSubtitleSet?> ReadManagedSubtitlesAsync(string projectId, string timelineId, CancellationToken cancellationToken = default)
    { events.Add("read-managed"); return Task.FromResult(Managed); }
    public Task SaveGenerationAsync(GenerationResult result, CancellationToken cancellationToken = default)
    { events.Add("save-generation"); if (FailSave) throw new IOException("save failure"); Saved = result; return Task.CompletedTask; }
    public Task TransitionAsync(RunContext run, RunUpdate update, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (Status != update.ExpectedStatus) throw new CaptionForgeOperationException(OperationErrorCode.RunStateConflict, "state conflict", run.RunId);
            if (FailTerminal && update.Status is RunStatus.Failed or RunStatus.Cancelled or RunStatus.RecoveryRequired) throw new IOException("persist failure");
            Status = update.Status; events.Add(update.Status.ToString());
        }
        return Task.CompletedTask;
    }
    public Task CompleteAsync(GenerationResult result, SubtitleApplyResult applied, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        CompletionToken = cancellationToken; events.Add("complete"); if (FailComplete) throw new IOException("completion failure");
        Status = RunStatus.Completed; Managed = result.Plan.ManagedSubtitles; return Task.CompletedTask;
    }
}
