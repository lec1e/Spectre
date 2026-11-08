using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using LucideAvalonia.Enum;

namespace Froststrap.UI.Elements.Dialogs
{
    public partial class FluentMessageBox : Base.AvaloniaWindow
    {
        public MessageBoxResult Result = MessageBoxResult.None;

        public FluentMessageBox()
        {
            InitializeComponent();
            UI.Utility.SpectreChrome.Apply(this);
            Backdrop.ApplyFromSettings();
        }

        public FluentMessageBox(string message, MessageBoxImage image, MessageBoxButton buttons) : this()
        {
            switch (image)
            {
                case MessageBoxImage.Error:
                    GlyphIcon.Icon = LucideIconNames.CircleAlert;
                    break;

                case MessageBoxImage.Question:
                    GlyphIcon.Icon = LucideIconNames.CircleQuestionMark;
                    break;

                case MessageBoxImage.Warning:
                    GlyphIcon.Icon = LucideIconNames.TriangleAlert;
                    break;

                case MessageBoxImage.Information:
                    GlyphIcon.Icon = LucideIconNames.Info;
                    break;

                default:
                    GlyphIcon.IsVisible = false;
                    break;
            }

            IconImage.IsVisible = false;

            Title = App.BrandName;

            MessageMarkdownTextBlock.MarkdownText = message;

            ButtonOne.IsVisible = false;
            ButtonTwo.IsVisible = false;
            ButtonThree.IsVisible = false;

            switch (buttons)
            {
                case MessageBoxButton.YesNo:
                    SetButton(ButtonOne, MessageBoxResult.Yes);
                    SetButton(ButtonTwo, MessageBoxResult.No);
                    break;

                case MessageBoxButton.YesNoCancel:
                    SetButton(ButtonOne, MessageBoxResult.Yes);
                    SetButton(ButtonTwo, MessageBoxResult.No);
                    SetButton(ButtonThree, MessageBoxResult.Cancel);
                    break;

                case MessageBoxButton.OKCancel:
                    SetButton(ButtonOne, MessageBoxResult.OK);
                    SetButton(ButtonTwo, MessageBoxResult.Cancel);
                    break;

                case MessageBoxButton.OK:
                default:
                    SetButton(ButtonOne, MessageBoxResult.OK);
                    break;
            }

            if (ButtonThree.IsVisible)
                Width = 480;
            else if (ButtonTwo.IsVisible)
                Width = 440;

            double textWidth = 180;

            if (image != MessageBoxImage.None)
                textWidth += 50;

            textWidth += message.Length * 0.6;

            if (textWidth > MaxWidth)
                Width = MaxWidth;
            else if (textWidth > Width)
                Width = textWidth;

            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            }
        }

        private static string GetTextForResult(MessageBoxResult result)
        {
            switch (result)
            {
                case MessageBoxResult.OK:
                    return Strings.Common_OK;
                case MessageBoxResult.Cancel:
                    return Strings.Common_Cancel;
                case MessageBoxResult.Yes:
                    return Strings.Common_Yes;
                case MessageBoxResult.No:
                    return Strings.Common_No;
                default:
                    Debug.Assert(false);
                    return result.ToString();
            }
        }

        private void Close_Click(object? sender, RoutedEventArgs e)
        {
            if (Result == MessageBoxResult.None)
                Result = MessageBoxResult.Cancel;
            Close();
        }

        public void SetButton(Button button, MessageBoxResult result)
        {
            button.IsVisible = true;
            button.Content = GetTextForResult(result);
            button.Click += (_, _) =>
            {
                Result = result;
                Close();
            };
        }
    }
}