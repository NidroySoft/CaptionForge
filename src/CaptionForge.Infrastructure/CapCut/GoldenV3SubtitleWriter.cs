using System.Text.Json.Nodes;
using CaptionForge.Application.Abstractions;
using CaptionForge.Application.Enums;
using CaptionForge.Application.Exceptions;
using CaptionForge.Application.Models.CapCut;
using CaptionForge.Application.Models.Writing;
using CaptionForge.Core.Models.Subtitles;
using CaptionForge.Core.ValueObjects;
using CaptionForge.Infrastructure.Internal;
using CaptionForge.Infrastructure.Workspace;

namespace CaptionForge.Infrastructure.CapCut;

public sealed class GoldenV3SubtitleWriter : ICapCutSubtitleWriter
{
    private readonly TemplateAssetPaths? _assets;
    private readonly string? _templateSegmentId;
    private readonly bool _validateTemplateAssets;
    private readonly ICapCutProcessGuard _process;
    private readonly IFileCommitter _committer;
    public GoldenV3SubtitleWriter(TemplateAssetPaths assets,ICapCutProcessGuard? processGuard = null,IFileCommitter? fileCommitter = null)
    { ArgumentNullException.ThrowIfNull(assets);_assets=assets;_process=processGuard ?? new CapCutProcessGuard();_committer=fileCommitter ?? new AtomicFileCommitter(); }

    internal GoldenV3SubtitleWriter(string templateSegmentId, bool validateTemplateAssets, ICapCutProcessGuard? processGuard, IFileCommitter? fileCommitter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateSegmentId);
        _templateSegmentId = templateSegmentId; _validateTemplateAssets = validateTemplateAssets;
        _process = processGuard ?? new CapCutProcessGuard(); _committer = fileCommitter ?? new AtomicFileCommitter();
    }

    public async Task<PreparedSubtitlePlan> PrepareAsync(SubtitleWriteRequest request,CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);_assets?.Validate();ValidateLocations(request);
        await JsonFiles.VerifyAsync(request.Snapshot.SourceFiles,cancellationToken).ConfigureAwait(false);
        var original=await JsonFiles.ReadObjectAsync(request.Snapshot.Timeline.DraftContentPath,cancellationToken).ConfigureAwait(false);
        if (JsonFiles.Long(original,"version")!=360000) throw new InvalidDataException("Esquema de CapCut aún no verificado: no se aplicará el molde golden.");
        if (JsonFiles.String(original,"id")!=request.Run.TimelineId) throw new InvalidDataException("El documento tiene otra identidad.");
        string registry=Path.Combine(request.Run.ProjectDirectory,"managed",PathSafety.Part(request.Run.TimelineId)+".json");
        PathSafety.RejectLinks(registry);var registryStamp=await JsonFiles.StampAsync(registry,cancellationToken).ConfigureAwait(false);
        JsonObject? previousRegistry=registryStamp.Exists?await JsonFiles.ReadObjectAsync(registry,cancellationToken).ConfigureAwait(false):null;
        await JsonFiles.VerifyAsync(new[] {registryStamp},cancellationToken).ConfigureAwait(false);
        var registered=previousRegistry is null?null:JsonWorkspaceStore.ReadManaged(previousRegistry["managed"]!);
        if ((registered is null)!=(request.PreviouslyManaged is null) || (registered is not null && !registered.Objects.SequenceEqual(request.PreviouslyManaged!.Objects)))
            throw new CaptionForgeOperationException(OperationErrorCode.RunStateConflict,"El registro propio cambió antes de preparar; vuelve a leer la timeline.",request.Run.RunId);
        var entries=DraftTemplateRegistry.Entries(previousRegistry);
        JsonObject? prototype=null; ManagedSubtitleSet? selectedManaged=null; SubtitleOverwriteInfo? overwrite=null;
        if(_templateSegmentId is not null)
        {
            string selectedTrack=JsonFiles.String(JsonFiles.Array(original,"tracks").Single(t=>JsonFiles.Array(t,"segments").Any(s=>s?["id"]?.GetValue<string>()==_templateSegmentId)),"id");
            if (selectedTrack == request.SourceSubtitleTrackId) throw new InvalidDataException("Selecciona una plantilla en una pista distinta de los subtítulos originales.");
            var entry=entries.SingleOrDefault(e=>DraftTemplateRegistry.TrackId(JsonWorkspaceStore.ReadManaged(e!["managed"]!))==selectedTrack);
            selectedManaged=entry is null?null:JsonWorkspaceStore.ReadManaged(entry["managed"]!);
            if(entry?["prototype"] is JsonObject cached && entry["seedFingerprint"]?.GetValue<string>()==DraftTemplateRegistry.Fingerprint(original,_templateSegmentId))
                prototype=cached.DeepClone().AsObject();
            overwrite=DraftTemplateLayout.Inspect(original,_templateSegmentId,selectedManaged is not null);
        }
        var assetPaths = _templateSegmentId is null ? Array.Empty<string>() : DraftTemplateDocumentPatcher.AssetPaths(prototype ?? original, _templateSegmentId);
        if (_validateTemplateAssets) DraftTemplateDocumentPatcher.ValidateAssets(assetPaths);
        var (document,managed,patchWarnings)=_templateSegmentId is null
            ? GoldenV3DocumentPatcher.Patch(request,original,_assets!)
            : DraftTemplateDocumentPatcher.Patch(new SubtitleWriteRequest(request.Run,request.Snapshot,request.Captions,selectedManaged),original,_templateSegmentId,prototype);
        var warnings=patchWarnings.ToList();
        if (request.RemoveSourceTrackId is { } removeId)
        {
            if (_templateSegmentId is null) throw new InvalidDataException("Eliminar el origen requiere una plantilla del proyecto.");
            var sourceTracks = await ExistingSubtitleCatalog.ReadAsync(request.Snapshot, cancellationToken).ConfigureAwait(false);
            var source = sourceTracks.SingleOrDefault(t => t.Id == removeId && t.IsSupported)
                ?? throw new InvalidDataException("La pista original no es una pista completa de subtítulos compatible.");
            if (DraftTemplateRegistry.TrackId(managed) == removeId) throw new InvalidDataException("La pista original y la plantilla deben ser distintas.");
            if (!source.Captions.SequenceEqual(request.Captions) &&
                !source.Captions.Select(c => (c.SourceSegmentId, c.Text, c.TimelineRange)).SequenceEqual(request.Captions.Select(c => (c.SourceSegmentId, c.Text, c.TimelineRange))))
                throw new InvalidDataException("El resultado no corresponde a todos los subtítulos de la pista que se va a eliminar.");
            var tracks = JsonFiles.Array(document, "tracks");
            var remove = tracks.Single(t => t?["id"]?.GetValue<string>() == removeId); tracks.Remove(remove);
            foreach (var entry in entries.Where(e => DraftTemplateRegistry.TrackId(JsonWorkspaceStore.ReadManaged(e!["managed"]!)) == removeId).ToArray()) entries.Remove(entry);
            warnings.Add("Se eliminará la pista original al aplicar el resultado. El backup permite restaurarla.");
        }
        if(overwrite?.RequiresConfirmation==true) warnings.Add(overwrite.Message);
        if(_templateSegmentId is not null)
        {
            if(entries.Any(e=>!JsonFiles.Array(original,"tracks").Any(t=>t?["id"]?.GetValue<string>()==DraftTemplateRegistry.TrackId(JsonWorkspaceStore.ReadManaged(e!["managed"]!)))))
                warnings.Add("Una pista utilizada anteriormente fue eliminada. Sus materiales restantes se conservarán.");
            string trackId=DraftTemplateRegistry.TrackId(managed);
            foreach(var entry in entries.Where(e=>DraftTemplateRegistry.TrackId(JsonWorkspaceStore.ReadManaged(e!["managed"]!))==trackId).ToArray()) entries.Remove(entry);
            entries.Add(new JsonObject { ["managed"]=JsonFiles.Node(managed),["prototype"]=(prototype ?? DraftTemplateRegistry.SeedGraph(original,_templateSegmentId)).DeepClone(),
                ["seedFingerprint"]=DraftTemplateRegistry.Fingerprint(document,_templateSegmentId) });
        }
        else entries=new JsonArray(new JsonObject { ["managed"]=JsonFiles.Node(managed) });
        var nextRegistry=new JsonObject { ["schemaVersion"]=1,["runId"]=request.Run.RunId,["managed"]=JsonFiles.Node(managed),["tracks"]=entries };
        var targets=new List<string> {request.Snapshot.Timeline.DraftContentPath,request.Snapshot.Timeline.DraftContentPath+".bak"};
        string timelineRegistryPath=Path.Combine(request.Snapshot.Project.DirectoryPath,"Timelines","project.json");
        var timelineRegistryStamp=request.Snapshot.SourceFiles.SingleOrDefault(f=>PathSafety.Full(f.Path).Equals(PathSafety.Full(timelineRegistryPath),PathSafety.Comparison));
        if (timelineRegistryStamp?.Exists!=true) throw new InvalidDataException("Falta la huella del registro de timelines.");
        PathSafety.RejectLinks(timelineRegistryPath);
        var timelineRegistry=await JsonFiles.ReadObjectAsync(timelineRegistryPath,cancellationToken).ConfigureAwait(false);
        string pinnedTimelineId=JsonFiles.String(timelineRegistry,"main_timeline_id");
        var timelineEntries=JsonFiles.Array(timelineRegistry,"timelines");
        if (string.IsNullOrWhiteSpace(pinnedTimelineId) ||
            timelineEntries.Count(t=>t?["id"]?.GetValue<string>()==pinnedTimelineId && t?["is_marked_delete"]?.GetValue<bool>()!=true)!=1 ||
            timelineEntries.Count(t=>t?["id"]?.GetValue<string>()==request.Run.TimelineId && t?["is_marked_delete"]?.GetValue<bool>()!=true)!=1)
            throw new InvalidDataException("La timeline fijada o la seleccionada no tiene una entrada única y válida en el registro.");
        string root=Path.Combine(request.Snapshot.Project.DirectoryPath,"draft_content.json");
        if (pinnedTimelineId==request.Run.TimelineId)
        {
            var rootStamp=request.Snapshot.SourceFiles.SingleOrDefault(f=>PathSafety.Full(f.Path).Equals(PathSafety.Full(root),PathSafety.Comparison));
            if (rootStamp?.Exists!=true)
                throw new CaptionForgeOperationException(OperationErrorCode.SourceChanged,"Falta el JSON raíz que debe reflejar la timeline fijada.",request.Run.RunId);
            PathSafety.RejectLinks(root);
            var mirror=await JsonFiles.ReadObjectAsync(root,cancellationToken).ConfigureAwait(false);
            if (mirror["id"]?.GetValue<string>()!=pinnedTimelineId || !JsonFiles.Equivalent(mirror,original))
                throw new CaptionForgeOperationException(OperationErrorCode.SourceChanged,"El JSON raíz no coincide con la timeline fijada; no se modificará el proyecto.",request.Run.RunId);
            targets.Add(root);targets.Add(root+".bak");
        }
        else warnings.Add("La timeline seleccionada no está fijada; se conservarán el JSON raíz y su .bak.");
        await JsonFiles.VerifyAsync(request.Snapshot.SourceFiles,cancellationToken).ConfigureAwait(false);
        string stagedDirectory=Path.Combine(request.Run.RunDirectory,"result","staged");Directory.CreateDirectory(stagedDirectory);
        var changes=new JsonArray();var expected=new List<SourceFileStamp>();byte[] bytes=JsonFiles.Bytes(document);
        for (int i=0;i<targets.Count;i++)
        {
            string target=PathSafety.Full(targets[i]);PathSafety.RejectLinks(target);
            var before=request.Snapshot.SourceFiles.SingleOrDefault(f=>PathSafety.Full(f.Path).Equals(target,PathSafety.Comparison))
                ?? throw new InvalidDataException("Falta una huella del archivo a modificar.");
            string staged=Path.Combine(stagedDirectory,i.ToString("D3")+".json");
            await JsonFiles.AtomicWriteAsync(staged,bytes,cancellationToken).ConfigureAwait(false);
            changes.Add(new JsonObject { ["before"]=JsonFiles.Node(before),["stagedPath"]=staged,["afterSha256"]=JsonFiles.Hash(bytes),
                ["backupPath"]=before.Exists?Path.Combine(request.Run.RunDirectory,"backup",i.ToString("D3")+".bin"):null });expected.Add(before);
        }
        var plan=new JsonObject { ["schemaVersion"]=1,["templateSegmentId"]=_templateSegmentId,["templateAssets"]=JsonFiles.Node(assetPaths),["run"]=JsonFiles.Node(request.Run),["snapshot"]=JsonFiles.Node(request.Snapshot),
            ["files"]=changes,["readFiles"]=JsonFiles.Node(request.Snapshot.SourceFiles.Concat(new[] {registryStamp})),["managed"]=JsonFiles.Node(managed),
            ["nextRegistry"]=nextRegistry,["overwriteInfo"]=overwrite is null?null:JsonFiles.Node(overwrite),["registryPath"]=registry,["previousRegistry"]=previousRegistry?.DeepClone(),
            ["previouslyManaged"]=request.PreviouslyManaged is null?null:JsonFiles.Node(request.PreviouslyManaged),["captions"]=JsonFiles.Node(request.Captions),["warnings"]=JsonFiles.Node(warnings) };
        string path=Path.Combine(request.Run.RunDirectory,"result","plan.json");byte[] planBytes=JsonFiles.Bytes(plan);
        await JsonFiles.AtomicWriteAsync(path,planBytes,cancellationToken).ConfigureAwait(false);
        return new(request.Run,path,JsonFiles.Hash(planBytes),request.Captions.Count,expected,managed,warnings,overwrite);
    }

    public async Task<SubtitleApplyResult> ApplyAsync(PreparedSubtitlePlan plan,CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);cancellationToken.ThrowIfCancellationRequested();_process.EnsureClosed();_assets?.Validate();
        PathSafety.RequireInside(plan.PlanPath,plan.Run.RunDirectory);PathSafety.RejectLinks(plan.Run.RunDirectory);
        byte[] planBytes=await File.ReadAllBytesAsync(plan.PlanPath,cancellationToken).ConfigureAwait(false);
        if (!JsonFiles.Hash(planBytes).Equals(plan.PlanSha256,StringComparison.OrdinalIgnoreCase)) throw new CaptionForgeOperationException(OperationErrorCode.SourceChanged,"El plan preparado cambió.",plan.Run.RunId);
        var doc=JsonFiles.Parse(planBytes);ValidatePlan(doc,plan);
        if (_validateTemplateAssets) DraftTemplateDocumentPatcher.ValidateAssets(JsonFiles.Array(doc,"templateAssets").Select(n=>n!.GetValue<string>()));
        using var lease=await FileLease.AcquireAsync(TimelineLock(plan.Run.ProjectDirectory,JsonFiles.String(doc["snapshot"]!["timeline"],"draftContentPath")),cancellationToken).ConfigureAwait(false);
        using var registryLease=await FileLease.AcquireAsync(JsonFiles.String(doc,"registryPath")+".lock",cancellationToken).ConfigureAwait(false);
        var manifest=await JsonWorkspaceStore.ManifestAsync(plan.Run,cancellationToken).ConfigureAwait(false);JsonWorkspaceStore.RequireStatus(manifest,RunStatus.Applying,plan.Run.RunId);
        string journalPath=Path.Combine(plan.Run.RunDirectory,"journal.json");
        if (File.Exists(journalPath)) throw new CaptionForgeOperationException(OperationErrorCode.RunStateConflict,"Esta ejecución ya tiene un journal. Revísalo antes de volver a aplicar.",plan.Run.RunId);
        await JsonFiles.VerifyAsync(JsonFiles.Array(doc,"readFiles").Select(n=>JsonFiles.ReadStamp(n!)),cancellationToken).ConfigureAwait(false);
        foreach (var change in JsonFiles.Array(doc,"files"))
        {
            string staged=JsonFiles.String(change,"stagedPath");var bytes=await File.ReadAllBytesAsync(staged,cancellationToken).ConfigureAwait(false);
            if (JsonFiles.Hash(bytes)!=JsonFiles.String(change,"afterSha256")) throw new CaptionForgeOperationException(OperationErrorCode.SourceChanged,"Cambió un archivo preparado.",plan.Run.RunId);
            var original=JsonFiles.ReadStamp(change!["before"]!);
            if (original.Exists)
            {
                var previous=await File.ReadAllBytesAsync(original.Path,cancellationToken).ConfigureAwait(false);
                if (!JsonFiles.Hash(previous).Equals(original.Sha256,StringComparison.OrdinalIgnoreCase)) throw new CaptionForgeOperationException(OperationErrorCode.SourceChanged,"El original cambió antes del backup.",plan.Run.RunId);
                await JsonFiles.AtomicWriteAsync(JsonFiles.String(change,"backupPath"),previous,cancellationToken).ConfigureAwait(false);
            }
        }
        await JsonFiles.VerifyAsync(JsonFiles.Array(doc,"readFiles").Select(n=>JsonFiles.ReadStamp(n!)),cancellationToken).ConfigureAwait(false);_process.EnsureClosed();
        cancellationToken.ThrowIfCancellationRequested();
        var journal=new JsonObject { ["schemaVersion"]=1,["run"]=JsonFiles.Node(plan.Run),["planPath"]=plan.PlanPath,["planSha256"]=plan.PlanSha256,
            ["phase"]="Applying",["files"]=doc["files"]!.DeepClone(),["managed"]=doc["managed"]!.DeepClone(),["previouslyManaged"]=doc["previouslyManaged"]?.DeepClone(),
            ["registryPath"]=doc["registryPath"]!.DeepClone(),["previousRegistry"]=doc["previousRegistry"]?.DeepClone(),["writtenFiles"]=0 };
        // Punto de entrada al commit: desde aquí no se acepta cancelación intermedia.
        await JsonFiles.WriteAsync(journalPath,journal,CancellationToken.None).ConfigureAwait(false);
        try
        {
            int written=0;
            foreach (var change in JsonFiles.Array(doc,"files"))
            {
                string destination=JsonFiles.String(change!["before"],"path");
                await _committer.ReplaceAsync(destination,await File.ReadAllBytesAsync(JsonFiles.String(change,"stagedPath")).ConfigureAwait(false)).ConfigureAwait(false);
                var actual=await JsonFiles.StampAsync(destination).ConfigureAwait(false);
                if (!actual.Exists || actual.Sha256!=JsonFiles.String(change,"afterSha256")) throw new IOException("El contenido escrito no coincide con el plan.");
                journal["writtenFiles"]=++written;await JsonFiles.WriteAsync(journalPath,journal).ConfigureAwait(false);
            }
            var receipt=new SubtitleApplyResult(plan.Run.RunId,journalPath,JsonFiles.Array(doc,"files").Select(c=>new AppliedFile(JsonFiles.ReadStamp(c!["before"]!),JsonFiles.String(c,"afterSha256"),c["backupPath"]?.GetValue<string>())));
            await JsonFiles.WriteAsync(Path.Combine(plan.Run.RunDirectory,"result","commit-receipt.json"),JsonFiles.Node(receipt)).ConfigureAwait(false);
            // Publicar la identidad bajo la misma exclusión del commit evita una ventana de subtítulos sin autoría.
            await JsonFiles.WriteAsync(JsonFiles.String(doc,"registryPath"),doc["nextRegistry"]?.DeepClone() ?? new JsonObject { ["schemaVersion"]=1,["runId"]=plan.Run.RunId,["managed"]=doc["managed"]!.DeepClone() }).ConfigureAwait(false);
            journal["phase"]="Committed";journal["committedAt"]=JsonFiles.Node(DateTimeOffset.UtcNow);await JsonFiles.WriteAsync(journalPath,journal).ConfigureAwait(false);
            return receipt;
        }
        catch (Exception commitError)
        {
            try { await RestoreFilesAsync(journal,plan.Run.RunDirectory).ConfigureAwait(false);await RestoreRegistryAsync(doc).ConfigureAwait(false);journal["phase"]="RolledBack";journal["error"]=commitError.Message;await JsonFiles.WriteAsync(journalPath,journal).ConfigureAwait(false); }
            catch (Exception rollbackError)
            {
                journal["phase"]="RecoveryRequired";journal["error"]=new AggregateException(commitError,rollbackError).Message;
                try { await JsonFiles.WriteAsync(journalPath,journal).ConfigureAwait(false); } catch (Exception persistError) { throw new AggregateException(commitError,rollbackError,persistError); }
                throw new AggregateException(commitError,rollbackError);
            }
            throw new IOException("Falló el commit; se restauraron los archivos anteriores. Revisa el journal antes de reintentar.",commitError);
        }
    }

    /// <summary>Restauración explícita del último estado respaldado por esta ejecución; rechaza cambios posteriores.</summary>
    public async Task RestoreAsync(string journalPath,CancellationToken cancellationToken = default)
    {
        _process.EnsureClosed();var journal=await JsonFiles.ReadObjectAsync(journalPath,cancellationToken).ConfigureAwait(false);
        var run=JsonFiles.Read<Application.Models.Workspace.RunContext>(journal["run"]!);
        if (!PathSafety.Full(journalPath).Equals(Path.Combine(PathSafety.Full(run.RunDirectory),"journal.json"),PathSafety.Comparison)) throw new InvalidDataException("Journal fuera de su ejecución.");
        var planBytes=await File.ReadAllBytesAsync(JsonFiles.String(journal,"planPath"),cancellationToken).ConfigureAwait(false);
        if (JsonFiles.Hash(planBytes)!=JsonFiles.String(journal,"planSha256")) throw new InvalidDataException("El plan del journal cambió.");
        var doc=JsonFiles.Parse(planBytes);ValidateDiskLocations(doc,run);
        if (!JsonNode.DeepEquals(journal["files"],doc["files"]) || JsonFiles.Long(journal,"schemaVersion")!=1) throw new InvalidDataException("Journal incoherente con su plan.");
        using var runLease=await FileLease.AcquireAsync(Path.Combine(run.RunDirectory,"run.lock"),cancellationToken).ConfigureAwait(false);
        using var lease=await FileLease.AcquireAsync(TimelineLock(run.ProjectDirectory,JsonFiles.String(doc["snapshot"]!["timeline"],"draftContentPath")),cancellationToken).ConfigureAwait(false);
        string registry=JsonFiles.String(doc,"registryPath");
        using var registryLease=await FileLease.AcquireAsync(registry+".lock",cancellationToken).ConfigureAwait(false);
        if (File.Exists(registry))
        {
            var current=await JsonFiles.ReadObjectAsync(registry,cancellationToken).ConfigureAwait(false);
            if (current["runId"]?.GetValue<string>()!=run.RunId && !JsonNode.DeepEquals(current,doc["previousRegistry"]))
                throw new CaptionForgeOperationException(OperationErrorCode.SourceChanged,"Hay una ejecución posterior registrada; no se restaurará este backup.",run.RunId);
        }
        await ValidateRestorationAsync(journal,run.RunDirectory,cancellationToken).ConfigureAwait(false);_process.EnsureClosed();cancellationToken.ThrowIfCancellationRequested();
        journal["phase"]="Restoring";await JsonFiles.WriteAsync(journalPath,journal).ConfigureAwait(false);
        try
        {
            await RestoreFilesAsync(journal,run.RunDirectory).ConfigureAwait(false);
            await RestoreRegistryAsync(doc).ConfigureAwait(false);
            journal["phase"]="Restored";journal["restoredAt"]=JsonFiles.Node(DateTimeOffset.UtcNow);await JsonFiles.WriteAsync(journalPath,journal).ConfigureAwait(false);
            var manifest=await JsonWorkspaceStore.ManifestAsync(run,CancellationToken.None).ConfigureAwait(false);
            manifest["restoration"]=new JsonObject { ["phase"]="Restored",["at"]=JsonFiles.Node(DateTimeOffset.UtcNow),["journalPath"]=journalPath };
            await JsonFiles.WriteAsync(Path.Combine(run.RunDirectory,"run.json"),manifest).ConfigureAwait(false);
        }
        catch { journal["phase"]="RecoveryRequired";await JsonFiles.WriteAsync(journalPath,journal).ConfigureAwait(false);throw; }
    }

    private static void ValidateLocations(SubtitleWriteRequest request)
    {
        PathSafety.RequireSeparate(request.Run.ProjectDirectory,request.Snapshot.Project.DirectoryPath);PathSafety.RejectLinks(request.Run.RunDirectory);
        string timeline=Path.Combine(PathSafety.Full(request.Snapshot.Project.DirectoryPath),"Timelines",PathSafety.Part(request.Run.TimelineId),"draft_content.json");
        if (!PathSafety.Full(request.Snapshot.Timeline.DraftContentPath).Equals(timeline,PathSafety.Comparison)) throw new InvalidDataException("La ruta no corresponde a la timeline seleccionada.");
        if (!PathSafety.Full(request.Run.RunDirectory).Equals(Path.Combine(PathSafety.Full(request.Run.ProjectDirectory),"runs",PathSafety.Part(request.Run.RunId)),PathSafety.Comparison)) throw new InvalidDataException("Carpeta de ejecución no válida.");
    }
    private static string TimelineLock(string projectDirectory,string path) => Path.Combine(projectDirectory,"locks",JsonFiles.Hash(System.Text.Encoding.UTF8.GetBytes(OperatingSystem.IsWindows()?PathSafety.Full(path).ToUpperInvariant():PathSafety.Full(path)))+".lock");
    private static void ValidatePlan(JsonObject doc,PreparedSubtitlePlan plan)
    {
        if (JsonFiles.Long(doc,"schemaVersion")!=1 || JsonFiles.Read<Application.Models.Workspace.RunContext>(doc["run"]!)!=plan.Run) throw new InvalidDataException("Plan de otra ejecución o versión.");
        var managed=JsonWorkspaceStore.ReadManaged(doc["managed"]!);
        if (managed.ProjectId!=plan.ManagedSubtitles.ProjectId || managed.TimelineId!=plan.ManagedSubtitles.TimelineId || !managed.Objects.SequenceEqual(plan.ManagedSubtitles.Objects)) throw new InvalidDataException("Identidades propias distintas al plan revisado.");
        var files=JsonFiles.Array(doc,"files");
        if (files.Count!=plan.ExpectedFiles.Count || files.Where((f,i)=>JsonFiles.ReadStamp(f!["before"]!)!=plan.ExpectedFiles[i]).Any()) throw new InvalidDataException("Huellas distintas al plan revisado.");
        if (JsonFiles.Array(doc,"captions").Count!=plan.CaptionCount) throw new InvalidDataException("Cantidad de captions distinta al resultado revisado.");
        if(doc["nextRegistry"] is JsonObject nextRegistry)
        {
            _ = DraftTemplateRegistry.Entries(nextRegistry);
            if(JsonFiles.String(nextRegistry,"runId")!=plan.Run.RunId || !JsonNode.DeepEquals(nextRegistry["managed"],doc["managed"]))
                throw new InvalidDataException("El registro preparado no coincide con la ejecución y sus identidades.");
        }
        if(!JsonNode.DeepEquals(doc["overwriteInfo"],plan.OverwriteInfo is null?null:JsonFiles.Node(plan.OverwriteInfo)))
            throw new InvalidDataException("La información de sobrescritura cambió respecto del plan revisado.");
        ValidateDiskLocations(doc,plan.Run);
    }
    private static void ValidateDiskLocations(JsonObject doc,Application.Models.Workspace.RunContext run)
    {
        var snapshot=doc["snapshot"]!;string project=PathSafety.Full(JsonFiles.String(snapshot["project"],"directoryPath"));
        if (JsonFiles.String(snapshot["project"],"id")!=run.ProjectId || JsonFiles.String(snapshot["timeline"],"id")!=run.TimelineId) throw new InvalidDataException("Identidades incoherentes del plan.");
        string selected=Path.Combine(project,"Timelines",PathSafety.Part(run.TimelineId),"draft_content.json"),root=Path.Combine(project,"draft_content.json");
        var allowed=new[] {selected,selected+".bak",root,root+".bak"};
        PathSafety.RequireSeparate(run.ProjectDirectory,project);PathSafety.RejectLinks(run.RunDirectory);
        string registry=Path.Combine(run.ProjectDirectory,"managed",PathSafety.Part(run.TimelineId)+".json");
        if (!PathSafety.Full(JsonFiles.String(doc,"registryPath")).Equals(PathSafety.Full(registry),PathSafety.Comparison)) throw new InvalidDataException("Registro propio fuera de la timeline.");
        PathSafety.RejectLinks(registry);
        foreach (var change in JsonFiles.Array(doc,"files"))
        {
            string path=PathSafety.Full(JsonFiles.String(change!["before"],"path"));
            if (!allowed.Any(a=>a.Equals(path,PathSafety.Comparison))) throw new InvalidDataException("El plan intenta modificar un documento no autorizado.");
            PathSafety.RejectLinks(path);PathSafety.RequireInside(JsonFiles.String(change,"stagedPath"),run.RunDirectory);
            if (change["backupPath"] is not null) PathSafety.RequireInside(JsonFiles.String(change,"backupPath"),run.RunDirectory);
        }
    }
    private static async Task ValidateRestorationAsync(JsonObject journal,string runDirectory,CancellationToken ct)
    {
        foreach (var change in JsonFiles.Array(journal,"files"))
        {
            var before=JsonFiles.ReadStamp(change!["before"]!);PathSafety.RejectLinks(before.Path);
            var current=await JsonFiles.StampAsync(before.Path,ct).ConfigureAwait(false);
            bool already=current.Exists==before.Exists && string.Equals(current.Sha256,before.Sha256,StringComparison.OrdinalIgnoreCase);
            bool after=current.Exists && current.Sha256==JsonFiles.String(change,"afterSha256");
            if (!already && !after) throw new CaptionForgeOperationException(OperationErrorCode.SourceChanged,"El proyecto cambió después de este backup; no se sobrescribirá.");
            if (before.Exists)
            {
                string backup=JsonFiles.String(change,"backupPath");PathSafety.RequireInside(backup,runDirectory);
                var bytes=await File.ReadAllBytesAsync(backup,ct).ConfigureAwait(false);
                if (!JsonFiles.Hash(bytes).Equals(before.Sha256,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Backup propio dañado o diferente al original.");
            }
        }
    }
    private static async Task RestoreFilesAsync(JsonObject journal,string runDirectory)
    {
        await ValidateRestorationAsync(journal,runDirectory,CancellationToken.None).ConfigureAwait(false);
        foreach (var change in JsonFiles.Array(journal,"files").Reverse())
        {
            var before=JsonFiles.ReadStamp(change!["before"]!);
            if (before.Exists) await JsonFiles.AtomicWriteAsync(before.Path,await File.ReadAllBytesAsync(JsonFiles.String(change,"backupPath")).ConfigureAwait(false)).ConfigureAwait(false);
            else if (File.Exists(before.Path)) File.Delete(before.Path);
        }
    }
    private static async Task RestoreRegistryAsync(JsonObject plan)
    {
        string registry=JsonFiles.String(plan,"registryPath");
        if (plan["previousRegistry"] is JsonNode previous) await JsonFiles.WriteAsync(registry,previous).ConfigureAwait(false);
        else if (File.Exists(registry)) File.Delete(registry);
    }
}
