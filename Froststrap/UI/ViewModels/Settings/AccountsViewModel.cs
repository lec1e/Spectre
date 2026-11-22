using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Froststrap.Integrations;
using System.Windows.Input;

namespace Froststrap.UI.ViewModels.Settings;

public sealed class AccountsViewModel : NotifyPropertyChangedViewModel
{
    private readonly AccountStudioClient _client = new();
    private string _displayName = "Account";
    private string _username = "";
    private string _userId = "";
    private string _presence = "Offline";
    private string _robux = "R$ —";
    private string _counts = "";
    private string _about = "";
    private string _joined = "";
    private string _accountAge = "";
    private string _country = "";
    private string _ageGroup = "";
    private string _twoStep = "";
    private string _voice = "";
    private string _group = "";
    private string _status = "Loading the account…";
    private Bitmap? _portrait;

    public AccountsViewModel()
    {
        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        AccountVault.Changed += () => Dispatcher.UIThread.Post(() => _ = LoadAsync());
        _ = LoadAsync();
    }

    public ICommand RefreshCommand { get; }
    public string DisplayName { get => _displayName; private set => SetProperty(ref _displayName, value); }
    public string Username { get => _username; private set => SetProperty(ref _username, value); }
    public string UserIdText { get => _userId; private set => SetProperty(ref _userId, value); }
    public string Presence { get => _presence; private set => SetProperty(ref _presence, value); }
    public string Robux { get => _robux; private set => SetProperty(ref _robux, value); }
    public string Counts { get => _counts; private set => SetProperty(ref _counts, value); }
    public string About { get => _about; private set => SetProperty(ref _about, value); }
    public string Joined { get => _joined; private set => SetProperty(ref _joined, value); }
    public string AccountAge { get => _accountAge; private set => SetProperty(ref _accountAge, value); }
    public string Country { get => _country; private set => SetProperty(ref _country, value); }
    public string AgeGroup { get => _ageGroup; private set => SetProperty(ref _ageGroup, value); }
    public string TwoStep { get => _twoStep; private set => SetProperty(ref _twoStep, value); }
    public string Voice { get => _voice; private set => SetProperty(ref _voice, value); }
    public string Group { get => _group; private set => SetProperty(ref _group, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public Bitmap? Portrait { get => _portrait; private set => SetProperty(ref _portrait, value); }

    private async Task LoadAsync()
    {
        if (AccountVault.CurrentUserId <= 0)
        {
            Status = "Play Roblox while signed in. Spectre stores that login, then you can select it here.";
            return;
        }

        Status = "Loading the profile…";
        try
        {
            var profile = await _client.GetProfileAsync(CancellationToken.None);
            DisplayName = string.IsNullOrWhiteSpace(profile.DisplayName) ? profile.Username : profile.DisplayName;
            Username = string.IsNullOrWhiteSpace(profile.Username) ? "" : "@" + profile.Username;
            UserIdText = "ID: " + profile.UserId;
            Presence = profile.Presence;
            Robux = profile.Robux;
            Counts = $"{profile.Friends} friends    {profile.Followers} followers    {profile.Following} following";
            About = profile.About;
            Joined = profile.Joined;
            AccountAge = profile.AccountAge;
            Country = profile.Country;
            AgeGroup = profile.AgeGroup;
            TwoStep = profile.TwoStep;
            Voice = profile.Voice;
            Group = profile.PrimaryGroup;
            Status = "";
            if (!string.IsNullOrWhiteSpace(profile.HeadshotUrl))
                Portrait = await AccountImages.LoadAsync(_client, profile.HeadshotUrl);
        }
        catch (Exception ex)
        {
            Status = ex is RobloxApiException or HttpRequestException ? ex.Message : "The profile could not be loaded.";
            App.Logger.WriteException("Accounts", ex);
        }
    }
}
