using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Froststrap.UI.ViewModels.Dialogs;

namespace Froststrap.UI.Elements.Dialogs
{
    /// <summary>
    /// Interaction logic for LaunchMenuDialog.axaml
    /// </summary>
    public partial class LaunchMenuDialog : Base.AvaloniaWindow
    {
        public NextAction CloseAction = NextAction.Terminate;

        public LaunchMenuDialog()
        {
            InitializeComponent();

            TransparencyLevelHint =
            [
                WindowTransparencyLevel.AcrylicBlur,
                WindowTransparencyLevel.Mica,
                WindowTransparencyLevel.Blur,
                WindowTransparencyLevel.None
            ];
            Background = Brushes.Transparent;

            var viewModel = new LaunchMenuViewModel();

            viewModel.CloseWindowRequest += (_, closeAction) =>
            {
                CloseAction = closeAction;
                Close();
            };

            DataContext = viewModel;
            ShellGlass?.ApplyFromSettings();
            AbyssBackground?.SyncFromTheme();
            AbyssBackground?.SyncFromSettings();

            PointerMoved += (_, e) =>
            {
                if (AbyssBackground is null)
                    return;
                AbyssBackground.SetPointer(e.GetPosition(AbyssBackground), true);
            };
            PointerExited += (_, _) => AbyssBackground?.SetPointer(new Point(-40, -40), false);

            Random Chance = new();
            if (Chance.Next(0, 10000) == 1)
            {
                LaunchTitle.Text = "Cartistrap";
            }
        }
    }
}
