using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Input;
using CaptionForge.Application.Enums;
using CaptionForge.Application.Models.CapCut;
using CaptionForge.Application.Models.Generation;
using CaptionForge.Application.Models.Settings;
using CaptionForge.Application.Services;
using CaptionForge.Desktop.Mvvm;
using CaptionForge.Desktop.Services;
using CaptionForge.Infrastructure.CapCut;
using CaptionForge.Infrastructure.Configuration;
using CaptionForge.Infrastructure.Media;
using CaptionForge.Infrastructure.Settings;
using CaptionForge.Infrastructure.Transcription;
using CaptionForge.Infrastructure.Workspace;
namespace CaptionForge.Desktop.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly IDesktopDialogs _dialogs;
    private readonly CapCutCatalog _catalog=new();
    private readonly JsonApplicationSettingsStore _settings=new();
    private readonly WhisperNetTranscriptionService _whisper=new();
    private TimelineSnapshot? _snapshot;
    private CaptionGenerationService? _generation;
    private DraftTemplateSubtitleWriter? _writer;
    private GenerationResult? _result;
    private CancellationTokenSource? _cts;
    private TaskCompletionSource? _operationDone;
    private bool _busy,_applied,_failedApply;
    private int _step,_threads=CpuThreadRecommendation.Suggest(Environment.ProcessorCount);
    private double _progress;
    private string _lastRunDirectory="";
    private string _status="Listo",_message="",_projectFilter="",_progressLabel="",_model="",_root=InfrastructurePaths.DefaultCapCutProjects,
        _workspace=InfrastructurePaths.DefaultWorkspace,_ffmpeg=EnginePath("ffmpeg"),_ffprobe=EnginePath("ffprobe");
    private ProjectItem? _project;
    private TimelineItem? _timeline;
    private DraftTemplateCandidate? _template;
    private LanguageItem _language=new("auto","Detectar automáticamente");
    private BackupItem? _backup;
    public ObservableCollection<ProjectItem> Projects {get;}=[];
    public ObservableCollection<ProjectItem> VisibleProjects {get;}=[];
    public ObservableCollection<TimelineItem> Timelines {get;}=[];
    public ObservableCollection<AudioSegmentItem> Segments {get;}=[];
    public ObservableCollection<DraftTemplateCandidate> Templates {get;}=[];
    public ObservableCollection<CaptionItem> Captions {get;}=[];
    public ObservableCollection<string> Warnings {get;}=[];
    public ObservableCollection<string> TargetFiles {get;}=[];
    public ObservableCollection<BackupItem> Backups {get;}=[];
    public AudioPreviewService Preview {get;}
    public AppearanceViewModel Appearance {get;}
    public IReadOnlyList<int> ThreadChoices {get;}=Enumerable.Range(1,Environment.ProcessorCount).ToArray();
    public IReadOnlyList<LanguageItem> Languages {get;}=[new("auto","Detectar automáticamente"),new("es","Español"),new("en","Inglés"),new("pt","Portugués"),new("fr","Francés"),new("de","Alemán"),new("it","Italiano")];
    public string CpuHint=>$"{Environment.ProcessorCount} hilos lógicos · sugeridos: {CpuThreadRecommendation.Suggest(Environment.ProcessorCount)}";
    public int Step {get=>_step;private set{if(Set(ref _step,value)){Preview.Stop();Raise(nameof(Title));Raise(nameof(Subtitle));Raise(nameof(NextLabel));RefreshCommands();}}}
    public string Title=>Step switch{0=>"Tus proyectos",1=>"Elige la timeline",2=>"Prepara tus subtítulos",_=>"Revisa el resultado"};
    public string Subtitle=>Step switch{0=>"Encuentra el proyecto de CapCut con el que quieres trabajar.",1=>"Selecciona la timeline. El pin de CapCut se conserva.",2=>"Escoge el molde que ya configuraste en CapCut y los fragmentos que quieres transcribir.",_=>"Comprueba el texto, aplica el resultado o restaura una ejecución anterior."};
    public string NextLabel=>Step==0?"Elegir timeline →":"Configurar subtítulos →";
    public bool IsBusy {get=>_busy;private set{if(Set(ref _busy,value)){Raise(nameof(IsIdle));RefreshCommands();}}}
    public bool IsIdle=>!IsBusy;
    public string Status {get=>_status;private set=>Set(ref _status,value);}
    public string Message {get=>_message;private set=>Set(ref _message,value);}
    public string ProgressLabel {get=>_progressLabel;private set=>Set(ref _progressLabel,value);}
    public double Progress {get=>_progress;private set=>Set(ref _progress,value);}
    public string ProjectsRoot {get=>_root;set{if(Set(ref _root,value)){SelectedProject=null;Projects.Clear();VisibleProjects.Clear();Raise(nameof(ProjectCount));}}}
    public string WorkspaceRoot {get=>_workspace;set{if(Set(ref _workspace,value)){InvalidateResult();Backups.Clear();SelectedBackup=null;}}}
    public string ModelPath {get=>_model;set{if(Set(ref _model,value))InvalidateResult();}}
    public string FfmpegPath {get=>_ffmpeg;set{if(Set(ref _ffmpeg,value))InvalidateResult();}}
    public string FfprobePath {get=>_ffprobe;set{if(Set(ref _ffprobe,value))InvalidateResult();}}
    public int CpuThreads {get=>_threads;set{if(Set(ref _threads,Math.Clamp(value,1,Environment.ProcessorCount)))InvalidateResult();}}
    public LanguageItem SelectedLanguage {get=>_language;set{if(value is not null && Set(ref _language,value))InvalidateResult();}}
    public string ProjectFilter {get=>_projectFilter;set{if(Set(ref _projectFilter,value))FilterProjects();}}
    public string ProjectCount=>$"{VisibleProjects.Count} proyectos";
    public string SelectedSummary=>$"{SelectedProject?.Name ?? "Sin proyecto"} / {SelectedTimeline?.Name ?? "Sin timeline"}";
    public string SelectedSegmentsSummary=>$"{Segments.Count(s=>s.Included)} de {Segments.Count} fragmentos incluidos";
    public string TemplateDetail=>SelectedTemplate is null?"Añade una plantilla de subtítulos en CapCut y vuelve a cargar la timeline.":
        SelectedTemplate.UnsupportedReason ?? $"Pista {SelectedTemplate.TrackNumber} · {SelectedTemplate.TextLayerCount} capa(s) · recurso {SelectedTemplate.ResourceId}";
    public ProjectItem? SelectedProject {get=>_project;set{if(Set(ref _project,value)){SelectedTimeline=null;Timelines.Clear();Raise(nameof(SelectedSummary));RefreshCommands();}}}
    public TimelineItem? SelectedTimeline {get=>_timeline;set{if(Set(ref _timeline,value)){ClearTimeline();Raise(nameof(SelectedSummary));RefreshCommands();}}}
    public DraftTemplateCandidate? SelectedTemplate {get=>_template;set{if(Set(ref _template,value)){InvalidateResult();Raise(nameof(TemplateDetail));}}}
    public BackupItem? SelectedBackup {get=>_backup;set{if(Set(ref _backup,value))RefreshCommands();}}
    public string ResultSummary=>_result is null?"Todavía no hay subtítulos generados":$"{_result.Captions.Count} subtítulos · {_result.Transcriptions.Count} fragmentos · {_result.Plan.ExpectedFiles.Count} archivos";
    public string RunDirectory=>_result?.Run.RunDirectory ?? (!string.IsNullOrEmpty(_lastRunDirectory)?_lastRunDirectory:SelectedBackup is not null?Path.GetDirectoryName(SelectedBackup.JournalPath)!:"");
    public bool HasResult=>_result is not null;
    public bool Applied=>_applied;
    public ICommand RefreshProjects {get;}
    public ICommand BrowseProjects {get;}
    public ICommand BrowseWorkspace {get;}
    public ICommand BrowseModel {get;}
    public ICommand BrowseFfmpeg {get;}
    public ICommand BrowseFfprobe {get;}
    public ICommand Next {get;}
    public ICommand Back {get;}
    public ICommand Navigate {get;}
    public ICommand ReloadTimeline {get;}
    public ICommand Generate {get;}
    public ICommand Apply {get;}
    public ICommand Cancel {get;}
    public ICommand ExportSrt {get;}
    public ICommand OpenArtifacts {get;}
    public ICommand Restore {get;}
    public ICommand RefreshBackups {get;}
    public ICommand LocateSource {get;}
    public ICommand PlaySegment {get;}
    public ICommand TogglePreview {get;}
    public ICommand StopPreview {get;}
    public ICommand IncludeAll {get;}
    public ICommand ExcludeAll {get;}

    public MainViewModel(IDesktopDialogs dialogs,AudioPreviewService preview,AppearanceViewModel? appearance=null)
    {
        _dialogs=dialogs;Preview=preview;Appearance=appearance ?? new AppearanceViewModel();
        AsyncCommand Async(Func<object?,Task> action,Predicate<object?>? allowed=null)=>new(action,p=>IsIdle && (allowed?.Invoke(p) ?? true),ReportError);
        RefreshProjects=Async(_=>RunAsync(LoadProjectsAsync));
        BrowseProjects=Async(async _=>{var path=_dialogs.Folder("Carpeta que contiene los proyectos de CapCut",ProjectsRoot);if(path is null)return;ProjectsRoot=path;await RunAsync(LoadProjectsAsync);});
        BrowseWorkspace=new RelayCommand(_=>{var path=_dialogs.Folder("Carpeta de trabajo de CaptionForge",WorkspaceRoot);if(path is not null)WorkspaceRoot=path;},_=>IsIdle);
        BrowseModel=new RelayCommand(_=>{var path=_dialogs.File("Selecciona un modelo Whisper GGML","Modelo Whisper (*.bin)|*.bin");if(path is not null)ModelPath=path;},_=>IsIdle);
        BrowseFfmpeg=new RelayCommand(_=>{var p=_dialogs.File("Localizar FFmpeg","FFmpeg (ffmpeg.exe)|ffmpeg.exe");if(p is not null)FfmpegPath=p;},_=>IsIdle);
        BrowseFfprobe=new RelayCommand(_=>{var p=_dialogs.File("Localizar FFprobe","FFprobe (ffprobe.exe)|ffprobe.exe");if(p is not null)FfprobePath=p;},_=>IsIdle);
        Next=Async(_=>RunAsync(AdvanceAsync),_=>Step==0?SelectedProject is not null:Step==1 && SelectedTimeline is not null);
        Back=new RelayCommand(_=>Step--,_=>IsIdle && Step>0);
        Navigate=new RelayCommand(p=>{if(int.TryParse(p?.ToString(),out var step))Step=step;},p=>IsIdle && int.TryParse(p?.ToString(),out var step) && CanVisit(step));
        ReloadTimeline=Async(_=>RunAsync(LoadSnapshotAsync),_=>SelectedProject is not null && SelectedTimeline is not null);
        Generate=Async(_=>RunAsync(GenerateAsync),_=>_snapshot is not null && SelectedTemplate?.IsSupported==true && Segments.Any(s=>s.Included) && File.Exists(ModelPath));
        Apply=Async(_=>RunAsync(ApplyAsync),_=>_result is not null && !_applied && !_failedApply);
        Cancel=new RelayCommand(_=>_cts?.Cancel(),_=>IsBusy);
        ExportSrt=Async(async _=>{var path=_dialogs.SaveFile("Exportar subtítulos","SubRip (*.srt)|*.srt","subtitulos.srt");if(path is not null && _result is not null)await File.WriteAllTextAsync(path,SrtFormatter.Format(_result.Captions),new System.Text.UTF8Encoding(false));},_=>_result is not null);
        OpenArtifacts=new RelayCommand(_=>{try{_dialogs.OpenFolder(RunDirectory);}catch(Exception ex){ReportError(ex);}},_=>IsIdle && Directory.Exists(RunDirectory));
        RefreshBackups=Async(_=>RunAsync(LoadBackupsAsync),_=>_snapshot is not null);
        Restore=Async(_=>RunAsync(RestoreAsync),_=>SelectedBackup is not null && SelectedBackup.Phase is not ("Restored" or "RolledBack"));
        LocateSource=new RelayCommand(p=>{if(p is AudioSegmentItem item){var path=_dialogs.File("Localizar medio original","Audio y vídeo|*.mp3;*.wav;*.m4a;*.aac;*.flac;*.mp4;*.mov;*.mkv;*.webm|Todos los archivos|*.*");if(path is not null)item.OverridePath=path;}},_=>IsIdle);
        PlaySegment=new RelayCommand(p=>{if(p is AudioSegmentItem item)try{Preview.Play(item.ResolvedPath,item.Model.SourceRange.StartUs,item.Model.SourceRange.DurationUs,item.Name);}catch(Exception ex){ReportError(ex);}},p=>IsIdle && p is AudioSegmentItem item && item.CanInclude);
        TogglePreview=new RelayCommand(_=>Preview.Toggle(),_=>IsIdle);
        StopPreview=new RelayCommand(_=>Preview.Stop());
        IncludeAll=new RelayCommand(_=>{foreach(var item in Segments)item.Included=true;},_=>IsIdle);
        ExcludeAll=new RelayCommand(_=>{foreach(var item in Segments)item.Included=false;},_=>IsIdle);
    }
    private static string EnginePath(string name)
    {string local=Path.Combine(AppContext.BaseDirectory,"Tools",name+".exe");return File.Exists(local)?local:name;}
    private bool CanVisit(int step)=>step switch{0=>true,1=>SelectedProject is not null && Timelines.Count>0,2=>_snapshot is not null,3=>_snapshot is not null,_=>false};
    public Task InitializeAsync()=>RunAsync(async()=>
    {
        try
        {
            var saved=await _settings.LoadAsync(_cts!.Token);ProjectsRoot=saved.ProjectsRoot ?? InfrastructurePaths.DefaultCapCutProjects;
            WorkspaceRoot=saved.WorkspaceRoot ?? InfrastructurePaths.DefaultWorkspace;ModelPath=saved.ModelPath ?? "";
            CpuThreads=CpuThreadRecommendation.ResolveSaved(saved.CpuThreads,Environment.ProcessorCount);
            SelectedLanguage=Languages.FirstOrDefault(l=>l.Code==saved.Language) ?? Languages[0];
            var tools=await DesktopToolSettingsStore.LoadAsync(_cts!.Token);if(tools is not null){FfmpegPath=tools.FfmpegPath;FfprobePath=tools.FfprobePath;}
        }
        catch(Exception ex){AppLog.Write(ex);Message="No se pudieron leer las preferencias. Puedes configurar las rutas de nuevo.";}
        if(Directory.Exists(ProjectsRoot))await LoadProjectsAsync();else Message="No se encontró la carpeta predeterminada. Usa Buscar carpeta para indicar dónde están tus proyectos.";
    });
    private async Task RunAsync(Func<Task> operation)
    {
        if(IsBusy)return;_operationDone=new(TaskCreationOptions.RunContinuationsAsynchronously);_cts=new();IsBusy=true;Preview.Stop();Message="";
        try{await operation();}
        catch(OperationCanceledException){Status="Cancelado";Message="La operación se canceló. Los artefactos conservados están en la carpeta de trabajo.";}
        catch(Exception ex){ReportError(ex);}
        finally{_cts.Dispose();_cts=null;IsBusy=false;_operationDone.TrySetResult();}
    }
    public void ReportError(Exception ex)
    {
        AppLog.Write(ex);Message=ex.Message;Status="Revisa el diagnóstico";
        if(ex is CaptionForge.Application.Exceptions.CaptionForgeOperationException {RunId:not null} op && _snapshot is not null)
            _lastRunDirectory=Path.Combine(WorkspaceRoot,_snapshot.Project.Id,"runs",op.RunId);
        Raise(nameof(RunDirectory));RefreshCommands();
    }
    private void RefreshCommands()=>CommandManager.InvalidateRequerySuggested();
    private void FilterProjects()
    {
        VisibleProjects.Clear();foreach(var item in Projects.Where(p=>p.Name.Contains(ProjectFilter,StringComparison.CurrentCultureIgnoreCase)))VisibleProjects.Add(item);
        if(SelectedProject is not null && !VisibleProjects.Contains(SelectedProject))SelectedProject=null;
        Raise(nameof(ProjectCount));
    }
    private async Task LoadProjectsAsync()
    {
        Status="Buscando proyectos…";var projects=await Task.Run(()=>_catalog.FindProjectsAsync(ProjectsRoot,_cts!.Token));
        SelectedProject=null;Projects.Clear();foreach(var p in projects.OrderByDescending(p=>p.LastModifiedAt))Projects.Add(new(p));FilterProjects();
        Message=_catalog.LastWarnings.Count>0?string.Join("\n",_catalog.LastWarnings):projects.Count==0?"No hay proyectos válidos en esta carpeta. Selecciona la carpeta que los contiene.":"";
        await SaveSettingsAsync();Status="Proyectos cargados";
    }
    private async Task SaveSettingsAsync()
    {
        await _settings.SaveAsync(new(ProjectsRoot,WorkspaceRoot,string.IsNullOrWhiteSpace(ModelPath)?null:ModelPath,SelectedLanguage.Code,CpuThreads),_cts?.Token ?? default);
        await DesktopToolSettingsStore.SaveAsync(new(FfmpegPath,FfprobePath),_cts?.Token ?? default);
    }
    private async Task AdvanceAsync()
    {
        if(Step==0 && SelectedProject is not null)
        {
            var timelines=await Task.Run(()=>_catalog.GetTimelinesAsync(SelectedProject.Model,_cts!.Token));
            Timelines.Clear();foreach(var t in timelines)Timelines.Add(new(t));SelectedTimeline=null;
            if(Timelines.Count==1)SelectedTimeline=Timelines[0];Step=1;Status="Timelines cargadas";
        }
        else if(Step==1){await LoadSnapshotAsync();Step=2;}
    }
    private void ClearTimeline()
    {
        _snapshot=null;foreach(var item in Segments)item.PropertyChanged-=SegmentChanged;
        Segments.Clear();Templates.Clear();SelectedTemplate=null;Backups.Clear();SelectedBackup=null;InvalidateResult();Raise(nameof(SelectedSegmentsSummary));
    }
    private async Task LoadSnapshotAsync()
    {
        if(SelectedProject is null || SelectedTimeline is null)return;
        ClearTimeline();Status="Leyendo timeline…";
        _snapshot=await Task.Run(()=>_catalog.ReadTimelineAsync(SelectedProject.Model,SelectedTimeline.Model,_cts!.Token));
        var managed=await new JsonWorkspaceStore(WorkspaceRoot).ReadManagedSubtitlesAsync(_snapshot.Project.Id,_snapshot.Timeline.Id,_cts!.Token);
        // The first generated block keeps the chosen seed and can be reused as
        // the template on the next run. Hide only the other generated phrases.
        var candidates=await DraftTemplateCatalog.ReadAsync(_snapshot,managed?.Objects.Where(o=>o.Kind==SubtitleObjectKind.Segment).Skip(1).Select(o=>o.Id),_cts!.Token);
        foreach(var item in candidates)Templates.Add(item);
        if(Templates.Count==1 && Templates[0].IsSupported)SelectedTemplate=Templates[0];
        int trackNumber=0;foreach(var track in _snapshot.Tracks)
        {
            trackNumber++;foreach(var segment in track.Segments)
            {var item=new AudioSegmentItem(segment,$"{track.Type} · pista {trackNumber}");item.PropertyChanged+=SegmentChanged;Segments.Add(item);}
        }
        Warnings.Clear();foreach(string warning in _catalog.LastWarnings)Warnings.Add(warning);
        Raise(nameof(SelectedSegmentsSummary));await LoadBackupsAsync();Status="Timeline cargada";
        if(Templates.Count==0)Message="Esta timeline no tiene un subtítulo con plantilla. Añádelo en CapCut, guarda y usa Volver a cargar.";
    }
    private void SegmentChanged(object? sender,PropertyChangedEventArgs e)
    {
        if(e.PropertyName is nameof(AudioSegmentItem.Included) or nameof(AudioSegmentItem.OverridePath)){InvalidateResult();Raise(nameof(SelectedSegmentsSummary));}
    }
    private void InvalidateResult()
    {
        _result=null;_lastRunDirectory="";_applied=false;_failedApply=false;Captions.Clear();TargetFiles.Clear();
        Raise(nameof(HasResult));Raise(nameof(Applied));Raise(nameof(ResultSummary));Raise(nameof(RunDirectory));RefreshCommands();
    }
    private async Task GenerateAsync()
    {
        if(_snapshot is null || SelectedTemplate is null)return;
        var included=Segments.Where(s=>s.Included).ToArray();
        if(included.Any(s=>!File.Exists(s.ResolvedPath)))throw new FileNotFoundException("Localiza los medios ausentes antes de generar.");
        await DraftTemplateCatalog.ValidateResourcesAsync(_snapshot, SelectedTemplate.SegmentId, _cts!.Token);
        await SaveSettingsAsync();InvalidateResult();_writer=new(SelectedTemplate.SegmentId);
        _generation=new(new FfmpegAudioPreparationService(FfmpegPath,FfprobePath),_whisper,_writer,new JsonWorkspaceStore(WorkspaceRoot));
        var options=new TranscriptionOptions(Path.GetFileNameWithoutExtension(ModelPath),Path.GetFullPath(ModelPath),SelectedLanguage.Code,CpuThreads,Environment.ProcessorCount);
        var request=new GenerateCaptionsRequest(_snapshot,included.Select(s=>s.Id),options,WorkspaceRoot,
            included.Where(s=>s.OverridePath is not null).Select(s=>new SourcePathOverride(s.Id,s.ResolvedPath)));
        var progress=new Progress<GenerationProgress>(p=>
        {
            _lastRunDirectory=Path.Combine(WorkspaceRoot,_snapshot.Project.Id,"runs",p.RunId);Raise(nameof(RunDirectory));
            Progress=100.0*p.CompletedSegments/p.TotalSegments;
            string stage=p.Stage switch {RunStatus.PreparingAudio=>"Preparando audio",RunStatus.Transcribing=>"Transcribiendo con Whisper",RunStatus.PreparingSubtitles=>"Preparando el draft",RunStatus.ReadyToApply=>"Listo para aplicar",_=>p.Stage.ToString()};
            ProgressLabel=$"{stage} · {p.CompletedSegments}/{p.TotalSegments} fragmentos completados";Status=stage;
        });
        Step=3;Progress=0;ProgressLabel="Preparando generación…";
        _result=await Task.Run(()=>_generation.GenerateAsync(request,progress,_cts!.Token));
        foreach(var cue in _result.Captions)Captions.Add(new(cue));TargetFiles.Clear();foreach(var file in _result.Plan.ExpectedFiles)TargetFiles.Add(file.Path);
        Warnings.Clear();foreach(var warning in _result.Plan.Warnings)Warnings.Add(warning);
        Raise(nameof(HasResult));Raise(nameof(ResultSummary));Raise(nameof(RunDirectory));Status="Resultado preparado";Progress=100;
    }
    private async Task ApplyAsync()
    {
        if(_result is null || _generation is null)return;
        if(_result.Plan.OverwriteInfo is { RequiresConfirmation:true } overwrite &&
            !_dialogs.Confirm("Sobrescribir la pista seleccionada",
                $"Plantilla: {SelectedTemplate?.Name ?? "seleccionada"}\n\n{overwrite.Message}\n\nSe guardará un backup antes de aplicar. ¿Quieres continuar?"))
        { Status="Aplicación pendiente";Message="Los subtítulos preparados se conservan. No se ha modificado CapCut.";return; }
        Status="Aplicando y guardando backups…";
        try{await Task.Run(()=>_generation.ApplyAsync(_result,_cts!.Token));_applied=true;Raise(nameof(Applied));Status="Subtítulos aplicados";Message="Abre el proyecto en CapCut para comprobar el resultado.";}
        catch(Exception ex){_failedApply=ex is not CaptionForge.Application.Exceptions.CaptionForgeOperationException {Code:OperationErrorCode.CapCutOpen or OperationErrorCode.ResourceUnavailable};throw;}
        finally{await LoadBackupsAsync();}
    }
    private async Task LoadBackupsAsync()
    {
        Backups.Clear();SelectedBackup=null;if(_snapshot is null)return;
        string folder=Path.Combine(WorkspaceRoot,_snapshot.Project.Id,"runs");if(!Directory.Exists(folder))return;
        var items=await Task.Run(()=>
        {
            var list=new List<BackupItem>();
            foreach(string runFolder in Directory.EnumerateDirectories(folder))
            {
                _cts?.Token.ThrowIfCancellationRequested();string path=Path.Combine(runFolder,"journal.json");if(!File.Exists(path))continue;
                try{var d=JsonNode.Parse(File.ReadAllBytes(path))!;if(d["run"]?["timelineId"]?.GetValue<string>()!=_snapshot.Timeline.Id)continue;
                    string phase=d["phase"]?.GetValue<string>() ?? "Desconocido";string created=d["run"]?["createdAt"]?.GetValue<string>() ?? Path.GetFileName(runFolder);
                    if(DateTimeOffset.TryParse(created,out var at))created=at.ToLocalTime().ToString("dd MMM yyyy HH:mm:ss");
                    list.Add(new(path,$"{created} · {phase}",phase));}
                catch(Exception ex) when(ex is IOException or JsonException or InvalidOperationException){AppLog.Write(ex);}
            }
            return list.OrderByDescending(b=>File.GetLastWriteTimeUtc(b.JournalPath)).ToArray();
        });
        foreach(var item in items)Backups.Add(item);SelectedBackup=Backups.FirstOrDefault();Raise(nameof(RunDirectory));
    }
    private async Task RestoreAsync()
    {
        if(SelectedBackup is null)return;
        if(!_dialogs.Confirm("Restaurar backup","Se restaurará el estado anterior de los archivos de esta ejecución. CapCut debe estar cerrado. ¿Continuar?"))return;
        string journal=SelectedBackup.JournalPath;var writer=new DraftTemplateSubtitleWriter(SelectedTemplate?.SegmentId ?? "restore-only");
        await Task.Run(()=>writer.RestoreAsync(journal,_cts!.Token));await LoadSnapshotAsync();Status="Backup restaurado";Message="Se restauraron los archivos respaldados. La timeline se ha vuelto a cargar.";
    }
    public async Task ShutdownAsync()
    {
        _cts?.Cancel();if(_operationDone is not null)await _operationDone.Task;Preview.Dispose();await _whisper.DisposeAsync();
    }
}
