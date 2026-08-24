using Avalonia.Controls;
using Avalonia.Input;
using Froststrap.UI.ViewModels.Settings;

namespace Froststrap.UI.Elements.Settings.Pages;

public partial class VipServerPage : UserControl
{
    public VipServerPage()
    {
        InitializeComponent();
        App.FrostRPC?.SetPage("VipServer");

        Loaded += (_, _) =>
        {
            SearchTextBox.KeyDown += (_, args) =>
            {
                if (args.Key != Key.Enter)
                    return;

                if (DataContext is VipServerViewModel vm && vm.SearchGamesCommand.CanExecute(null))
                    vm.SearchGamesCommand.Execute(null);

                args.Handled = true;
            };
        };
    }
}
