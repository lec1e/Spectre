using Avalonia;
using Froststrap.UI.Elements.Base;
using Froststrap.UI.ViewModels.Dialogs;

namespace Froststrap.UI.Elements.Dialogs
{
    public partial class UninstallerDialog : AvaloniaWindow
    {
        public bool Confirmed { get; private set; } = false;
        public bool KeepData { get; private set; } = false;

        public UninstallerDialog()
        {
            InitializeComponent();

            var viewModel = new UninstallerViewModel();

            viewModel.ConfirmUninstallRequest += (_, _) =>
            {
                Confirmed = true;
                KeepData = viewModel.KeepData;
                Close();
            };

            viewModel.CancelRequest += (_, _) =>
            {
                Confirmed = false;
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

            App.FrostRPC?.SetDialog("Uninstaller");
        }
    }
}
