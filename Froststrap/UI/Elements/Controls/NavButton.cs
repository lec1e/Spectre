using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using LucideAvalonia;
using LucideAvalonia.Enum;

namespace Froststrap.UI.Elements.Controls
{
    /// <summary>Sidebar navigation entry. Styled through the <c>Button.nav</c> selectors.</summary>
    public class NavButton : Button
    {
        public static readonly StyledProperty<LucideIconNames> IconProperty =
            AvaloniaProperty.Register<NavButton, LucideIconNames>(nameof(Icon), LucideIconNames.Circle);

        public static readonly StyledProperty<string?> TextProperty =
            AvaloniaProperty.Register<NavButton, string?>(nameof(Text));

        public LucideIconNames Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }
        public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }

        protected override Type StyleKeyOverride => typeof(Button);

        public NavButton()
        {
            Classes.Add("nav");

            var icon = new Lucide { VerticalAlignment = VerticalAlignment.Center };
            icon[!Lucide.IconProperty] = this[!IconProperty];

            var label = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis
            };
            label[!TextBlock.TextProperty] = this[!TextProperty];

            var pip = new Border { Classes = { "nav-pip" } };

            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 11 };
            Grid.SetColumn(label, 1);
            row.Children.Add(icon);
            row.Children.Add(label);

            var root = new Panel();
            root.Children.Add(pip);
            root.Children.Add(row);
            Content = root;
        }
    }
}
