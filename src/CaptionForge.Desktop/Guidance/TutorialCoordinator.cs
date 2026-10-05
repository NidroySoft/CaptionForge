using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CaptionForge.Desktop.Controls;
using CaptionForge.Desktop.Localization;
using CaptionForge.Desktop.ViewModels;
namespace CaptionForge.Desktop.Guidance;
public sealed class TutorialCoordinator : IDisposable
{
    private readonly FrameworkElement _root;
    private readonly TutorialOverlay _overlay;
    private readonly MainViewModel _vm;
    private readonly TutorialCatalog _catalog;
    private TutorialSession? _session;
    private string? _lastTarget;
    private string? _lastPresentation;
    private readonly Func<string,bool> _featureAvailable;
    private bool _queued,_disposed;
    public TutorialCoordinator(FrameworkElement root,TutorialOverlay overlay,MainViewModel vm,TutorialCatalog catalog,Func<string,bool>? featureAvailable=null)
    {
        _root=root;_overlay=overlay;_vm=vm;_catalog=catalog;_featureAvailable=featureAvailable ?? (_=>false);
        _overlay.NextRequested+=Next;_overlay.PreviousRequested+=Previous;_overlay.SkipRequested+=Skip;
        _root.LayoutUpdated+=LayoutChanged;_vm.PropertyChanged+=ModelChanged;
        LocalizationService.Current.LanguageChanged+=LanguageChanged;
    }
    public IReadOnlySet<string> UnknownSteps=>_catalog.Steps.Where(s=>(s.Feature is null || _featureAvailable(s.Feature)) && !LocalizationService.Current.KnownSteps.Contains(s.Id)).Select(s=>s.Id).ToHashSet(StringComparer.Ordinal);
    public bool HasNewSteps=>_catalog.Steps.Any(s=>(s.Feature is null || _featureAvailable(s.Feature)) && !LocalizationService.Current.CompletedSteps.Contains(s.Id));
    public void Start(bool onlyNew=false,IReadOnlySet<string>? stepIds=null)
    {
        var steps=_catalog.Steps.Where(s=>(s.Feature is null || _featureAvailable(s.Feature)) && (!onlyNew || !LocalizationService.Current.CompletedSteps.Contains(s.Id)) && (stepIds is null || stepIds.Contains(s.Id))).ToArray();
        _session=new(steps);_session.Start();_lastTarget=null;_lastPresentation=null;
        if(_session.Current is { } first && !_vm.IsBusy)_vm.Navigate.Execute(first.Page.ToString());
        Schedule();
    }
    private bool OnRequiredPage(TutorialStep step)=>step.Page==_vm.Step || step.Requirement==TutorialRequirement.Result && _vm.Step==3;
    private bool Ready(TutorialStep step)=>step.Requirement switch
    {
        TutorialRequirement.Project=>_vm.SelectedProject is not null,
        TutorialRequirement.Timeline=>_vm.SelectedTimeline is not null,
        TutorialRequirement.SupportedTemplate=>_vm.SelectedTemplate?.IsSupported==true,
        TutorialRequirement.Result=>_vm.HasResult,
        _=>true
    };
    private void Next(object? sender,EventArgs e)
    {
        if(_vm.IsBusy || _session?.Current is not { } step || !Ready(step) || !OnRequiredPage(step))return;
        _session.Next(true,LocalizationService.Current.CompleteStep);_lastTarget=null;
        if(_session.Current is { } next && next.Page!=_vm.Step && _vm.Next.CanExecute(null))
        {
            _vm.Next.Execute(null); // Existing navigation loads timelines/snapshot; no generation/application is automated.
        }
        Schedule();
    }
    private void Previous(object? sender,EventArgs e)
    {
        if(_vm.IsBusy || _session is null)return;_session.Previous();_lastTarget=null;
        if(_session.Current is {} previous && previous.Page!=_vm.Step)_vm.Navigate.Execute(previous.Page.ToString());
        Schedule();
    }
    private void Skip(object? sender,EventArgs e){_session?.Stop();_overlay.Visibility=Visibility.Collapsed;}
    private void ModelChanged(object? sender,PropertyChangedEventArgs e)
    {
        if(e.PropertyName==nameof(MainViewModel.Step) && _session?.Current is {} current && current.Page!=_vm.Step && current.Requirement!=TutorialRequirement.Result)
            _session.FollowPage(_vm.Step);
        Schedule();
    }
    private void LanguageChanged(object? sender,EventArgs e)=>Schedule();
    private void LayoutChanged(object? sender,EventArgs e)=>Schedule();
    private void Schedule()
    {
        if(_disposed || _queued || _session?.Active!=true)
        {if(_session?.Active!=true)_overlay.Visibility=Visibility.Collapsed;return;}
        _queued=true;
        _root.Dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>{_queued=false;if(!_disposed)Refresh();}));
    }
    private void Refresh()
    {
        if(_session?.Current is not {} step){_overlay.Visibility=Visibility.Collapsed;return;}
        _overlay.Visibility=Visibility.Visible;
        var target=FindTarget(_root,step.TargetId);
        if(target is not null && _lastTarget!=step.Id){_lastTarget=step.Id;target.BringIntoView();}
        Rect bounds=Rect.Empty;
        if(target is not null && target.IsVisible && target.ActualWidth>0)
        {
            try
            {
                bounds=target.TransformToVisual(_overlay).TransformBounds(new Rect(target.RenderSize));
                // Keep the highlight inside the visible clip of every parent scroll viewer.
                for(DependencyObject? p=VisualTreeHelper.GetParent(target);p is not null && p!=_root;p=VisualTreeHelper.GetParent(p))
                    if(p is ScrollViewer scroll){var clip=scroll.TransformToVisual(_overlay).TransformBounds(new Rect(scroll.RenderSize));bounds.Intersect(clip);}
                bounds.Intersect(new Rect(_overlay.RenderSize));
            }
            catch(InvalidOperationException){bounds=Rect.Empty;}
        }
        string condition=!OnRequiredPage(step)?L.T("tutorial.targetPending"):_vm.IsBusy?L.T("tutorial.busy"):!Ready(step)?L.T("tutorial.requirement."+step.Requirement):bounds.IsEmpty?L.T("tutorial.targetPending"):"";
        bool last=_session.Position==_session.Count;
        string title=L.T(step.TitleKey),body=L.T(step.BodyKey),counter=L.F("tutorial.counter",_session.Position,_session.Count);
        bool previous=_session.CanPrevious && !_vm.IsBusy,next=Ready(step) && !_vm.IsBusy && OnRequiredPage(step);
        string signature=$"{step.Id}|{title}|{body}|{counter}|{condition}|{previous}|{next}|{bounds}|{_overlay.RenderSize}";
        if(signature==_lastPresentation)return;
        _lastPresentation=signature;
        _overlay.Present(title,body,counter,condition,previous,next,last,bounds);
    }
    private static FrameworkElement? FindTarget(DependencyObject parent,string id)
    {
        if(parent is FrameworkElement f && f.IsVisible && TutorialTarget.GetId(f)==id)return f;
        int count=VisualTreeHelper.GetChildrenCount(parent);
        for(int i=0;i<count;i++){var found=FindTarget(VisualTreeHelper.GetChild(parent,i),id);if(found is not null)return found;}
        return null;
    }
    public void Dispose()
    {
        _disposed=true;_root.LayoutUpdated-=LayoutChanged;_vm.PropertyChanged-=ModelChanged;LocalizationService.Current.LanguageChanged-=LanguageChanged;
        _overlay.NextRequested-=Next;_overlay.PreviousRequested-=Previous;_overlay.SkipRequested-=Skip;
    }
}
