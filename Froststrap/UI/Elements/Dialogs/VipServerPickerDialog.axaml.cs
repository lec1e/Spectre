using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Froststrap.UI.Elements.Base;
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

            TransparencyLevelHint =
            [
                WindowTransparencyLevel.AcrylicBlur,
                WindowTransparencyLevel.Mica,
                WindowTransparencyLevel.Blur,
                WindowTransparencyLevel.None
            ];
            Background = Brushes.Transparent;

            _vm = new VipServerPickerViewModel(placeId);
            DataContext = _vm;
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
