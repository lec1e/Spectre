using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Froststrap.Integrations;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace Froststrap.UI.ViewModels.Settings;

public sealed class InventoryItemRow : NotifyPropertyChangedViewModel
{
    public required long Id { get; init; }
    public required string Name { get; init; }
    public required string TypeName { get; init; }
    public string TypeLabel => AccountStudioClient.TypeLabel(TypeName);
    public string PriceText { get; init; } = "";

    private Bitmap? _image;
    public Bitmap? Image { get => _image; set => SetProperty(ref _image, value); }
}

public sealed class InventoryStudioViewModel : NotifyPropertyChangedViewModel
{
    private readonly AccountStudioClient _client = new();
    private readonly Dictionary<string, (List<InventoryItemRow> Items, string? Cursor)> _pages = [];
    private AccountMesh? _mesh;
    private bool _usedLiveModel;
    private float _yaw = 0.7f;
    private float _pitch = 0.2f;
    private int _renderGeneration;
    private string _categoryId = "accessories";
    private string _filter = "";
    private string _status = "Loading accessories…";
    private bool _loading;
    private bool _showingModel;
    private bool _showLive;
    private int _loadTicket;
    private AvatarSnapshot? _avatar;
    private InventoryItemRow? _selected;
    private string _details = "Select an item to wear it, turn it in 3D, or download it.";
    private Bitmap? _preview;
    private WriteableBitmap? _front;
    private WriteableBitmap? _back;
    private double _speed = 1;
    private bool _playing;
    private bool _rendering;
    private readonly DispatcherTimer _playTimer;

    public ObservableCollection<InventoryCategory> Categories { get; } = new(AccountStudioClient.Categories);
    public ObservableCollection<InventoryItemRow> VisibleItems { get; } = [];
    public ICommand SelectCategoryCommand { get; }
    public ICommand SelectItemCommand { get; }
    public ICommand LoadMoreCommand { get; }
    public ICommand WearCommand { get; }
    public ICommand DownloadCommand { get; }
    public ICommand ShowModelCommand { get; }
    public ICommand DownloadModelCommand { get; }
    public ICommand TogglePlayCommand { get; }

    public InventoryStudioViewModel()
    {
        SelectCategoryCommand = new AsyncRelayCommand<string>(category => LoadCategoryAsync(category ?? "accessories", append: false));
        SelectItemCommand = new AsyncRelayCommand<long>(SelectAsync);
        LoadMoreCommand = new AsyncRelayCommand(() => LoadCategoryAsync(_categoryId, append: true));
        WearCommand = new AsyncRelayCommand(WearAsync);
        DownloadCommand = new AsyncRelayCommand(DownloadAsync);
        ShowModelCommand = new AsyncRelayCommand(ShowModelAsync);
        DownloadModelCommand = new AsyncRelayCommand(DownloadModelAsync);
        TogglePlayCommand = new RelayCommand(TogglePlay);
        _playTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _playTimer.Tick += (_, _) =>
        {
            if (_mesh is null)
                return;
            _yaw += (float)(0.016 * Math.Clamp(Speed, 0.25, 2));
            RequestRender();
        };
        AccountVault.Changed += () => Dispatcher.UIThread.Post(() =>
        {
            _avatar = null;
            _ = LoadCategoryAsync(_categoryId, false);
        });
        _ = LoadCategoryAsync(_categoryId, false);
    }

    public event EventHandler<ModelPackage>? ModelReady;
    public event EventHandler? LiveChanged;
    public string? LiveDocument { get; private set; }

    public string Filter
    {
        get => _filter;
        set
        {
            if (SetProperty(ref _filter, value))
                ApplyFilter();
        }
    }

    public string Status { get => _status; set => SetProperty(ref _status, value); }
    public bool IsLoading { get => _loading; set => SetProperty(ref _loading, value); }
    public bool ShowingModel { get => _showingModel; set => SetProperty(ref _showingModel, value); }
    public bool ShowLive
    {
        get => _showLive;
        private set
        {
            if (SetProperty(ref _showLive, value))
                OnPropertyChanged(nameof(ShowPhoto));
        }
    }

    public bool ShowPhoto => !ShowLive;
    public string CategoryId => _categoryId;
    public bool HasMore => _pages.TryGetValue(_categoryId, out var page) && !string.IsNullOrEmpty(page.Cursor);
    public bool CanWear => Selected is not null && _categoryId is not ("badges" or "passes" or "bundles" or "collectibles" or "decals" or "audio" or "models" or "places");
    public InventoryItemRow? Selected
    {
        get => _selected;
        private set
        {
            if (SetProperty(ref _selected, value))
                NotifyActions();
        }
    }
    public string Details { get => _details; set => SetProperty(ref _details, value); }
    public Bitmap? Preview { get => _preview; set => SetProperty(ref _preview, value); }
    public string PreviewTitle => Selected?.Name ?? "Click an item";
    public double Speed
    {
        get => _speed;
        set
        {
            var clamped = Math.Clamp(value, 0.25, 2);
            if (SetProperty(ref _speed, clamped))
                OnPropertyChanged(nameof(SpeedLabel));
            OnPropertyChanged(nameof(SpeedText));
        }
    }
    public string SpeedText
    {
        get => Speed.ToString("0.00", CultureInfo.InvariantCulture);
        set
        {
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                && !double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed))
            {
                OnPropertyChanged(nameof(SpeedText));
                return;
            }

            Speed = parsed;
        }
    }
    public string SpeedLabel => $"{Speed:0.00}×";
    public string PlayLabel => IsPlaying ? "Pause" : "Play";
    public bool IsPlaying
    {
        get => _playing;
        private set
        {
            if (SetProperty(ref _playing, value))
                OnPropertyChanged(nameof(PlayLabel));
        }
    }

    private int _viewWidth = 360;
    private int _viewHeight = 420;
    private float _zoom = 1f;
    private bool _renderQueued;

    public void Zoom(float notches)
    {
        if (_mesh is null)
            return;

        _zoom = Math.Clamp(_zoom * MathF.Pow(1.12f, notches), 0.45f, 3.4f);
        RequestRender();
    }

    public bool ShowMotion => Selected is not null && _categoryId is "emotes" or "animations";
    public bool ShowSpin => ShowMotion;
    public bool ShowAudio => Selected is not null && _categoryId == "audio";
    public bool CanDownload => Selected is not null && _categoryId is not ("badges" or "passes" or "bundles" or "collectibles" or "places");
    public bool CanDownload3D => Selected is not null && _categoryId is "accessories" or "classic" or "layered" or "bodies" or "gear" or "models";
    private double _volume = 0.8;
    public double Volume
    {
        get => _volume;
        set
        {
            var clamped = Math.Clamp(value, 0, 1);
            if (SetProperty(ref _volume, clamped))
            {
                AccountAudio.Volume = (float)clamped;
                OnPropertyChanged(nameof(VolumeText));
            }
        }
    }

    public string VolumeText
    {
        get => Volume.ToString("0.00", CultureInfo.InvariantCulture);
        set
        {
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                && !double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed))
            {
                OnPropertyChanged(nameof(VolumeText));
                return;
            }

            if (parsed > 1 && parsed <= 100)
                parsed /= 100;
            Volume = parsed;
        }
    }

    public void Orbit(float dx, float dy)
    {
        if (_mesh is null)
            return;

        IsPlaying = false;
        _playTimer.Stop();
        _yaw += dx * 0.01f;
        _pitch = Math.Clamp(_pitch + dy * 0.01f, -1f, 1f);
        RequestRender();
    }

    public void EndOrbit()
    {
    }

    private void RequestRender()
    {
        if (_rendering)
        {
            _renderQueued = true;
            return;
        }

        _ = RenderAsync();
    }

    private void TogglePlay()
    {
        if (_categoryId == "audio")
        {
            _ = PlayAudioAsync();
            return;
        }

        if (_mesh is null)
        {
            Status = _categoryId is "emotes" or "animations"
                ? "Open Avatar and choose that emote to play it on your character."
                : "Click an item to open its 3D preview.";
            return;
        }

        IsPlaying = !IsPlaying;
        if (IsPlaying)
            _playTimer.Start();
        else
            _playTimer.Stop();
    }

    private async Task LoadCategoryAsync(string categoryId, bool append)
    {
        var ticket = ++_loadTicket;
        _categoryId = categoryId;
        if (!append)
        {
            ShowLive = false;
            Preview = null;
            Details = "Select an item to wear it, turn it in 3D, or download it.";
            Selected = null;
        }
        NotifyActions();
        AccountAudio.Stop();
        IsPlaying = false;
        IsLoading = true;
        try
        {
            var stored = append && _pages.TryGetValue(categoryId, out var previous) ? previous.Items : [];
            var seen = stored.Select(item => item.Id).ToHashSet();
            var cursor = append && _pages.TryGetValue(categoryId, out var existing) ? existing.Cursor : null;
            var label = Categories.First(category => category.Id == categoryId).Label.ToLowerInvariant();
            for (var pageNumber = 0; pageNumber < 40; pageNumber++)
            {
                var page = await _client.GetInventoryAsync(categoryId, cursor, CancellationToken.None);
                if (ticket != _loadTicket)
                    return;

                var rows = new List<InventoryItemRow>();
                foreach (var item in page.Items)
                {
                    if (!seen.Add(item.Id))
                        continue;
                    rows.Add(new InventoryItemRow
                    {
                        Id = item.Id,
                        Name = item.Name,
                        TypeName = string.IsNullOrWhiteSpace(item.TypeName) ? label : item.TypeName,
                        PriceText = AccountStudioClient.TypeLabel(item.TypeName)
                    });
                }

                stored.AddRange(rows);
                var next = page.NextCursor;
                _pages[categoryId] = (stored, next);
                ApplyFilter();
                Status = $"{stored.Count} {label}";
                OnPropertyChanged(nameof(HasMore));
                _ = LoadImagesAsync(rows, categoryId);
                if (page.Items.Count == 0 || string.IsNullOrEmpty(next) || next == cursor)
                    break;
                cursor = next;
            }
        }
        catch (Exception ex)
        {
            if (ticket != _loadTicket)
                return;
            Status = ex is RobloxApiException or HttpRequestException ? ex.Message : "That list could not be loaded. Try another category.";
            App.Logger.WriteException("Inventory", ex);
        }
        finally
        {
            if (ticket == _loadTicket)
                IsLoading = false;
        }
    }

    private void ApplyFilter()
    {
        VisibleItems.Clear();
        if (!_pages.TryGetValue(_categoryId, out var page))
            return;

        foreach (var item in page.Items.Where(item => item.Name.Contains(Filter, StringComparison.OrdinalIgnoreCase)))
            VisibleItems.Add(item);
    }

    private async Task LoadImagesAsync(List<InventoryItemRow> rows, string categoryId)
    {
        var type = categoryId switch
        {
            "bundles" => ThumbnailType.BundleThumbnail,
            "badges" => ThumbnailType.BadgeIcon,
            "passes" => ThumbnailType.GamePass,
            _ => ThumbnailType.Asset
        };
        try
        {
            var thumbs = new Dictionary<long, string>();
            var ids = rows.Select(row => row.Id).ToList();
            for (var index = 0; index < ids.Count; index += 100)
            {
                var batch = await _client.GetThumbnailsAsync(ids.Skip(index).Take(100).ToList(), type, "150x150", CancellationToken.None);
                foreach (var pair in batch)
                    thumbs[pair.Key] = pair.Value;
            }
            foreach (var row in rows)
            {
                if (thumbs.TryGetValue(row.Id, out var url))
                    row.Image = await AccountImages.LoadAsync(_client, url);
            }
        }
        catch (Exception ex)
        {
            App.Logger.WriteException("Inventory", ex);
        }
    }

    private async Task SelectAsync(long id)
    {
        var row = VisibleItems.FirstOrDefault(item => item.Id == id);
        if (row is null)
            return;

        AccountAudio.Stop();
        IsPlaying = false;
        Selected = row;
        ShowingModel = false;
        _mesh = null;
        ShowLive = false;
        Preview = row.Image;
        Details = $"{row.Name}\n{row.TypeLabel}";
        if (_categoryId is "badges" or "passes" or "bundles")
            return;

        try
        {
            var details = await _client.GetDetailsAsync(id, CancellationToken.None);
            var price = !details.ForSale ? "Off sale" : details.Price is null ? "Free" : $"R$ {details.Price}";
            Details = $"{details.Name}\n{row.TypeLabel}    {price}\n{details.Creator}\n\n{details.Description}";
        }
        catch (RobloxApiException ex)
        {
            Details = $"{row.Name}\n{row.TypeLabel}\n\n{ex.Message}";
        }

        if (ShowMotion)
        {
            await ShowLiveAsync(row);
            return;
        }

        if (CanDownload3D)
            await ShowMeshPreviewAsync(row);
    }

    private void NotifyActions()
    {
        OnPropertyChanged(nameof(HasMore));
        OnPropertyChanged(nameof(CanWear));
        OnPropertyChanged(nameof(PreviewTitle));
        OnPropertyChanged(nameof(ShowMotion));
        OnPropertyChanged(nameof(ShowSpin));
        OnPropertyChanged(nameof(ShowAudio));
        OnPropertyChanged(nameof(CanDownload));
        OnPropertyChanged(nameof(CanDownload3D));
    }

    private async Task ShowLiveAsync(InventoryItemRow row)
    {
        ShowLive = true;
        OnPropertyChanged(nameof(ShowPhoto));
        try
        {
            Status = $"Opening {row.Name}…";
            _avatar ??= await _client.GetAvatarAsync(CancellationToken.None);
            var animation = row.TypeName switch
            {
                "EmoteAnimation" => "emote." + row.Id.ToString(CultureInfo.InvariantCulture),
                "MoodAnimation" => "mood." + row.Id.ToString(CultureInfo.InvariantCulture),
                "WalkAnimation" => "set.walk." + row.Id.ToString(CultureInfo.InvariantCulture),
                "RunAnimation" => "set.run." + row.Id.ToString(CultureInfo.InvariantCulture),
                "JumpAnimation" => "set.jump." + row.Id.ToString(CultureInfo.InvariantCulture),
                "FallAnimation" => "set.fall." + row.Id.ToString(CultureInfo.InvariantCulture),
                "ClimbAnimation" => "set.climb." + row.Id.ToString(CultureInfo.InvariantCulture),
                "SwimAnimation" => "set.swim." + row.Id.ToString(CultureInfo.InvariantCulture),
                "IdleAnimation" => "set.idle." + row.Id.ToString(CultureInfo.InvariantCulture),
                _ => "emote." + row.Id.ToString(CultureInfo.InvariantCulture)
            };
            PublishLive($"Playing {row.Name} on your avatar.", "outfit", row.Id, animation, _avatar);
        }
        catch (Exception ex)
        {
            ShowLive = false;
            OnPropertyChanged(nameof(ShowPhoto));
            Status = ex is RobloxApiException or HttpRequestException ? ex.Message : "That preview could not be opened.";
        }
    }

    private async Task ShowMeshPreviewAsync(InventoryItemRow row)
    {
        ShowLive = true;
        OnPropertyChanged(nameof(ShowPhoto));
        _usedLiveModel = false;
        try
        {
            await ShowTexturedModelAsync(row);
        }
        catch (Exception ex) when (ex is RobloxApiException or HttpRequestException or InvalidOperationException)
        {
            App.Logger.WriteException("Inventory", ex);
            _usedLiveModel = true;
            PublishLiveModel(row);
        }
    }

    public void UseLiveModelFallback()
    {
        if (_usedLiveModel || Selected is null || !CanDownload3D)
            return;

        _usedLiveModel = true;
        PublishLiveModel(Selected);
    }

    private void PublishLiveModel(InventoryItemRow row)
    {
        Status = "Drag the model to turn it.";
        LiveDocument = JsonSerializer.Serialize(new { kind = "model", assetId = row.Id });
        LiveChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task ShowTexturedModelAsync(InventoryItemRow row)
    {
        Status = $"Opening {row.Name}…";
        var package = await _client.GetModelAsync(row.Id, avatar: false, CancellationToken.None);
        var textures = new Dictionary<string, string>();
        foreach (var (name, png) in package.Surfaces)
        {
            if (png is not { Length: > 8 } || string.IsNullOrWhiteSpace(name))
                continue;
            var mime = png[0] == 0xFF && png[1] == 0xD8 ? "image/jpeg" : "image/png";
            textures[name] = "data:" + mime + ";base64," + Convert.ToBase64String(png);
        }

        var shot = package.Shot;
        LiveDocument = JsonSerializer.Serialize(new
        {
            kind = "mesh",
            objText = package.Obj,
            mtl = package.Material ?? "",
            textures,
            shot = shot is null ? null : new
            {
                eye = new[] { shot.Value.EyeX, shot.Value.EyeY, shot.Value.EyeZ },
                target = new[] { shot.Value.TargetX, shot.Value.TargetY, shot.Value.TargetZ }
            }
        });
        Status = "Drag the model to turn it.";
        if (LiveDocument.Length > 6_000_000)
            throw new RobloxApiException("This model is too large to preview here.");
        LiveChanged?.Invoke(this, EventArgs.Empty);
    }

    private void PublishLive(string status, string kind, long assetId, string animation, AvatarSnapshot? avatar)
    {
        Status = status;
        var paint = avatar?.Paint ?? new BodyPaint("A3A2A5", "A3A2A5", "A3A2A5", "A3A2A5", "A3A2A5", "A3A2A5", 1, 1, 1, 0, 0);
        IReadOnlyList<WornAsset> worn = avatar?.Assets ?? [];
        if (animation.StartsWith("mood.", StringComparison.Ordinal))
        {
            var head = worn.FirstOrDefault(asset => asset.TypeName is "DynamicHead" or "Head");
            if (head is null || head.Id is 15093053680 or 134082579 || head.Name.Contains("headless", StringComparison.OrdinalIgnoreCase))
            {
                worn = worn.Where(asset => asset.TypeName is not ("DynamicHead" or "Head"))
                    .Append(new WornAsset(10638267973, "Classic head", "DynamicHead", "Head", "Heads and bodies"))
                    .ToArray();
            }
        }
        LiveDocument = JsonSerializer.Serialize(new
        {
            kind,
            assetId,
            animation,
            speed = 1,
            playing = true,
            outfit = new
            {
                playerAvatarType = string.IsNullOrWhiteSpace(avatar?.RigType) ? "R15" : avatar.RigType,
                scale = new
                {
                    height = paint.Height,
                    width = paint.Width,
                    head = paint.HeadScale,
                    depth = paint.Width,
                    proportion = paint.Proportion,
                    bodyType = paint.BodyType
                },
                bodyColor3s = new
                {
                    headColor3 = Hash(paint.Head),
                    torsoColor3 = Hash(paint.Torso),
                    leftArmColor3 = Hash(paint.LeftArm),
                    rightArmColor3 = Hash(paint.RightArm),
                    leftLegColor3 = Hash(paint.LeftLeg),
                    rightLegColor3 = Hash(paint.RightLeg)
                },
                assets = worn.Select(asset => new
                {
                    id = asset.Id,
                    name = asset.Name,
                    assetType = new { name = asset.TypeName }
                })
            }
        });
        LiveChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string Hash(string color)
    {
        var text = (color ?? "").Trim();
        if (text.Length == 0)
            return "#A3A2A5";
        return text.StartsWith('#') ? text : "#" + text;
    }

    private async Task WearAsync()
    {
        if (Selected is null)
            return;

        try
        {
            Status = $"Wearing {Selected.Name}…";
            await _client.WearAsync(Selected.Id, Selected.TypeName, CancellationToken.None);
            Status = $"Wearing {Selected.Name}. Open Avatar and redraw if the picture is old.";
        }
        catch (Exception ex) when (ex is RobloxApiException or HttpRequestException)
        {
            Status = ex.Message;
        }
    }

    private async Task DownloadAsync()
    {
        if (Selected is null || !CanDownload)
            return;

        try
        {
            Status = "Downloading the asset…";
            var file = await _client.DownloadAssetAsync(Selected.Id, Selected.Name, Selected.TypeName, CancellationToken.None);
            await AccountImages.SaveAsync(file);
            Status = $"Saved {file.FileName}";
        }
        catch (Exception ex) when (ex is RobloxApiException or HttpRequestException)
        {
            Status = ex.Message;
        }
    }

    private async Task ShowModelAsync()
    {
        if (Selected is null)
            return;

        IsLoading = true;
        try
        {
            Status = "Downloading the 3D model…";
            var package = await _client.GetModelAsync(Selected.Id, avatar: false, CancellationToken.None);
            _mesh = AccountMesh.Parse(package.Obj, package.Material, package.Surfaces, package.Shot);
            if (package.Shot is not null)
            {
                _yaw = 0;
                _pitch = 0;
                _zoom = 1;
            }
            ShowingModel = true;
            ModelReady?.Invoke(this, package);
            await RenderAsync();
            Status = "Drag the model to turn it.";
        }
        catch (Exception ex)
        {
            ShowingModel = false;
            _mesh = null;
            Status = ex is RobloxApiException or HttpRequestException ? ex.Message : "This item has no 3D model. The thumbnail is still shown.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task DownloadModelAsync()
    {
        if (Selected is null || !CanDownload3D)
            return;

        try
        {
            Status = "Downloading the 3D model…";
            var package = await _client.GetModelAsync(Selected.Id, avatar: false, CancellationToken.None);
            await AccountImages.SaveManyAsync(AccountStudioClient.SafeFileName(Selected.Name, Selected.Id, "3d"), package.Files);
            Status = "Saved the mesh and texture.";
        }
        catch (Exception ex) when (ex is RobloxApiException or HttpRequestException or IOException or InvalidOperationException)
        {
            Status = ex.Message;
        }
    }

    private async Task RenderAsync()
    {
        if (_mesh is null)
            return;

        var generation = ++_renderGeneration;
        var mesh = _mesh;
        var yaw = _yaw;
        var pitch = _pitch;
        var width = _viewWidth;
        var height = _viewHeight;
        var zoom = _zoom;
        _rendering = true;
        try
        {
            var pixels = await Task.Run(() => AccountMeshPreview.RenderBgra(mesh, null, yaw, pitch, width, height, zoom: zoom));
            if (generation != _renderGeneration)
                return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var shown = _front;
                _front = AccountMeshPreview.CopyTo(_back, pixels, width, height);
                _back = shown;
                Preview = _front;
            });
        }
        catch (Exception ex)
        {
            if (generation != _renderGeneration)
                return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ShowingModel = false;
                Status = ex is RobloxApiException ? ex.Message : "This item has no 3D model. The thumbnail is still shown.";
            });
        }
        finally
        {
            _rendering = false;
            if (_renderQueued)
            {
                _renderQueued = false;
                _ = RenderAsync();
            }
        }
    }

    private async Task PlayAudioAsync()
    {
        if (Selected is null)
        {
            Status = "Choose an audio item first.";
            return;
        }

        if (AccountAudio.IsPlaying && IsPlaying)
        {
            AccountAudio.Stop();
            IsPlaying = false;
            Status = "Stopped.";
            return;
        }

        try
        {
            Status = $"Loading {Selected.Name}…";
            var bytes = await _client.GetPlayableAudioAsync(Selected.Id, CancellationToken.None);
            AccountAudio.Play(bytes);
            IsPlaying = true;
            Status = $"Playing {Selected.Name}.";
        }
        catch (Exception ex)
        {
            IsPlaying = false;
            AccountAudio.Stop();
            Status = ex is RobloxApiException or HttpRequestException or InvalidOperationException
                ? ex.Message
                : "This audio could not be played. Download still saves the file.";
        }
    }
}
