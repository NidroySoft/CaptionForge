using System.Windows;
using System.Windows.Controls;

namespace CaptionForge.Desktop.Controls;

/// <summary>Two columns when both panels fit; stacked panels on narrow windows.</summary>
public sealed class AdaptiveGrid : Grid
{
    public double StackAtWidth { get; set; } = 880;
    private bool? _stacked;
    private int _childCount = -1;

    protected override Size MeasureOverride(Size availableSize)
    {
        var stacked = !double.IsInfinity(availableSize.Width) && availableSize.Width < StackAtWidth;
        if (_stacked != stacked || _childCount != Children.Count)
        {
            _stacked = stacked;
            _childCount = Children.Count;
            ColumnDefinitions.Clear();
            RowDefinitions.Clear();
            if (stacked)
            {
                ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
                for (var i = 0; i < Children.Count; i++)
                    RowDefinitions.Add(new() { Height = new GridLength(i == 0 ? 2 : 1, GridUnitType.Star) });
            }
            else
            {
                for (var i = 0; i < Children.Count; i++)
                    ColumnDefinitions.Add(new() { Width = new GridLength(i == 0 ? 1.2 : 1, GridUnitType.Star) });
            }
            for (var i = 0; i < Children.Count; i++)
            {
                SetColumn(Children[i], stacked ? 0 : i);
                SetRow(Children[i], stacked ? i : 0);
                if (Children[i] is FrameworkElement child)
                    child.Margin = i == Children.Count - 1 ? new Thickness(0)
                        : stacked ? new Thickness(0, 0, 0, 12) : new Thickness(0, 0, 12, 0);
            }
        }
        return base.MeasureOverride(availableSize);
    }
}
