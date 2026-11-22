using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Froststrap.UI.ViewModels.Settings;

namespace Froststrap.UI.Elements.Settings.Pages;

public partial class InventoryStudioPage : UserControl
{
    private bool _dragging;
    private Point _last;

    public InventoryStudioPage()
    {
        InitializeComponent();
    }

    private void PreviewPressed(object? sender, PointerPressedEventArgs e)
    {
        _dragging = true;
        _last = e.GetPosition(this);
        e.Pointer.Capture(sender as Control);
    }

    private void PreviewMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragging || DataContext is not InventoryStudioViewModel model)
            return;

        var point = e.GetPosition(this);
        model.Orbit((float)(point.X - _last.X), (float)(point.Y - _last.Y));
        _last = point;
    }

    private void PreviewReleased(object? sender, PointerReleasedEventArgs e)
    {
        _dragging = false;
        e.Pointer.Capture(null);
        if (DataContext is InventoryStudioViewModel model)
            model.EndOrbit();
    }

    private void PreviewWheel(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is not InventoryStudioViewModel model)
            return;

        model.Zoom((float)e.Delta.Y);
        e.Handled = true;
    }
}
