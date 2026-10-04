using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Diagnostics;
using CaptionForge.Application.Abstractions;
using CaptionForge.Application.Enums;
using CaptionForge.Application.Exceptions;
using CaptionForge.Application.Models.Generation;
using CaptionForge.Application.Models.Media;
using CaptionForge.Application.Models.Settings;
using CaptionForge.Application.Models.Workspace;
using CaptionForge.Application.Models.Writing;
using CaptionForge.Application.Services;
using CaptionForge.Core.Models.Subtitles;
using CaptionForge.Core.Models.Transcription;
using CaptionForge.Core.Models.Media;
using CaptionForge.Core.ValueObjects;
using CaptionForge.Infrastructure.CapCut;
using CaptionForge.Infrastructure.Configuration;
using CaptionForge.Infrastructure.Media;
using CaptionForge.Infrastructure.Settings;
using CaptionForge.Infrastructure.Transcription;
using CaptionForge.Infrastructure.Workspace;
using Whisper.net;

using Xunit;
using Xunit.Abstractions;
using CaptionForge.Tests.Support;
using static CaptionForge.Tests.Infrastructure.InfrastructureTools;

namespace CaptionForge.Tests.Infrastructure;

public sealed class InfrastructureRegressionTests(ITestOutputHelper output) : InfrastructureTestBase(output)
{
    [Fact]
    public async Task CatalogoGoldenBackupYRestauracionCompleta()
    {
        var normal=await SessionAsync();
        tests.Check(normal.Project.Id=="2A30AAE9-2228-48ab-BA00-AF0598C88C3B", "Catálogo: draft_id real del proyecto");
        tests.Check(normal.Project.TimelineRegistryId=="3A81E819-70C3-4b5c-8DB8-4D07897271C9", "Catálogo: ID de registro distinto");
        tests.Check(normal.Snapshot.Timeline.Id=="EFA8ACC8-F181-4059-96C5-6BC82353A0C6" && normal.Snapshot.Timeline.IsMain,"Catálogo: timeline real y main");
        tests.Check(normal.Snapshot.Timeline.FrameRate==new FrameRate(30) && normal.Snapshot.Timeline.DurationUs==36_533_333,"FPS y duración reales");
        tests.Check(normal.Snapshot.Tracks.Count==2 && normal.Snapshot.Tracks[1].Segments.Count==1,"Pistas existentes, vídeo vacío y audio");
        tests.Check(normal.Snapshot.Tracks[1].Segments[0].MaterialId=="46EA7246-9A61-4611-951E-B2E93F0715ED","material_id resuelve audio");
        // Usa una sesión independiente para garantizar que el medio no existe.
var missingMedia = await SessionAsync();
var missingDraft = Read(missingMedia.Draft);
var missingPath = Path.Combine(missingMedia.Directory, "audio-inexistente.wav");
missingDraft["materials"]!["audios"]![0]!["path"] = missingPath;
Write(missingMedia.Draft, missingDraft);
var missingSnapshot = await missingMedia.Catalog.ReadTimelineAsync(
    missingMedia.Project, missingMedia.Snapshot.Timeline);

tests.Check(
    missingSnapshot.Tracks[1].Segments[0].SourcePath == missingPath &&
    missingMedia.Catalog.LastWarnings.Any(x =>
        x.Contains("medio ausente", StringComparison.Ordinal)),
    "Ruta ausente conservada con diagnóstico");
        tests.Check(normal.Snapshot.SourceFiles.Count==6 && normal.Snapshot.SourceFiles.Any(f=>!f.Exists && f.Path.EndsWith(".bak",StringComparison.Ordinal)),"Captura incluye .bak ausentes y metadata/registro");
        tests.Check(normal.Project.CoverPath is not null && normal.Snapshot.Timeline.CoverPath is not null,"Covers en catálogo");
        
        using var expected=JsonDocument.Parse(File.ReadAllText(Path.Combine(fixturePath,"expected_captions_v3.json")));
        var initialBytes=normal.Files.ToDictionary(p=>p,p=>File.Exists(p)?File.ReadAllBytes(p):null);
        var result=await normal.GenerateAsync();
        tests.Check(normal.Status()=="ReadyToApply" && normal.Files.All(p=>EqualBytes(p,initialBytes[p])),"Preparar no modifica CapCut");
        tests.Check(File.Exists(Path.Combine(result.Run.RunDirectory,"result","generation.json")) && File.Exists(Path.Combine(result.Run.RunDirectory,"result","subtitles.srt")),"Generación y SRT propios persistidos");
        tests.Check(result.Plan.ExpectedFiles.Count==4,"Espejo raíz coincidente incluye cuatro archivos");
        tests.Check(result.Plan.ManagedSubtitles.Objects.Count==91,"Grafo v3: una pista y 6 identidades por cada uno de 15 captions");
        tests.Check(result.Captions.Count==15,"15 captions mediante Application e Infrastructure");
        for (int i=0;i<15;i++)
        {
         var cue=result.Captions[i];var exp=expected.RootElement[i];var range=exp.GetProperty("range");
         tests.Check(cue.TimelineRange.StartUs==range.GetProperty("start").GetInt64() && cue.TimelineRange.DurationUs==range.GetProperty("duration").GetInt64(),$"Golden {i+1}: rango exacto");
         tests.Check(cue.Text==exp.GetProperty("text").GetString(),$"Golden {i+1}: texto editorial del fixture");
         var words=exp.GetProperty("words");
         tests.Check(cue.Words.Select(x=>x.Text).SequenceEqual(words.GetProperty("text").EnumerateArray().Select(x=>x.GetString())) &&
          cue.Words.Select(x=>x.StartTimeMs).SequenceEqual(words.GetProperty("start_time").EnumerateArray().Select(x=>x.GetInt64())) &&
          cue.Words.Select(x=>x.EndTimeMs).SequenceEqual(words.GetProperty("end_time").EnumerateArray().Select(x=>x.GetInt64())),$"Golden {i+1}: arrays words exactos desde normalizador Whisper.net");
        }
        var receipt=await normal.Service.ApplyAsync(result);
        tests.Check(normal.Status()=="Completed" && receipt.Files.Count==4,"Commit real sobre cuatro archivos de copia");
        tests.Check(File.ReadAllBytes(normal.Draft).SequenceEqual(File.ReadAllBytes(normal.Draft+".bak")) && File.ReadAllBytes(normal.RootDraft).SequenceEqual(File.ReadAllBytes(normal.RootDraft+".bak")),".bak nativos sincronizados al JSON nuevo");
        tests.Check(File.ReadAllBytes(normal.RootDraft).SequenceEqual(File.ReadAllBytes(normal.Draft)),"Espejo sincronizado a la timeline correspondiente");
        tests.Check(receipt.Files.Where(f=>f.Before.Exists).All(f=>File.ReadAllBytes(f.BackupPath!).SequenceEqual(initialBytes[f.Before.Path]!)),"Backup byte a byte del estado inmediatamente anterior");
        var patched=Read(normal.Draft);var original=Read(Path.Combine(fixturePath,"snapshot_audio_only.json"));
        tests.Check(patched["id"]!.GetValue<string>()==original["id"]!.GetValue<string>(),"ID original conservado, sin identidad del golden extranjero");
        foreach (var pair in original)
         if (pair.Key is not ("tracks" or "materials")) tests.Check(JsonNode.DeepEquals(pair.Value,patched[pair.Key]),"Preserva propiedad raíz: "+pair.Key);
        var originalTracks=original["tracks"]!.AsArray();var newTracks=patched["tracks"]!.AsArray();
        tests.Check(originalTracks.All(t=>newTracks.Any(n=>JsonNode.DeepEquals(t,n))),"Pistas originales sin cambios");
        foreach (var pair in original["materials"]!.AsObject())
        {
         var old=pair.Value!.AsArray();var current=patched["materials"]![pair.Key]!.AsArray();
         tests.Check(old.All(o=>current.Any(n=>JsonNode.DeepEquals(o,n))),"Preserva materiales originales: "+pair.Key);
        }
        ValidateGraph(patched,result,tests);
        var managed=await normal.Store.ReadManagedSubtitlesAsync(normal.Project.Id,normal.Snapshot.Timeline.Id);
        tests.Check(managed!.Objects.SequenceEqual(result.Plan.ManagedSubtitles.Objects),"Registro de identidades persiste tras commit");
        tests.Check(Read(receipt.JournalPath)["phase"]!.GetValue<string>()=="Committed","Journal durable confirmado");
        await tests.ErrorAsync(()=>normal.Service.ApplyAsync(result),OperationErrorCode.RunStateConflict,"No reaplica ejecución completada");
        await normal.Writer.RestoreAsync(receipt.JournalPath);
        tests.Check(normal.Files.All(p=>EqualBytes(p,initialBytes[p])),"Restauración exacta: originales anteriores y eliminación de .bak nuevos");
        tests.Check(await normal.Store.ReadManagedSubtitlesAsync(normal.Project.Id,normal.Snapshot.Timeline.Id) is null,"Restaurar primera ejecución elimina su registro propio");
        tests.Check(Read(receipt.JournalPath)["phase"]!.GetValue<string>()=="Restored" && Read(Path.Combine(result.Run.RunDirectory,"run.json"))["restoration"] is not null,"Restauración registrada sin fingir ejecución nueva");
        
    }

    [Fact]
    public async Task TextoAjenoActualizacionDeIdsYRestauracionAnterior()
    {
        var foreign=await SessionAsync("snapshot_with_manual_template.json","root_draft_content.json");
        var beforeForeign=Read(foreign.Draft);var foreignIds=beforeForeign["tracks"]!.AsArray().Select(t=>t!["id"]!.GetValue<string>()).ToArray();
        var foreignResult=await foreign.GenerateAsync();var foreignReceipt=await foreign.Service.ApplyAsync(foreignResult);
        var afterForeign=Read(foreign.Draft);
        tests.Check(foreignResult.Plan.Warnings.Any(x=>x.Contains("ajenos",StringComparison.Ordinal)),"Advierte duplicación visual por captions ajenos");
        tests.Check(beforeForeign["tracks"]!.AsArray().All(t=>afterForeign["tracks"]!.AsArray().Any(n=>JsonNode.DeepEquals(t,n))),"SRT y plantilla manual ajenos preservados completos");
        tests.Check(foreignResult.Plan.ExpectedFiles.Count==4,"Espejo admite la diferencia numérica mínima del fixture real");
        var withUnknowns=Read(foreign.Draft);string firstOwnTextId=foreignResult.Plan.ManagedSubtitles.Objects.First(o=>o.Kind==SubtitleObjectKind.Text).Id;
        var ownText=withUnknowns["materials"]!["texts"]!.AsArray().Single(t=>t!["id"]!.GetValue<string>()==firstOwnTextId)!;
        ownText["future_field"]=new JsonObject {["retain"]=true};var ownContent=JsonNode.Parse(ownText["content"]!.GetValue<string>())!;ownContent["future_content_field"]="retain";ownText["content"]=ownContent.ToJsonString();
        Write(foreign.Draft,withUnknowns);Write(foreign.RootDraft,withUnknowns);
        var firstAppliedBytes=foreign.Files.ToDictionary(p=>p,p=>File.ReadAllBytes(p));
        
        // Una segunda ejecución usa el registro real y conserva los IDs propios al regenerar.
        var firstManaged=foreignResult.Plan.ManagedSubtitles.Objects.ToArray();
        foreign.Snapshot=await foreign.Catalog.ReadTimelineAsync(foreign.Project,foreign.Snapshot.Timeline);foreign.Transcriber.ChangeFirstWord=true;
        var second=await foreign.GenerateAsync();await foreign.Service.ApplyAsync(second);
        tests.Check(second.Run.RunId!=foreignResult.Run.RunId && second.Run.ProjectDirectory==foreignResult.Run.ProjectDirectory,"Carpeta de proyecto reutilizada, run único");
        tests.Check(firstManaged.SequenceEqual(second.Plan.ManagedSubtitles.Objects),"Regeneración conserva todos los IDs administrados");
        tests.Check(Read(foreign.Draft)["tracks"]!.AsArray().Count==beforeForeign["tracks"]!.AsArray().Count+1,"Regenerar no duplica la pista propia");
        tests.Check(second.Captions[0].Text.StartsWith("That",StringComparison.Ordinal),"Actualización cambia texto conservando grafo");
        var updatedText=Read(foreign.Draft)["materials"]!["texts"]!.AsArray().Single(t=>t!["id"]!.GetValue<string>()==firstOwnTextId)!;
        tests.Check(updatedText["future_field"]?["retain"]?.GetValue<bool>()==true && JsonNode.Parse(updatedText["content"]!.GetValue<string>())?["future_content_field"]?.GetValue<string>()=="retain","Actualización conserva campos desconocidos en material propio y JSON content");
        await tests.ErrorAsync(()=>foreign.Writer.RestoreAsync(foreignReceipt.JournalPath),OperationErrorCode.SourceChanged,"No restaura backup anterior sobre una ejecución posterior");
        await foreign.Writer.RestoreAsync(Path.Combine(second.Run.RunDirectory,"journal.json"));
        tests.Check(foreign.Files.All(p=>File.ReadAllBytes(p).SequenceEqual(firstAppliedBytes[p])),"Restauración segunda ejecución recupera exactamente sus cuatro estados anteriores");
        tests.Check((await foreign.Store.ReadManagedSubtitlesAsync(foreign.Project.Id,foreign.Snapshot.Timeline.Id))!.Objects.SequenceEqual(firstManaged),"Restaurar actualización devuelve registro anterior");
        
    }

    [Fact]
    public async Task RaizDeOtraTimelineSeConserva()
    {
        var differentRoot=await SessionAsync();var rootOther=Read(differentRoot.RootDraft);rootOther["id"]="OTHER-TIMELINE";Write(differentRoot.RootDraft,rootOther);
        string registryPath=Path.Combine(differentRoot.Project.DirectoryPath,"Timelines","project.json");
        var registry=Read(registryPath);var entry=registry["timelines"]![0]!.DeepClone();
        entry["id"]="OTHER-TIMELINE";entry["name"]="Other timeline";
        registry["timelines"]!.AsArray().Add(entry);registry["main_timeline_id"]="OTHER-TIMELINE";Write(registryPath,registry);
        differentRoot.Snapshot=await differentRoot.Catalog.ReadTimelineAsync(differentRoot.Project,differentRoot.Snapshot.Timeline);byte[] otherBytes=File.ReadAllBytes(differentRoot.RootDraft);
        var otherPlan=await differentRoot.GenerateAsync();await differentRoot.Service.ApplyAsync(otherPlan);
        tests.Check(otherPlan.Plan.ExpectedFiles.Count==2 && File.ReadAllBytes(differentRoot.RootDraft).SequenceEqual(otherBytes),"Raíz de otra timeline no se toca");
        
    }

    [Fact]
    public async Task BakExistenteSeRespaldaYSeRestaura()
    {
        var existingBak=await SessionAsync();File.WriteAllBytes(existingBak.Draft+".bak",Encoding.UTF8.GetBytes("backup nativo anterior"));File.WriteAllBytes(existingBak.RootDraft+".bak",Encoding.UTF8.GetBytes("otro backup nativo anterior"));
        existingBak.Snapshot=await existingBak.Catalog.ReadTimelineAsync(existingBak.Project,existingBak.Snapshot.Timeline);var bakBytes=existingBak.Files.ToDictionary(p=>p,p=>File.ReadAllBytes(p));
        var bakRun=await existingBak.GenerateAsync();var bakReceipt=await existingBak.Service.ApplyAsync(bakRun);
        tests.Check(bakReceipt.Files.All(f=>f.Before.Exists && File.ReadAllBytes(f.BackupPath!).SequenceEqual(bakBytes[f.Before.Path])),"Respaldos propios incluyen bytes de .bak nativos viejos");
        await existingBak.Writer.RestoreAsync(bakReceipt.JournalPath);tests.Check(existingBak.Files.All(p=>File.ReadAllBytes(p).SequenceEqual(bakBytes[p])),"Restaurar también recupera los .bak nativos anteriores");
        
    }

    [Fact]
    public async Task OriginalAlteradoBloqueaCommit()
    {
        var stale=await SessionAsync();var staleReady=await stale.GenerateAsync();File.AppendAllText(stale.Draft," ");byte[] edited=File.ReadAllBytes(stale.Draft);
        await tests.ErrorAsync(()=>stale.Service.ApplyAsync(staleReady),OperationErrorCode.SourceChanged,"Hashes detectan cambio previo a commit");
        tests.Check(File.ReadAllBytes(stale.Draft).SequenceEqual(edited) && !File.Exists(stale.Draft+".bak"),"Rechazo de hash no escribe ni crea .bak");
    }

    [Fact]
    public async Task PlanAlteradoBloqueaCommit()
    {
        var stalePlan=await SessionAsync();var stalePlanReady=await stalePlan.GenerateAsync();File.AppendAllText(stalePlanReady.Plan.PlanPath," ");
        await tests.ErrorAsync(()=>stalePlan.Service.ApplyAsync(stalePlanReady),OperationErrorCode.SourceChanged,"Detecta modificación del plan durable");
    }

    [Fact]
    public async Task StagingAlteradoBloqueaCommit()
    {
        var staged=await SessionAsync();var stagedReady=await staged.GenerateAsync();var stagedDoc=Read(stagedReady.Plan.PlanPath);File.AppendAllText(stagedDoc["files"]![0]!["stagedPath"]!.GetValue<string>()," ");
        await tests.ErrorAsync(()=>staged.Service.ApplyAsync(stagedReady),OperationErrorCode.SourceChanged,"Detecta copia preparada alterada");
    }

    [Fact]
    public async Task EdicionPosteriorBloqueaRestauracion()
    {
        var changedRestore=await SessionAsync();var changedRun=await changedRestore.GenerateAsync();var changedReceipt=await changedRestore.Service.ApplyAsync(changedRun);File.AppendAllText(changedRestore.Draft," ");
        await tests.ErrorAsync(()=>changedRestore.Writer.RestoreAsync(changedReceipt.JournalPath),OperationErrorCode.SourceChanged,"Restauración rechaza edición posterior");
        
    }

    [Fact]
    public async Task FalloParcialProduceRollback()
    {
        var interrupted=await SessionAsync();var beforeInterrupted=interrupted.Files.ToDictionary(p=>p,p=>File.Exists(p)?File.ReadAllBytes(p):null);
        interrupted.SetWriter(new GoldenV3SubtitleWriter(interrupted.Assets,new ClosedGuard(),new FailCommitter(2)));
        var interruptedRun=await interrupted.GenerateAsync();await tests.ErrorAsync(()=>interrupted.Service.ApplyAsync(interruptedRun),OperationErrorCode.RecoveryRequired,"Fallo en segundo reemplazo exige revisar journal");
        tests.Check(interrupted.Files.All(p=>EqualBytes(p,beforeInterrupted[p])),"Rollback recupera conjunto tras fallo parcial real");
        tests.Check(Read(Path.Combine(interruptedRun.Run.RunDirectory,"journal.json"))["phase"]!.GetValue<string>()=="RolledBack","Journal identifica rollback exitoso");
        
    }

    [Fact]
    public async Task FalloDeRollbackQuedaEnJournal()
    {
        var recovery=await SessionAsync();recovery.SetWriter(new GoldenV3SubtitleWriter(recovery.Assets,new ClosedGuard(),new BreakRollbackCommitter(recovery.Directory)));var recoveryRun=await recovery.GenerateAsync();
        await tests.ErrorAsync(()=>recovery.Service.ApplyAsync(recoveryRun),OperationErrorCode.RecoveryRequired,"Fallo de commit y backup dañado conservan estado incierto");
        tests.Check(Read(Path.Combine(recoveryRun.Run.RunDirectory,"journal.json"))["phase"]!.GetValue<string>()=="RecoveryRequired","Journal persiste fallo de rollback");
        
    }

    [Fact]
    public async Task CapCutAbiertoBloqueaCommit()
    {
        var open=await SessionAsync();open.SetWriter(new GoldenV3SubtitleWriter(open.Assets,new OpenGuard()));var openRun=await open.GenerateAsync();
        await tests.ErrorAsync(()=>open.Service.ApplyAsync(openRun),OperationErrorCode.CapCutOpen,"CapCut abierto bloquea escritura");tests.Check(open.Status()=="ReadyToApply","CapCut abierto conserva plan revisable");
    }

    [Fact]
    public async Task EspejoConMismoIdDiscordanteSeRechaza()
    {
        var invalidMirror=await SessionAsync();var badMirror=Read(invalidMirror.RootDraft);badMirror["duration"]=999;Write(invalidMirror.RootDraft,badMirror);
        invalidMirror.Snapshot=await invalidMirror.Catalog.ReadTimelineAsync(invalidMirror.Project,invalidMirror.Snapshot.Timeline);
        await tests.ErrorAsync(()=>invalidMirror.GenerateAsync(),OperationErrorCode.SourceChanged,"Mismo ID con raíz discordante no se copia ciegamente");
    }

    [Fact]
    public async Task FuenteAusenteImpidePreparar()
    {
        var missingAssets=await SessionAsync();File.Delete(missingAssets.Assets.FontFile);
        await tests.ErrorAsync(()=>missingAssets.GenerateAsync(),OperationErrorCode.ResourceUnavailable,"Fuente ausente no produce plan supuestamente válido");
        
    }

    [Fact]
    public async Task SettingsSeGuardanYRecuperan()
    {
        var settingsStore=new JsonApplicationSettingsStore(Path.Combine(work,"settings","settings.json"));tests.Check((await settingsStore.LoadAsync()).Language=="auto","Ajustes por defecto si no existe archivo");
        var settings=new ApplicationSettings("projects","workspace","model","es",4);await settingsStore.SaveAsync(settings);tests.Check(await settingsStore.LoadAsync()==settings,"Ajustes persistidos y leídos con ctor validado");
    }

    [Fact]
    public async Task CatalogoDiagnosticaCarpetasCorruptas()
    {
        var catalogRoot=Path.Combine(work,"invalid-projects");Directory.CreateDirectory(Path.Combine(catalogRoot,"broken"));File.WriteAllText(Path.Combine(catalogRoot,"broken","draft_meta_info.json"),"broken");
        var badCatalog=new CapCutCatalog();tests.Check((await badCatalog.FindProjectsAsync(catalogRoot)).Count==0 && badCatalog.LastWarnings.Count==1,"Proyecto corrupto diagnosticado sin inventarlo");
        await tests.ThrowsAsync<DirectoryNotFoundException>(()=>badCatalog.FindProjectsAsync(Path.Combine(work,"missing")),"Raíz inexistente requiere ruta alternativa");
    }

    [Fact]
    public async Task EsquemaFuturoSeRechaza()
    {
        var future=await SessionAsync();var futureDoc=Read(future.Draft);futureDoc["version"]=400000;Write(future.Draft,futureDoc);
        tests.Check((await future.Catalog.GetTimelinesAsync(future.Project)).Count==0 && future.Catalog.LastWarnings.Any(w=>w.Contains("esquema",StringComparison.Ordinal)),"Formato futuro no se interpreta ni modifica como si estuviera verificado");
    }

    [Fact]
    public async Task DraftIdDuplicadoSeExcluye()
    {
        var duplicate=await SessionAsync();string secondFolder=Path.Combine(Path.GetDirectoryName(duplicate.Project.DirectoryPath)!,"duplicate");Directory.CreateDirectory(Path.Combine(secondFolder,"Timelines",duplicate.Snapshot.Timeline.Id));
        File.Copy(Path.Combine(duplicate.Project.DirectoryPath,"draft_meta_info.json"),Path.Combine(secondFolder,"draft_meta_info.json"));File.Copy(Path.Combine(duplicate.Project.DirectoryPath,"Timelines","project.json"),Path.Combine(secondFolder,"Timelines","project.json"));File.Copy(duplicate.Draft,Path.Combine(secondFolder,"Timelines",duplicate.Snapshot.Timeline.Id,"draft_content.json"));
        tests.Check((await duplicate.Catalog.FindProjectsAsync(Path.GetDirectoryName(duplicate.Project.DirectoryPath)!)).Count==0 && duplicate.Catalog.LastWarnings.Any(w=>w.Contains("varias carpetas",StringComparison.Ordinal)),"IDs duplicados de proyecto no comparten silenciosamente el espacio propio");
    }

    [Fact]
    public async Task TransicionIlegalYCancelacionNoModificanManifiesto()
    {
        var illegal=await SessionAsync();var created=await illegal.Store.CreateRunAsync(illegal.Request());
        await tests.ErrorAsync(()=>illegal.Store.TransitionAsync(created,new(RunStatus.Created,RunStatus.Applying,DateTimeOffset.UtcNow)),OperationErrorCode.RunStateConflict,"Manifiesto rechaza transición ilegal");
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();await tests.ThrowsAsync<OperationCanceledException>(()=>illegal.Store.TransitionAsync(created,new(RunStatus.Created,RunStatus.PreparingAudio,DateTimeOffset.UtcNow),cancelled.Token),"Estado no cambia con cancelación previa");
        tests.Check(Read(Path.Combine(created.RunDirectory,"run.json"))["status"]!.GetValue<string>()=="Created","Transición cancelada conserva estado anterior");
        
    }

    [Fact]
    public async Task FinalizacionTardiaNoSobrescribeIdsNuevos()
    {
        // Commit de una ejecución y finalización tardía mientras otra ya termina: no pisa el registro posterior.
        var race=await SessionAsync();var delayed=new DelayedCompletionStore(race.Store);
        var delayedService=new CaptionGenerationService(new FixtureAudio(),race.Transcriber,race.Writer,delayed);
        var slowResult=await delayedService.GenerateAsync(race.Request());var slowApply=delayedService.ApplyAsync(slowResult);await delayed.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        tests.Check((await race.Store.ReadManagedSubtitlesAsync(race.Project.Id,race.Snapshot.Timeline.Id)) is not null,"El escritor publica IDs antes de finalizar manifiesto");
        race.Snapshot=await race.Catalog.ReadTimelineAsync(race.Project,race.Snapshot.Timeline);var fastResult=await race.GenerateAsync();await race.Service.ApplyAsync(fastResult);
        delayed.Release.SetResult();await slowApply;
        var latestRegistry=Read(Path.Combine(fastResult.Run.ProjectDirectory,"managed",fastResult.Run.TimelineId+".json"));
        tests.Check(latestRegistry["runId"]!.GetValue<string>()==fastResult.Run.RunId,"Finalización tardía no sobrescribe el registro de la ejecución posterior");
        tests.Check(Read(Path.Combine(slowResult.Run.RunDirectory,"run.json"))["status"]!.GetValue<string>()=="Completed","Ejecución anterior confirma su recibo sin revertir la posterior");
        
    }

    [Fact]
    public async Task TokensNativosUsanDiezMilisegundos()
    {
        // Los tiempos de la API nativa son unidades de 10 ms, independientemente del timestamp DTW diagnóstico.
        var nativeSegment=new SegmentData(" Hello",TimeSpan.Zero,TimeSpan.FromSeconds(1),0,0,0,0,"en",new[] { new WhisperToken {Id=770,Text=" Hello",Start=20,End=60,DtwTimestamp=999999},new WhisperToken {Id=50256,Text="<|endoftext|>",Start=-1,End=-1} });
        var normalized=WhisperTokenNormalizer.Normalize(nativeSegment,true);
        tests.Check(normalized.Tokens[0].Range==new TimeRangeUs(200_000,400_000) && normalized.Tokens[1].IsControl,"Normaliza 10ms→µs y separa controles; no interpreta t_dtw");
        tests.Throws<InvalidDataException>(()=>WhisperTokenNormalizer.Normalize(new SegmentData("bad",TimeSpan.Zero,TimeSpan.FromSeconds(1),0,0,0,0,"en",new[] {new WhisperToken {Id=1,Text=" bad",Start=-1,End=0}}),true),"Token sin offset no recibe tiempo inventado");
        
    }

}
