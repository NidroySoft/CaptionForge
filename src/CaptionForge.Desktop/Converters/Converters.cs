using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;
namespace CaptionForge.Desktop.Converters;
public sealed class StepVisibilityConverter : IValueConverter
{
 public object Convert(object value,Type targetType,object parameter,CultureInfo culture)=>value.ToString()==parameter.ToString()?Visibility.Visible:Visibility.Collapsed;
 public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture)=>Binding.DoNothing;
}
public sealed class CoverConverter : IValueConverter
{
 public object? Convert(object value,Type targetType,object parameter,CultureInfo culture)
 {
  if(value is not string path || !File.Exists(path))return null;
  try {var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.DecodePixelWidth=640;bitmap.UriSource=new Uri(Path.GetFullPath(path));bitmap.EndInit();bitmap.Freeze();return bitmap;}
  catch(Exception ex) when(ex is IOException or NotSupportedException or System.IO.FileFormatException or ArgumentException){return null;}
 }
 public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture)=>Binding.DoNothing;
}
