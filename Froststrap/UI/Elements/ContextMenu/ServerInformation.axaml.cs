using Froststrap.UI.Utility;
using Froststrap.UI.ViewModels.ContextMenu;

namespace Froststrap.UI.Elements.ContextMenu;

public partial class ServerInformation : Base.AvaloniaWindow
{
    public ServerInformation()
    {
        InitializeComponent();
        SpectreChrome.Apply(this, ShellGlass, AbyssBackground);
        SpectreChrome.AttachPointer(this, AbyssBackground);
    }

    public ServerInformation(Watcher watcher) : this()
    {
        DataContext = new ServerInformationViewModel(watcher);
    }
}
