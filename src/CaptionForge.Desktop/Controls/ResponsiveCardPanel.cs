using System.Windows;
using System.Windows.Controls;
namespace CaptionForge.Desktop.Controls;
/// <summary>Columnas fluidas con tarjetas iguales; no recorta covers verticales ni horizontales.</summary>
public sealed class ResponsiveCardPanel : Panel
{
 private int _columns=1;private double _width=260;private const double Gap=12;
 protected override Size MeasureOverride(Size available)
 {
  double width=double.IsInfinity(available.Width)?900:available.Width;
  _columns=Math.Max(1,(int)Math.Floor((width+Gap)/(220+Gap)));_width=Math.Max(0,(width-Gap*(_columns-1))/_columns);
  double rowHeight=0,height=0;int column=0;
  foreach(UIElement child in InternalChildren){child.Measure(new(_width,double.PositiveInfinity));rowHeight=Math.Max(rowHeight,child.DesiredSize.Height);if(++column==_columns){height+=rowHeight+Gap;rowHeight=0;column=0;}}
  if(column>0)height+=rowHeight+Gap;return new(width,Math.Max(0,height-Gap));
 }
 protected override Size ArrangeOverride(Size final)
 {
  double y=0;for(int start=0;start<InternalChildren.Count;start+=_columns)
  {int count=Math.Min(_columns,InternalChildren.Count-start);double h=Enumerable.Range(start,count).Max(i=>InternalChildren[i].DesiredSize.Height);
   for(int j=0;j<count;j++)InternalChildren[start+j].Arrange(new Rect(j*(_width+Gap),y,_width,h));y+=h+Gap;}
  return final;
 }
}
