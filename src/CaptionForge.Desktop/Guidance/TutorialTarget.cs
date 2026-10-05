using System.Windows;
namespace CaptionForge.Desktop.Guidance;
public static class TutorialTarget
{
    public static readonly DependencyProperty IdProperty=DependencyProperty.RegisterAttached("Id",typeof(string),typeof(TutorialTarget),new PropertyMetadata(null));
    public static void SetId(DependencyObject target,string value)=>target.SetValue(IdProperty,value);
    public static string? GetId(DependencyObject target)=>(string?)target.GetValue(IdProperty);
}
