using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using System.Collections.ObjectModel;

namespace Froststrap.UI.Elements.Dialogs
{
    public sealed class VersionEntry
    {
        public string Hash { get; init; } = string.Empty;
        public string Version { get; init; } = string.Empty;
        public string Date { get; init; } = string.Empty;
        public DateTime ReleaseDate { get; init; }
        public string FullHash => $"version-{Hash}";
    }

    public partial class VersionOverrideDialog : Base.AvaloniaWindow
    {
        private const string DeployHistoryUrl = "https://setup-rbxcdn.github.io/DeployHistory.txt";

        private readonly string _binaryType = "Studio";
        private bool _isLoading;

        public ObservableCollection<VersionEntry> Versions { get; } = [];

        public string? SelectedHash { get; private set; }

        public VersionOverrideDialog()
        {
            InitializeComponent();
            Froststrap.UI.Utility.SpectreChrome.Apply(this);
            DataContext = this;

            VersionsList.SelectionChanged += (_, _) =>
            {
                bool hasSelection = VersionsList.SelectedItem is VersionEntry;
                CopyHashButton.IsEnabled = hasSelection;
                UseButton.IsEnabled = hasSelection;
            };
        }

        public VersionOverrideDialog(LaunchMode mode) : this()
        {
            if (mode == LaunchMode.Player)
            {
                _binaryType = "WindowsPlayer";
                HeaderText.Text = "Player versions";
            }

            _ = LoadVersionsAsync();
        }

        private async Task LoadVersionsAsync()
        {
            if (_isLoading)
                return;

            _isLoading = true;
            RefreshButton.IsEnabled = false;
            Versions.Clear();
            SetStatus(null, loading: true);

            try
            {
                string content = await App.HttpClient.GetStringAsync(DeployHistoryUrl);
                var cutoff = DateTime.Now.AddYears(-1);

                foreach (var entry in ParseDeployHistory(content, _binaryType)
                    .Where(e => e.ReleaseDate >= cutoff)
                    .OrderByDescending(e => e.ReleaseDate)
                    .DistinctBy(e => e.Hash))
                {
                    Versions.Add(entry);
                }

                SetStatus(Versions.Count == 0 ? "No builds found in Roblox's deploy history." : null, loading: false);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("VersionOverrideDialog::LoadVersionsAsync", ex);
                SetStatus($"Couldn't load the deploy history: {ex.Message}", loading: false);
            }
            finally
            {
                _isLoading = false;
                RefreshButton.IsEnabled = true;
            }
        }

        private void SetStatus(string? message, bool loading)
        {
            LoadingRing.IsActive = loading;
            LoadingRing.IsVisible = loading;
            StatusText.Text = message ?? string.Empty;
            StatusText.IsVisible = message is not null;
        }

        private static List<VersionEntry> ParseDeployHistory(string content, string binaryType)
        {
            var results = new List<VersionEntry>();
            var regex = new Regex(
                $@"New\s+{binaryType}(?:64)?\s+version-(?<hash>[a-f0-9]+|hidden)\s+at\s+(?<date>[^,]+),\s+file\s+version:\s+(?<fileVer>[0-9]+(?:,\s*[0-9]+)*)",
                RegexOptions.IgnoreCase);

            foreach (Match match in regex.Matches(content))
            {
                string hash = match.Groups["hash"].Value;
                if (hash.Equals("hidden", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!DateTime.TryParse(match.Groups["date"].Value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                    continue;

                string fileVersion = string.Join(".", match.Groups["fileVer"].Value
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim()));

                results.Add(new VersionEntry
                {
                    Hash = hash,
                    Version = fileVersion,
                    Date = date.ToString("yyyy-MM-dd hh:mm tt", CultureInfo.InvariantCulture),
                    ReleaseDate = date
                });
            }

            return results;
        }

        private void OnUseClicked(object? sender, RoutedEventArgs e) => UseSelected();

        private void OnVersionDoubleTapped(object? sender, TappedEventArgs e) => UseSelected();

        private void UseSelected()
        {
            if (VersionsList.SelectedItem is not VersionEntry selected)
                return;

            SelectedHash = selected.FullHash;
            Close();
        }

        private async void OnCopyClicked(object? sender, RoutedEventArgs e)
        {
            if (VersionsList.SelectedItem is VersionEntry selected && Clipboard is IClipboard clipboard)
                await clipboard.SetTextAsync(selected.FullHash);
        }

        private async void OnRefreshClicked(object? sender, RoutedEventArgs e) => await LoadVersionsAsync();

        private void OnCloseClicked(object? sender, RoutedEventArgs e) => Close();

        private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        }
    }
}
