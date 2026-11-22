using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Froststrap.UI.Elements.Controls;

/// <summary>
/// Keeps a page inside the space the window actually gave it, so inner lists can scroll.
/// </summary>
public class FitHost : Decorator
{
    private Control? _host;

    public FitHost()
    {
        ClipToBounds = true;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _host = this.GetVisualParent() as Control;
        if (_host is not null)
            _host.PropertyChanged += OnHostPropertyChanged;
        InvalidateMeasure();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_host is not null)
            _host.PropertyChanged -= OnHostPropertyChanged;
        _host = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnHostPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == BoundsProperty)
            InvalidateMeasure();
    }

    protected override Size MeasureOverride(Size available)
    {
        var slot = new Size(Resolve(available.Width, horizontal: true), Resolve(available.Height, horizontal: false));
        Child?.Measure(slot);
        return slot;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Child?.Arrange(new Rect(finalSize));
        return finalSize;
    }

    private double Resolve(double available, bool horizontal)
    {
        if (!double.IsInfinity(available) && available > 1)
            return available;

        var top = TopLevel.GetTopLevel(this);
        if (top is not null)
        {
            var origin = this.TranslatePoint(default, top);
            if (origin is { } point)
            {
                var room = (horizontal ? top.ClientSize.Width - point.X : top.ClientSize.Height - point.Y) - 16;
                if (room > 80)
                    return room;
            }

            var client = horizontal ? top.ClientSize.Width : top.ClientSize.Height;
            if (client > 80)
                return Math.Max(240, client - (horizontal ? 280 : 160));
        }

        return horizontal ? 960 : 560;
    }
}
