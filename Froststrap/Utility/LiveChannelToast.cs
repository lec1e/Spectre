namespace Froststrap.Utility
{
    public static class LiveChannelToast
    {
        private const string LOG_IDENT = "LiveChannelToast";

        public static void Show()
        {
            if (App.Settings?.Prop?.ShowLiveChannelToast == false)
                return;

            ShowToast(
                title: "Channel: LIVE",
                message: $"Roblox launched on the LIVE channel. Enforced by {App.BrandName}.");
        }

        public static void ShowChannelLockFailed(string? reason = null)
        {
            string baseMessage = "Roblox may have launched on a non-LIVE channel.";
            string fullMessage = string.IsNullOrEmpty(reason)
                ? baseMessage + " Check the log for details."
                : baseMessage + $" Reason: {reason}";

            ShowToast(title: "Channel lock could not be verified", message: fullMessage);
        }

        public static void ShowToast(string title, string message, object? icon = null)
        {
            App.Logger.WriteLine(LOG_IDENT, $"{title} — {message}");

            try
            {
                Frontend.ShowBalloonTip(title, message);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
            }
        }
    }
}
