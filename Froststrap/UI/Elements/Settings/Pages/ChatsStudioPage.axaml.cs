using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Froststrap.UI.ViewModels.Settings;

namespace Froststrap.UI.Elements.Settings.Pages;

public partial class ChatsStudioPage : UserControl
{
    public ChatsStudioPage()
    {
        InitializeComponent();
        DraftBox.AddHandler(InputElement.KeyDownEvent, DraftKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        DataContextChanged += (_, _) =>
        {
            if (DataContext is ChatsStudioViewModel model)
                model.ThreadChanged += (_, _) => Dispatcher.UIThread.Post(() => ThreadScroll.ScrollToEnd(), DispatcherPriority.Background);
        };
    }

    private void DraftKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers.HasFlag(KeyModifiers.Shift) || DataContext is not ChatsStudioViewModel model)
            return;

        model.SendCommand.Execute(null);
        e.Handled = true;
    }
}
