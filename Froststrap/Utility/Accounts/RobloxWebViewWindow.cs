using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;

namespace Froststrap.Utility.Accounts
{
    /// <summary>
    /// AltMan-style isolated WebView2 window for Roblox login / cookie browsing (Windows only).
    /// Runs on a dedicated STA thread with its own Win32 message pump.
    /// </summary>
    internal static class RobloxWebViewWindow
    {
        private const string LOG_IDENT = "RobloxWebViewWindow";
        private const string LoginUrl = "https://www.roblox.com/login";
        private const string HomeNeedle = "roblox.com/home";

        public static Task<string?> CaptureLoginCookieAsync(CancellationToken cancellationToken = default)
            => ShowAsync($"{App.BrandName} — Roblox login", LoginUrl, null, monitorLogin: true, cancellationToken);

        public static async Task BrowseWithCookieAsync(string cookie, string url = "https://www.roblox.com/home", CancellationToken cancellationToken = default)
            => await ShowAsync($"{App.BrandName} — Roblox", url, cookie, monitorLogin: false, cancellationToken).ConfigureAwait(false);

        private static Task<string?> ShowAsync(
            string title,
            string url,
            string? cookieToInject,
            bool monitorLogin,
            CancellationToken cancellationToken)
        {
            if (!OperatingSystem.IsWindows())
            {
                App.Logger.WriteLine(LOG_IDENT, "WebView2 login is Windows-only.");
                return Task.FromResult<string?>(null);
            }

            var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            string userData = Path.Combine(
                Paths.LocalAppData, App.ProjectName, "WebViewProfiles",
                monitorLogin ? "Login" : "Browse",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(userData);

            var thread = new Thread(() =>
            {
                try
                {
                    RunStaWindow(title, url, cookieToInject, monitorLogin, userData, tcs, cancellationToken);
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
                Name = "Eclipse-WebView2"
            };

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return tcs.Task;
        }

        private static void RunStaWindow(
            string title,
            string url,
            string? cookieToInject,
            bool monitorLogin,
            string userDataFolder,
            TaskCompletionSource<string?> tcs,
            CancellationToken cancellationToken)
        {
            EnsureWindowClass();

            IntPtr hwnd = CreateWindowExW(
                0, WindowClassName, title,
                WS_OVERLAPPEDWINDOW | WS_VISIBLE,
                CW_USEDEFAULT, CW_USEDEFAULT, 1280, 800,
                IntPtr.Zero, IntPtr.Zero, GetModuleHandleW(null), IntPtr.Zero);

            if (hwnd == IntPtr.Zero)
            {
                tcs.TrySetResult(null);
                return;
            }

            ShowWindow(hwnd, SW_SHOW);
            UpdateWindow(hwnd);

            var state = new HostState(hwnd, tcs, monitorLogin);
            var stateHandle = GCHandle.Alloc(state);
            SetWindowLongPtr(hwnd, GWLP_USERDATA, GCHandle.ToIntPtr(stateHandle));

            // Kick off WebView2 init without blocking the message pump (avoids COM deadlock).
            _ = InitializeWebViewAsync(state, url, cookieToInject, userDataFolder);

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

            try
            {
                state.Controller?.Close();
            }
            catch { /* ignore */ }

            if (stateHandle.IsAllocated)
                stateHandle.Free();
        }

        private static async Task InitializeWebViewAsync(
            HostState state,
            string url,
            string? cookieToInject,
            string userDataFolder)
        {
            try
            {
                var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder).ConfigureAwait(true);
                var controller = await env.CreateCoreWebView2ControllerAsync(state.Hwnd).ConfigureAwait(true);
                state.Controller = controller;
                var webview = controller.CoreWebView2;

                FitController(state.Hwnd, controller);

                if (!string.IsNullOrEmpty(cookieToInject))
                {
                    var cookie = webview.CookieManager.CreateCookie(".ROBLOSECURITY", cookieToInject, ".roblox.com", "/");
                    cookie.IsSecure = true;
                    cookie.IsHttpOnly = true;
                    cookie.Expires = DateTime.UtcNow.AddYears(10);
                    webview.CookieManager.AddOrUpdateCookie(cookie);
                }

                if (state.MonitorLogin)
                {
                    webview.NavigationCompleted += async (_, args) =>
                    {
                        if (!args.IsSuccess || state.CookieCaptured)
                            return;

                        if (!webview.Source.Contains(HomeNeedle, StringComparison.OrdinalIgnoreCase))
                            return;

                        try
                        {
                            var cookies = await webview.CookieManager.GetCookiesAsync("https://www.roblox.com");
                            string? value = cookies.FirstOrDefault(c => c.Name == ".ROBLOSECURITY")?.Value;
                            if (string.IsNullOrEmpty(value))
                                return;

                            state.CookieCaptured = true;
                            state.Completion.TrySetResult(value);
                            PostMessage(state.Hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                        }
                        catch (Exception ex)
                        {
                            App.Logger.WriteException(LOG_IDENT + "::ReadCookie", ex);
                        }
                    };
                }

                webview.Navigate(url);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT + "::Init", ex);
                state.Completion.TrySetResult(null);
                if (IsWindow(state.Hwnd))
                    PostMessage(state.Hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            }
        }

        private static void FitController(IntPtr hwnd, CoreWebView2Controller controller)
        {
            GetClientRect(hwnd, out RECT rc);
            controller.Bounds = new System.Drawing.Rectangle(rc.left, rc.top, Math.Max(0, rc.right - rc.left), Math.Max(0, rc.bottom - rc.top));
        }

        private sealed class HostState(IntPtr hwnd, TaskCompletionSource<string?> completion, bool monitorLogin)
        {
            public IntPtr Hwnd { get; } = hwnd;
            public TaskCompletionSource<string?> Completion { get; } = completion;
            public bool MonitorLogin { get; } = monitorLogin;
            public CoreWebView2Controller? Controller { get; set; }
            public bool CookieCaptured { get; set; }
        }

        private static readonly object ClassLock = new();
        private static bool _classRegistered;
        private const string WindowClassName = "Eclipse_RobloxWebView2";
        private static WndProcDelegate? _wndProcKeepAlive;

        private static void EnsureWindowClass()
        {
            lock (ClassLock)
            {
                if (_classRegistered)
                    return;

                _wndProcKeepAlive = WndProc;
                var wc = new WNDCLASSEXW
                {
                    cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                    style = CS_HREDRAW | CS_VREDRAW,
                    lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcKeepAlive),
                    hInstance = GetModuleHandleW(null),
                    hCursor = LoadCursorW(IntPtr.Zero, (IntPtr)IDC_ARROW),
                    lpszClassName = WindowClassName
                };

                if (RegisterClassExW(ref wc) == 0)
                {
                    int err = Marshal.GetLastWin32Error();
                    if (err != 1410)
                        throw new InvalidOperationException($"RegisterClassEx failed: {err}");
                }

                _classRegistered = true;
            }
        }

        private static IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_SIZE)
            {
                IntPtr ptr = GetWindowLongPtr(hwnd, GWLP_USERDATA);
                if (ptr != IntPtr.Zero)
                {
                    try
                    {
                        var handle = GCHandle.FromIntPtr(ptr);
                        if (handle.Target is HostState { Controller: { } controller })
                            FitController(hwnd, controller);
                    }
                    catch { /* ignore */ }
                }
            }
            else if (msg == WM_DESTROY)
            {
                PostQuitMessage(0);
                return IntPtr.Zero;
            }

            return DefWindowProcW(hwnd, msg, wParam, lParam);
        }

        private const int CS_HREDRAW = 0x0002;
        private const int CS_VREDRAW = 0x0001;
        private const int WS_OVERLAPPEDWINDOW = 0x00CF0000;
        private const int WS_VISIBLE = 0x10000000;
        private const int CW_USEDEFAULT = unchecked((int)0x80000000);
        private const int SW_SHOW = 5;
        private const int WM_SIZE = 0x0005;
        private const int WM_DESTROY = 0x0002;
        private const int WM_CLOSE = 0x0010;
        private const int IDC_ARROW = 32512;
        private const int GWLP_USERDATA = -21;

        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASSEXW
        {
            public uint cbSize;
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
            public IntPtr hIconSm;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int left, top, right, bottom; }

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
        private static extern ushort RegisterClassExW(ref WNDCLASSEXW lpwcx);

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
        [DllImport("user32.dll")] private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);
        [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG lpMsg);
        [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref MSG lpMsg);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadCursorW(IntPtr hInstance, IntPtr lpCursorName);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandleW(string? lpModuleName);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern IntPtr SetWindowLong32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern IntPtr GetWindowLong32(IntPtr hWnd, int nIndex);

        private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
            => IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong) : SetWindowLong32(hWnd, nIndex, dwNewLong);

        private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
            => IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : GetWindowLong32(hWnd, nIndex);
    }
}
