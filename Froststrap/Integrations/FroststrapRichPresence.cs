using System.Net.Sockets;
using DiscordRPC;

namespace Froststrap.Integrations
{
    public class FroststrapRichPresence : IDisposable
    {
        /// <summary>
        /// Spectre Discord application.
        /// Client IDs are public; manage the app at https://discord.com/developers/applications
        /// </summary>
        public const string SpectreApplicationId = "1529388012620746913";
        public const string EclipseApplicationId = SpectreApplicationId;

        /// <summary>Hosted Spectre mark — Discord accepts external image URLs for LargeImageKey.</summary>
        public const string SpectreLogoUrl =
            "https://cdn.jsdelivr.net/gh/lec1e/Spectre@main/Froststrap/SpectreMark.png";
        private const string EclipseLogoUrl = SpectreLogoUrl;

        private readonly DiscordRpcClient? _rpcClient;
        private readonly Timestamps _startTimestamps;
        private readonly Stopwatch _uptimeStopwatch;
        private bool _disposed = false;
        private string _currentPage = "Idle";
        private string? _currentDialog = null;
        private string _lastState = "";
        private readonly bool _isMacOS;

        public bool IsConnected => _rpcClient?.IsInitialized == true;

        public FroststrapRichPresence()
        {
            const string LOG_IDENT = "FroststrapRichPresence";

            _isMacOS = OperatingSystem.IsMacOS();

            if (_isMacOS)
            {
                App.Logger.WriteLine(LOG_IDENT, "Skipping Discord RPC initialization on macOS");
                _rpcClient = null!;
                _startTimestamps = new Timestamps { Start = DateTime.UtcNow };
                _uptimeStopwatch = Stopwatch.StartNew();
                return;
            }

            _rpcClient = new DiscordRpcClient(SpectreApplicationId)
            {
                SkipIdenticalPresence = false
            };

            _rpcClient.OnReady += OnReady;
            _rpcClient.OnConnectionEstablished += OnConnectionEstablished;
            _rpcClient.OnError += OnError;
            _rpcClient.OnClose += OnClose;

            _startTimestamps = new Timestamps
            {
                Start = DateTime.UtcNow
            };

            _uptimeStopwatch = Stopwatch.StartNew();

            try
            {
                _rpcClient.Initialize();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Failed to init RPC: {ex.Message}");
            }
        }

        private void OnReady(object sender, DiscordRPC.Message.ReadyMessage args)
        {
            if (_disposed || _isMacOS) return;

            App.Logger.WriteLine("FroststrapRichPresence", $"Connected as {args.User.Username}");
            _lastState = "";
            UpdatePresence();
        }

        private void OnConnectionEstablished(object sender, DiscordRPC.Message.ConnectionEstablishedMessage args)
        {
            if (_disposed || _isMacOS) return;

            App.Logger.WriteLine("FroststrapRichPresence", "Established connection with Discord RPC");
            _lastState = "";
            UpdatePresence();
        }

        private void OnError(object sender, DiscordRPC.Message.ErrorMessage args)
        {
            App.Logger.WriteLine("FroststrapRichPresence", $"An RPC error occurred - {args.Message}");
        }

        private void OnClose(object sender, DiscordRPC.Message.CloseMessage args)
        {
            App.Logger.WriteLine("FroststrapRichPresence", $"Lost connection to Discord RPC - {args.Reason} ({args.Code})");
        }

        public void SetPage(string pageName)
        {
            if (_disposed || _isMacOS) return;

            _currentPage = pageName;
            _currentDialog = null;
            UpdatePresence();
        }

        public void SetDialog(string dialogName)
        {
            if (_disposed || _isMacOS) return;

            _currentDialog = dialogName;
            UpdatePresence();
        }

        public void ClearDialog()
        {
            if (_disposed || _isMacOS) return;

            _currentDialog = null;
            UpdatePresence();
        }

        public void UpdatePresence()
        {
            const string LOG_IDENT = "FroststrapRichPresence";

            if (_disposed || _isMacOS || _rpcClient == null || !_rpcClient.IsInitialized)
                return;

            string state = !string.IsNullOrEmpty(_currentDialog)
                ? $"Page: {_currentPage} | Dialog: {_currentDialog}"
                : (_currentPage is "Idle" or "Home"
                    ? "Customize Roblox to your liking!"
                    : $"Page: {_currentPage}");

            // Fingerprint includes assets so logo/title updates aren't skipped.
            string fingerprint = $"{state}|spectre|{App.Version}";
            if (fingerprint == _lastState)
                return;

            _lastState = fingerprint;

            var presence = new DiscordRPC.RichPresence
            {
                // Status shows the product name (Spectre). The Discord application id is
                // unchanged so existing rich-presence installs keep working.
                Details = App.BrandName,
                State = state,
                Type = ActivityType.Playing,
                StatusDisplay = StatusDisplayType.Details,
                Timestamps = _startTimestamps,
                Assets = new Assets
                {
                    LargeImageKey = EclipseLogoUrl,
                    LargeImageText = App.BrandName,
                    SmallImageText = $"v{App.Version}"
                },
                Buttons =
                [
                    new Button { Label = "GitHub", Url = $"https://github.com/{App.ProjectRepository}" }
                ]
            };

            try
            {
                _rpcClient.SetPresence(presence);
            }
            catch (IOException ex) when (ex.InnerException is SocketException)
            {
                App.Logger.WriteLine(LOG_IDENT, "Socket interrupted (Operation Canceled). This is expected on macOS.");
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
                try
                {
                    presence.Assets = null;
                    _rpcClient.SetPresence(presence);
                }
                catch (Exception retryEx)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Presence retry without assets failed: {retryEx.Message}");
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            App.Logger.WriteLine("FroststrapRichPresence::Dispose", "Cleaning up Discord RPC");

            if (_rpcClient != null)
            {
                try
                {
                    _rpcClient.OnReady -= OnReady;
                    _rpcClient.OnConnectionEstablished -= OnConnectionEstablished;
                    _rpcClient.OnError -= OnError;
                    _rpcClient.OnClose -= OnClose;

                    if (_rpcClient.IsInitialized)
                    {
                        try
                        {
                            _rpcClient.ClearPresence();
                        }
                        catch (IOException) { /* Ignore pipe closure issues */ }
                    }

                    _rpcClient.Dispose();
                }
                catch (IOException ex) when (ex.InnerException is SocketException)
                {
                    // Ignore
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine("FroststrapRichPresence::Dispose", $"Cleanup error: {ex.Message}");
                }
            }

            _uptimeStopwatch.Stop();
            GC.SuppressFinalize(this);
        }
    }
}
