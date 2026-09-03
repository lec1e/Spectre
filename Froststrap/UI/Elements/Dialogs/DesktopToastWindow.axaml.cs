using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Froststrap.UI.Elements.Base;

namespace Froststrap.UI.Elements.Dialogs
{
    public partial class DesktopToastWindow : AvaloniaWindow
    {
        private static DesktopToastWindow? _current;
        private DispatcherTimer? _closeTimer;

        public DesktopToastWindow()
        {
            InitializeComponent();
            ShowInTaskbar = false;
            Topmost = true;
            ShellGlass?.ApplyFromSettings();
            AbyssBackground?.SyncFromTheme();
            AbyssBackground?.SyncFromSettings();
        }

        public static void ShowToast(string title, string message, int durationSeconds = 8)
        {
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    _current?.Close();
                }
                catch { /* previous toast already gone */ }

                var toast = new DesktopToastWindow();
                toast.TitleText.Text = title;
                toast.MessageText.Text = message;
                _current = toast;
                toast.Closed += (_, _) =>
                {
                    if (ReferenceEquals(_current, toast))
                        _current = null;
                };

                toast.Show();
                toast.PositionOnPrimaryScreen();

                toast._closeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Max(3, durationSeconds)) };
                toast._closeTimer.Tick += (_, _) =>
                {
                    toast._closeTimer?.Stop();
                    toast.Close();
                };
                toast._closeTimer.Start();
            });
        }

        private void PositionOnPrimaryScreen()
        {
            var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
            if (screen is null)
                return;

            var area = screen.WorkingArea;
            double scale = DesktopScaling > 0 ? DesktopScaling : screen.Scaling;
            int width = (int)(Width * scale);
            int height = (int)(Height * scale);
            int margin = (int)(16 * scale);

            Position = new PixelPoint(
                area.X + area.Width - width - margin,
                area.Y + area.Height - height - margin);
        }
    }
}
