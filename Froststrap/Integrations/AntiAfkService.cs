using System.Runtime.InteropServices;

namespace Froststrap.Integrations
{
    /// <summary>
    /// Prevents Roblox's idle kick by periodically nudging input while RobloxPlayerBeta is running.
    /// Toggle from the tray (mini-task) menu; state is persisted in Settings.EnableAntiAfk.
    /// </summary>
    public sealed class AntiAfkService : IDisposable
    {
        private const string LOG_IDENT = "AntiAfkService";
        private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

        private readonly CancellationTokenSource _cts = new();
        private Task? _loop;
        private bool _disposed;

        public bool IsEnabled => App.Settings.Prop.EnableAntiAfk;

        public void Start()
        {
            if (_loop is not null)
                return;

            _loop = Task.Run(() => RunAsync(_cts.Token));
            App.Logger.WriteLine(LOG_IDENT, $"Started (enabled={IsEnabled})");
        }

        public void SetEnabled(bool enabled)
        {
            App.Settings.Prop.EnableAntiAfk = enabled;
            try { App.Settings.Save(); } catch { /* non-fatal */ }
            App.Logger.WriteLine(LOG_IDENT, enabled ? "Enabled" : "Disabled");
        }

        private async Task RunAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(Interval, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (!App.Settings.Prop.EnableAntiAfk)
                    continue;

                if (!OperatingSystem.IsWindows())
                    continue;

                try
                {
                    NudgeRobloxInput();
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Nudge failed: {ex.Message}");
                }
            }
        }

        private static void NudgeRobloxInput()
        {
            Process[] procs;
            try
            {
                procs = Process.GetProcessesByName("RobloxPlayerBeta");
            }
            catch
            {
                return;
            }

            if (procs.Length == 0)
                return;

            try
            {
                // Tiny relative move and back — resets Roblox client idle without a visible jump.
                var move = new INPUT
                {
                    type = INPUT_MOUSE,
                    U = new InputUnion
                    {
                        mi = new MOUSEINPUT
                        {
                            dx = 1,
                            dy = 0,
                            dwFlags = MOUSEEVENTF_MOVE,
                            mouseData = 0,
                            time = 0,
                            dwExtraInfo = IntPtr.Zero
                        }
                    }
                };
                var moveBack = move;
                moveBack.U.mi.dx = -1;

                SendInput(2, new[] { move, moveBack }, Marshal.SizeOf<INPUT>());
            }
            finally
            {
                foreach (var p in procs)
                    p.Dispose();
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _cts.Cancel();
            try { _loop?.Wait(TimeSpan.FromSeconds(1)); } catch { /* ignore */ }
            _cts.Dispose();
        }

        private const int INPUT_MOUSE = 0;
        private const uint MOUSEEVENTF_MOVE = 0x0001;

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public int type;
            public InputUnion U;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
    }
}
