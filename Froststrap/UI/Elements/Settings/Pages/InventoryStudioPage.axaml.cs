using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Froststrap.UI.ViewModels.Settings;

namespace Froststrap.UI.Elements.Settings.Pages;

public partial class InventoryStudioPage : UserControl
{
    private bool _dragging;
    private Point _last;
    private InventoryStudioViewModel? _model;

    public InventoryStudioPage()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Hook(DataContext as InventoryStudioViewModel);
        Stage.StatusChanged += OnStageStatus;
    }

    private void Hook(InventoryStudioViewModel? model)
    {
        if (_model is not null)
            _model.LiveChanged -= OnLive;

        _model = model;
        if (_model is null)
            return;

        _model.LiveChanged += OnLive;
        if (_model.LiveDocument is not null)
            _ = Stage.ApplyAsync(_model.LiveDocument);
    }

    private void OnLive(object? sender, EventArgs e)
    {
        if (_model?.LiveDocument is string document)
            _ = Stage.ApplyAsync(document);
    }

    private void OnStageStatus(string state, string? message)
    {
        if (_model is null)
            return;

        if (state is not ("failed" or "loading"))
            return;

        _model.Status = state == "failed"
            ? message ?? "That preview could not be opened."
            : "Opening the preview…";
        if (state == "failed")
            _model.UseLiveModelFallback();
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
