using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;

namespace Froststrap.Utility
{
    /// <summary>
    /// Shows an rbxservers.xyz embed in WebView2 and captures the VIP access code
    /// when the user clicks a server. Works for launches from any browser
    /// (Chrome / Edge / Firefox / Brave / Opera) that invoke the roblox-player protocol.
    /// </summary>
    internal static class VipServerPicker
    {
        private const string LOG_IDENT = "VipServerPicker";
        private const string WindowClassName = "Eclipse_VipServerPicker";

        public static Task<string?> PickAsync(long placeId, CancellationToken cancellationToken = default)
        {
            if (!OperatingSystem.IsWindows())
                return Task.FromResult<string?>(null);

            var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            string userData = Path.Combine(Paths.LocalAppData, App.ProjectName, "WebViewProfiles", "VipPicker", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(userData);

            var thread = new Thread(() =>
            {
                try
                {
                    RunStaWindow(placeId, userData, tcs, cancellationToken);
                }
                catch (Exception ex)
                {
                    App.Logger.WriteException(LOG_IDENT, ex);
                    tcs.TrySetResult(null);
                }
                finally
                {
                    try
                    {
                        if (Directory.Exists(userData))
                            Directory.Delete(userData, recursive: true);
                    }
                    catch { /* best-effort */ }
                }
            })
            {
                IsBackground = true,
                Name = "Eclipse-VipPicker"
            };

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return tcs.Task;
        }

        private static void RunStaWindow(
            long placeId,
            string userDataFolder,
            TaskCompletionSource<string?> tcs,
            CancellationToken cancellationToken)
        {
            EnsureWindowClass();

            string title = $"{App.BrandName} — Pick a VIP server";
            IntPtr hwnd = CreateWindowExW(
                0, WindowClassName, title,
                WS_OVERLAPPEDWINDOW | WS_VISIBLE,
                CW_USEDEFAULT, CW_USEDEFAULT, 720, 820,
                IntPtr.Zero, IntPtr.Zero, GetModuleHandleW(null), IntPtr.Zero);

            if (hwnd == IntPtr.Zero)
            {
                tcs.TrySetResult(null);
                return;
            }

            ShowWindow(hwnd, SW_SHOW);
            UpdateWindow(hwnd);

            var state = new HostState(hwnd, tcs);
            var stateHandle = GCHandle.Alloc(state);
            SetWindowLongPtr(hwnd, GWLP_USERDATA, GCHandle.ToIntPtr(stateHandle));

            _ = InitializeWebViewAsync(state, placeId, userDataFolder);

            using var cancelReg = cancellationToken.Register(() =>
            {
                tcs.TrySetCanceled(cancellationToken);
                if (IsWindow(hwnd))
                    PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            });

            MSG msg;
            while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }

            if (!tcs.Task.IsCompleted)
                tcs.TrySetResult(null);

            try { state.Controller?.Close(); } catch { /* ignore */ }

            if (stateHandle.IsAllocated)
                stateHandle.Free();
        }

        private static async Task InitializeWebViewAsync(HostState state, long placeId, string userDataFolder)
        {
            try
            {
                var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder).ConfigureAwait(true);
                var controller = await env.CreateCoreWebView2ControllerAsync(state.Hwnd).ConfigureAwait(true);
                state.Controller = controller;
                var webview = controller.CoreWebView2;

                FitController(state.Hwnd, controller);

                webview.NavigationStarting += (_, e) =>
                {
                    string uri = e.Uri ?? "";

                    string? quickCode = LaunchArgsUtility.TryExtractRbxServersQuickLaunchCode(uri);
                    if (quickCode is not null)
                    {
                        e.Cancel = true;
                        App.Logger.WriteLine(LOG_IDENT, "Captured accessCode from quicklaunch URL.");
                        Finish(state, quickCode);
                        return;
                    }

                    bool isRobloxNav = uri.StartsWith("roblox://", StringComparison.OrdinalIgnoreCase)
                        || uri.StartsWith("roblox-player:", StringComparison.OrdinalIgnoreCase)
                        || uri.StartsWith("https://www.roblox.com/", StringComparison.OrdinalIgnoreCase)
                        || uri.StartsWith("https://roblox.com/", StringComparison.OrdinalIgnoreCase);

                    if (!isRobloxNav)
                        return;

                    e.Cancel = true;
                    string? code = LaunchArgsUtility.TryExtractAccessCode(uri);
                    if (code is not null)
                        App.Logger.WriteLine(LOG_IDENT, "Captured accessCode from Roblox navigation.");
                    Finish(state, code);
                };

                webview.NewWindowRequested += (_, e) =>
                {
                    e.Handled = true;
                    try { webview.Navigate(e.Uri); } catch { /* best-effort */ }
                };

                string url = $"https://rbxservers.xyz/embedded/game/{placeId}";
                App.Logger.WriteLine(LOG_IDENT, $"Navigating to {url}");
                webview.Navigate(url);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, "WebView2 unavailable; skipping VIP picker.");
                App.Logger.WriteException(LOG_IDENT, ex);
                Finish(state, null);
            }
        }

        private static void Finish(HostState state, string? accessCode)
        {
            if (state.Completed)
                return;
            state.Completed = true;
            state.Completion.TrySetResult(accessCode);
            if (IsWindow(state.Hwnd))
                PostMessage(state.Hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }

        private static void FitController(IntPtr hwnd, CoreWebView2Controller controller)
        {
            if (!GetClientRect(hwnd, out RECT rect))
                return;
            controller.Bounds = new System.Drawing.Rectangle(0, 0, rect.Right - rect.Left, rect.Bottom - rect.Top);
        }

        private sealed class HostState(IntPtr hwnd, TaskCompletionSource<string?> completion)
        {
            public IntPtr Hwnd { get; } = hwnd;
            public TaskCompletionSource<string?> Completion { get; } = completion;
            public CoreWebView2Controller? Controller { get; set; }
            public bool Completed { get; set; }
        }

        #region Win32
        private const int WS_OVERLAPPEDWINDOW = 0x00CF0000;
        private const int WS_VISIBLE = 0x10000000;
        private const int CW_USEDEFAULT = unchecked((int)0x80000000);
        private const int SW_SHOW = 5;
        private const int WM_SIZE = 0x0005;
        private const int WM_DESTROY = 0x0002;
        private const int WM_CLOSE = 0x0010;
        private const int GWLP_USERDATA = -21;

        private static bool _classRegistered;
        private static readonly object _classLock = new();
        private static WndProcDelegate? _wndProcKeepAlive;

        private static void EnsureWindowClass()
        {
            lock (_classLock)
            {
                if (_classRegistered)
                    return;

                _wndProcKeepAlive = WndProc;
                var wc = new WNDCLASS
                {
                    lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcKeepAlive),
                    hInstance = GetModuleHandleW(null),
                    lpszClassName = WindowClassName,
                    hCursor = LoadCursorW(IntPtr.Zero, (IntPtr)32512),
                    hbrBackground = (IntPtr)6
                };

                if (RegisterClassW(ref wc) == 0 && Marshal.GetLastWin32Error() != 1410)
                    throw new InvalidOperationException("Failed to register VIP picker window class.");

                _classRegistered = true;
            }
        }

        private static IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_SIZE)
            {
                var ptr = GetWindowLongPtr(hwnd, GWLP_USERDATA);
                if (ptr != IntPtr.Zero)
                {
                    var state = (HostState)GCHandle.FromIntPtr(ptr).Target!;
                    if (state.Controller is not null)
                        FitController(hwnd, state.Controller);
                }
            }
            else if (msg == WM_DESTROY)
            {
                PostQuitMessage(0);
            }

            return DefWindowProcW(hwnd, msg, wParam, lParam);
        }

        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASS
        {
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public string? lpszMenuName;
            public string lpszClassName;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public int pt_x;
            public int pt_y;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern ushort RegisterClassW(ref WNDCLASS lpWndClass);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowExW(
            int dwExStyle, string lpClassName, string lpWindowName, int dwStyle,
            int x, int y, int nWidth, int nHeight,
            IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] private static extern bool UpdateWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern void PostQuitMessage(int nExitCode);
        [DllImport("user32.dll")] private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);
        [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG lpMsg);
        [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref MSG lpMsg);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadCursorW(IntPtr hInstance, IntPtr lpCursorName);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandleW(string? lpModuleName);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
        #endregion
    }
}
