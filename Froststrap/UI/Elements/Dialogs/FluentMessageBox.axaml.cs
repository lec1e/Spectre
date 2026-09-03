using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
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
            TransparencyLevelHint =
            [
                WindowTransparencyLevel.AcrylicBlur,
                WindowTransparencyLevel.Mica,
                WindowTransparencyLevel.Blur,
                WindowTransparencyLevel.None
            ];
            Background = Brushes.Transparent;
            PointerMoved += OnWindowPointerMoved;
            PointerExited += (_, _) => AbyssBackground?.SetPointer(new Point(-40, -40), false);

            Dispatcher.UIThread.Post(() =>
            {
                ShellGlass?.SyncTintFromTheme();
                ShellGlass?.ApplyFromSettings();
                AbyssBackground?.SyncFromTheme();
                AbyssBackground?.SyncFromSettings();
            }, DispatcherPriority.Loaded);
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

            Loaded += (s, e) =>
            {
                ShellGlass?.ApplyFromSettings();
                AbyssBackground?.SyncFromTheme();
            };
        }

        private void OnWindowPointerMoved(object? sender, Avalonia.Input.PointerEventArgs e)
        {
            if (AbyssBackground is null)
                return;
            AbyssBackground.SetPointer(e.GetPosition(AbyssBackground), true);
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