using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Froststrap.Models;
using Froststrap.Models.APIs.Roblox;
using Froststrap.Utility;
using Froststrap.Utility.Accounts;

namespace Froststrap.Integrations
{
    /// <summary>
    /// AltMan-backed account store. Cookies encrypted with AES-256-GCM;
    /// master key in Windows Credential Manager (DPAPI file fallback).
    /// </summary>
    public class AccountManager
    {
        private const string LOG_IDENT = "AccountManager";
        private const string FileName = "accounts.v2.json";
        private const int SchemaVersion = 2;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public event Action<AltManAccount?>? ActiveAccountChanged;

        private readonly string _filePath;
        private readonly byte[] _masterKey;
        private readonly ObservableCollection<AltManAccount> _accounts = [];
        private AltManAccount? _activeAccount;
        private int _nextId = 1;

        public static AccountManager Shared { get; } = new();

        public ObservableCollection<AltManAccount> Accounts => _accounts;
        public IReadOnlyList<AltManAccount> AccountList => _accounts;

        public AltManAccount? ActiveAccount
        {
            get => _activeAccount;
            private set
            {
                _activeAccount = value;
                ActiveAccountChanged?.Invoke(value);
            }
        }

        public long CurrentPlaceId { get; set; }
        public string CurrentServerInstanceId { get; set; } = "";
        public int DefaultAccountId { get; set; } = -1;

        public HashSet<int> SelectedAccountIds { get; } = [];

        private AccountManager()
        {
            _filePath = Path.Combine(Paths.Base, "AltMan", FileName);
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            _masterKey = AccountMasterKeyStore.GetOrCreateMasterKey();
            Load();
            MigrateLegacyIfNeeded();
        }

        public void Load()
        {
            _accounts.Clear();
            if (!File.Exists(_filePath))
                return;

            try
            {
                string json = File.ReadAllText(_filePath);
                var dto = JsonSerializer.Deserialize<AccountsFileDto>(json, JsonOptions);
                if (dto?.Accounts is null)
                    return;

                DefaultAccountId = dto.DefaultAccountId;
                foreach (var row in dto.Accounts)
                {
                    var acc = FromDto(row);
                    _accounts.Add(acc);
                    _nextId = Math.Max(_nextId, acc.Id + 1);
                }

                if (dto.ActiveAccountId is int activeId)
                    ActiveAccount = _accounts.FirstOrDefault(a => a.Id == activeId)
                        ?? _accounts.FirstOrDefault(a => a.UserIdLong == activeId);

                App.Logger.WriteLine(LOG_IDENT, $"Loaded {_accounts.Count} AltMan account(s).");
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT + "::Load", ex);
            }
        }

        public void Save()
        {
            try
            {
                var dto = new AccountsFileDto
                {
                    Version = SchemaVersion,
                    DefaultAccountId = DefaultAccountId,
                    ActiveAccountId = ActiveAccount?.Id,
                    Accounts = _accounts.Select(ToDto).ToList()
                };

                string tmp = _filePath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(dto, JsonOptions));
                File.Copy(tmp, _filePath, overwrite: true);
                File.Delete(tmp);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT + "::Save", ex);
            }
        }

        public void SetActiveAccount(AltManAccount? account)
        {
            ActiveAccount = account;
            if (account is not null)
                DefaultAccountId = account.Id;
            Save();
        }

        public void SetActiveAccount(long? userId)
        {
            if (userId is null)
            {
                ActiveAccount = null;
                Save();
                return;
            }

            var acc = _accounts.FirstOrDefault(a => a.UserIdLong == userId);
            SetActiveAccount(acc);
        }

        public string? GetRoblosecurityForUser(long userId)
            => _accounts.FirstOrDefault(a => a.UserIdLong == userId)?.Cookie;

        public void AddAccount(AltManAccount account)
        {
            if (account.Id <= 0)
                account.Id = _nextId++;

            var existing = _accounts.FirstOrDefault(a =>
                !string.IsNullOrEmpty(account.UserId) && a.UserId == account.UserId);

            if (existing is not null)
            {
                existing.Cookie = account.Cookie;
                existing.DisplayName = account.DisplayName;
                existing.Username = account.Username;
                existing.Status = account.Status;
                existing.Note = account.Note;
                existing.HbaPrivateKey = account.HbaPrivateKey;
                existing.HbaEnabled = account.HbaEnabled;
                SetActiveAccount(existing);
            }
            else
            {
                _accounts.Add(account);
                SetActiveAccount(account);
            }

            Save();
        }

        // Compatibility for QuickPlay / older callers expecting AccountManagerAccount
        public void AddAccount(AccountManagerAccount legacy)
        {
            AddAccount(new AltManAccount
            {
                Cookie = legacy.SecurityToken,
                UserId = legacy.UserId.ToString(),
                Username = legacy.Username,
                DisplayName = legacy.DisplayName,
                Status = "Online"
            });
        }

        public bool RemoveAccount(AltManAccount account)
        {
            bool removed = _accounts.Remove(account);
            if (removed)
            {
                SelectedAccountIds.Remove(account.Id);
                if (ActiveAccount?.Id == account.Id)
                    ActiveAccount = _accounts.FirstOrDefault();
                Save();
            }
            return removed;
        }

        public bool RemoveAccount(AccountManagerAccount legacy)
        {
            var acc = _accounts.FirstOrDefault(a => a.UserIdLong == legacy.UserId);
            return acc is not null && RemoveAccount(acc);
        }

        public async Task<AltManAccount?> AddAccountByCookieAsync(string cookie)
        {
            cookie = (cookie ?? "").Trim();
            if (string.IsNullOrEmpty(cookie))
                return null;

            var info = await FetchUserFromCookieAsync(cookie);
            if (info is null)
                return null;

            var account = new AltManAccount
            {
                Cookie = cookie,
                UserId = info.Value.UserId.ToString(),
                Username = info.Value.Username,
                DisplayName = info.Value.DisplayName,
                Status = "Online",
                HbaEnabled = true
            };

            AddAccount(account);
            return account;
        }

        /// <summary>
        /// Isolated client for per-account cookie requests. Must not reuse
        /// <see cref="App.HttpClient"/> — its shared CookieContainer contaminates
        /// .ROBLOSECURITY across accounts and blocks AltMan saves.
        /// </summary>
        private static HttpClient CreateCookieIsolatedClient()
        {
            var handler = new HttpClientHandler
            {
                UseCookies = false,
                AutomaticDecompression = DecompressionMethods.All
            };
            return new HttpClient(handler);
        }

        public static async Task<(long UserId, string Username, string DisplayName)?> FetchUserFromCookieAsync(string cookie)
        {
            try
            {
                using var client = CreateCookieIsolatedClient();
                using var req = new HttpRequestMessage(HttpMethod.Get, "https://users.roblox.com/v1/users/authenticated");
                req.Headers.TryAddWithoutValidation("Cookie", $".ROBLOSECURITY={cookie}");
                using var resp = await client.SendAsync(req);
                if (!resp.IsSuccessStatusCode)
                {
                    App.Logger.WriteLine(LOG_IDENT + "::FetchUser",
                        $"Authenticated user lookup failed: {(int)resp.StatusCode} {resp.ReasonPhrase}");
                    return null;
                }

                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                long id = doc.RootElement.GetProperty("id").GetInt64();
                string name = doc.RootElement.GetProperty("name").GetString() ?? "";
                string display = doc.RootElement.TryGetProperty("displayName", out var dn)
                    ? dn.GetString() ?? name
                    : name;
                return (id, name, display);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT + "::FetchUser", ex);
                return null;
            }
        }

        public static async Task<bool> ValidateAccountAsync(AltManAccount account)
        {
            if (string.IsNullOrEmpty(account.Cookie))
                return false;

            var info = await FetchUserFromCookieAsync(account.Cookie);
            if (info is null)
            {
                account.Status = "Invalid";
                return false;
            }

            account.UserId = info.Value.UserId.ToString();
            account.Username = info.Value.Username;
            account.DisplayName = info.Value.DisplayName;
            if (account.Status is "Invalid" or "Unknown")
                account.Status = "Online";
            return true;
        }

        public static async Task<bool> ValidateAccountAsync(AccountManagerAccount account)
        {
            var info = await FetchUserFromCookieAsync(account.SecurityToken);
            return info is not null;
        }

        public static async Task RefreshPresenceAsync(AltManAccount account)
        {
            if (string.IsNullOrEmpty(account.Cookie) || account.UserIdLong == 0)
                return;

            try
            {
                using var client = CreateCookieIsolatedClient();
                using var req = new HttpRequestMessage(HttpMethod.Post, "https://presence.roblox.com/v1/presence/users");
                req.Headers.TryAddWithoutValidation("Cookie", $".ROBLOSECURITY={account.Cookie}");
                req.Content = new StringContent(
                    JsonSerializer.Serialize(new { userIds = new[] { account.UserIdLong } }),
                    Encoding.UTF8,
                    "application/json");

                using var resp = await client.SendAsync(req);
                if (!resp.IsSuccessStatusCode)
                    return;

                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                if (!doc.RootElement.TryGetProperty("userPresences", out var list) || list.GetArrayLength() == 0)
                    return;

                var p = list[0];
                int type = p.TryGetProperty("userPresenceType", out var t) ? t.GetInt32() : 0;
                account.Status = type switch
                {
                    0 => "Offline",
                    1 => "Online",
                    2 => "InGame",
                    3 => "InStudio",
                    _ => account.Status
                };
                account.LastLocation = p.TryGetProperty("lastLocation", out var loc) ? loc.GetString() ?? "" : "";
                account.PlaceId = p.TryGetProperty("placeId", out var pid) && pid.ValueKind == JsonValueKind.Number
                    ? pid.GetUInt64()
                    : 0;
                account.JobId = p.TryGetProperty("gameId", out var gid) ? gid.GetString() ?? "" : "";
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT + "::RefreshPresence", ex);
            }
        }

        public static async Task RefreshBanStatusAsync(AltManAccount account)
        {
            if (string.IsNullOrEmpty(account.Cookie))
                return;

            try
            {
                using var client = CreateCookieIsolatedClient();
                using var req = new HttpRequestMessage(HttpMethod.Get, "https://usermoderation.roblox.com/v1/not-approved");
                req.Headers.TryAddWithoutValidation("Cookie", $".ROBLOSECURITY={account.Cookie}");
                using var resp = await client.SendAsync(req);

                if ((int)resp.StatusCode == 401 || (int)resp.StatusCode == 403)
                {
                    // Not banned / endpoint denied for valid cookies — leave status alone
                    return;
                }

                if (resp.IsSuccessStatusCode)
                {
                    string body = await resp.Content.ReadAsStringAsync();
                    if (body.Contains("Ban", StringComparison.OrdinalIgnoreCase)
                        || body.Contains("Terminated", StringComparison.OrdinalIgnoreCase)
                        || body.Contains("Warned", StringComparison.OrdinalIgnoreCase))
                    {
                        if (body.Contains("Terminated", StringComparison.OrdinalIgnoreCase))
                            account.Status = "Terminated";
                        else if (body.Contains("Warned", StringComparison.OrdinalIgnoreCase))
                            account.Status = "Warned";
                        else
                            account.Status = "Banned";
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT + "::RefreshBan", ex);
            }
        }

        public static async Task RefreshAgeGroupAsync(AltManAccount account)
        {
            if (string.IsNullOrEmpty(account.Cookie))
                return;

            try
            {
                using var client = CreateCookieIsolatedClient();
                using var req = new HttpRequestMessage(HttpMethod.Get, "https://apis.roblox.com/user-settings-api/v1/account-insights/age-group");
                req.Headers.TryAddWithoutValidation("Cookie", $".ROBLOSECURITY={account.Cookie}");
                using var resp = await client.SendAsync(req);
                if (!resp.IsSuccessStatusCode)
                    return;

                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                string key = doc.RootElement.TryGetProperty("ageGroupTranslationKey", out var k)
                    ? k.GetString() ?? ""
                    : "";
                account.AgeGroup = key switch
                {
                    "Label.AgeGroupUnder9" => "<9",
                    "Label.AgeGroup9To12" => "9-12",
                    "Label.AgeGroup13To15" => "13-15",
                    "Label.AgeGroup16To17" => "16-17",
                    "Label.AgeGroup18To20" => "18-20",
                    "Label.AgeGroupOver21" => "21+",
                    _ => string.IsNullOrEmpty(key) ? account.AgeGroup : key.Replace("Label.AgeGroup", "")
                };
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT + "::RefreshAge", ex);
            }
        }

        public static async Task RefreshVoiceStatusAsync(AltManAccount account)
        {
            if (string.IsNullOrEmpty(account.Cookie))
                return;

            if (account.IsBannedLike || account.Status == "Invalid")
            {
                account.VoiceStatus = "N/A";
                return;
            }

            try
            {
                using var client = CreateCookieIsolatedClient();
                using var req = new HttpRequestMessage(HttpMethod.Get, "https://voice.roblox.com/v1/settings");
                req.Headers.TryAddWithoutValidation("Cookie", $".ROBLOSECURITY={account.Cookie}");
                using var resp = await client.SendAsync(req);
                if (!resp.IsSuccessStatusCode)
                {
                    account.VoiceStatus = "Unknown";
                    return;
                }

                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                var root = doc.RootElement;
                bool banned = root.TryGetProperty("isBanned", out var b) && b.GetBoolean();
                bool enabled = root.TryGetProperty("isVoiceEnabled", out var e) && e.GetBoolean();
                bool eligible = root.TryGetProperty("isUserEligible", out var el) && el.GetBoolean();
                bool opted = root.TryGetProperty("isUserOptIn", out var o) && o.GetBoolean();

                if (banned)
                {
                    account.VoiceStatus = "Banned";
                    if (root.TryGetProperty("bannedUntil", out var until)
                        && until.ValueKind == JsonValueKind.Object
                        && until.TryGetProperty("Seconds", out var sec))
                        account.VoiceBanExpiry = sec.GetInt64();
                }
                else if (enabled || opted)
                    account.VoiceStatus = "Enabled";
                else if (eligible)
                    account.VoiceStatus = "Disabled";
                else
                    account.VoiceStatus = "Disabled";
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT + "::RefreshVoice", ex);
                account.VoiceStatus = "Unknown";
            }
        }

        /// <summary>Full AltMan-style refresh: ban + presence + age + voice.</summary>
        public static async Task RefreshAccountDetailsAsync(AltManAccount account)
        {
            await ValidateAccountAsync(account);
            await RefreshBanStatusAsync(account);
            await RefreshPresenceAsync(account);
            await RefreshAgeGroupAsync(account);
            await RefreshVoiceStatusAsync(account);
        }

        public static int KillRobloxProcesses()
        {
            string[] names = ["RobloxPlayerBeta", "RobloxPlayer", "RobloxCrashHandler", "Roblox"];
            int killed = 0;
            foreach (string name in names)
            {
                try
                {
                    foreach (var p in Process.GetProcessesByName(name))
                    {
                        try
                        {
                            p.Kill(entireProcessTree: true);
                            killed++;
                        }
                        catch { /* ignore per-process failures */ }
                        finally
                        {
                            p.Dispose();
                        }
                    }
                }
                catch (Exception ex)
                {
                    App.Logger.WriteException(LOG_IDENT + "::KillRoblox", ex);
                }
            }
            return killed;
        }

        /// <summary>
        /// Builds a roblox-player: URI with a fresh auth ticket (AltMan "Copy Launch Link").
        /// </summary>
        public static async Task<string?> BuildLaunchLinkAsync(AltManAccount account, long placeId, string jobId = "")
        {
            if (string.IsNullOrEmpty(account.Cookie) || placeId <= 0)
                return null;

            string? csrf = await GetCsrfTokenAsync(account.Cookie).ConfigureAwait(false);
            if (string.IsNullOrEmpty(csrf))
                return null;

            string? ticket = await GetAuthTicketAsync(account.Cookie, csrf, placeId).ConfigureAwait(false);
            if (string.IsNullOrEmpty(ticket))
                return null;

            string placeLauncher = string.IsNullOrEmpty(jobId)
                ? $"https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestGame%26placeId={placeId}"
                : $"https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestGameJob%26placeId={placeId}%26gameId={Uri.EscapeDataString(jobId)}";

            long launchTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return $"roblox-player://1/1+launchmode:play+gameinfo:{ticket}+launchtime:{launchTime}+browsertrackerid:1+placelauncherurl:{placeLauncher}+robloxLocale:en_us+gameLocale:en_us";
        }

        public static async Task<(bool Ok, string Error)> UpdateUserSettingAsync(AltManAccount account, string field, string value)
        {
            if (string.IsNullOrEmpty(account.Cookie))
                return (false, "No cookie");

            try
            {
                string body = JsonSerializer.Serialize(new Dictionary<string, string> { [field] = value });
                var resp = await RobloxAuthenticatedClient.PostWithAutoCsrfAsync(
                    "https://apis.roblox.com/user-settings-api/v1/user-settings",
                    RobloxAuthConfig.From(account),
                    body).ConfigureAwait(false);

                if (resp.IsSuccess)
                    return (true, "");

                return (false, $"HTTP {resp.StatusCode}: {resp.Body}");
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT + "::UpdateUserSetting", ex);
                return (false, ex.Message);
            }
        }

        public static async Task<string> JoinServer(AltManAccount account, long placeId, string jobId = "", bool followUser = false, bool joinVip = false)
            => await JoinServerInternal(account.Cookie, placeId, jobId, followUser, joinVip, account.VersionProfileId);

        public static async Task<string> JoinServer(AccountManagerAccount account, long placeId, string jobId = "", bool followUser = false, bool joinVip = false)
            => await JoinServerInternal(account.SecurityToken, placeId, jobId, followUser, joinVip, null);

        /// <summary>
        /// The channel the normal launch path would resolve to. Joins must carry it explicitly:
        /// an empty <c>channel:</c> segment makes the bootstrapper fall back to production and,
        /// under <see cref="ChannelChangeMode.Automatic"/>, overwrite the saved player channel.
        /// </summary>
        private static string ResolveLaunchChannel()
        {
            if (App.Settings.Prop.ForceLiveChannel)
                return RobloxInterfaces.Deployment.DefaultChannel;

            string channel = App.Settings.Prop.PlayerChannel;

            return string.IsNullOrWhiteSpace(channel)
                ? RobloxInterfaces.Deployment.DefaultChannel
                : channel.Trim();
        }

        /// <summary>
        /// Versions Manager profile the join should pin to: the account's own override when it has
        /// one, otherwise the globally active profile.
        /// </summary>
        private static string ResolveVersionProfileId(string? accountProfileId)
        {
            if (!string.IsNullOrWhiteSpace(accountProfileId))
                return accountProfileId.Trim();

            return App.Settings.Prop.ActiveVersionProfileId ?? "";
        }

        /// <summary>
        /// Resolve the exact Roblox client GUID the join must use, from the currently selected
        /// Versions Manager profile (in-memory), falling back to the installed player build.
        /// Passing this as <c>-version</c> prevents the child bootstrapper from re-querying CDN
        /// "latest" and deploying a different build than the one the user has selected / is on.
        /// </summary>
        private static string? ResolveVersionGuid(string profileId, out VersionProfile? profile)
        {
            profile = null;

            if (!string.IsNullOrEmpty(profileId))
                profile = App.Settings.Prop.VersionProfiles.FirstOrDefault(p => p.Id == profileId);

            if (profile is not null && VersionGuidValidator.IsWellFormed(profile.VersionGuid))
                return profile.VersionGuid;

            if (App.Settings.Prop.UseCustomVersion
                && VersionGuidValidator.IsWellFormed(App.Settings.Prop.CustomVersionGuid))
                return App.Settings.Prop.CustomVersionGuid;

            // Latest LIVE / no pin: stick to whatever player build is already installed so AltMan
            // joins don't redeploy a different CDN hash than the one the user is currently on.
            if (VersionGuidValidator.IsWellFormed(App.PlayerState.Prop.VersionGuid))
                return App.PlayerState.Prop.VersionGuid;

            return null;
        }

        /// <summary>
        /// Runs the join through this Froststrap build with an explicit channel and version pin.
        /// Handing the URI to the OS instead would let whichever app currently owns the
        /// <c>roblox-player:</c> association (stock Roblox, another bootstrapper) service the join
        /// from its own install and deploy an unrelated client version.
        /// </summary>
        private static void LaunchThroughFroststrap(string launchUri, string channel, string versionProfileId, string? versionGuid)
        {
            const string LOG = LOG_IDENT + "::LaunchThroughFroststrap";

            // Prefer the running executable so AltMan always uses this build's bootstrapper,
            // not a stale Paths.Application left behind by a partial update.
            string appPath = Paths.Process;
            if (string.IsNullOrEmpty(appPath) || !File.Exists(appPath))
                appPath = Paths.Application;

            if (string.IsNullOrEmpty(appPath) || !File.Exists(appPath))
            {
                App.Logger.WriteLine(LOG, "Froststrap executable unavailable; deferring to the protocol handler");
                Utilities.ShellExecute(launchUri);
                return;
            }

            // Persist in-memory profile selection (e.g. Behaviour dropdown) so the child
            // process loads the same Versions Manager state, and point the version junction
            // at the selected profile's install folder before launch.
            try
            {
                App.Settings.Save();
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG + "::SaveSettings", ex);
            }

            if (!string.IsNullOrEmpty(versionProfileId))
            {
                var profile = App.Settings.Prop.VersionProfiles.FirstOrDefault(p => p.Id == versionProfileId);
                if (profile is not null && VersionGuidValidator.IsWellFormed(profile.VersionGuid))
                {
                    try
                    {
                        VersionJunctionManager.SetInstallTarget(profile);
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteException(LOG + "::SetInstallTarget", ex);
                    }
                }
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = appPath,
                UseShellExecute = false
            };

            startInfo.ArgumentList.Add("-player");
            startInfo.ArgumentList.Add(launchUri);
            startInfo.ArgumentList.Add("-channel");
            startInfo.ArgumentList.Add(channel);

            if (!string.IsNullOrEmpty(versionProfileId))
            {
                startInfo.ArgumentList.Add("-versionprofile");
                startInfo.ArgumentList.Add(versionProfileId);
            }

            // Hard pin: bootstrapper uses this GUID instead of Deployment.GetInfo("latest").
            if (VersionGuidValidator.IsWellFormed(versionGuid))
            {
                startInfo.ArgumentList.Add("-version");
                startInfo.ArgumentList.Add(versionGuid!);
            }

            App.Logger.WriteLine(LOG,
                $"Spawning '{appPath}' channel='{channel}' profile='{(string.IsNullOrEmpty(versionProfileId) ? "<none>" : versionProfileId)}' version='{(versionGuid ?? "<cdn-latest>")}'");

            try
            {
                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG, ex);
                Utilities.ShellExecute(launchUri);
            }
        }

        private static async Task<string> JoinServerInternal(string cookie, long placeId, string jobId, bool followUser, bool joinVip, string? versionProfileId)
        {
            const string LOG = LOG_IDENT + "::JoinServer";
            try
            {
                string? csrf = await GetCsrfTokenAsync(cookie).ConfigureAwait(false);
                if (string.IsNullOrEmpty(csrf))
                    return "CSRF_FAIL";

                string? ticket = await GetAuthTicketAsync(cookie, csrf, placeId).ConfigureAwait(false);
                if (string.IsNullOrEmpty(ticket))
                    return "TICKET_FAIL";

                string placeLauncher = "";
                if (placeId > 0)
                {
                    placeLauncher = followUser
                        ? $"https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestFollowUser&userId={placeId}"
                        : joinVip && !string.IsNullOrEmpty(jobId)
                            ? $"https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestPrivateGame&placeId={placeId}&accessCode={Uri.EscapeDataString(jobId)}"
                            : !string.IsNullOrEmpty(jobId)
                                ? $"https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestGameJob&placeId={placeId}&gameId={Uri.EscapeDataString(jobId)}"
                                : $"https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestGame&placeId={placeId}";
                }

                string launcherSegment = string.IsNullOrEmpty(placeLauncher)
                    ? ""
                    : $"+placelauncherurl:{Uri.EscapeDataString(placeLauncher)}";

                string browserTrackerId = Random.Shared.Next(1_000_000_000, int.MaxValue).ToString();
                string channel = ResolveLaunchChannel();
                string profileId = ResolveVersionProfileId(versionProfileId);
                string? versionGuid = ResolveVersionGuid(profileId, out _);

                string launchUri =
                    $"roblox-player:1+launchmode:play+gameinfo:{ticket}{launcherSegment}+browsertrackerid:{browserTrackerId}+robloxLocale:en_us+gameLocale:en_us+channel:{channel}";

                App.Logger.WriteLine(LOG,
                    $"Launching auth-ticket join for place {placeId} on channel '{channel}', profile '{(string.IsNullOrEmpty(profileId) ? "<global>" : profileId)}', version '{(versionGuid ?? "<cdn-latest>")}'");
                LaunchThroughFroststrap(launchUri, channel, profileId, versionGuid);
                return "Success";
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG, ex);
                throw;
            }
        }

        public static async Task<string?> GetCsrfTokenAsync(string securityCookie)
        {
            try
            {
                var handler = new HttpClientHandler { CookieContainer = new CookieContainer() };
                handler.CookieContainer.Add(new Cookie(".ROBLOSECURITY", securityCookie, "/", ".roblox.com"));
                using var client = new HttpClient(handler);
                client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

                using var req = new HttpRequestMessage(HttpMethod.Post, "https://auth.roblox.com/v1/authentication-ticket/");
                using var resp = await client.SendAsync(req).ConfigureAwait(false);
                return resp.Headers.TryGetValues("x-csrf-token", out var vals) ? vals.FirstOrDefault() : null;
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT + "::GetCsrfToken", ex);
                return null;
            }
        }

        private static async Task<string?> GetAuthTicketAsync(string securityCookie, string csrfToken, long placeId)
        {
            try
            {
                var handler = new HttpClientHandler { CookieContainer = new CookieContainer() };
                handler.CookieContainer.Add(new Cookie(".ROBLOSECURITY", securityCookie, "/", ".roblox.com"));
                using var client = new HttpClient(handler);
                client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                client.DefaultRequestHeaders.TryAddWithoutValidation("X-CSRF-TOKEN", csrfToken);
                client.DefaultRequestHeaders.TryAddWithoutValidation("Origin", "https://www.roblox.com");
                client.DefaultRequestHeaders.Referrer = new Uri($"https://www.roblox.com/games/{Math.Max(placeId, 1)}/");

                using var req = new HttpRequestMessage(HttpMethod.Post, "https://auth.roblox.com/v1/authentication-ticket/")
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json")
                };
                using var resp = await client.SendAsync(req).ConfigureAwait(false);
                if (resp.Headers.TryGetValues("rbx-authentication-ticket", out var vals))
                    return vals.FirstOrDefault();

                App.Logger.WriteLine(LOG_IDENT + "::GetAuthTicket", await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
                return null;
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT + "::GetAuthTicket", ex);
                return null;
            }
        }

        public async Task<Dictionary<long, string?>> GetAvatarUrlsBulkAsync(IEnumerable<long> userIds)
        {
            var ids = userIds.Where(id => id > 0).Distinct().ToList();
            var result = ids.ToDictionary(id => id, _ => (string?)null);
            if (ids.Count == 0)
                return result;

            try
            {
                var requests = ids.Select(id => new ThumbnailRequest
                {
                    TargetId = (ulong)id,
                    Type = ThumbnailType.AvatarHeadShot,
                    Size = "150x150",
                    Format = ThumbnailFormat.Png,
                    IsCircular = true
                }).ToList();

                string?[] urls = await Thumbnails.GetThumbnailUrlsAsync(requests, CancellationToken.None);
                for (int i = 0; i < ids.Count && i < urls.Length; i++)
                    result[ids[i]] = urls[i];
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT + "::GetAvatarUrlsBulk", ex);
            }

            return result;
        }

        public static async Task<UserPresence?> GetUserPresenceAsync(long userId)
        {
            if (userId <= 0)
                return null;

            try
            {
                string? cookie = Shared.GetRoblosecurityForUser(userId);
                using var client = CreateCookieIsolatedClient();
                using var req = new HttpRequestMessage(HttpMethod.Post, "https://presence.roblox.com/v1/presence/users");
                if (!string.IsNullOrEmpty(cookie))
                    req.Headers.TryAddWithoutValidation("Cookie", $".ROBLOSECURITY={cookie}");
                req.Content = new StringContent(
                    JsonSerializer.Serialize(new { userIds = new[] { userId } }),
                    Encoding.UTF8,
                    "application/json");

                using var resp = await client.SendAsync(req);
                if (!resp.IsSuccessStatusCode)
                    return null;

                var parsed = JsonSerializer.Deserialize<UserPresenceResponse>(await resp.Content.ReadAsStringAsync());
                return parsed?.UserPresences?.FirstOrDefault();
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT + "::GetUserPresence", ex);
                return null;
            }
        }

        /// <summary>Open an isolated Chromium window with the account cookie (AltMan-style).</summary>
        public static Task OpenBrowserWithCookieAsync(AltManAccount account, string url = "https://www.roblox.com/home")
            => AccountManagerLegacy.OpenBrowserWithCookieAsync(account.Cookie, url);

        // Delegates kept so QuickPlay / dialogs that still call these compile
        public static Task<AccountManagerAccount?> AddAccountByQuickSignInAsync(UI.Elements.Dialogs.QuickSignCodeDialog dialog, CancellationToken cancellationToken)
            => Integrations.AccountManagerLegacy.AddAccountByQuickSignInAsync(dialog, cancellationToken);

        public Task<AccountManagerAccount?> AddAccountByBrowser()
            => Integrations.AccountManagerLegacy.AddAccountByBrowser(this);

        private void MigrateLegacyIfNeeded()
        {
            string legacyPath = Path.Combine(Paths.Cache, "AccountManager.json");
            if (!File.Exists(legacyPath) || _accounts.Count > 0)
                return;

            try
            {
                string json = File.ReadAllText(legacyPath);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("Accounts", out var arr))
                    return;

                foreach (var item in arr.EnumerateArray())
                {
                    string token = item.TryGetProperty("SecurityToken", out var st) ? st.GetString() ?? "" : "";
                    // Legacy tokens may still be DPAPI-wrapped base64 — best-effort leave as-is if decrypt fails in Unprotect of old manager
                    long userId = item.TryGetProperty("UserId", out var uid) ? uid.GetInt64() : 0;
                    string username = item.TryGetProperty("Username", out var un) ? un.GetString() ?? "" : "";
                    string display = item.TryGetProperty("DisplayName", out var dn) ? dn.GetString() ?? username : username;

                    if (string.IsNullOrEmpty(token) || userId == 0)
                        continue;

                    // Try DPAPI unprotect like the old manager
                    token = TryUnprotectLegacy(token);

                    _accounts.Add(new AltManAccount
                    {
                        Id = _nextId++,
                        Cookie = token,
                        UserId = userId.ToString(),
                        Username = username,
                        DisplayName = display,
                        Status = "Online"
                    });
                }

                if (_accounts.Count > 0)
                {
                    ActiveAccount = _accounts[0];
                    Save();
                    App.Logger.WriteLine(LOG_IDENT, $"Migrated {_accounts.Count} account(s) from legacy AccountManager.json");
                    try { File.Move(legacyPath, legacyPath + ".migrated"); } catch { /* ignore */ }
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT + "::MigrateLegacy", ex);
            }
        }

        private static string TryUnprotectLegacy(string text)
        {
            if (string.IsNullOrEmpty(text) || !OperatingSystem.IsWindows())
                return text;

            try
            {
                byte[] entropy = Encoding.UTF8.GetBytes("Froststrap_DPAPI_v1");
                return Encoding.UTF8.GetString(System.Security.Cryptography.ProtectedData.Unprotect(
                    Convert.FromBase64String(text), entropy, System.Security.Cryptography.DataProtectionScope.CurrentUser));
            }
            catch
            {
                return text;
            }
        }

        private AccountDto ToDto(AltManAccount a) => new()
        {
            Id = a.Id,
            DisplayName = a.DisplayName,
            Username = a.Username,
            UserId = a.UserId,
            Status = a.Status,
            AgeGroup = a.AgeGroup,
            VoiceStatus = a.VoiceStatus,
            VoiceBanExpiry = a.VoiceBanExpiry,
            BanExpiry = a.BanExpiry,
            Note = a.Note,
            IsFavorite = a.IsFavorite,
            LastLocation = a.LastLocation,
            PlaceId = a.PlaceId,
            JobId = a.JobId,
            HbaEnabled = a.HbaEnabled,
            VersionProfileId = a.VersionProfileId,
            EncryptedCookie = string.IsNullOrEmpty(a.Cookie) ? "" : AesGcmCrypto.EncryptToBase64(a.Cookie, _masterKey),
            EncryptedHbaKey = string.IsNullOrEmpty(a.HbaPrivateKey) ? "" : AesGcmCrypto.EncryptToBase64(a.HbaPrivateKey, _masterKey)
        };

        private AltManAccount FromDto(AccountDto row)
        {
            string cookie = "";
            string hba = "";
            try
            {
                if (!string.IsNullOrEmpty(row.EncryptedCookie))
                    cookie = AesGcmCrypto.DecryptFromBase64(row.EncryptedCookie, _masterKey);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT + "::DecryptCookie", ex);
            }

            try
            {
                if (!string.IsNullOrEmpty(row.EncryptedHbaKey))
                    hba = AesGcmCrypto.DecryptFromBase64(row.EncryptedHbaKey, _masterKey);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT + "::DecryptHba", ex);
            }

            return new AltManAccount
            {
                Id = row.Id,
                DisplayName = row.DisplayName ?? "",
                Username = row.Username ?? "",
                UserId = row.UserId ?? "",
                Status = row.Status ?? "Unknown",
                AgeGroup = row.AgeGroup ?? "",
                VoiceStatus = row.VoiceStatus ?? "",
                VoiceBanExpiry = row.VoiceBanExpiry,
                BanExpiry = row.BanExpiry,
                Note = row.Note ?? "",
                Cookie = cookie,
                IsFavorite = row.IsFavorite,
                LastLocation = row.LastLocation ?? "",
                PlaceId = row.PlaceId,
                JobId = row.JobId ?? "",
                HbaEnabled = row.HbaEnabled,
                HbaPrivateKey = hba,
                VersionProfileId = row.VersionProfileId
            };
        }

        private sealed class AccountsFileDto
        {
            public int Version { get; set; } = SchemaVersion;
            public int DefaultAccountId { get; set; } = -1;
            public int? ActiveAccountId { get; set; }
            public List<AccountDto> Accounts { get; set; } = [];
        }

        private sealed class AccountDto
        {
            public int Id { get; set; }
            public string? DisplayName { get; set; }
            public string? Username { get; set; }
            public string? UserId { get; set; }
            public string? Status { get; set; }
            public string? AgeGroup { get; set; }
            public string? VoiceStatus { get; set; }
            public long VoiceBanExpiry { get; set; }
            public long BanExpiry { get; set; }
            public string? Note { get; set; }
            public bool IsFavorite { get; set; }
            public string? LastLocation { get; set; }
            public ulong PlaceId { get; set; }
            public string? JobId { get; set; }
            public bool HbaEnabled { get; set; } = true;
            public string? VersionProfileId { get; set; }
            public string EncryptedCookie { get; set; } = "";
            public string EncryptedHbaKey { get; set; } = "";
        }
    }
}
