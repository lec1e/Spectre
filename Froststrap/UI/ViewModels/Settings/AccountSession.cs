using Froststrap.Integrations;
using Froststrap.UI.ViewModels;
using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace Froststrap.UI.ViewModels.Settings;

public sealed class AccountCard : NotifyPropertyChangedViewModel
{
    public required long UserId { get; init; }
    public required string DisplayName { get; init; }
    public required string Username { get; init; }

    private bool _active;
    public bool IsActive
    {
        get => _active;
        set
        {
            if (SetProperty(ref _active, value))
                OnPropertyChanged(nameof(Line));
        }
    }

    public string Line => string.IsNullOrWhiteSpace(Username)
        ? DisplayName
        : IsActive ? $"{DisplayName}   @{Username}   now" : $"{DisplayName}   @{Username}";
}

public sealed class AccountSession : NotifyPropertyChangedViewModel
{
    public static AccountSession Instance { get; } = new();

    private string _status = "Sign in to Roblox and play. Spectre saves that login.";
    private AccountCard? _selected;

    private AccountSession()
    {
        SelectCommand = new RelayCommand<long>(AccountVault.Select);
        CaptureCommand = new AsyncRelayCommand(CaptureAsync);
        AccountVault.Changed += Reload;
        Reload();
        AccountVault.WatchRobloxLogins();
    }

    public ObservableCollection<AccountCard> Accounts { get; } = [];
    public ICommand SelectCommand { get; }
    public ICommand CaptureCommand { get; }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    public AccountCard? Selected
    {
        get => _selected;
        set
        {
            if (!SetProperty(ref _selected, value) || value is not { IsActive: false })
                return;

            AccountVault.Select(value.UserId);
        }
    }

    public static long UserId => AccountVault.Active()?.UserId ?? App.Cookies.User?.Id ?? 0;

    public static string DisplayName => AccountVault.Active()?.Label ?? App.Cookies.User?.DisplayName ?? "";

    public static string Username => AccountVault.Active()?.Username ?? App.Cookies.User?.Username ?? "";

    public static string Cookie
    {
        get
        {
            var saved = AccountVault.Active()?.Cookie;
            if (!string.IsNullOrWhiteSpace(saved))
                return saved;
            return App.Cookies.GetAuthCookie();
        }
    }

    private void Reload()
    {
        var selected = AccountVault.ActiveUserId;
        var cards = AccountVault.Accounts.Select(account => new AccountCard
        {
            UserId = account.UserId,
            DisplayName = account.Label,
            Username = account.Username,
            IsActive = account.UserId == selected
        }).ToList();

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            Accounts.Clear();
            foreach (var card in cards)
                Accounts.Add(card);
            Selected = Accounts.FirstOrDefault(card => card.IsActive);
            var active = Selected;
            Status = active is null
                ? "Sign in to Roblox and play. Spectre saves that login."
                : $"Using {active.DisplayName}.";
        });
    }

    private async Task CaptureAsync()
    {
        Status = "Reading the Roblox login…";
        var saved = await App.Cookies.CaptureCurrentSessionAsync();
        if (!saved || App.Cookies.User is not { Id: > 0 } user)
        {
            Status = "Roblox is not signed in. Launch it, sign in, then save the login.";
            return;
        }

        AccountVault.Select(user.Id);
        Status = $"Saved {user.DisplayName}.";
    }
}
