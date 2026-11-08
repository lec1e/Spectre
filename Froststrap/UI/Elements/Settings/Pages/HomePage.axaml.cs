using Avalonia;
using Avalonia.Controls;
using Froststrap.UI.ViewModels.Settings;

namespace Froststrap.UI.Elements.Settings.Pages;

public partial class HomePage : UserControl
{
    private HomeViewModel? _viewModel;
    private bool _attached;

    public HomePage()
    {
        InitializeComponent();
        App.FrostRPC?.SetPage("Home");
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        _viewModel?.Activate();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        _viewModel?.Deactivate();
        _viewModel = DataContext as HomeViewModel;

        if (_attached)
            _viewModel?.Activate();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        _viewModel?.Deactivate();
    }
}
