using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Froststrap.UI.Elements.Base;
using Froststrap.UI.Utility;

namespace Froststrap.UI.Elements.Dialogs
{
    public partial class DesktopToastWindow : AvaloniaWindow
    {
        private static DesktopToastWindow? _current;
        private DispatcherTimer? _closeTimer;
        private PixelPoint _anchoredPosition;
        private bool _layoutHooked;

        public DesktopToastWindow()
        {
            InitializeComponent();
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            SpectreChrome.Apply(this, ShellGlass, AbyssBackground);
            if (ShellGlass is not null)
                ShellGlass.CornerRadius = new CornerRadius(18);
        }

        public static void ShowToast(string title, string message, int durationSeconds = 8)
        {
            void ShowCore()
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
                    toast.LayoutUpdated -= toast.OnLayoutUpdated;
                    if (ReferenceEquals(_current, toast))
                        _current = null;
                };

                toast.PositionAboveTray();
                toast.Show();
                toast.PositionAboveTray();
                toast.HookLayout();

                toast._closeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Max(3, durationSeconds)) };
                toast._closeTimer.Tick += (_, _) =>
                {
                    toast._closeTimer?.Stop();
                    toast.Close();
                };
                toast._closeTimer.Start();
            }

            if (Dispatcher.UIThread.CheckAccess())
                ShowCore();
            else
                Dispatcher.UIThread.Post(ShowCore);
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            PositionAboveTray();
            HookLayout();
        }

        private void HookLayout()
        {
            if (_layoutHooked)
                return;

            _layoutHooked = true;
            LayoutUpdated += OnLayoutUpdated;
        }

        private void OnLayoutUpdated(object? sender, EventArgs e) => PositionAboveTray();

        private void PositionAboveTray()
        {
            var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary ?? Screens.All.FirstOrDefault();
            if (screen is null)
                return;

            double scale = RenderScaling > 0 ? RenderScaling : (DesktopScaling > 0 ? DesktopScaling : screen.Scaling);
            if (scale <= 0)
                scale = 1;

            var work = screen.WorkingArea;
            var bounds = screen.Bounds;

            double dipWidth = Bounds.Width > 1 ? Bounds.Width : Width;
            double dipHeight = Bounds.Height > 1 ? Bounds.Height : Math.Max(MinHeight, 92);

            int width = Math.Max(1, (int)Math.Ceiling(dipWidth * scale));
            int height = Math.Max(1, (int)Math.Ceiling(dipHeight * scale));
            int gapX = (int)Math.Round(16 * scale);
            int gapY = (int)Math.Round(16 * scale);

            // WorkingArea already excludes the taskbar. If it doesn't (auto-hide / overlay),
            // keep a tray-sized reserve so the toast never sits on the bar.
            int workBottom = work.Y + work.Height;
            int screenBottom = bounds.Y + bounds.Height;
            if (screenBottom - workBottom < (int)(8 * scale))
                gapY += (int)Math.Round(48 * scale);

            int x = work.X + work.Width - width - gapX;
            int y = workBottom - height - gapY;

            x = Math.Clamp(x, work.X + gapX, Math.Max(work.X + gapX, work.X + work.Width - width - gapX));
            y = Math.Max(work.Y + gapY, y);

            var next = new PixelPoint(x, y);
            if (next == _anchoredPosition && next == Position)
                return;

            _anchoredPosition = next;
            Position = next;
        }
    }
}
