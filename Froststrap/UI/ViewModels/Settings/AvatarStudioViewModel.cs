using Avalonia.Media.Imaging;
using System.Numerics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Froststrap.Integrations;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace Froststrap.UI.ViewModels.Settings;

public sealed class WornItemRow : NotifyPropertyChangedViewModel
{
    public required long Id { get; init; }
    public required string Name { get; init; }
    public required string TypeLabel { get; init; }
    public required string Group { get; init; }

    private Bitmap? _image;
    public Bitmap? Image { get => _image; set => SetProperty(ref _image, value); }
}

public sealed class WornGroup
{
    public required string Title { get; init; }
    public ObservableCollection<WornItemRow> Items { get; } = [];
}

public sealed class AvatarStudioViewModel : NotifyPropertyChangedViewModel
{
    private readonly AccountStudioClient _client = new();
    private AccountMesh? _mesh;
    private AccountMesh? _avatarMesh;
    private WriteableBitmap? _front;
    private WriteableBitmap? _back;
    private byte[]? _frame;
    private AccountClip? _clip;
    private BodyPaint _paint = BodyPaint.Default;
    private readonly Dictionary<long, AccountClip> _clipCache = [];
    private Dictionary<string, long> _clipIds = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<WornAsset> _worn = [];
    private string _liveKind = "outfit";
    private long _liveAssetId;
    private bool _liveShown;
    private bool _clipMode;
    private double _clipTime;
    private float _yaw = 0.22f;
    private float _pitch = 0.08f;
    private int _renderGeneration;
    private int _motionTicket;
    private bool _loading;
    private bool _is3D;
    private string _status = "Loading the avatar…";
    private string _heading = "Avatar";
    private string _rig = "";
    private Bitmap? _portrait;
    private string _previewTitle = "Avatar";
    private string _motion = "Idle";
    private double _speed = 1;
    private bool _playing;
    private bool _rendering;
    private double _phase;
    private float _basePitch = 0.08f;
    private bool _renderQueued;
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private double _lastTick;
    private long? _previewAssetId;
    private readonly DispatcherTimer _playTimer;

    public ObservableCollection<WornGroup> Groups { get; } = [];
    public ObservableCollection<string> Motions { get; } = ["Turntable", "Idle", "Walk", "Run", "Jump", "Fall", "Climb", "Swim"];
    public ICommand RefreshCommand { get; }
    public ICommand RedrawCommand { get; }
    public ICommand ShowFlatCommand { get; }
    public ICommand ShowModelCommand { get; }
    public ICommand DownloadModelCommand { get; }
    public ICommand DownloadFileCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand PreviewCommand { get; }
    public ICommand TogglePlayCommand { get; }

    public AvatarStudioViewModel()
    {
        RefreshCommand = new AsyncRelayCommand(() => LoadAsync(redraw: false));
        RedrawCommand = new AsyncRelayCommand(() => LoadAsync(redraw: true));
        ShowFlatCommand = new RelayCommand(() => { Is3D = false; _ = ShowFlatAsync(); });
        ShowModelCommand = new AsyncRelayCommand(ShowAvatarAsync);
        DownloadModelCommand = new AsyncRelayCommand(DownloadModelAsync);
        DownloadFileCommand = new AsyncRelayCommand<long>(DownloadFileAsync);
        RemoveCommand = new AsyncRelayCommand<long>(RemoveAsync);
        PreviewCommand = new AsyncRelayCommand<long>(PreviewWornAsync);
        TogglePlayCommand = new RelayCommand(TogglePlay);
        _playTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _playTimer.Tick += (_, _) => AdvancePlayback();
        AccountVault.Changed += () => Dispatcher.UIThread.Post(() => _ = LoadAsync(false));
        _ = LoadAsync(false);
    }

    public event EventHandler<ModelPackage>? ModelReady;
    public event EventHandler? LiveChanged;
    public string? LiveDocument { get; private set; }

    public bool IsLoading { get => _loading; set => SetProperty(ref _loading, value); }
    public bool Is3D { get => _is3D; set => SetProperty(ref _is3D, value); }
    public string Status { get => _status; set => SetProperty(ref _status, value); }
    public string Heading { get => _heading; set => SetProperty(ref _heading, value); }
    public string Rig { get => _rig; set => SetProperty(ref _rig, value); }
    public Bitmap? Portrait { get => _portrait; set => SetProperty(ref _portrait, value); }
    public int WornCount => Groups.Sum(group => group.Items.Count);
    public string PreviewTitle { get => _previewTitle; set => SetProperty(ref _previewTitle, value); }
    public string Motion
    {
        get => _motion;
        set
        {
            if (SetProperty(ref _motion, value))
                _ = UseMotionAsync();
        }
    }
    public double Speed
    {
        get => _speed;
        set
        {
            var clamped = Math.Clamp(value, 0.25, 2);
            if (SetProperty(ref _speed, clamped))
            {
                OnPropertyChanged(nameof(SpeedLabel));
                if (_liveShown && _liveKind == "outfit")
                    Publish(null, false);
            }
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
    private int _viewHeight = 440;
    private float _zoom = 1f;

    public void Zoom(float notches)
    {
        if (_mesh is null && _clip is null)
            return;

        _zoom = Math.Clamp(_zoom * MathF.Pow(1.12f, notches), 0.45f, 3.4f);
        RequestRender();
    }

    public void Orbit(float dx, float dy)
    {
        if (_mesh is null && _clip is null)
            return;

        _yaw += dx * 0.01f;
        _basePitch = Math.Clamp(_basePitch + dy * 0.01f, -1f, 1f);
        _pitch = _basePitch;
        RequestRender();
    }

    public void EndOrbit()
    {
    }

    private void TogglePlay()
    {
        IsPlaying = !IsPlaying;
        if (_liveKind == "outfit")
            Publish(IsPlaying ? $"Playing {Motion} on your avatar." : "Paused.", false);
    }

    private void StopPlayback()
    {
        IsPlaying = false;
        _playTimer.Stop();
    }

    private void AdvancePlayback()
    {
        var now = _clock.Elapsed.TotalSeconds;
        var step = Math.Clamp(now - _lastTick, 0.001, 0.05);
        _lastTick = now;
        var rate = Math.Clamp(Speed, 0.25, 2);
        if (_clipMode && _clip is not null)
        {
            _clipTime += step * rate;
            _pitch = _basePitch;
            RequestRender();
            return;
        }

        if (_mesh is null)
            return;

        _phase += step * rate;
        _yaw += (float)(0.55 * step * rate);
        _pitch = _basePitch + (float)(Math.Sin(_phase * 2) * 0.025);
        RequestRender();
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

    private async Task LoadAsync(bool redraw)
    {
        IsLoading = true;
        _clipMode = false;
        _clip = null;
        StopPlayback();
        try
        {
            if (redraw)
            {
                Status = "Asking Roblox to redraw the avatar…";
                await _client.RedrawAvatarAsync(CancellationToken.None);
                await Task.Delay(1200);
            }

            var avatar = await _client.GetAvatarAsync(CancellationToken.None);
            Heading = $"{avatar.DisplayName}  @{avatar.Username}";
            Rig = avatar.RigType;
            _paint = avatar.Paint;
            _worn = avatar.Assets;
            _clipIds = new Dictionary<string, long>(avatar.Clips, StringComparer.OrdinalIgnoreCase);
            var worn = avatar.Assets.Concat(avatar.Emotes).GroupBy(item => item.Group);
            Groups.Clear();
            foreach (var group in worn)
            {
                var row = new WornGroup { Title = group.Key };
                foreach (var item in group)
                    row.Items.Add(new WornItemRow { Id = item.Id, Name = item.Name, TypeLabel = item.TypeLabel, Group = item.Group });
                Groups.Add(row);
            }

            foreach (var name in Groups.Where(group => group.Title is "Emotes" or "Animations").SelectMany(group => group.Items).Select(item => item.Name))
            {
                if (!Motions.Contains(name))
                    Motions.Add(name);
            }

            OnPropertyChanged(nameof(WornCount));
            _liveKind = "outfit";
            IsPlaying = !string.Equals(Motion, "Turntable", StringComparison.Ordinal);
            Publish(IsPlaying ? $"Playing {Motion} on your avatar." : "Drag to turn your avatar.", true);
            _ = LoadWornImagesAsync(Groups.SelectMany(group => group.Items).ToList());
        }
        catch (Exception ex)
        {
            Status = ex is RobloxApiException or HttpRequestException ? ex.Message : "The avatar could not be loaded. Try Refresh.";
            App.Logger.WriteException("Avatar", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task ShowFlatAsync()
    {
        var userId = AccountVault.CurrentUserId;
        if (userId <= 0)
            return;

        var url = await _client.GetAvatarThumbnailAsync(userId, CancellationToken.None);
        if (!string.IsNullOrEmpty(url))
            Portrait = await AccountImages.LoadAsync(_client, url);
    }

    private async Task ShowModelAsync()
    {
        var userId = AccountVault.CurrentUserId;
        if (userId <= 0)
            return;

        IsLoading = true;
        _previewAssetId = null;
        PreviewTitle = "Avatar";
        try
        {
            if (!_clipMode)
                Status = "Building your avatar…";
            try
            {
                var character = await AccountCharacter.CreateAsync(_client, _worn, _paint, CancellationToken.None);
                _avatarMesh = character;
                if (_previewAssetId is null)
                    _mesh = character;
                Is3D = true;
                await RenderAsync();
                Status = _clipMode
                    ? $"Playing {Motion} on your avatar."
                    : "Drag to turn it. Choose Walk, Run, Jump, or an emote to move this avatar.";
                return;
            }
            catch (Exception ex) when (ex is RobloxApiException or HttpRequestException or InvalidOperationException)
            {
                App.Logger.WriteException("Avatar", ex);
            }

            if (!_clipMode)
                Status = "Downloading the 3D avatar…";
            var package = await _client.GetModelAsync(userId, avatar: true, CancellationToken.None);
            _avatarMesh = AccountMesh.Parse(package.Obj, package.Material, package.Surfaces);
            _mesh = _avatarMesh;
            App.Logger.WriteLine("Avatar", $"Mesh ready: {_mesh.Positions.Count} points, {_mesh.Faces.Count} triangles, textures {package.Surfaces.Count}, pose {_mesh.CanPose}.");
            if (_mesh.Faces.Count == 0)
                throw new RobloxApiException("The downloaded file has no triangles. The photo is still shown.");
            Is3D = true;
            ModelReady?.Invoke(this, package);
            Status = "Drawing the 3D avatar…";
            await RenderAsync();
            Status = _clipMode
                ? $"Playing {Motion} on your avatar."
                : "Drag to turn it. Choose Walk, Run, Jump, or an emote to move this avatar.";
        }
        catch (Exception ex)
        {
            if (_clipMode)
                return;

            Is3D = false;
            _mesh = null;
            App.Logger.WriteException("Avatar", ex);
            Status = ex is RobloxApiException or HttpRequestException ? ex.Message : "The 3D model could not be drawn. The photo is still shown.";
            if (string.Equals(Motion, "Turntable", StringComparison.Ordinal))
            {
                _motion = "Idle";
                OnPropertyChanged(nameof(Motion));
                _ = UseMotionAsync();
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    private Task ShowAvatarAsync()
    {
        _previewAssetId = null;
        _liveKind = "outfit";
        if (string.Equals(Motion, "Turntable", StringComparison.Ordinal))
        {
            _motion = "Idle";
            OnPropertyChanged(nameof(Motion));
        }

        IsPlaying = true;
        Publish($"Playing {Motion} on your avatar.", true);
        return Task.CompletedTask;
    }

    private Task UseMotionAsync()
    {
        var wasItem = _liveKind == "item";
        _previewAssetId = null;
        _liveKind = "outfit";
        var turntable = string.Equals(Motion, "Turntable", StringComparison.Ordinal);
        IsPlaying = !turntable;
        Publish(turntable ? "Drag to turn your avatar." : $"Playing {Motion} on your avatar.", wasItem || !_liveShown);
        return Task.CompletedTask;
    }

    private async Task UseClipAsync()
    {
        if (string.Equals(Motion, "Turntable", StringComparison.Ordinal))
            return;

        _previewAssetId = null;
        if (_avatarMesh is not null)
            _mesh = _avatarMesh;

        if (!_clipIds.TryGetValue(Motion, out var assetId) || assetId <= 0)
        {
            Status = "That animation is not on this avatar.";
            return;
        }

        var ticket = ++_motionTicket;
        try
        {
            if (!_clipCache.TryGetValue(assetId, out var clip))
            {
                Status = $"Loading {Motion}…";
                clip = await _client.GetClipAsync(assetId, CancellationToken.None);
                _clipCache[assetId] = clip;
            }

            if (ticket != _motionTicket)
                return;

            _clip = clip;
            _clipTime = 0;
            _clipMode = true;
            if (clip.Frames.Count > 0)
                App.Logger.WriteLine("Avatar", $"Playing {Motion}: {clip.Frames.Count} frames, joints {string.Join(", ", clip.Frames[0].Bones.Keys.Take(18))}");
            IsPlaying = true;
            _playTimer.Start();
            Status = _mesh is { CanPose: true }
                ? $"Playing {Motion} on your avatar."
                : $"Playing {Motion}.";
            await RenderAsync();
        }
        catch (Exception ex)
        {
            if (ticket != _motionTicket)
                return;

            _clipMode = false;
            App.Logger.WriteException("Avatar", ex);
            Status = ex is RobloxApiException or HttpRequestException ? ex.Message : "That animation could not be played.";
        }
    }

    private async Task PreviewWornAsync(long assetId)
    {
        var row = FindWorn(assetId);
        if (row is null)
            return;

        if (row.Group is "Emotes" or "Animations")
        {
            _previewAssetId = null;
            if (_avatarMesh is not null)
                _mesh = _avatarMesh;
            if (Motions.Contains(row.Name))
                Motion = row.Name;
            else
                Status = $"{row.Name} cannot be played on the avatar.";
            return;
        }

        _liveKind = "item";
        _liveAssetId = assetId;
        _previewAssetId = assetId;
        PreviewTitle = row.Name;
        IsPlaying = false;
        Publish(row.Name, true);
        await Task.CompletedTask;
    }

    private async Task DownloadFileAsync(long assetId)
    {
        var row = FindWorn(assetId);
        if (row is null)
            return;

        try
        {
            Status = $"Downloading {row.Name}…";
            var file = await _client.DownloadAssetAsync(assetId, row.Name, row.TypeLabel, CancellationToken.None);
            await AccountImages.SaveAsync(file);
            Status = $"Saved {file.FileName}";
        }
        catch (Exception ex) when (ex is RobloxApiException or HttpRequestException)
        {
            Status = ex.Message;
        }
    }

    private WornItemRow? FindWorn(long assetId) => Groups.SelectMany(group => group.Items).FirstOrDefault(item => item.Id == assetId);

    private void Publish(string? status, bool full)
    {
        if (status is not null)
            Status = status;

        var kind = full || !_liveShown ? _liveKind : "motion";
        LiveDocument = JsonSerializer.Serialize(new
        {
            kind,
            assetId = _liveAssetId,
            animation = AnimationToken(),
            speed = Speed,
            playing = IsPlaying,
            outfit = new
            {
                playerAvatarType = string.IsNullOrWhiteSpace(Rig) ? "R15" : Rig,
                scale = new
                {
                    height = _paint.Height,
                    width = _paint.Width,
                    head = _paint.HeadScale,
                    depth = _paint.Width,
                    proportion = _paint.Proportion,
                    bodyType = _paint.BodyType
                },
                bodyColor3s = new
                {
                    headColor3 = Hash(_paint.Head),
                    torsoColor3 = Hash(_paint.Torso),
                    leftArmColor3 = Hash(_paint.LeftArm),
                    rightArmColor3 = Hash(_paint.RightArm),
                    leftLegColor3 = Hash(_paint.LeftLeg),
                    rightLegColor3 = Hash(_paint.RightLeg)
                },
                assets = _worn.Select(asset => new
                {
                    id = asset.Id,
                    name = asset.Name,
                    assetType = new { name = asset.TypeName }
                })
            }
        });
        _liveShown = kind == "outfit" || (_liveShown && kind == "motion");
        LiveChanged?.Invoke(this, EventArgs.Empty);
    }

    private string AnimationToken()
    {
        if (Slot(Motion) is string named)
            return named;

        foreach (var asset in _worn)
        {
            if (asset.Name.Equals(Motion, StringComparison.OrdinalIgnoreCase) && Slot(asset.TypeName) is string typed)
                return typed;
        }

        if (_clipIds.TryGetValue(Motion, out var id) && id > 0)
            return "emote." + id.ToString(CultureInfo.InvariantCulture);
        return "idle:0";
    }

    private static string? Slot(string name) => name switch
    {
        "Walk" or "WalkAnimation" => "walk",
        "Run" or "RunAnimation" => "run",
        "Jump" or "JumpAnimation" => "jump",
        "Fall" or "FallAnimation" => "fall",
        "Climb" or "ClimbAnimation" => "climb",
        "Swim" or "SwimAnimation" => "swim",
        "Idle" or "IdleAnimation" or "Mood" or "MoodAnimation" or "Turntable" => "idle:0",
        _ => null
    };

    private static string Hash(string color)
    {
        var text = (color ?? "").Trim();
        if (text.Length == 0)
            return "#A3A2A5";
        return text.StartsWith('#') ? text : "#" + text;
    }

    private async Task DownloadModelAsync()
    {
        try
        {
            if (_previewAssetId is long assetId)
            {
                var row = FindWorn(assetId);
                var package = await _client.GetModelAsync(assetId, avatar: false, CancellationToken.None);
                await AccountImages.SaveManyAsync(row?.Name ?? "item", package.Files);
                Status = "Saved that item's 3D model.";
                return;
            }

            var userId = AccountVault.CurrentUserId;
            if (userId <= 0)
                return;

            var avatar = await _client.GetModelAsync(userId, avatar: true, CancellationToken.None);
            _avatarMesh = AccountMesh.Parse(avatar.Obj, avatar.Material, avatar.Surfaces);
            _mesh = _avatarMesh;
            await AccountImages.SaveManyAsync($"avatar-{AccountVault.CurrentUsername}", avatar.Files);
            Status = "Saved the avatar mesh and texture.";
        }
        catch (Exception ex) when (ex is RobloxApiException or HttpRequestException)
        {
            Status = ex.Message;
        }
    }

    private async Task RemoveAsync(long assetId)
    {
        try
        {
            Status = "Removing that item…";
            await _client.RemoveAsync(assetId, CancellationToken.None);
            await LoadAsync(false);
        }
        catch (Exception ex) when (ex is RobloxApiException or HttpRequestException)
        {
            Status = ex.Message;
        }
    }

    private async Task LoadWornImagesAsync(List<WornItemRow> rows)
    {
        try
        {
            var thumbs = await _client.GetThumbnailsAsync(rows.Select(row => row.Id).ToList(), ThumbnailType.Asset, "150x150", CancellationToken.None);
            foreach (var row in rows)
            {
                if (thumbs.TryGetValue(row.Id, out var url))
                    row.Image = await AccountImages.LoadAsync(_client, url);
            }
        }
        catch (Exception ex)
        {
            App.Logger.WriteException("Avatar", ex);
        }
    }

    private async Task RenderAsync()
    {
        var clipMode = _clipMode;
        var clip = _clip;
        var paint = _paint;
        var time = (float)_clipTime;
        var mesh = _mesh;
        if (clipMode && clip is null || !clipMode && mesh is null)
            return;

        var generation = ++_renderGeneration;
        var yaw = _yaw;
        var pitch = _pitch;
        var width = _viewWidth;
        var height = _viewHeight;
        var zoom = _zoom;
        _rendering = true;
        try
        {
            var pixels = await Task.Run(() =>
            {
                Vector3[]? posed = null;
                var draw = mesh;
                if (clipMode && clip is not null)
                {
                    if (mesh is { CanPose: true })
                        posed = mesh.Pose(clip.Sample(time));
                    else
                        draw = AccountRig.Build(clip, time, paint);
                }

                return draw is null ? null : AccountMeshPreview.RenderBgra(draw, posed, yaw, pitch, width, height, _frame, zoom: zoom);
            });
            if (generation != _renderGeneration || pixels is null)
                return;

            _frame = pixels;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var shown = _front;
                _front = AccountMeshPreview.CopyTo(_back, pixels, width, height);
                _back = shown;
                Portrait = _front;
                Is3D = true;
            });
        }
        catch (Exception ex)
        {
            if (generation != _renderGeneration)
                return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Is3D = false;
                Status = ex is RobloxApiException ? ex.Message : "The 3D model could not be drawn. The photo is still shown.";
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
}
