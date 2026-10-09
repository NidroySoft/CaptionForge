using System.Text.Json.Nodes;
using CaptionForge.Application.Abstractions;
using CaptionForge.Application.Enums;
using CaptionForge.Application.Exceptions;
using CaptionForge.Application.Models.Generation;
using CaptionForge.Application.Models.Workspace;
using CaptionForge.Application.Models.Writing;
using CaptionForge.Infrastructure.Internal;

namespace CaptionForge.Infrastructure.Workspace;

public sealed class JsonWorkspaceStore : IWorkspaceStore
{
    public string RootDirectory { get; }
    private readonly TimeProvider _clock;
    public JsonWorkspaceStore(string rootDirectory, TimeProvider? timeProvider = null)
    { RootDirectory=PathSafety.Full(rootDirectory); _clock=timeProvider ?? TimeProvider.System; }

    public Task<RunContext> CreateRunAsync(GenerateCaptionsRequest request, CancellationToken cancellationToken = default)
        => CreateRunAsync(request.Snapshot, request.WorkspaceRoot, JsonFiles.Node(request), cancellationToken);
    public Task<RunContext> CreateRunAsync(ExistingCaptionsRequest request, CancellationToken cancellationToken = default)
        => CreateRunAsync(request.Snapshot, request.WorkspaceRoot, JsonFiles.Node(request), cancellationToken);
    private async Task<RunContext> CreateRunAsync(Application.Models.CapCut.TimelineSnapshot snapshot, string workspaceRoot, JsonNode request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request); cancellationToken.ThrowIfCancellationRequested();
        if (!PathSafety.Full(workspaceRoot).Equals(RootDirectory,PathSafety.Comparison))
            throw new InvalidDataException("La raíz de la petición no coincide con el almacén configurado.");
        PathSafety.RequireSeparate(RootDirectory,snapshot.Project.DirectoryPath);
        var projectsRoot=Directory.GetParent(PathSafety.Full(snapshot.Project.DirectoryPath))?.FullName;
        if (projectsRoot is not null) PathSafety.RequireSeparate(RootDirectory,projectsRoot);
        PathSafety.RejectLinks(RootDirectory);
        string projectDirectory=Path.Combine(RootDirectory,PathSafety.Part(snapshot.Project.Id));
        DateTimeOffset at=_clock.GetUtcNow();
        string id=at.ToString("yyyyMMdd'T'HHmmssfff'Z'",System.Globalization.CultureInfo.InvariantCulture)+"_"+Guid.NewGuid().ToString("N");
        string directory=Path.Combine(projectDirectory,"runs",id);
        var run=new RunContext(id,snapshot.Project.Id,snapshot.Timeline.Id,projectDirectory,directory,at);
        try
        {
            Directory.CreateDirectory(directory);
            foreach (string name in new[] { "audio","transcription","result","backup","logs" }) Directory.CreateDirectory(Path.Combine(directory,name));
            var manifest=new JsonObject { ["schemaVersion"]=1,["run"]=JsonFiles.Node(run),["status"]=RunStatus.Created.ToString(),
                ["updatedAt"]=JsonFiles.Node(at),["request"]=request.DeepClone(),["error"]=null };
            await JsonFiles.WriteAsync(Path.Combine(directory,"run.json"),manifest,cancellationToken).ConfigureAwait(false);
            return run;
        }
        catch { if (Directory.Exists(directory) && !File.Exists(Path.Combine(directory,"run.json"))) Directory.Delete(directory,recursive:true); throw; }
    }
    public async Task<ManagedSubtitleSet?> ReadManagedSubtitlesAsync(string projectId, string timelineId, CancellationToken cancellationToken = default)
    {
        string path=ManagedPath(projectId,timelineId);
        if (!File.Exists(path)) return null;
        var doc=await JsonFiles.ReadObjectAsync(path,cancellationToken).ConfigureAwait(false);
        if (JsonFiles.Long(doc,"schemaVersion") != 1) throw new InvalidDataException("Registro de IDs de otra versión.");
        var managed=ReadManaged(doc["managed"]!);
        if (managed.ProjectId!=projectId || managed.TimelineId!=timelineId) throw new InvalidDataException("Registro de IDs de otro proyecto/timeline.");
        return managed;
    }
    public async Task SaveGenerationAsync(GenerationResult result, CancellationToken cancellationToken = default)
    {
        ValidateRun(result.Run);
        using var lease=await FileLease.AcquireAsync(Path.Combine(result.Run.RunDirectory,"run.lock"),cancellationToken).ConfigureAwait(false);
        var manifest=await ManifestAsync(result.Run,cancellationToken).ConfigureAwait(false);
        RequireStatus(manifest,RunStatus.PreparingSubtitles,result.Run.RunId);
        await JsonFiles.WriteAsync(Path.Combine(result.Run.RunDirectory,"result","generation.json"),JsonFiles.Node(result),cancellationToken).ConfigureAwait(false);
        string srt=Application.Services.SrtFormatter.Format(result.Captions);
        await JsonFiles.AtomicWriteAsync(Path.Combine(result.Run.RunDirectory,"result","subtitles.srt"),System.Text.Encoding.UTF8.GetBytes(srt),cancellationToken).ConfigureAwait(false);
        manifest["generationPath"]=Path.Combine(result.Run.RunDirectory,"result","generation.json");
        manifest["planPath"]=result.Plan.PlanPath;
        await JsonFiles.WriteAsync(Path.Combine(result.Run.RunDirectory,"run.json"),manifest,cancellationToken).ConfigureAwait(false);
    }
    public async Task TransitionAsync(RunContext run, RunUpdate update, CancellationToken cancellationToken = default)
    {
        ValidateRun(run);
        using var lease=await FileLease.AcquireAsync(Path.Combine(run.RunDirectory,"run.lock"),cancellationToken).ConfigureAwait(false);
        var doc=await ManifestAsync(run,cancellationToken).ConfigureAwait(false);
        RequireStatus(doc,update.ExpectedStatus,run.RunId);
        if (!Legal(update.ExpectedStatus,update.Status)) throw new CaptionForgeOperationException(OperationErrorCode.RunStateConflict,"Transición de ejecución no permitida.",run.RunId);
        doc["status"]=update.Status.ToString();doc["updatedAt"]=JsonFiles.Node(update.At);doc["error"]=update.Error;
        await JsonFiles.WriteAsync(Path.Combine(run.RunDirectory,"run.json"),doc,cancellationToken).ConfigureAwait(false);
    }
    public async Task CompleteAsync(GenerationResult result, SubtitleApplyResult applied, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        ValidateRun(result.Run);
        using var lease=await FileLease.AcquireAsync(Path.Combine(result.Run.RunDirectory,"run.lock"),cancellationToken).ConfigureAwait(false);
        var doc=await ManifestAsync(result.Run,cancellationToken).ConfigureAwait(false);RequireStatus(doc,RunStatus.Applying,result.Run.RunId);
        if (applied.RunId != result.Run.RunId) throw new InvalidDataException("Recibo de otra ejecución.");
        await JsonFiles.WriteAsync(Path.Combine(result.Run.RunDirectory,"result","receipt.json"),JsonFiles.Node(applied),cancellationToken).ConfigureAwait(false);
        string registry=ManagedPath(result.Run.ProjectId,result.Run.TimelineId);
        using var managedLease=await FileLease.AcquireAsync(registry+".lock",cancellationToken).ConfigureAwait(false);
        // El escritor publica el registro como parte del commit; completar un run viejo no puede pisar uno posterior.
        var journal=await JsonFiles.ReadObjectAsync(applied.JournalPath,cancellationToken).ConfigureAwait(false);
        if (JsonFiles.String(journal,"phase")!="Committed" || !File.Exists(registry)) throw new InvalidDataException("El commit no tiene un registro durable vigente.");
        var index=await JsonFiles.ReadObjectAsync(registry,cancellationToken).ConfigureAwait(false);
        if (JsonFiles.String(index,"runId")==result.Run.RunId && !ReadManaged(index["managed"]!).Objects.SequenceEqual(result.Plan.ManagedSubtitles.Objects))
            throw new InvalidDataException("El registro confirmado no coincide con los IDs del plan.");
        doc["status"]=RunStatus.Completed.ToString();doc["updatedAt"]=JsonFiles.Node(at);doc["error"]=null;doc["journalPath"]=applied.JournalPath;
        await JsonFiles.WriteAsync(Path.Combine(result.Run.RunDirectory,"run.json"),doc,cancellationToken).ConfigureAwait(false);
    }
    private string ManagedPath(string project,string timeline) => Path.Combine(RootDirectory,PathSafety.Part(project),"managed",PathSafety.Part(timeline)+".json");
    private void ValidateRun(RunContext run)
    {
        ArgumentNullException.ThrowIfNull(run);
        string project=Path.Combine(RootDirectory,PathSafety.Part(run.ProjectId));
        string directory=Path.Combine(project,"runs",PathSafety.Part(run.RunId));
        if (!PathSafety.Full(run.ProjectDirectory).Equals(project,PathSafety.Comparison) || !PathSafety.Full(run.RunDirectory).Equals(directory,PathSafety.Comparison))
            throw new InvalidDataException("La ejecución está fuera de este almacén.");
        PathSafety.RejectLinks(directory);
    }
    internal static async Task<JsonObject> ManifestAsync(RunContext run,CancellationToken ct)
    {
        var doc=await JsonFiles.ReadObjectAsync(Path.Combine(run.RunDirectory,"run.json"),ct).ConfigureAwait(false);
        if (JsonFiles.Long(doc,"schemaVersion")!=1 || JsonFiles.Read<RunContext>(doc["run"]!)!=run) throw new InvalidDataException("Manifiesto o identidad incoherentes.");
        return doc;
    }
    internal static void RequireStatus(JsonObject doc,RunStatus status,string runId)
    { if (JsonFiles.String(doc,"status")!=status.ToString()) throw new CaptionForgeOperationException(OperationErrorCode.RunStateConflict,"La ejecución cambió de estado o ya fue aplicada.",runId); }
    internal static ManagedSubtitleSet ReadManaged(JsonNode node) => new(JsonFiles.String(node,"projectId"),JsonFiles.String(node,"timelineId"),
        JsonFiles.Array(node,"objects").Select(x => new ManagedSubtitleObject(Enum.Parse<SubtitleObjectKind>(JsonFiles.String(x,"kind")),JsonFiles.String(x,"id"),JsonFiles.String(x,"logicalKey"))));
    private static bool Legal(RunStatus from,RunStatus to) => to switch
    {
        RunStatus.Failed => from is not (RunStatus.Completed or RunStatus.Cancelled or RunStatus.Failed or RunStatus.RecoveryRequired),
        RunStatus.Cancelled => from is RunStatus.Created or RunStatus.PreparingAudio or RunStatus.Transcribing or RunStatus.PreparingSubtitles,
        RunStatus.RecoveryRequired => from == RunStatus.Applying,
        RunStatus.PreparingAudio => from is RunStatus.Created or RunStatus.Transcribing,
        RunStatus.Transcribing => from == RunStatus.PreparingAudio,
        RunStatus.PreparingSubtitles => from is RunStatus.Transcribing or RunStatus.Created,
        RunStatus.ReadyToApply => from is RunStatus.PreparingSubtitles or RunStatus.Applying,
        RunStatus.Applying => from == RunStatus.ReadyToApply,
        _ => false
    };
}
