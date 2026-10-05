using System.Windows;
using System.Windows.Controls;
using CaptionForge.Desktop.Guidance;
namespace CaptionForge.Desktop.Controls;
public partial class TutorialOverlay : UserControl
{
    public event EventHandler? PreviousRequested,NextRequested,SkipRequested;
    public TutorialOverlay()=>InitializeComponent();
    public void Present(string title,string body,string counter,string condition,bool previous,bool next,bool last,Rect target)
    {
        StepTitle.Text=title;StepBody.Text=body;Counter.Text=counter;Condition.Text=condition;
        PreviousButton.IsEnabled=previous;NextButton.IsEnabled=next;
        NextButton.SetResourceReference(ContentControl.ContentProperty,"Loc."+(last?"tutorial.finish":"tutorial.next"));
        Highlight.Visibility=target.IsEmpty || target.Width<1 || target.Height<1?Visibility.Collapsed:Visibility.Visible;
        if(Highlight.Visibility==Visibility.Visible){Canvas.SetLeft(Highlight,target.X);Canvas.SetTop(Highlight,target.Y);Highlight.Width=target.Width;Highlight.Height=target.Height;}
        Card.MaxWidth=Math.Max(100,ActualWidth-16);Card.MaxHeight=Math.Max(100,ActualHeight-16);
        Card.Measure(new Size(Card.MaxWidth,Card.MaxHeight));
        var p=TutorialPlacement.Place(ActualWidth,ActualHeight,Card.DesiredSize.Width,Card.DesiredSize.Height,target.IsEmpty?default:new(target.X,target.Y,target.Width,target.Height));
        Canvas.SetLeft(Card,p.X);Canvas.SetTop(Card,p.Y);
    }
    private void PreviousClick(object sender,RoutedEventArgs e)=>PreviousRequested?.Invoke(this,EventArgs.Empty);
    private void NextClick(object sender,RoutedEventArgs e)=>NextRequested?.Invoke(this,EventArgs.Empty);
    private void SkipClick(object sender,RoutedEventArgs e)=>SkipRequested?.Invoke(this,EventArgs.Empty);
}
