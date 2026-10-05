using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using CaptionForge.Desktop.ViewModels;
namespace CaptionForge.Desktop.Views;
internal static class DialogSizing
{
    public static void Fit(Window dialog,Window owner)
    {
        var work=SystemParameters.WorkArea;
        var info=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()};
        var handle=new WindowInteropHelper(owner).Handle;
        if(GetMonitorInfo(MonitorFromWindow(handle,2),ref info))
        {
            var matrix=PresentationSource.FromVisual(owner)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            var a=matrix.Transform(new Point(info.Work.Left,info.Work.Top));var b=matrix.Transform(new Point(info.Work.Right,info.Work.Bottom));
            work=new Rect(a,b);
        }
        dialog.MaxHeight=Math.Max(100,work.Height-32);dialog.MaxWidth=Math.Max(100,work.Width-32);
        dialog.MinHeight=Math.Min(dialog.MinHeight,dialog.MaxHeight);dialog.MinWidth=Math.Min(dialog.MinWidth,dialog.MaxWidth);
        dialog.Height=Math.Min(dialog.Height,dialog.MaxHeight);dialog.Width=Math.Min(dialog.Width,dialog.MaxWidth);
        dialog.WindowStartupLocation=WindowStartupLocation.Manual;
        dialog.Left=Math.Clamp(owner.Left+(owner.ActualWidth-dialog.Width)/2,work.Left+16,work.Right-dialog.Width-16);
        dialog.Top=Math.Clamp(owner.Top+(owner.ActualHeight-dialog.Height)/2,work.Top+16,work.Bottom-dialog.Height-16);
        var appearance=(owner.DataContext as MainViewModel)?.Appearance;
        void Apply()
        {
            nint h=new WindowInteropHelper(dialog).Handle;if(h==0)return;
            int dark=appearance?.IsDark==false?0:1;
            if(DwmSetWindowAttribute(h,20,ref dark,sizeof(int))<0)DwmSetWindowAttribute(h,19,ref dark,sizeof(int));
            int Color(string key){var c=((SolidColorBrush)dialog.FindResource(key)).Color;return c.R|c.G<<8|c.B<<16;}
            int caption=Color("BackgroundBrush"),text=Color("TextBrush"),border=Color("BorderBrush");
            DwmSetWindowAttribute(h,35,ref caption,sizeof(int));DwmSetWindowAttribute(h,36,ref text,sizeof(int));DwmSetWindowAttribute(h,34,ref border,sizeof(int));
        }
        EventHandler changed=(_,_)=>Apply();
        dialog.SourceInitialized+=(_,_)=>Apply();
        if(appearance is not null){appearance.ThemeChanged+=changed;dialog.Closed+=(_,_)=>appearance.ThemeChanged-=changed;}
    }
    [StructLayout(LayoutKind.Sequential)]private struct NativeRect{public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)]private struct MonitorInfo{public int Size;public NativeRect Monitor,Work;public uint Flags;}
    [DllImport("user32.dll")]private static extern nint MonitorFromWindow(nint hwnd,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Auto)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool GetMonitorInfo(nint monitor,ref MonitorInfo info);
    [DllImport("dwmapi.dll")]private static extern int DwmSetWindowAttribute(nint hwnd,int attribute,ref int value,int size);
}
