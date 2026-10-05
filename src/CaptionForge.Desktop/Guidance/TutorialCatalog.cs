namespace CaptionForge.Desktop.Guidance;
public enum TutorialRequirement { None,Project,Timeline,SupportedTemplate,Result }
public sealed record TutorialStep(string Id,int Page,string TargetId,string TitleKey,string BodyKey,TutorialRequirement Requirement=TutorialRequirement.None,string? Feature=null);
/// <summary>Un módulo puede registrar sus pasos antes de iniciar el recorrido. IDs estables preservan el historial.</summary>
public sealed class TutorialCatalog
{
    private readonly List<TutorialStep> _steps=[];
    public IReadOnlyList<TutorialStep> Steps=>_steps.AsReadOnly();
    public void Register(TutorialStep step)
    {
        if(string.IsNullOrWhiteSpace(step.Id) || string.IsNullOrWhiteSpace(step.TargetId) || step.Page is <0 or >3 || _steps.Any(s=>s.Id==step.Id))throw new ArgumentException("Tutorial: invalid/duplicate step");
        _steps.Add(step);
    }
    public static TutorialCatalog CreateDefault()
    {
        var c=new TutorialCatalog();
        void Add(string id,int page,string target,TutorialRequirement condition=TutorialRequirement.None)=>c.Register(new(id,page,target,"tutorial."+id+".title","tutorial."+id+".body",condition));
        Add("projects.folder",0,"projects.folder");Add("projects.choose",0,"projects.choose",TutorialRequirement.Project);
        Add("timelines.choose",1,"timelines.choose",TutorialRequirement.Timeline);
        Add("audio.clips",2,"audio.clips");Add("audio.preview",2,"audio.preview");
        Add("template.choose",2,"template.choose",TutorialRequirement.SupportedTemplate);Add("whisper.options",2,"whisper.options");
        Add("captions.generate",2,"captions.generate",TutorialRequirement.Result);Add("captions.review",3,"captions.review");
        Add("captions.apply",3,"captions.apply");Add("backups.restore",3,"backups.restore");Add("settings.open",3,"settings.open");
        return c;
    }
}
public sealed class TutorialSession
{
    private readonly IReadOnlyList<TutorialStep> _steps;
    private int _index;
    public bool Active {get;private set;}
    public TutorialStep? Current=>Active && _index<_steps.Count?_steps[_index]:null;
    public int Position=>_index+1;
    public int Count=>_steps.Count;
    public bool CanPrevious=>Active && _index>0;
    public TutorialSession(IEnumerable<TutorialStep> steps)=>_steps=steps.ToArray();
    public void Start(){_index=0;Active=_steps.Count>0;}
    public void Stop()=>Active=false;
    public bool Next(bool requirementSatisfied,Action<string> complete)
    {
        if(Current is null || !requirementSatisfied)return false;
        complete(Current.Id);_index++;if(_index>=_steps.Count)Active=false;return true;
    }
    public void Previous(){if(CanPrevious)_index--;}
    public void FollowPage(int page)
    {
        if(Current?.Page==page)return;
        int found=Enumerable.Range(0,_steps.Count).FirstOrDefault(i=>_steps[i].Page==page,-1);
        if(found>=0)_index=found;
    }
}
public readonly record struct TutorialRectangle(double X,double Y,double Width,double Height);
public static class TutorialPlacement
{
    public static TutorialRectangle Place(double width,double height,double cardWidth,double cardHeight,TutorialRectangle target)
    {
        double w=Math.Min(cardWidth,Math.Max(1,width-16)),h=Math.Min(cardHeight,Math.Max(1,height-16));
        double right=Math.Max(8,width-w-8),bottom=Math.Max(8,height-h-8);
        var corners=new[]{new TutorialRectangle(right,bottom,w,h),new TutorialRectangle(8,bottom,w,h),new TutorialRectangle(right,8,w,h),new TutorialRectangle(8,8,w,h)};
        double Overlap(TutorialRectangle a)=>Math.Max(0,Math.Min(a.X+a.Width,target.X+target.Width)-Math.Max(a.X,target.X))*Math.Max(0,Math.Min(a.Y+a.Height,target.Y+target.Height)-Math.Max(a.Y,target.Y));
        return corners.OrderBy(Overlap).First();
    }
}
