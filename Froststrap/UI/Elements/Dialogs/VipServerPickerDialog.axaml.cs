using Avalonia.Interactivity;
using Froststrap.UI.Elements.Base;
using Froststrap.UI.Utility;
using Froststrap.UI.ViewModels.Dialogs;

namespace Froststrap.UI.Elements.Dialogs
{
    public partial class VipServerPickerDialog : AvaloniaWindow
    {
        private readonly VipServerPickerViewModel _vm;

        public VipServerPickerDialog() : this(0)
        {
        }

        public VipServerPickerDialog(long placeId)
        {
            InitializeComponent();

            _vm = new VipServerPickerViewModel(placeId);
            DataContext = _vm;
            SpectreChrome.Apply(this, ShellGlass, AbyssBackground);
            SpectreChrome.AttachPointer(this, AbyssBackground);
        }

        public string? PickedAccessCode => _vm.SelectedServer?.AccessCode;

        private void Skip_Click(object? sender, RoutedEventArgs e)
        {
            Close(null);
        }

        private void Join_Click(object? sender, RoutedEventArgs e)
        {
            if (_vm.SelectedServer is null)
                return;

            Close(_vm.SelectedServer.AccessCode);
        }
    }
}
