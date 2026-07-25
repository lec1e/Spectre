using System.Net;
using System.Text;
using System.Text.Json;
using Avalonia.Threading;
using Froststrap.Models;
using Froststrap.UI.Elements.Dialogs;
using Froststrap.Utility.Accounts;
using PuppeteerExtraSharp;
using PuppeteerExtraSharp.Plugins.ExtraStealth;
using PuppeteerSharp;

namespace Froststrap.Integrations
{
    /// <summary>
    /// Quick Sign-In + Chromium browser login flows (ported from Froststrap AccountManager).
    /// Returns accounts without persisting â€” callers add via <see cref="AccountManager"/>.
    /// </summary>
    internal static class AccountManagerLegacy
    {
        private const string LOG_IDENT = "AccountManagerLegacy";

        public static async Task<AccountManagerAccount?> AddAccountByQuickSignInAsync(
            QuickSignCodeDialog dialog,
            CancellationToken cancellationToken)
        {
            const string log = LOG_IDENT + "::QuickSignIn";
            App.Logger.WriteLine(log, "Starting Quick Sign-In.");

            QuickTokenCreation? creation = null;
            try
            {
                creation = await CreateQuickTokenAsync().ConfigureAwait(false);
                if (creation is null)
                {
                    await Frontend.ShowMessageBox("Failed to start Quick Sign-In. Please check your internet connection.", MessageBoxImage.Error);
                    return null;
                }

                await Dispatcher.UIThread.InvokeAsync(() => dialog.StartNewSignIn(creation.Code));

                var status = await PollQuickTokenStatusAsync(
                    creation.Code, creation.PrivateKey, creation.ExpirationTime, cancellationToken, dialog)
                    .ConfigureAwait(false);

                if (status is null)
                    return null;

                if (status.Status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase))
                    return null;

                if (!status.Status.Equals("Validated", StringComparison.OrdinalIgnoreCase))
                {
                    await Frontend.ShowMessageBox($"Quick Sign-In failed: {status.Status}", MessageBoxImage.Error);
                    return null;
                }

                string? cookie = await PerformLoginWithAuthTokenAsync(creation.Code, creation.PrivateKey).ConfigureAwait(false);
                if (string.IsNullOrEmpty(cookie))
                {
                    await Frontend.ShowMessageBox("Failed to log in with Quick Sign-In. Please try again.", MessageBoxImage.Error);
                    return null;
                }

                var info = await AccountManager.FetchUserFromCookieAsync(cookie).ConfigureAwait(false);
                if (info is null)
                {
                    try { await LogoutRoblosecurityAsync(cookie).ConfigureAwait(false); } catch { /* ignore */ }
                    await Frontend.ShowMessageBox("Failed to get account information. Please try again.", MessageBoxImage.Error);
                    return null;
                }

                return new AccountManagerAccount(cookie, info.Value.UserId, info.Value.Username, info.Value.DisplayName);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(log, ex);
                await Frontend.ShowMessageBox($"Quick Sign-In error: {ex.Message}", MessageBoxImage.Error);
                return null;
            }
            finally
            {
                if (creation is not null)
                {
                    try { await CancelQuickTokenAsync(creation.Code).ConfigureAwait(false); } catch { /* ignore */ }
                }
            }
        }

        public static async Task OpenBrowserWithCookieAsync(string cookie, string url = "https://www.roblox.com/home")
        {
            const string log = LOG_IDENT + "::OpenBrowserWithCookie";
            if (string.IsNullOrEmpty(cookie))
                return;

            try
            {
                if (OperatingSystem.IsWindows())
                {
                    await RobloxWebViewWindow.BrowseWithCookieAsync(cookie, url).ConfigureAwait(false);
                    return;
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(log, ex);
            }

            Utilities.ShellExecute(url);
        }

        public static async Task<AccountManagerAccount?> AddAccountByBrowser(AccountManager _)
        {
            const string log = LOG_IDENT + "::Browser";

            try
            {
                App.Logger.WriteLine(log, "Opening AltMan-style WebView2 loginâ€¦");

                string? cookie = null;
                if (OperatingSystem.IsWindows())
                {
                    cookie = await RobloxWebViewWindow.CaptureLoginCookieAsync().ConfigureAwait(false);
                }
                else
                {
                    // Non-Windows fallback: Puppeteer guest profile
                    cookie = await CaptureCookieViaPuppeteerAsync(log).ConfigureAwait(false);
                }

                if (string.IsNullOrEmpty(cookie))
                {
                    App.Logger.WriteLine(log, "Login cancelled or no cookie captured.");
                    return null;
                }

                var info = await AccountManager.FetchUserFromCookieAsync(cookie).ConfigureAwait(false);
                if (info is null)
                {
                    App.Logger.WriteLine(log, "Cookie captured but authenticated-user lookup failed.");
                    throw new InvalidOperationException(
                        "Login succeeded but the account cookie could not be validated. Try again.");
                }

                App.Logger.WriteLine(log, $"Validated @{info.Value.Username} ({info.Value.UserId}).");
                return new AccountManagerAccount(cookie, info.Value.UserId, info.Value.Username, info.Value.DisplayName);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(log, ex);
                return null;
            }
        }

        private static async Task<string?> CaptureCookieViaPuppeteerAsync(string log)
        {
            var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            Browser? browser = null;
            string guestProfileDir = Path.Combine(Paths.Temp, "GuestBrowser", Guid.NewGuid().ToString("N"));

            try
            {
                Directory.CreateDirectory(guestProfileDir);
                string executablePath = await EnsureChromiumAsync(log).ConfigureAwait(false);

                browser = (Browser)await new PuppeteerExtra()
                    .Use(new StealthPlugin())
                    .LaunchAsync(new LaunchOptions
                    {
                        Headless = false,
                        DefaultViewport = null,
                        ExecutablePath = executablePath,
                        UserDataDir = guestProfileDir,
                        Args = ["--no-first-run", "--no-default-browser-check"]
                    }).ConfigureAwait(false);

                browser.Closed += (_, _) =>
                {
                    if (!completion.Task.IsCompleted)
                        completion.TrySetResult(null);
                };

                var pages = await browser.PagesAsync().ConfigureAwait(false);
                var mainPage = pages.FirstOrDefault();
                if (mainPage is null)
                    return null;

                mainPage.Close += (_, _) =>
                {
                    if (!completion.Task.IsCompleted)
                        completion.TrySetResult(null);
                };

                await SafeGoToAsync(mainPage, "https://www.roblox.com/login", log).ConfigureAwait(false);

                mainPage.RequestFinished += async (_, _) =>
                {
                    try
                    {
                        if (mainPage.IsClosed || completion.Task.IsCompleted)
                            return;

                        var cookies = await mainPage.GetCookiesAsync("https://www.roblox.com/");
                        var security = cookies.FirstOrDefault(c => c.Name == ".ROBLOSECURITY");
                        if (security is not null)
                            completion.TrySetResult(security.Value);
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteException(log, ex);
                    }
                };

                return await completion.Task.ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    if (browser is not null && !browser.IsClosed)
                        await browser.CloseAsync().ConfigureAwait(false);
                }
                catch { /* ignore */ }

                try
                {
                    if (Directory.Exists(guestProfileDir))
                        Directory.Delete(guestProfileDir, recursive: true);
                }
                catch { /* ignore */ }
            }
        }
        private static async Task SafeGoToAsync(IPage page, string url, string log)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    if (page.IsClosed)
                        return;

                    await page.GoToAsync(url, new NavigationOptions
                    {
                        WaitUntil = [WaitUntilNavigation.Networkidle0, WaitUntilNavigation.DOMContentLoaded],
                        Timeout = 60000
                    }).ConfigureAwait(false);
                    return;
                }
                catch (NavigationException)
                {
                    if (attempt >= 2) throw;
                    App.Logger.WriteLine(log, "Navigation failed, retrying...");
                    await Task.Delay(1000).ConfigureAwait(false);
                }
            }
        }

        private static async Task<string> EnsureChromiumAsync(string log)
        {
            var fetcher = new BrowserFetcher();
            var installed = fetcher.GetInstalledBrowsers().FirstOrDefault(b => b.Browser == SupportedBrowser.Chromium);
            if (installed is not null)
            {
                try
                {
                    string path = installed.GetExecutablePath();
                    if (File.Exists(path))
                        return path;
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(log, $"Error checking BrowserFetcher path: {ex.Message}");
                }
            }

            string puppeteerDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PuppeteerSharp");
            if (Directory.Exists(puppeteerDir))
            {
                string[] chromeFiles = Directory.GetFiles(puppeteerDir, "chrome.exe", SearchOption.AllDirectories);
                if (chromeFiles.Length > 0)
                    return chromeFiles[0];
            }

            App.Logger.WriteLine(log, "Chromium not found, downloading...");
            var browserInfo = await fetcher.DownloadAsync().ConfigureAwait(false);
            await Task.Delay(1000).ConfigureAwait(false);
            return browserInfo.GetExecutablePath();
        }

        private sealed record QuickTokenCreation(string Code, string PrivateKey, DateTime ExpirationTime);
        private sealed record QuickTokenStatus(string Status, string? AccountName);

        private static async Task<QuickTokenCreation?> CreateQuickTokenAsync()
        {
            try
            {
                using var content = new StringContent("{}", Encoding.UTF8, "application/json");
                using var resp = await App.HttpClient.PostAsync(
                    "https://apis.roblox.com/auth-token-service/v1/login/create", content).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                    return null;

                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
                string code = doc.RootElement.TryGetProperty("code", out var c) ? c.GetString() ?? "" : "";
                string privateKey = doc.RootElement.TryGetProperty("privateKey", out var pk) ? pk.GetString() ?? "" : "";
                string exp = doc.RootElement.TryGetProperty("expirationTime", out var e) ? e.GetString() ?? "" : "";

                DateTime expiration = DateTime.UtcNow.AddMinutes(2);
                if (!string.IsNullOrEmpty(exp) && DateTime.TryParse(exp, out var parsed))
                    expiration = parsed.ToUniversalTime();

                if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(privateKey))
                    return null;

                return new QuickTokenCreation(code, privateKey, expiration);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT + "::CreateQuickToken", ex);
                return null;
            }
        }

        private static async Task<QuickTokenStatus?> PollQuickTokenStatusAsync(
            string code,
            string privateKey,
            DateTime expirationTime,
            CancellationToken token,
            QuickSignCodeDialog dialog)
        {
            const string log = LOG_IDENT + "::PollQuickToken";
            string? csrfToken = null;
            var deadline = expirationTime > DateTime.UtcNow ? expirationTime : DateTime.UtcNow.AddMinutes(2);

            while (!token.IsCancellationRequested && DateTime.UtcNow < deadline)
            {
                try
                {
                    using var request = new HttpRequestMessage(
                        HttpMethod.Post,
                        "https://apis.roblox.com/auth-token-service/v1/login/status")
                    {
                        Content = new StringContent(
                            JsonSerializer.Serialize(new { code, privateKey }),
                            Encoding.UTF8,
                            "application/json")
                    };

                    if (!string.IsNullOrEmpty(csrfToken))
                        request.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", csrfToken);
                    request.Headers.TryAddWithoutValidation("Origin", "https://www.roblox.com");
                    request.Headers.TryAddWithoutValidation("Referer", "https://www.roblox.com/");

                    using var resp = await App.HttpClient.SendAsync(request, token).ConfigureAwait(false);

                    if (resp.StatusCode == HttpStatusCode.Forbidden && resp.Headers.Contains("x-csrf-token"))
                    {
                        csrfToken = resp.Headers.GetValues("x-csrf-token").FirstOrDefault();
                        await Task.Delay(1000, token).ConfigureAwait(false);
                        continue;
                    }

                    if (resp.StatusCode == HttpStatusCode.BadRequest)
                    {
                        string errorText = await resp.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                        if (errorText.Contains("CodeInvalid", StringComparison.OrdinalIgnoreCase))
                        {
                            dialog.UpdateStatus("Cancelled", "Code expired or invalid");
                            return new QuickTokenStatus("Cancelled", null);
                        }
                    }

                    if (!resp.IsSuccessStatusCode)
                    {
                        await Task.Delay(3000, token).ConfigureAwait(false);
                        continue;
                    }

                    using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(token).ConfigureAwait(false));
                    string status = doc.RootElement.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";
                    string? accountName = doc.RootElement.TryGetProperty("accountName", out var an) ? an.GetString() : null;

                    if (status == "Created" && string.IsNullOrEmpty(accountName))
                        dialog.UpdateStatus(status, "Ready for sign-in");
                    else
                        dialog.UpdateStatus(status, accountName ?? "Unknown");

                    if (status.Equals("Validated", StringComparison.OrdinalIgnoreCase)
                        || status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase))
                        return new QuickTokenStatus(status, accountName);

                    await Task.Delay(3000, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return null;
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(log, $"Poll error: {ex.Message}");
                    await Task.Delay(3000, token).ConfigureAwait(false);
                }
            }

            dialog.UpdateStatus("TimedOut", "Sign-in timed out");
            return null;
        }

        private static async Task<string?> PerformLoginWithAuthTokenAsync(string code, string privateKey)
        {
            const string log = LOG_IDENT + "::LoginExchange";
            try
            {
                var handler = new HttpClientHandler
                {
                    CookieContainer = new CookieContainer(),
                    UseCookies = true
                };
                using var client = new HttpClient(handler);
                client.DefaultRequestHeaders.TryAddWithoutValidation("Origin", "https://www.roblox.com");
                client.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://www.roblox.com/");

                string json = JsonSerializer.Serialize(new { ctype = "AuthToken", cvalue = code, password = privateKey });
                string? csrfToken = null;

                for (int attempt = 0; attempt < 3; attempt++)
                {
                    using var content = new StringContent(json, Encoding.UTF8, "application/json");
                    using var request = new HttpRequestMessage(HttpMethod.Post, "https://auth.roblox.com/v2/login")
                    {
                        Content = content
                    };

                    if (string.IsNullOrEmpty(csrfToken))
                    {
                        using var csrfResp = await client.PostAsync("https://auth.roblox.com/v2/login",
                            new StringContent("{}", Encoding.UTF8, "application/json")).ConfigureAwait(false);
                        if (csrfResp.Headers.TryGetValues("x-csrf-token", out var vals))
                            csrfToken = vals.FirstOrDefault();
                    }

                    if (!string.IsNullOrEmpty(csrfToken))
                        request.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", csrfToken);

                    using var resp = await client.SendAsync(request).ConfigureAwait(false);

                    if (resp.StatusCode == HttpStatusCode.Forbidden && resp.Headers.Contains("x-csrf-token"))
                    {
                        csrfToken = resp.Headers.GetValues("x-csrf-token").FirstOrDefault();
                        await Task.Delay(1000).ConfigureAwait(false);
                        continue;
                    }

                    if (!resp.IsSuccessStatusCode)
                    {
                        if (resp.StatusCode != HttpStatusCode.Forbidden)
                            return null;
                        continue;
                    }

                    if (resp.Headers.TryGetValues("Set-Cookie", out var setCookies))
                    {
                        foreach (var header in setCookies)
                        {
                            int idx = header.IndexOf(".ROBLOSECURITY=", StringComparison.Ordinal);
                            if (idx < 0) continue;
                            int start = idx + ".ROBLOSECURITY=".Length;
                            int end = header.IndexOf(';', start);
                            if (end < 0) end = header.Length;
                            string token = header[start..end];
                            if (!string.IsNullOrEmpty(token))
                                return token;
                        }
                    }

                    var cookies = handler.CookieContainer.GetCookies(new Uri("https://www.roblox.com"));
                    var security = cookies[".ROBLOSECURITY"];
                    if (security is not null && !string.IsNullOrEmpty(security.Value))
                        return security.Value;

                    break;
                }

                return null;
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(log, ex);
                return null;
            }
        }

        private static async Task CancelQuickTokenAsync(string code)
        {
            try
            {
                using var content = new StringContent(
                    JsonSerializer.Serialize(new { code }), Encoding.UTF8, "application/json");
                await App.HttpClient.PostAsync(
                    "https://apis.roblox.com/auth-token-service/v1/login/cancel", content).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT + "::CancelQuickToken", ex);
            }
        }

        private static async Task LogoutRoblosecurityAsync(string roblosecurity)
        {
            try
            {
                var handler = new HttpClientHandler { CookieContainer = new CookieContainer() };
                handler.CookieContainer.Add(new Cookie(".ROBLOSECURITY", roblosecurity, "/", ".roblox.com"));
                using var client = new HttpClient(handler);

                using var req = new HttpRequestMessage(HttpMethod.Post, "https://auth.roblox.com/v2/logout");
                using var resp = await client.SendAsync(req).ConfigureAwait(false);

                if (resp.StatusCode == HttpStatusCode.Forbidden
                    && resp.Headers.TryGetValues("x-csrf-token", out var vals))
                {
                    string? csrf = vals.FirstOrDefault();
                    if (!string.IsNullOrEmpty(csrf))
                    {
                        using var req2 = new HttpRequestMessage(HttpMethod.Post, "https://auth.roblox.com/v2/logout");
                        req2.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", csrf);
                        await client.SendAsync(req2).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT + "::Logout", ex);
            }
        }
    }
}
