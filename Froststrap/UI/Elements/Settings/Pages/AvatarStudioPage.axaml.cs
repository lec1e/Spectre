using Avalonia;
using Avalonia.Controls;
using Froststrap.UI.ViewModels.Settings;

namespace Froststrap.UI.Elements.Settings.Pages;

public partial class AvatarStudioPage : UserControl
{
    private AvatarStudioViewModel? _model;

    public AvatarStudioPage()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Hook(DataContext as AvatarStudioViewModel);
        Stage.StatusChanged += OnStageStatus;
    }

    private void Hook(AvatarStudioViewModel? model)
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
        if (_model is null || state is not ("failed" or "loading"))
            return;

        _model.Status = state == "failed"
            ? message ?? "The avatar could not be drawn."
            : "Building your avatar…";
    }

    private void FitDropDown(object? sender, EventArgs e)
    {
        if (sender is not ComboBox box)
            return;

        var window = TopLevel.GetTopLevel(box);
        if (window is null)
            return;

        var top = box.TranslatePoint(new Point(0, 0), window);
        var bottom = box.TranslatePoint(new Point(0, box.Bounds.Height), window);
        if (top is null || bottom is null)
            return;

        var below = window.ClientSize.Height - bottom.Value.Y - 12;
        var above = top.Value.Y - 12;
        var room = below >= 140 ? below : Math.Max(below, above);
        box.MaxDropDownHeight = Math.Clamp(room, 96, 240);
    }
}
