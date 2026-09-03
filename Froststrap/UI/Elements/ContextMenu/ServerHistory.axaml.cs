using Froststrap.Integrations;
using Froststrap.UI.Utility;
using Froststrap.UI.ViewModels.ContextMenu;

namespace Froststrap.UI.Elements.ContextMenu
{
    public partial class ServerHistory : Base.AvaloniaWindow
    {
        public ServerHistory()
        {
            InitializeComponent();
            SpectreChrome.Apply(this, ShellGlass, AbyssBackground);
            SpectreChrome.AttachPointer(this, AbyssBackground);
        }

        public ServerHistory(ActivityWatcher watcher) : this()
        {
            var viewModel = new ServerHistoryViewModel(watcher);

            viewModel.RequestCloseEvent += (_, _) => Close();

            DataContext = viewModel;
        }
    }
}