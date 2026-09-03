using Froststrap.UI.Elements.Base;
using Froststrap.UI.Utility;
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
            SpectreChrome.Apply(this, ShellGlass, AbyssBackground);
            SpectreChrome.AttachPointer(this, AbyssBackground);

            App.FrostRPC?.SetDialog("Uninstaller");
        }
    }
}
