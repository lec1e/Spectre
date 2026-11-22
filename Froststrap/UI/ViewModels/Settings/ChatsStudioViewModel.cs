using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Froststrap.Integrations;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace Froststrap.UI.ViewModels.Settings;

public sealed class ChatsStudioViewModel : NotifyPropertyChangedViewModel
{
    private readonly AccountStudioClient _client = new();
    private string _status = "Loading chats…";
    private string _draft = "";
    private bool _loading;
    private bool _pickingFriend;
    private bool _adjustingFriends;
    private string _partyName = "";
    private string _friendQuery = "";
    private bool _friendsLoading;
    private ChatConversation? _selected;

    public ObservableCollection<ChatConversation> Conversations { get; } = [];
    public ObservableCollection<ChatLine> Messages { get; } = [];
    public ObservableCollection<ChatFriend> Friends { get; } = [];
    public ObservableCollection<ChatFriend> ShownFriends { get; } = [];
    public ObservableCollection<ChatFriend> Chosen { get; } = [];
    public ICommand RefreshCommand { get; }
    public ICommand OpenCommand { get; }
    public ICommand SendCommand { get; }
    public ICommand ToggleFriendsCommand { get; }
    public ICommand ToggleFriendCommand { get; }
    public ICommand CreatePartyCommand { get; }

    public ChatsStudioViewModel()
    {
        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        OpenCommand = new AsyncRelayCommand<string>(OpenAsync);
        SendCommand = new AsyncRelayCommand(SendAsync);
        ToggleFriendsCommand = new AsyncRelayCommand(ToggleFriendsAsync);
        ToggleFriendCommand = new RelayCommand<ChatFriend>(ToggleFriend);
        CreatePartyCommand = new AsyncRelayCommand(CreatePartyAsync);
        AccountVault.Changed += () => Dispatcher.UIThread.Post(() => _ = LoadAsync());
        _ = LoadAsync();
    }

    public string Status { get => _status; set => SetProperty(ref _status, value); }
    public bool IsLoading { get => _loading; set => SetProperty(ref _loading, value); }
    public bool PickingFriend
    {
        get => _pickingFriend;
        set
        {
            if (SetProperty(ref _pickingFriend, value))
            {
                OnPropertyChanged(nameof(PickerLabel));
                OnPropertyChanged(nameof(ShowThread));
                OnPropertyChanged(nameof(NoFriends));
            }
        }
    }
    public bool ShowThread => !PickingFriend;
    public bool FriendsLoading
    {
        get => _friendsLoading;
        set
        {
            if (SetProperty(ref _friendsLoading, value))
                OnPropertyChanged(nameof(NoFriends));
        }
    }
    public bool NoFriends => PickingFriend && !FriendsLoading && Friends.Count == 0;
    public string PartyName { get => _partyName; set => SetProperty(ref _partyName, value); }
    public string FriendQuery
    {
        get => _friendQuery;
        set
        {
            if (SetProperty(ref _friendQuery, value))
                ShowFriends();
        }
    }
    public string PickerLabel => PickingFriend ? "Close" : "New chat";
    public int SelectedFriends => Chosen.Count;
    public bool CanCreate => SelectedFriends > 0;
    public bool NamingParty => SelectedFriends > 1;
    public bool HasChosen => SelectedFriends > 0;
    public bool NobodyChosen => !HasChosen;
    public bool HasShownFriends => ShownFriends.Count > 0;
    public bool NoFriendMatches => !FriendsLoading && Friends.Count > 0 && ShownFriends.Count == 0;
    public string CreateLabel => SelectedFriends > 1 ? "Create party" : "Start chat";
    public string PartyCount => SelectedFriends == 0 ? "Nobody selected yet." : $"{SelectedFriends} of 5 selected";
    public ChatConversation? Selected
    {
        get => _selected;
        private set
        {
            if (SetProperty(ref _selected, value))
            {
                OnPropertyChanged(nameof(ThreadTitle));
                OnPropertyChanged(nameof(HasConversation));
                OnPropertyChanged(nameof(AwaitingChat));
                OnPropertyChanged(nameof(ThreadHint));
            OnPropertyChanged(nameof(ShowThreadHint));
            }
        }
    }

    public string ThreadTitle => Selected?.Title ?? "Chats";
    public bool HasConversation => Selected is not null;
    public bool AwaitingChat => Selected is null;
    public int MessageCount => Messages.Count;
    public string ThreadHint => !HasConversation ? "Select a chat on the left." : MessageCount == 0 ? "No messages in this chat yet." : "";
    public bool ShowThreadHint => ThreadHint.Length > 0;
    public event EventHandler? ThreadChanged;

    public string Draft
    {
        get => _draft;
        set => SetProperty(ref _draft, value);
    }

    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var conversations = await _client.GetConversationsAsync(CancellationToken.None);
            var openId = Selected?.Id;
            ClearFriends();
            Conversations.Clear();
            foreach (var conversation in conversations)
                Conversations.Add(conversation);
            Status = conversations.Count == 0 ? "No chats yet." : $"{conversations.Count} chats";
            if (!string.IsNullOrEmpty(openId) && Conversations.Any(conversation => conversation.Id == openId))
                await OpenAsync(openId);
        }
        catch (Exception ex)
        {
            Status = ex is RobloxApiException or HttpRequestException ? ex.Message : "Chats could not be loaded. Try Refresh.";
            App.Logger.WriteException("Chats", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task OpenAsync(string? conversationId)
    {
        if (string.IsNullOrEmpty(conversationId))
            return;

        PickingFriend = false;
        Selected = Conversations.FirstOrDefault(conversation => conversation.Id == conversationId);
        IsLoading = true;
        try
        {
            var lines = await _client.GetMessagesAsync(conversationId, CancellationToken.None);
            Messages.Clear();
            foreach (var line in lines)
                Messages.Add(line);
            OnPropertyChanged(nameof(MessageCount));
            OnPropertyChanged(nameof(ThreadHint));
            OnPropertyChanged(nameof(ShowThreadHint));
            Status = lines.Count == 0 ? "No messages yet. Say hello." : $"{lines.Count} messages";
            ThreadChanged?.Invoke(this, EventArgs.Empty);
            try { await _client.MarkReadAsync(conversationId, CancellationToken.None); } catch (RobloxApiException) { }
        }
        catch (Exception ex)
        {
            Status = ex is RobloxApiException or HttpRequestException ? ex.Message : "Could not open that chat. Try again.";
            App.Logger.WriteException("Chats", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task SendAsync()
    {
        if (Selected is null || string.IsNullOrWhiteSpace(Draft))
            return;

        var text = Draft.Trim();
        try
        {
            await _client.SendMessageAsync(Selected.Id, text, CancellationToken.None);
            Draft = "";
            Messages.Add(new ChatLine("", AccountVault.CurrentUserId, text, "now", true));
            OnPropertyChanged(nameof(MessageCount));
            OnPropertyChanged(nameof(ThreadHint));
            OnPropertyChanged(nameof(ShowThreadHint));
            ThreadChanged?.Invoke(this, EventArgs.Empty);
            Status = "Sent.";
            var conversationId = Selected.Id;
            try
            {
                var lines = await _client.GetMessagesAsync(conversationId, CancellationToken.None);
                if (lines.Count > 0)
                {
                    Messages.Clear();
                    foreach (var line in lines)
                        Messages.Add(line);
                    if (lines.All(line => line.Text != text))
                        Messages.Add(new ChatLine("", AccountVault.CurrentUserId, text, "now", true));
                    OnPropertyChanged(nameof(MessageCount));
                    OnPropertyChanged(nameof(ThreadHint));
            OnPropertyChanged(nameof(ShowThreadHint));
                    ThreadChanged?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (RobloxApiException)
            {
            }
        }
        catch (Exception ex) when (ex is RobloxApiException or HttpRequestException)
        {
            Status = ex.Message;
        }
    }

    private async Task ToggleFriendsAsync()
    {
        PickingFriend = !PickingFriend;
        if (!PickingFriend || Friends.Count > 0)
            return;

        FriendsLoading = true;
        try
        {
            foreach (var friend in await _client.GetFriendsAsync(CancellationToken.None))
            {
                var row = new ChatFriend { Id = friend.Id, DisplayName = friend.DisplayName, Username = friend.Username };
                row.PropertyChanged += FriendChanged;
                Friends.Add(row);
            }
            ShowFriends();
            if (Friends.Count == 0)
                Status = "This account has no friends to message.";
        }
        catch (Exception ex) when (ex is RobloxApiException or HttpRequestException)
        {
            Status = ex.Message;
        }
        finally
        {
            FriendsLoading = false;
            OnPropertyChanged(nameof(NoFriendMatches));
        }
    }

    private void ToggleFriend(ChatFriend? friend)
    {
        if (friend is null)
            return;

        friend.Selected = !friend.Selected;
    }

    private void FriendChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_adjustingFriends || e.PropertyName != nameof(ChatFriend.Selected) || sender is not ChatFriend friend)
            return;

        if (friend.Selected && Friends.Count(item => item.Selected) > 5)
        {
            _adjustingFriends = true;
            friend.Selected = false;
            _adjustingFriends = false;
            Status = "A party can have up to 6 people, including you.";
            return;
        }

        RefreshChosen();
    }

    private void ShowFriends()
    {
        var term = _friendQuery.Trim().TrimStart('@');
        ShownFriends.Clear();
        foreach (var friend in Friends)
        {
            if (term.Length == 0
                || friend.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase)
                || friend.Username.Contains(term, StringComparison.OrdinalIgnoreCase))
                ShownFriends.Add(friend);
        }
        OnPropertyChanged(nameof(NoFriendMatches));
        OnPropertyChanged(nameof(HasShownFriends));
    }

    private void RefreshChosen()
    {
        Chosen.Clear();
        foreach (var friend in Friends.Where(item => item.Selected))
            Chosen.Add(friend);
        OnPropertyChanged(nameof(SelectedFriends));
        OnPropertyChanged(nameof(CanCreate));
        OnPropertyChanged(nameof(NamingParty));
        OnPropertyChanged(nameof(HasChosen));
        OnPropertyChanged(nameof(NobodyChosen));
        OnPropertyChanged(nameof(CreateLabel));
        OnPropertyChanged(nameof(PartyCount));
    }

    private async Task CreatePartyAsync()
    {
        var ids = Friends.Where(friend => friend.Selected).Select(friend => friend.Id).Distinct().ToArray();
        if (ids.Length is < 1 or > 5)
        {
            Status = "Select 1 to 5 people.";
            return;
        }

        try
        {
            Status = ids.Length > 1 ? "Creating the party…" : "Opening the chat…";
            var conversation = await _client.CreateConversationAsync(ids, ids.Length > 1 ? PartyName : "", CancellationToken.None);
            ClearFriends();
            if (Conversations.All(existing => existing.Id != conversation.Id))
                Conversations.Insert(0, conversation);
            await OpenAsync(conversation.Id);
        }
        catch (Exception ex) when (ex is RobloxApiException or HttpRequestException)
        {
            Status = ex.Message;
        }
    }

    private void ClearFriends()
    {
        foreach (var friend in Friends)
            friend.PropertyChanged -= FriendChanged;
        Friends.Clear();
        ShownFriends.Clear();
        Chosen.Clear();
        PickingFriend = false;
        PartyName = "";
        FriendQuery = "";
        RefreshChosen();
        OnPropertyChanged(nameof(NoFriendMatches));
        OnPropertyChanged(nameof(HasShownFriends));
    }
}

public sealed class ChatFriend : NotifyPropertyChangedViewModel
{
    private bool _selected;

    public long Id { get; init; }
    public string DisplayName { get; init; } = "";
    public string Username { get; init; } = "";
    public string Mark => _selected ? "✓" : "";

    public bool Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
                OnPropertyChanged(nameof(Mark));
        }
    }
}
