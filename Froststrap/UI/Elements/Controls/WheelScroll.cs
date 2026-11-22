using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Froststrap.UI.Elements.Controls;

/// <summary>
/// A list region that keeps the height it was given and moves its content with the wheel.
/// Buttons inside the list do not block scrolling.
/// </summary>
public class WheelScroll : Panel
{
    private readonly ScrollBar _bar;
    private double _offset;
    private bool _pinEnd;
    private bool _syncing;
    private double _contentWidth = -1;

    public WheelScroll()
    {
        ClipToBounds = true;
        Background = Brushes.Transparent;
        _bar = new ScrollBar
        {
            Orientation = Orientation.Vertical,
            Width = 10,
            MinWidth = 10,
            SmallChange = 48,
            ZIndex = 1
        };
        _bar.PropertyChanged += OnBarChanged;
        Children.Add(_bar);
        AddHandler(PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    public void ScrollToEnd()
    {
        _pinEnd = true;
        InvalidateMeasure();
        InvalidateArrange();
    }

    protected override Size MeasureOverride(Size available)
    {
        var content = Content;
        var width = double.IsInfinity(available.Width) ? _contentWidth > 1 ? _contentWidth : 360 : available.Width;
        MeasureContent(content, Math.Max(0, width - _bar.Width));
        _bar.Measure(new Size(_bar.Width, double.IsInfinity(available.Height) ? 0 : available.Height));

        var contentHeight = content?.DesiredSize.Height ?? 0;
        var viewport = double.IsInfinity(available.Height)
            ? Math.Min(contentHeight, 480)
            : Math.Min(available.Height, contentHeight);
        return new Size(width, Math.Max(viewport, 0));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var content = Content;
        var contentHeight = content?.DesiredSize.Height ?? 0;
        var needsBar = contentHeight > finalSize.Height + 1;
        var barWidth = needsBar ? _bar.Width : 0;
        var contentWidth = Math.Max(0, finalSize.Width - barWidth);
        if (content is not null && Math.Abs(_contentWidth - contentWidth) > 2)
        {
            MeasureContent(content, contentWidth);
            contentHeight = content.DesiredSize.Height;
            needsBar = contentHeight > finalSize.Height + 1;
            barWidth = needsBar ? _bar.Width : 0;
            contentWidth = Math.Max(0, finalSize.Width - barWidth);
        }

        _bar.IsVisible = needsBar;
        var max = Math.Max(0, contentHeight - finalSize.Height);
        _offset = Math.Clamp(_pinEnd ? max : _offset, 0, max);
        _pinEnd = false;

        content?.Arrange(new Rect(0, -_offset, contentWidth, Math.Max(contentHeight, finalSize.Height)));

        _syncing = true;
        _bar.Maximum = max;
        _bar.ViewportSize = Math.Max(finalSize.Height, 1);
        _bar.LargeChange = Math.Max(finalSize.Height * 0.8, 48);
        _bar.Value = _offset;
        _syncing = false;
        _bar.Arrange(new Rect(Math.Max(0, finalSize.Width - barWidth), 0, barWidth, finalSize.Height));
        return finalSize;
    }

    private void MeasureContent(Control? content, double width)
    {
        _contentWidth = width;
        content?.Measure(new Size(width, double.PositiveInfinity));
    }

    private Control? Content => Children.FirstOrDefault(child => child != _bar);

    private void OnBarChanged(object? sender, AvaloniaPropertyChangedEventArgs change)
    {
        if (_syncing || change.Property != RangeBase.ValueProperty)
            return;

        _offset = _bar.Value;
        InvalidateArrange();
    }

    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (e.Source is Visual source && IsOwnScroller(source))
            return;

        var content = Content;
        if (content is null || Math.Abs(e.Delta.Y) < 0.01)
            return;

        var max = Math.Max(0, content.DesiredSize.Height - Bounds.Height);
        if (max <= 1)
            return;

        var notch = Math.Clamp(e.Delta.Y, -4, 4);
        _offset = Math.Clamp(_offset - notch * 64, 0, max);
        InvalidateArrange();
        e.Handled = true;
    }

    private static bool IsOwnScroller(Visual source)
    {
        return source.FindAncestorOfType<Slider>(includeSelf: true) is not null
            || source.FindAncestorOfType<ComboBox>(includeSelf: true) is not null
            || source.FindAncestorOfType<TextBox>(includeSelf: true) is not null;
    }
}
