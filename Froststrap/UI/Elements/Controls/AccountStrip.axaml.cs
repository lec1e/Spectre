using Avalonia;
using Avalonia.Controls;

namespace Froststrap.UI.Elements.Controls;

public partial class AccountStrip : UserControl
{
    public AccountStrip()
    {
        InitializeComponent();
    }

    private void FitDropDown(object? sender, EventArgs e)
    {
        if (sender is not ComboBox box)
            return;

        var window = TopLevel.GetTopLevel(box);
        if (window is null)
            return;

        var top = box.TranslatePoint(new Point(0, 0), window);
        var bottom = box.TranslatePoint(new Point(0, box.Bounds.Height), window);
        if (top is null || bottom is null)
            return;

        var below = window.ClientSize.Height - bottom.Value.Y - 12;
        var above = top.Value.Y - 12;
        var room = below >= 140 ? below : Math.Max(below, above);
        box.MaxDropDownHeight = Math.Clamp(room, 96, 240);
    }
}
