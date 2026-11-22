using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using Froststrap.Integrations;
using Microsoft.Web.WebView2.Core;

namespace Froststrap.UI.Elements.Controls;

/// <summary>
/// Shows the signed-in avatar with the RoAvatar renderer, the same viewer the
/// account manager uses. Roblox files are downloaded here, and the page never
/// receives the account cookie.
/// </summary>
public partial class AccountStage : NativeControlHost
{
    private const int WsChild = 0x40000000;
    private const int WsVisible = 0x10000000;
    private const int WsClipChildren = 0x02000000;

    private readonly AccountStudioClient _client = new();
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private IntPtr _host;
    private CoreWebView2Controller? _controller;
    private string? _folder;
    private string? _pending;
    private bool _scriptReady;
    private int _file;

    public event Action<string, string?>? StatusChanged;

    public Task ApplyAsync(string? document)
    {
        _pending = document;
        return FlushAsync();
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        if (!OperatingSystem.IsWindows())
            return base.CreateNativeControlCore(parent);

        _host = CreateWindowEx(0, "static", "", WsChild | WsVisible | WsClipChildren, 0, 0, 320, 420, parent.Handle, 0, 0, 0);
        if (_host == 0)
        {
            _ready.TrySetException(new InvalidOperationException("Could not create the 3D view."));
            return base.CreateNativeControlCore(parent);
        }

        _ = StartAsync(_host);
        return new PlatformHandle(_host, "HWND");
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        _controller?.Close();
        _controller = null;
        if (control.Handle != 0)
            DestroyWindow(control.Handle);
        base.DestroyNativeControlCore(control);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        Fit();
    }

    private async Task StartAsync(IntPtr host)
    {
        try
        {
            _folder = PrepareFolder();
            var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Eclipse", "WebView2");
            Directory.CreateDirectory(data);
            var environment = await CoreWebView2Environment.CreateAsync(null, data);
            var controller = await environment.CreateCoreWebView2ControllerAsync(host);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _controller = controller;
                controller.DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 12, 12, 14);
                controller.IsVisible = true;
                var web = controller.CoreWebView2;
                web.Settings.IsStatusBarEnabled = false;
                web.Settings.AreDefaultContextMenusEnabled = false;
                web.Settings.IsZoomControlEnabled = false;
                web.Settings.AreDevToolsEnabled = false;
                web.Settings.IsWebMessageEnabled = true;
                web.WebMessageReceived += OnMessage;
                web.SetVirtualHostNameToFolderMapping("spectre.stage", _folder, CoreWebView2HostResourceAccessKind.Allow);
                web.Navigate("https://spectre.stage/index.html");
                Fit();
                _ready.TrySetResult();
            });
        }
        catch (Exception ex)
        {
            App.Logger.WriteException("AccountStage", ex);
            _ready.TrySetException(ex);
        }
    }

    private static string PrepareFolder()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Eclipse", "Stage");
        Directory.CreateDirectory(folder);
        var assembly = typeof(AccountStage).Assembly;
        var source = Path.Combine(AppContext.BaseDirectory, "Stage", "www");
        foreach (var name in new[] { "index.html", "viewer.js", "draco_decoder.js", "roavatar-renderer-LICENSE.txt" })
        {
            var to = Path.Combine(folder, name);
            using var packed = assembly.GetManifestResourceStream("Froststrap.Stage.www." + name);
            if (packed is not null)
            {
                WriteIfChanged(packed, to);
                continue;
            }

            var from = Path.Combine(source, name);
            if (File.Exists(from) && (!File.Exists(to) || File.GetLastWriteTimeUtc(from) > File.GetLastWriteTimeUtc(to)))
                File.Copy(from, to, true);
        }

        if (!File.Exists(Path.Combine(folder, "viewer.js")))
            throw new InvalidOperationException("The avatar viewer is missing.");

        var cache = Path.Combine(folder, "cache");
        Directory.CreateDirectory(cache);
        foreach (var file in Directory.EnumerateFiles(cache))
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
            }
        }

        return folder;
    }

    private static void WriteIfChanged(Stream source, string destination)
    {
        using var memory = new MemoryStream();
        source.CopyTo(memory);
        var bytes = memory.ToArray();
        if (File.Exists(destination))
        {
            var existing = new FileInfo(destination);
            if (existing.Length == bytes.Length)
            {
                var current = File.ReadAllBytes(destination);
                if (current.AsSpan().SequenceEqual(bytes))
                    return;
            }
        }

        File.WriteAllBytes(destination, bytes);
    }

    private async void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var document = JsonDocument.Parse(e.WebMessageAsJson);
            var root = document.RootElement;
            var kind = root.TryGetProperty("kind", out var kindValue) ? kindValue.GetString() : null;
            if (kind == "status")
            {
                var state = root.TryGetProperty("state", out var stateValue) ? stateValue.GetString() ?? "" : "";
                var message = root.TryGetProperty("message", out var messageValue) && messageValue.ValueKind == JsonValueKind.String ? messageValue.GetString() : null;
                App.Logger.WriteLine("AccountStage", string.IsNullOrWhiteSpace(message) ? state : state + ": " + message);
                if (state == "ready")
                {
                    _scriptReady = true;
                    await FlushAsync();
                }
                else
                    StatusChanged?.Invoke(state, message);
                return;
            }

            if (kind != "fetch" || !root.TryGetProperty("id", out var idValue) || !idValue.TryGetInt32(out var id))
                return;

            var address = root.TryGetProperty("url", out var urlValue) ? urlValue.GetString() : null;
            var headers = new Dictionary<string, string>(StringComparer.Ordinal);
            if (root.TryGetProperty("headers", out var bag) && bag.ValueKind == JsonValueKind.Object &&
                bag.TryGetProperty("Roblox-AssetFormat", out var format) && format.ValueKind == JsonValueKind.String)
            {
                var text = format.GetString();
                if (!string.IsNullOrWhiteSpace(text) && text.Length <= 64 && text.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '_'))
                    headers["Roblox-AssetFormat"] = text;
            }

            await AnswerAsync(id, address, headers);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            App.Logger.WriteException("AccountStage", ex);
        }
    }

    private async Task AnswerAsync(int id, string? address, Dictionary<string, string> headers)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(address) || address.Length > 4096 || !Uri.TryCreate(address, UriKind.Absolute, out var url))
                throw new RobloxApiException("The avatar viewer asked for a file Spectre cannot download.");

            var reply = await _client.ReadRenderRequestAsync(url, headers, CancellationToken.None);
            if (_folder is null)
                return;

            var name = $"{Interlocked.Increment(ref _file)}.bin";
            await File.WriteAllBytesAsync(Path.Combine(_folder, "cache", name), reply.Body);
            await PostAsync(new
            {
                kind = "file",
                id,
                status = reply.Status,
                type = reply.ContentType,
                local = "https://spectre.stage/cache/" + name,
                asLocation = reply.AsLocation
            });
        }
        catch (Exception ex) when (ex is RobloxApiException or HttpRequestException or IOException)
        {
            await PostAsync(new { kind = "file", id, error = ex is RobloxApiException or HttpRequestException ? ex.Message : "The file could not be downloaded." });
        }
    }

    private async Task FlushAsync()
    {
        try
        {
            await _ready.Task;
        }
        catch (Exception ex)
        {
            App.Logger.WriteException("AccountStage", ex);
            StatusChanged?.Invoke("failed", "The 3D view could not open.");
            return;
        }

        var document = _pending;
        if (!_scriptReady || string.IsNullOrWhiteSpace(document))
            return;

        await PostAsync(JsonDocument.Parse(document));
    }

    private Task PostAsync(object message)
    {
        var json = message is JsonDocument document ? document.RootElement.GetRawText() : JsonSerializer.Serialize(message);
        return Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_controller is null)
                return;

            _controller.CoreWebView2.PostWebMessageAsJson(json);
            if (message is JsonDocument document)
                document.Dispose();
        }).GetTask();
    }

    private void Fit()
    {
        if (_controller is null || _host == 0 || !GetClientRect(_host, out var rect))
            return;

        _controller.Bounds = System.Drawing.Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
    }

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr CreateWindowEx(int exStyle, string className, string windowName, int style, int x, int y, int width, int height, IntPtr parent, nint menu, nint instance, nint param);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetClientRect(IntPtr hwnd, out RectNative rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RectNative
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
