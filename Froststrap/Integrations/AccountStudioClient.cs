using System.Text.Json.Nodes;
using Froststrap.Utility;

namespace Froststrap.Integrations;

/// <summary>
/// Reads and changes the signed-in Roblox account: avatar, inventory, chats, and asset files.
/// Uses the Roblox session Spectre already loaded. Nothing here talks to the account manager vault.
/// </summary>
public sealed class AccountStudioClient
{
    private readonly HttpClient _http = new(new HttpClientHandler
    {
        AllowAutoRedirect = true,
        AutomaticDecompression = DecompressionMethods.All
    })
    {
        Timeout = TimeSpan.FromSeconds(40)
    };

    public AccountStudioClient()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
    }

    private string? _csrf;

    public static IReadOnlyList<InventoryCategory> Categories { get; } =
    [
        new("accessories", "Accessories", "Hat,HairAccessory,FaceAccessory,NeckAccessory,ShoulderAccessory,FrontAccessory,BackAccessory,WaistAccessory"),
        new("classic", "Classic clothing", "TShirt,Shirt,Pants"),
        new("layered", "Layered clothing", "TShirtAccessory,ShirtAccessory,PantsAccessory,JacketAccessory,SweaterAccessory,ShortsAccessory,LeftShoeAccessory,RightShoeAccessory,DressSkirtAccessory"),
        new("bodies", "Heads and bodies", "DynamicHead,Head,EyebrowAccessory,EyelashAccessory,Torso,RightArm,LeftArm,LeftLeg,RightLeg"),
        new("faces", "Faces", "Face"),
        new("bundles", "Bundles", null),
        new("emotes", "Emotes", "EmoteAnimation"),
        new("animations", "Animations", "ClimbAnimation,FallAnimation,IdleAnimation,JumpAnimation,RunAnimation,SwimAnimation,WalkAnimation,MoodAnimation"),
        new("collectibles", "Collectibles", null),
        new("gear", "Gear", "Gear"),
        new("badges", "Badges", null),
        new("passes", "Game passes", null),
        new("models", "Models", "Model"),
        new("decals", "Decals", "Decal"),
        new("audio", "Audio", "Audio"),
    ];

    public static string TypeLabel(string? typeName) => typeName switch
    {
        "Hat" => "Hat",
        "HairAccessory" => "Hair",
        "FaceAccessory" => "Face",
        "NeckAccessory" => "Neck",
        "ShoulderAccessory" => "Shoulder",
        "FrontAccessory" => "Front",
        "BackAccessory" => "Back",
        "WaistAccessory" => "Waist",
        "TShirt" => "T-shirt",
        "Shirt" => "Shirt",
        "Pants" => "Pants",
        "TShirtAccessory" => "T-shirt",
        "ShirtAccessory" => "Shirt",
        "PantsAccessory" => "Pants",
        "JacketAccessory" => "Jacket",
        "SweaterAccessory" => "Sweater",
        "ShortsAccessory" => "Shorts",
        "LeftShoeAccessory" => "Left shoe",
        "RightShoeAccessory" => "Right shoe",
        "DressSkirtAccessory" => "Dress or skirt",
        "DynamicHead" => "Head",
        "Head" => "Classic head",
        "EyebrowAccessory" => "Eyebrows",
        "EyelashAccessory" => "Eyelashes",
        "Torso" => "Torso",
        "RightArm" => "Right arm",
        "LeftArm" => "Left arm",
        "LeftLeg" => "Left leg",
        "RightLeg" => "Right leg",
        "Face" => "Face",
        "EmoteAnimation" => "Emote",
        "ClimbAnimation" => "Climb",
        "FallAnimation" => "Fall",
        "IdleAnimation" => "Idle",
        "JumpAnimation" => "Jump",
        "RunAnimation" => "Run",
        "SwimAnimation" => "Swim",
        "WalkAnimation" => "Walk",
        "MoodAnimation" => "Mood",
        "Gear" => "Gear",
        "Model" => "Model",
        "Decal" => "Decal",
        "Audio" => "Audio",
        "Bundle" => "Bundle",
        "Badge" => "Badge",
        "GamePass" => "Game pass",
        _ => string.IsNullOrWhiteSpace(typeName) ? "Item" : typeName
    };

    public static string GroupFor(string? typeName) => typeName switch
    {
        "TShirt" or "Shirt" or "Pants" => "Classic clothing",
        "TShirtAccessory" or "ShirtAccessory" or "PantsAccessory" or "JacketAccessory" or "SweaterAccessory" or "ShortsAccessory" or "LeftShoeAccessory" or "RightShoeAccessory" or "DressSkirtAccessory" => "Layered clothing",
        "DynamicHead" or "Head" or "Torso" or "RightArm" or "LeftArm" or "LeftLeg" or "RightLeg" or "EyebrowAccessory" or "EyelashAccessory" => "Heads and bodies",
        "Face" => "Faces",
        "EmoteAnimation" => "Emotes",
        "ClimbAnimation" or "FallAnimation" or "IdleAnimation" or "JumpAnimation" or "RunAnimation" or "SwimAnimation" or "WalkAnimation" or "MoodAnimation" => "Animations",
        "Gear" => "Gear",
        _ => "Accessories"
    };

    public async Task EnsureSessionAsync()
    {
        if (!App.Settings.Prop.AllowCookieAccess)
        {
            App.Settings.Prop.AllowCookieAccess = true;
            App.Settings.Save();
            App.Logger.WriteLine("AccountStudio", "Turned on Roblox session access for Avatar, Inventory, and Chats.");
        }

        if (!App.Cookies.Loaded)
            await App.Cookies.LoadCookies();

        if (string.IsNullOrWhiteSpace(AccountVault.CurrentCookie) || AccountVault.CurrentUserId <= 0)
            throw new RobloxApiException("Spectre could not read the Roblox session. Launch Roblox while signed in, then press Save login.");
    }

    private string RequireCookie()
    {
        var cookie = AccountVault.CurrentCookie;
        if (string.IsNullOrWhiteSpace(cookie) || AccountVault.CurrentUserId <= 0)
            throw new RobloxApiException("Spectre could not read the Roblox session. Launch Roblox while signed in, then press Save login.");

        return cookie;
    }

    public async Task<AvatarSnapshot> GetAvatarAsync(CancellationToken token)
    {
        await EnsureSessionAsync();
        var userId = AccountVault.CurrentUserId;
        JsonElement json;
        try
        {
            json = await GetJsonAsync(UrlBuilder.BuildApiUrl("avatar", $"v2/avatar/users/{userId}/avatar"), token);
        }
        catch (RobloxApiException)
        {
            json = await GetJsonAsync(UrlBuilder.BuildApiUrl("avatar", $"v1/users/{userId}/avatar"), token);
        }
        var avatar = json.Deserialize<AvatarPayload>() ?? throw new RobloxApiException("Roblox did not return the avatar.");

        var assets = avatar.Assets?.Select(asset => new WornAsset(
            asset.Id,
            asset.Name ?? "Item",
            asset.AssetType?.Name ?? "",
            TypeLabel(asset.AssetType?.Name),
            GroupFor(asset.AssetType?.Name))).ToList() ?? [];

        var emotes = avatar.Emotes?.Select(emote => new WornAsset(
            emote.AssetId,
            emote.AssetName ?? "Emote",
            "EmoteAnimation",
            "Emote",
            "Emotes")).ToList() ?? [];

        return new AvatarSnapshot(
            userId,
            AccountVault.CurrentDisplayName,
            AccountVault.CurrentUsername,
            avatar.PlayerAvatarType ?? "R15",
            assets,
            emotes,
            ReadPaint(json),
            ReadClips(avatar.PlayerAvatarType, assets, emotes));
    }

    public async Task<AccountProfile> GetProfileAsync(CancellationToken token)
    {
        await EnsureSessionAsync();
        var id = AccountVault.CurrentUserId;
        var user = await TryJsonAsync(UrlBuilder.BuildApiUrl("users", $"v1/users/{id}"), token);
        var description = Text(user, "description");
        var created = Text(user, "created");
        var joined = "Unavailable";
        var age = "";
        if (DateTime.TryParse(created, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var createdAt))
        {
            var local = createdAt.ToLocalTime();
            joined = local.ToString("MMM d, yyyy, h:mm tt", CultureInfo.CurrentCulture);
            var years = DateTime.Today.Year - local.Year;
            if (local.Date > DateTime.Today.AddYears(-years))
                years--;
            age = years <= 0 ? "Joined this year" : years == 1 ? "1 year old" : $"{years} years old";
        }

        var robux = await TryJsonAsync(UrlBuilder.BuildApiUrl("economy", $"v1/users/{id}/currency"), token);
        var friends = await TryJsonAsync(UrlBuilder.BuildApiUrl("friends", $"v1/users/{id}/friends/count"), token);
        var followers = await TryJsonAsync(UrlBuilder.BuildApiUrl("friends", $"v1/users/{id}/followers/count"), token);
        var following = await TryJsonAsync(UrlBuilder.BuildApiUrl("friends", $"v1/users/{id}/followings/count"), token);
        var country = await TryJsonAsync(UrlBuilder.BuildApiUrl("users", "v1/users/authenticated/country-code"), token);
        var birth = await TryJsonAsync(UrlBuilder.BuildApiUrl("accountinformation", "v1/birthdate"), token);
        var verified = await TryJsonAsync(UrlBuilder.BuildApiUrl("apis", "age-verification-service/v1/age-verification/verified-age"), token);
        var twoStep = await TryJsonAsync(UrlBuilder.BuildApiUrl("twostepverification", $"v1/users/{id}/configuration"), token);
        var voice = await TryJsonAsync(UrlBuilder.BuildApiUrl("voice", "v1/settings"), token);
        var group = await TryJsonAsync(UrlBuilder.BuildApiUrl("groups", $"v1/users/{id}/groups/primary/role"), token);
        var presence = await TryPostAsync(UrlBuilder.BuildApiUrl("presence", "v1/presence/users"), JsonSerializer.Serialize(new { userIds = new[] { id } }), token);
        var headshot = await TryJsonAsync(UrlBuilder.BuildApiUrl("thumbnails", $"v1/users/avatar-headshot?userIds={id}&size=150x150&format=Png&isCircular=false"), token);

        return new AccountProfile(
            AccountVault.CurrentDisplayName,
            AccountVault.CurrentUsername,
            id,
            string.IsNullOrWhiteSpace(description) ? "No about text." : description,
            PresenceText(presence),
            Number(robux, "robux") is long coins ? $"R$ {coins:N0}" : "Unavailable",
            Count(friends),
            Count(followers),
            Count(following),
            joined,
            age,
            CountryName(Text(country, "countryCode")),
            AgeGroup(birth, verified),
            TwoStepText(twoStep),
            VoiceText(voice),
            GroupName(group),
            HeadshotUrl(headshot));
    }

    public Task<AccountClip> GetClipAsync(long assetId, CancellationToken token)
        => LoadClipAsync(assetId, token, 0);

    private async Task<AccountClip> LoadClipAsync(long assetId, CancellationToken token, int depth)
    {
        var bytes = await DownloadAssetBytesAsync(assetId, token);
        App.Logger.WriteLine("Avatar", $"Animation {assetId} file {bytes.Length} bytes, {Convert.ToHexString(bytes.AsSpan(0, Math.Min(8, bytes.Length)))}.");
        try
        {
            var clip = AccountClip.Read(bytes);
            App.Logger.WriteLine("Avatar", $"Animation {assetId}: {clip.Frames.Count} frames, {clip.Length:0.00}s.");
            return clip;
        }
        catch (RobloxApiException ex) when (depth < 2)
        {
            AccountClip? best = null;
            foreach (var nestedId in AccountClip.ReferencedAssets(bytes).Where(id => id != assetId).Distinct().Take(4))
            {
                try
                {
                    var nested = await LoadClipAsync(nestedId, token, depth + 1);
                    if (best is null || nested.Frames.Count > best.Frames.Count)
                        best = nested;
                }
                catch (RobloxApiException)
                {
                }
            }

            return best ?? throw new RobloxApiException(ex.Message.Contains("curve", StringComparison.OrdinalIgnoreCase)
                ? ex.Message
                : "This animation has no poses.");
        }
    }

    public async Task<string?> GetAvatarThumbnailAsync(long userId, CancellationToken token)
    {
        var json = await GetJsonAsync(UrlBuilder.BuildApiUrl("thumbnails", $"v1/users/avatar?userIds={userId}&size=420x420&format=Png&isCircular=false"), token);
        return json.GetProperty("data").EnumerateArray().Select(row => row.TryGetProperty("imageUrl", out var url) ? url.GetString() : null).FirstOrDefault();
    }

    public async Task RedrawAvatarAsync(CancellationToken token)
    {
        await SendAsync(HttpMethod.Post, UrlBuilder.BuildApiUrl("avatar", "v1/avatar/redraw-thumbnail"), "{}", write: true, token);
    }

    public async Task WearAsync(long assetId, string typeName, CancellationToken token)
    {
        var (assets, emotes, avatar) = await ReadEditableAvatarAsync(token);
        if (typeName == "EmoteAnimation")
        {
            if (emotes.All(emote => emote.Id != assetId))
            {
                var slot = emotes.Count == 0 ? 1 : emotes.Max(emote => emote.Slot) + 1;
                if (slot > 8)
                    throw new RobloxApiException("The emote wheel is full. Remove one on the Avatar page first.");
                emotes.Add(new AvatarEmoteSlot(assetId, slot));
            }
        }
        else
        {
            if (!Stacks(typeName))
                assets.RemoveAll(asset => asset.TypeName == typeName);
            if (assets.All(asset => asset.Id != assetId))
                assets.Add(new EditableAsset(assetId, typeName, null));
        }

        await PatchAvatarAsync(avatar, assets, emotes, token);
    }

    public async Task RemoveAsync(long assetId, CancellationToken token)
    {
        var (assets, emotes, avatar) = await ReadEditableAvatarAsync(token);
        var before = assets.Count + emotes.Count;
        assets.RemoveAll(asset => asset.Id == assetId);
        emotes.RemoveAll(emote => emote.Id == assetId);
        if (assets.Count + emotes.Count == before)
            throw new RobloxApiException("That item is not on the avatar. Refresh and try again.");

        await PatchAvatarAsync(avatar, assets, emotes, token);
    }

    public async Task<InventoryPage> GetInventoryAsync(string categoryId, string? cursor, CancellationToken token)
    {
        await EnsureSessionAsync();
        var userId = AccountVault.CurrentUserId;
        var category = Categories.First(item => item.Id == categoryId);
        var query = $"limit=25&sortOrder=Desc";
        if (!string.IsNullOrEmpty(cursor))
            query += "&cursor=" + Uri.EscapeDataString(cursor);

        JsonElement json;
        List<InventoryAsset> items;
        if (category.Id == "bundles")
        {
            json = await GetJsonAsync(UrlBuilder.BuildApiUrl("catalog", $"v1/users/{userId}/bundles?{query}"), token);
            items = ReadItems(json, "id", "name", "Bundle");
        }
        else if (category.Id == "collectibles")
        {
            json = await GetJsonAsync(UrlBuilder.BuildApiUrl("inventory", $"v1/users/{userId}/assets/collectibles?{query}"), token);
            items = ReadItems(json, "assetId", "name", "Collectible");
        }
        else if (category.Id == "badges")
        {
            json = await GetJsonAsync(UrlBuilder.BuildApiUrl("badges", $"v1/users/{userId}/badges?{query}"), token);
            items = json.TryGetProperty("data", out var data)
                ? data.EnumerateArray().Select(row => new InventoryAsset(row.GetProperty("id").GetInt64(), row.GetProperty("name").GetString() ?? "Badge", "Badge")).ToList()
                : [];
        }
        else if (category.Id == "passes")
        {
            json = await GetJsonAsync(UrlBuilder.BuildApiUrl("apis", $"game-passes/v1/users/{userId}/game-passes?count=30"), token);
            items = json.TryGetProperty("gamePasses", out var passes)
                ? passes.EnumerateArray().Select(row => new InventoryAsset(row.GetProperty("gamePassId").GetInt64(), row.GetProperty("name").GetString() ?? "Game pass", "GamePass")).ToList()
                : [];
            return new InventoryPage(items, null);
        }
        else
        {
            json = await GetJsonAsync(UrlBuilder.BuildApiUrl("inventory", $"v2/users/{userId}/inventory?{query}&assetTypes={category.AssetTypes}"), token);
            items = ReadItems(json, "assetId", "name", null);
        }

        var next = json.TryGetProperty("nextPageCursor", out var cursorElement) && cursorElement.ValueKind == JsonValueKind.String
            ? cursorElement.GetString()
            : null;
        return new InventoryPage(items, next);
    }

    public async Task<Dictionary<long, string>> GetThumbnailsAsync(IReadOnlyList<long> ids, ThumbnailType type, string size, CancellationToken token)
    {
        var map = new Dictionary<long, string>();
        if (ids.Count == 0)
            return map;

        var requests = ids.Select(id => new ThumbnailRequest
        {
            TargetId = (ulong)id,
            Type = type,
            Size = size,
            Format = ThumbnailFormat.Png,
            IsCircular = false
        }).ToList();

        var urls = await Thumbnails.GetThumbnailUrlsAsync(requests, token);
        for (var i = 0; i < ids.Count && i < urls.Length; i++)
        {
            if (!string.IsNullOrEmpty(urls[i]))
                map[ids[i]] = urls[i]!;
        }

        return map;
    }

    public async Task<AssetDetails> GetDetailsAsync(long assetId, CancellationToken token)
    {
        var json = await GetJsonAsync(UrlBuilder.BuildApiUrl("economy", $"v2/assets/{assetId}/details"), token);
        var price = json.TryGetProperty("PriceInRobux", out var priceElement) && priceElement.ValueKind == JsonValueKind.Number
            ? priceElement.GetInt64()
            : (long?)null;
        var forSale = json.TryGetProperty("IsForSale", out var sale) && sale.ValueKind == JsonValueKind.True;
        var creator = json.TryGetProperty("Creator", out var creatorElement) ? creatorElement.GetProperty("Name").GetString() : null;
        var description = json.TryGetProperty("Description", out var descriptionElement) ? descriptionElement.GetString() : null;
        var name = json.TryGetProperty("Name", out var nameElement) ? nameElement.GetString() : null;
        return new AssetDetails(name ?? "Item", description ?? "", creator ?? "", price, forSale);
    }

    public async Task<byte[]> DownloadBytesAsync(string url, CancellationToken token)
    {
        var uri = new Uri(url);
        try
        {
            return await DownloadPublicAsync(uri, token);
        }
        catch (RobloxApiException) when (IsHashPath(uri))
        {
            var name = uri.AbsolutePath.Trim('/');
            var hosts = new List<string> { $"t{CdnIndex(name)}.rbxcdn.com", "tr.rbxcdn.com" };
            for (var server = 0; server < 8; server++)
                hosts.Add($"t{server}.rbxcdn.com");

            foreach (var host in hosts.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (host.Equals(uri.Host, StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    return await DownloadPublicAsync(new Uri($"https://{host}/{name}"), token);
                }
                catch (RobloxApiException)
                {
                }
            }

            throw new RobloxApiException("The file could not be downloaded (403).");
        }
        catch (RobloxApiException ex) when (uri.Host.EndsWith("roblox.com", StringComparison.OrdinalIgnoreCase) && !uri.Host.Contains("rbxcdn", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var response = await SendAsync(HttpMethod.Get, uri, null, write: false, token, json: false);
                return await response.Content.ReadAsByteArrayAsync(token);
            }
            catch (RobloxApiException)
            {
                throw ex;
            }
        }
    }

    public async Task<DownloadedFile> DownloadAssetAsync(long assetId, string name, string typeName, CancellationToken token)
    {
        var bytes = await DownloadAssetBytesAsync(assetId, token);
        if (typeName is "Audio")
        {
            bytes = ExtractAudio(bytes, out var audioExtension);
            return new DownloadedFile(SafeFileName(name, assetId, audioExtension), bytes);
        }

        return new DownloadedFile(SafeFileName(name, assetId, ExtensionFor(bytes, typeName)), bytes);
    }

    public async Task<byte[]> GetPlayableAudioAsync(long assetId, CancellationToken token)
        => ExtractAudio(await DownloadAssetBytesAsync(assetId, token), out _);

    public static byte[] ExtractAudio(byte[] bytes, out string extension)
    {
        if (StartsWith(bytes, "OggS"u8))
        {
            extension = "ogg";
            return bytes;
        }

        if (StartsWith(bytes, "ID3"u8) || LooksLikeMp3(bytes))
        {
            extension = "mp3";
            return bytes;
        }

        if (StartsWith(bytes, "RIFF"u8))
        {
            extension = "wav";
            return bytes;
        }

        var ogg = IndexOf(bytes, "OggS"u8);
        if (ogg >= 0)
        {
            extension = "ogg";
            return bytes[ogg..];
        }

        var id3 = IndexOf(bytes, "ID3"u8);
        if (id3 >= 0)
        {
            extension = "mp3";
            return bytes[id3..];
        }

        var riff = IndexOf(bytes, "RIFF"u8);
        if (riff >= 0)
        {
            extension = "wav";
            return bytes[riff..];
        }

        extension = "ogg";
        throw new RobloxApiException("Roblox did not include a playable audio file for this item.");
    }

    private static bool StartsWith(byte[] bytes, ReadOnlySpan<byte> marker)
        => bytes.AsSpan().StartsWith(marker);

    private static bool LooksLikeMp3(byte[] bytes)
        => bytes.Length > 2 && bytes[0] == 0xFF && (bytes[1] & 0xE0) == 0xE0;

    private static int IndexOf(byte[] bytes, ReadOnlySpan<byte> marker)
    {
        var limit = bytes.Length;
        for (var i = 0; i <= limit - marker.Length; i++)
        {
            if (bytes.AsSpan(i, marker.Length).SequenceEqual(marker))
                return i;
        }

        return -1;
    }

    public async Task<ModelPackage> GetModelAsync(long id, bool avatar, CancellationToken token)
    {
        var path = avatar
            ? $"v1/users/avatar-3d?userId={id}"
            : $"v1/assets-thumbnail-3d?assetId={id}";

        JsonElement manifest = default;
        string? imageUrl = null;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var json = await GetJsonAsync(UrlBuilder.BuildApiUrl("thumbnails", path), token);
            if (json.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array && data.GetArrayLength() > 0)
                json = data[0];

            var state = json.TryGetProperty("state", out var stateElement) && stateElement.ValueKind == JsonValueKind.String
                ? stateElement.GetString()
                : "Completed";
            App.Logger.WriteLine("Avatar", $"3D thumbnail {id} state {state ?? "unknown"}");
            if (string.Equals(state, "Pending", StringComparison.OrdinalIgnoreCase) || string.Equals(state, "InReview", StringComparison.OrdinalIgnoreCase))
            {
                await Task.Delay(900, token);
                continue;
            }

            if (string.Equals(state, "Error", StringComparison.OrdinalIgnoreCase) || string.Equals(state, "Blocked", StringComparison.OrdinalIgnoreCase))
                throw new RobloxApiException("Roblox could not build a 3D model for this. The photo is still shown.");

            imageUrl = json.TryGetProperty("imageUrl", out var urlElement) && urlElement.ValueKind == JsonValueKind.String ? urlElement.GetString() : null;
            if (string.IsNullOrEmpty(imageUrl))
                throw new RobloxApiException("Roblox has no 3D model for this item. The flat thumbnail is still available.");
            if (Uri.TryCreate(imageUrl, UriKind.Absolute, out var imageUri))
                App.Logger.WriteLine("Avatar", $"3D thumbnail host {imageUri.Host}.");
            manifest = JsonDocument.Parse(await DownloadBytesAsync(imageUrl, token)).RootElement.Clone();
            break;
        }

        if (imageUrl is null)
            throw new RobloxApiException("Roblox is still rendering the 3D model. Try again in a moment.");

        var manifestUri = new Uri(imageUrl);
        var objName = manifest.TryGetProperty("obj", out var objElement) && objElement.ValueKind == JsonValueKind.String
            ? objElement.GetString()
            : null;
        if (string.IsNullOrEmpty(objName))
            throw new RobloxApiException("The 3D model has no mesh.");
        var objBytes = await DownloadBytesAsync(ResolveCdn(manifestUri, objName).ToString(), token);
        var obj = Encoding.UTF8.GetString(objBytes);
        if (!obj.Contains("\nv ", StringComparison.Ordinal) && !obj.StartsWith("v ", StringComparison.Ordinal))
            throw new RobloxApiException("Roblox returned a file that is not a 3D mesh.");
        string? material = null;
        if (manifest.TryGetProperty("mtl", out var materialElement) && materialElement.ValueKind == JsonValueKind.String && materialElement.GetString() is string materialName && materialName.Length > 0)
        {
            try
            {
                material = Encoding.UTF8.GetString(await DownloadBytesAsync(ResolveCdn(manifestUri, materialName).ToString(), token));
            }
            catch (RobloxApiException)
            {
            }
        }

        var textureNames = new List<string>();
        if (manifest.TryGetProperty("textures", out var textures) && textures.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in textures.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && item.GetString() is string textureName && textureName.Length > 0)
                    textureNames.Add(textureName);
            }
        }

        var loaded = new List<(string Name, byte[] Png)>();
        var files = new List<DownloadedFile> { new(SafeFileName("mesh", id, "obj"), Encoding.UTF8.GetBytes(obj)) };
        var downloads = await Task.WhenAll(textureNames.Select(async name =>
        {
            try
            {
                return (Name: name, Png: (byte[]?)await DownloadBytesAsync(ResolveCdn(manifestUri, name).ToString(), token));
            }
            catch (RobloxApiException)
            {
                return (Name: name, Png: (byte[]?)null);
            }
        }));
        foreach (var download in downloads)
        {
            if (download.Png is null)
                continue;

            files.Add(new DownloadedFile(SafeFileName($"texture-{loaded.Count + 1}", id, "png"), download.Png));
            loaded.Add((download.Name, download.Png));
        }

        App.Logger.WriteLine("Avatar", $"3D model textures {loaded.Count}.");
        return new ModelPackage(obj, loaded.Count > 0 ? loaded[0].Png : null, files, material, loaded);
    }

    public async Task<IReadOnlyList<ChatConversation>> GetConversationsAsync(CancellationToken token)
    {
        await EnsureSessionAsync();
        var json = await GetJsonAsync(UrlBuilder.BuildApiUrl("apis", "platform-chat-api/v1/get-user-conversations?include_user_data=true&pageSize=30"), token);
        if (!json.TryGetProperty("conversations", out var rows))
            return [];

        return rows.EnumerateArray().Select(ReadConversation).Where(conversation => conversation.Id.Length > 0 && !conversation.Id.StartsWith("friends-", StringComparison.Ordinal)).ToList();
    }

    public async Task<IReadOnlyList<ChatLine>> GetMessagesAsync(string conversationId, CancellationToken token)
    {
        var lines = new List<ChatLine>();
        string? cursor = null;
        for (var page = 0; page < 4; page++)
        {
            var address = "platform-chat-api/v1/get-conversation-messages?pageSize=50&conversation_id=" + Uri.EscapeDataString(conversationId);
            if (!string.IsNullOrEmpty(cursor))
                address += "&cursor=" + Uri.EscapeDataString(cursor);

            var json = await GetJsonAsync(UrlBuilder.BuildApiUrl("apis", address), token);
            if (!json.TryGetProperty("messages", out var rows) || rows.ValueKind != JsonValueKind.Array)
                break;

            foreach (var row in rows.EnumerateArray())
            {
                var senderId = row.TryGetProperty("sender_user_id", out var sender) ? LongOf(sender) : 0;
                var mine = senderId > 0 && senderId == AccountVault.CurrentUserId;
                var text = MessageText(row);
                if (string.IsNullOrWhiteSpace(text))
                    text = " ";
                lines.Add(new ChatLine(JsonText(row, "id"), senderId, text, When(JsonText(row, "created_at")), mine));
            }

            cursor = json.TryGetProperty("next_cursor", out var next) && next.ValueKind == JsonValueKind.String ? next.GetString() : null;
            if (string.IsNullOrEmpty(cursor))
                break;
        }

        lines.Reverse();
        return lines;
    }

    public async Task SendMessageAsync(string conversationId, string text, CancellationToken token)
    {
        var body = JsonSerializer.Serialize(new
        {
            conversation_id = conversationId,
            messages = new[] { new { content = text } }
        });
        await SendAsync(HttpMethod.Post, UrlBuilder.BuildApiUrl("apis", "platform-chat-api/v1/send-messages"), body, write: true, token);
    }

    public async Task MarkReadAsync(string conversationId, CancellationToken token)
    {
        var body = JsonSerializer.Serialize(new { conversation_ids = new[] { conversationId } });
        await SendAsync(HttpMethod.Post, UrlBuilder.BuildApiUrl("apis", "platform-chat-api/v1/mark-conversations"), body, write: true, token);
    }

    public Task<ChatConversation> StartChatAsync(long userId, CancellationToken token)
        => CreateConversationAsync([userId], "", token);

    public async Task<ChatConversation> CreateConversationAsync(IReadOnlyList<long> userIds, string name, CancellationToken token)
    {
        var ids = userIds.Where(id => id > 0).Distinct().Take(6).ToArray();
        if (ids.Length is < 1 or > 5)
            throw new RobloxApiException("Select 1 to 5 people.");

        var partyName = (name ?? "").Trim();
        if (partyName.Length > 150)
            throw new RobloxApiException("Shorten the party name to 150 characters or fewer.");

        var party = ids.Length > 1 || partyName.Length > 0;
        var conversation = new JsonObject
        {
            ["type"] = party ? "group" : "one_to_one",
            ["participant_user_ids"] = new JsonArray(ids.Select(id => JsonValue.Create(id)).ToArray())
        };
        if (party)
            conversation["name"] = partyName;

        var body = new JsonObject
        {
            ["conversations"] = new JsonArray(conversation),
            ["include_user_data"] = true
        }.ToJsonString();
        var response = await SendAsync(HttpMethod.Post, UrlBuilder.BuildApiUrl("apis", "platform-chat-api/v1/create-conversations"), body, write: true, token);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        if (!document.RootElement.TryGetProperty("conversations", out var rows) || rows.ValueKind != JsonValueKind.Array)
            throw new RobloxApiException("Roblox did not open the chat.");

        var row = rows.EnumerateArray().FirstOrDefault();
        if (row.ValueKind != JsonValueKind.Object || string.IsNullOrEmpty(JsonText(row, "id")))
            throw new RobloxApiException("Roblox did not open the chat.");

        var status = JsonText(row, "status");
        if (status.Equals("moderated", StringComparison.OrdinalIgnoreCase))
            throw new RobloxApiException("Roblox blocked this party name. Choose another name.");
        if (status.Length > 0 && !status.Equals("success", StringComparison.OrdinalIgnoreCase))
            throw new RobloxApiException("Roblox did not create this chat. Try again.");

        return ReadConversation(row);
    }

    public async Task<IReadOnlyList<FriendCard>> GetFriendsAsync(CancellationToken token)
    {
        var userId = AccountVault.CurrentUserId;
        if (userId <= 0)
            throw new RobloxApiException("No Roblox session is loaded.");
        var json = await GetJsonAsync(UrlBuilder.BuildApiUrl("friends", $"v1/users/{userId}/friends"), token);
        if (!json.TryGetProperty("data", out var rows) || rows.ValueKind != JsonValueKind.Array)
            return [];

        var friends = new List<FriendCard>();
        var missing = new List<long>();
        foreach (var row in rows.EnumerateArray())
        {
            if (!row.TryGetProperty("id", out var idElement))
                continue;
            var id = LongOf(idElement);
            if (id <= 0)
                continue;
            var username = FieldText(row, "name");
            var display = FieldText(row, "displayName");
            if (username.Length == 0 && display.Length == 0)
                missing.Add(id);
            friends.Add(new FriendCard(id, display.Length > 0 ? display : username, username));
        }

        if (missing.Count > 0)
        {
            var names = await LookupUsersAsync(missing, token);
            for (var index = 0; index < friends.Count; index++)
            {
                if (!names.TryGetValue(friends[index].Id, out var named))
                    continue;
                friends[index] = friends[index] with { DisplayName = named.Display, Username = named.Username };
            }
        }

        return friends
            .OrderBy(friend => friend.DisplayName.Length > 0 ? friend.DisplayName : friend.Username, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<Dictionary<long, (string Display, string Username)>> LookupUsersAsync(IReadOnlyList<long> userIds, CancellationToken token)
    {
        var names = new Dictionary<long, (string Display, string Username)>();
        for (var start = 0; start < userIds.Count; start += 100)
        {
            var batch = userIds.Skip(start).Take(100).ToArray();
            var body = new JsonObject
            {
                ["userIds"] = new JsonArray(batch.Select(id => JsonValue.Create(id)).ToArray()),
                ["excludeBannedUsers"] = false
            }.ToJsonString();
            using var response = await SendAsync(HttpMethod.Post, UrlBuilder.BuildApiUrl("users", "v1/users"), body, write: true, token);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            if (!document.RootElement.TryGetProperty("data", out var rows) || rows.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var row in rows.EnumerateArray())
            {
                if (!row.TryGetProperty("id", out var idElement))
                    continue;
                var id = LongOf(idElement);
                if (id <= 0)
                    continue;
                var username = FieldText(row, "name");
                var display = FieldText(row, "displayName");
                if (display.Length == 0)
                    display = username;
                if (display.Length == 0)
                    display = "Friend";
                names[id] = (display, username);
            }
        }

        return names;
    }

    private static string FieldText(JsonElement row, string name)
        => row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? (value.GetString() ?? "").Trim() : "";

    private static List<InventoryAsset> ReadItems(JsonElement json, string idName, string nameName, string? forcedType)
    {
        if (!json.TryGetProperty("data", out var data))
            return [];

        return data.EnumerateArray().Select(row =>
        {
            var id = row.GetProperty(idName).GetInt64();
            var name = row.TryGetProperty(nameName, out var nameElement) ? nameElement.GetString() ?? "Item" : "Item";
            var type = forcedType ?? (row.TryGetProperty("assetType", out var typeElement) ? typeElement.GetString() ?? "" : "");
            return new InventoryAsset(id, name, type);
        }).ToList();
    }

    private static ChatConversation ReadConversation(JsonElement row)
    {
        var id = JsonText(row, "id");
        var given = row.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String ? nameElement.GetString() : null;
        var others = new List<string>();
        if (row.TryGetProperty("user_data", out var users) && users.ValueKind == JsonValueKind.Object)
        {
            var self = AccountVault.CurrentUserId;
            foreach (var user in users.EnumerateObject().Select(entry => entry.Value))
            {
                if (user.TryGetProperty("id", out var userId) && LongOf(userId) == self)
                    continue;
                var label = user.TryGetProperty("display_name", out var display) && display.ValueKind == JsonValueKind.String ? display.GetString() : null;
                if (string.IsNullOrWhiteSpace(label) && user.TryGetProperty("name", out var userName) && userName.ValueKind == JsonValueKind.String)
                    label = userName.GetString();
                if (!string.IsNullOrWhiteSpace(label))
                    others.Add(label);
            }
        }

        var party = JsonText(row, "type").Equals("group", StringComparison.OrdinalIgnoreCase);
        var name = party
            ? string.IsNullOrWhiteSpace(given) ? others.Count > 0 ? string.Join(", ", others) : "Party" : given
            : others.FirstOrDefault() ?? given;

        var preview = row.TryGetProperty("preview_message", out var previewElement) && previewElement.ValueKind == JsonValueKind.Object
            ? MessageText(previewElement)
            : "";
        var members = row.TryGetProperty("participant_user_ids", out var participants) ? participants.GetArrayLength() : 0;
        var unread = row.TryGetProperty("unread_message_count", out var unreadElement) && unreadElement.ValueKind == JsonValueKind.Number && unreadElement.TryGetInt32(out var count) ? count : 0;
        var updated = row.TryGetProperty("updated_at", out var updatedElement) ? updatedElement.GetString() ?? "" : "";
        return new ChatConversation(id, string.IsNullOrWhiteSpace(name) ? "Chat" : name!, preview, members, unread, updated);
    }

    private static long LongOf(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
            return number;
        if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        return 0;
    }

    private static string JsonText(JsonElement row, string name)
    {
        if (!row.TryGetProperty(name, out var value))
            return "";

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.Number => value.ToString(),
            _ => ""
        };
    }

    private static string When(string raw)
    {
        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var time))
            return time.LocalDateTime.ToString("MMM d, h:mm tt", CultureInfo.CurrentCulture);
        return raw;
    }

    private async Task<(List<EditableAsset> Assets, List<AvatarEmoteSlot> Emotes, JsonElement Avatar)> ReadEditableAvatarAsync(CancellationToken token)
    {
        await EnsureSessionAsync();
        var avatar = await GetJsonAsync(UrlBuilder.BuildApiUrl("avatar", "v2/avatar/avatar"), token);
        var assets = new List<EditableAsset>();
        if (avatar.TryGetProperty("assets", out var assetRows))
        {
            foreach (var row in assetRows.EnumerateArray())
            {
                if (!row.TryGetProperty("id", out var idElement) || !idElement.TryGetInt64(out var id))
                    continue;
                var type = row.TryGetProperty("assetType", out var typeElement) && typeElement.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "";
                JsonElement? meta = row.TryGetProperty("meta", out var metaElement) && metaElement.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined
                    ? metaElement.Clone()
                    : null;
                assets.Add(new EditableAsset(id, type, meta));
            }
        }

        var emotes = new List<AvatarEmoteSlot>();
        if (avatar.TryGetProperty("emotes", out var emoteRows))
        {
            foreach (var row in emoteRows.EnumerateArray())
            {
                if (row.TryGetProperty("assetId", out var idElement) && idElement.TryGetInt64(out var id))
                    emotes.Add(new AvatarEmoteSlot(id, row.TryGetProperty("position", out var slot) && slot.TryGetInt32(out var position) ? position : emotes.Count + 1));
            }
        }

        return (assets, emotes, avatar);
    }

    private async Task PatchAvatarAsync(JsonElement avatar, List<EditableAsset> assets, List<AvatarEmoteSlot> emotes, CancellationToken token)
    {
        var assetArray = new JsonArray();
        foreach (var asset in assets)
        {
            var item = new JsonObject { ["id"] = asset.Id };
            if (asset.Meta is { } meta)
                item["meta"] = JsonNode.Parse(meta.GetRawText());
            assetArray.Add(item);
        }

        var emoteArray = new JsonArray();
        var position = 1;
        foreach (var emote in emotes.OrderBy(item => item.Slot).Take(8))
        {
            emoteArray.Add(new JsonObject { ["assetId"] = emote.Id, ["position"] = position });
            position++;
        }

        var colors = avatar.TryGetProperty("bodyColor3s", out var listedColors) ? listedColors : default;
        var body = new JsonObject
        {
            ["headColor"] = AvatarColor(colors, "headColor3"),
            ["torsoColor"] = AvatarColor(colors, "torsoColor3"),
            ["leftArmColor"] = AvatarColor(colors, "leftArmColor3"),
            ["rightArmColor"] = AvatarColor(colors, "rightArmColor3"),
            ["leftLegColor"] = AvatarColor(colors, "leftLegColor3"),
            ["rightLegColor"] = AvatarColor(colors, "rightLegColor3")
        };

        var scales = new JsonObject();
        if (avatar.TryGetProperty("scales", out var listedScales))
        {
            foreach (var name in new[] { "height", "width", "head", "proportion", "bodyType" })
            {
                if (listedScales.TryGetProperty(name, out var value) && value.TryGetDouble(out var number))
                    scales[name] = number;
            }
        }

        var types = new JsonArray(
            JsonValue.Create("UpdateAvatarType"),
            JsonValue.Create("UpdateBodyColors"),
            JsonValue.Create("UpdateAssets"));
        var model = new JsonObject
        {
            ["playerAvatarType"] = RigName(avatar),
            ["bodyColors"] = body,
            ["assets"] = assetArray
        };
        if (scales.Count == 5)
        {
            types.Insert(1, JsonValue.Create("UpdateScales"));
            model["scales"] = scales;
        }

        types.Add(JsonValue.Create("UpdateEmotes"));
        var request = new JsonObject
        {
            ["updateTypes"] = types,
            ["avatarDefinition"] = new JsonObject
            {
                ["updateAvatarModel"] = model,
                ["updateAvatarConfig"] = new JsonObject
                {
                    ["emoteRequestModels"] = emoteArray
                }
            }
        };

        using var response = await SendAsync(HttpMethod.Patch, UrlBuilder.BuildApiUrl("avatar", "v4/avatar"), request.ToJsonString(), write: true, token);
        var saved = await response.Content.ReadAsStringAsync(token);
        if (saved.Contains("\"success\":false", StringComparison.OrdinalIgnoreCase) || saved.Contains("\"success\": false", StringComparison.OrdinalIgnoreCase))
            throw new RobloxApiException("Roblox did not keep that change. Refresh the avatar and try a different item.");
    }

    private static string RigName(JsonElement avatar)
    {
        if (!avatar.TryGetProperty("playerAvatarType", out var rig))
            return "R15";
        if (rig.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(rig.GetString()))
            return rig.GetString()!;
        if (rig.ValueKind == JsonValueKind.Object && rig.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
            return string.IsNullOrWhiteSpace(name.GetString()) ? "R15" : name.GetString()!;
        return "R15";
    }

    private static string AvatarColor(JsonElement colors, string name)
    {
        if (colors.ValueKind == JsonValueKind.Object && colors.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
        {
            var text = (value.GetString() ?? "").Trim().TrimStart('#');
            if (text.Length >= 6)
                return text[..6].ToUpperInvariant();
        }

        return "A3A2A5";
    }

    private static bool Stacks(string typeName) => typeName is "Hat" or "HairAccessory" or "FaceAccessory" or "NeckAccessory" or "ShoulderAccessory" or "FrontAccessory" or "BackAccessory" or "WaistAccessory";

    private static string MessageText(JsonElement row)
    {
        if (row.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
        {
            var text = content.GetString();
            if (!string.IsNullOrWhiteSpace(text))
                return text;
        }

        if (row.TryGetProperty("pieces", out var pieces) && pieces.ValueKind == JsonValueKind.Array)
        {
            var text = string.Join("", pieces.EnumerateArray().Select(piece => piece.TryGetProperty("content", out var pieceContent) && pieceContent.ValueKind == JsonValueKind.String ? pieceContent.GetString() : ""));
            if (!string.IsNullOrWhiteSpace(text))
                return text;
        }

        if (row.TryGetProperty("preview_content", out var preview) && preview.ValueKind == JsonValueKind.String)
            return preview.GetString() ?? "";

        foreach (var name in new[] { "text", "body" })
        {
            if (row.TryGetProperty(name, out var extra) && extra.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(extra.GetString()))
                return extra.GetString()!;
        }

        return "";
    }

    private async Task<byte[]> DownloadPublicAsync(Uri url, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Version = HttpVersion.Version11;
        request.VersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
        request.Headers.TryAddWithoutValidation("Accept", "*/*");
        request.Headers.Referrer = new Uri("https://www.roblox.com/");
        using var response = await _http.SendAsync(request, token);
        if (!response.IsSuccessStatusCode)
        {
            App.Logger.WriteLine("Avatar", $"Download {url.Host} returned {(int)response.StatusCode}.");
            throw new RobloxApiException($"The file could not be downloaded ({(int)response.StatusCode}).");
        }

        return await response.Content.ReadAsByteArrayAsync(token);
    }

    private async Task<byte[]> DownloadAssetBytesAsync(long assetId, CancellationToken token, string? format = null)
    {
        if (format is "avatar_meshpart_accessory" or "avatar_meshpart_head")
        {
            try
            {
                var list = Convert.ToBase64String(Encoding.UTF8.GetBytes($"[{{\"format\":\"{format}\",\"majorVersion\":\"1\"}}]"));
                var located = UrlBuilder.BuildApiUrl("assetdelivery", $"v2/asset/?id={assetId}&contentRepresentationPriorityList={Uri.EscapeDataString(list)}");
                using var response = await SendAsync(HttpMethod.Get, located, null, write: false, token, json: false, headers: new Dictionary<string, string> { ["Roblox-AssetFormat"] = format });
                var formatted = await ReadLocatedAsync(await response.Content.ReadAsByteArrayAsync(token), token);
                if (formatted.Length > 32)
                    return formatted;
            }
            catch (RobloxApiException)
            {
            }
        }

        var address = UrlBuilder.BuildApiUrl("assetdelivery", $"v1/asset/?id={assetId}");
        byte[] bytes;
        try
        {
            bytes = await DownloadPublicAsync(address, token);
        }
        catch (RobloxApiException)
        {
            using var response = await SendAsync(HttpMethod.Get, address, null, write: false, token, json: false);
            bytes = await response.Content.ReadAsByteArrayAsync(token);
        }

        return await ReadLocatedAsync(bytes, token);
    }

    private async Task<byte[]> ReadLocatedAsync(byte[] bytes, CancellationToken token)
    {
        bytes = Unpack(bytes);
        if (bytes.Length == 0 || bytes[0] != (byte)'{')
            return bytes;

        var json = JsonDocument.Parse(bytes).RootElement;
        var location = json.TryGetProperty("locations", out var rows)
            ? rows.EnumerateArray().Select(row => row.TryGetProperty("location", out var value) ? value.GetString() : null).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            : null;
        if (string.IsNullOrWhiteSpace(location))
            throw new RobloxApiException("Roblox did not return that file.");

        return Unpack(await DownloadPublicAsync(new Uri(location), token));
    }

    private static async Task<byte[]> ReadCappedAsync(HttpContent content, CancellationToken token)
    {
        await using var stream = await content.ReadAsStreamAsync(token);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, token);
            if (read == 0)
                return buffer.ToArray();
            if (buffer.Length + read > 32 * 1024 * 1024)
                throw new RobloxApiException("That file is too large to preview.");
            buffer.Write(chunk, 0, read);
        }
    }

    private async Task<byte[]> ReadPublicCappedAsync(Uri url, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Version = HttpVersion.Version11;
        request.VersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
        request.Headers.TryAddWithoutValidation("Accept", "*/*");
        request.Headers.Referrer = new Uri("https://www.roblox.com/");
        using var response = await _http.SendAsync(request, token);
        if (!response.IsSuccessStatusCode)
        {
            App.Logger.WriteLine("Avatar", $"Render {url.Host} returned {(int)response.StatusCode}.");
            throw new RobloxApiException($"The file could not be downloaded ({(int)response.StatusCode}).");
        }

        return await ReadCappedAsync(response.Content, token);
    }

    private static bool TryLocation(byte[] body, out Uri location)
    {
        location = null!;
        if (body.Length == 0 || body[0] != (byte)'{')
            return false;

        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("locations", out var rows) || rows.ValueKind != JsonValueKind.Array)
                return false;

            foreach (var row in rows.EnumerateArray())
            {
                if (row.ValueKind == JsonValueKind.Object && row.TryGetProperty("location", out var value) && value.ValueKind == JsonValueKind.String &&
                    Uri.TryCreate(value.GetString(), UriKind.Absolute, out var uri) && uri.Scheme == "https")
                {
                    location = uri;
                    return true;
                }
            }
        }
        catch (JsonException)
        {
        }

        return false;
    }

    private static string GuessType(byte[] body)
    {
        if (body.Length >= 8 && body[0] == 0x89 && body[1] == 0x50)
            return "image/png";
        if (body.Length >= 3 && body[0] == 0xff && body[1] == 0xd8)
            return "image/jpeg";
        if (body.Length > 0 && body[0] == (byte)'{')
            return "application/json";
        if (body.Length >= 8 && body[0] == (byte)'<' && body[1] == (byte)'r')
            return "application/octet-stream";
        return "application/octet-stream";
    }

    private static byte[] Unpack(byte[] bytes)
    {
        if (bytes.Length < 2 || bytes[0] != 0x1f || bytes[1] != 0x8b)
            return bytes;

        using var source = new MemoryStream(bytes);
        using var gzip = new System.IO.Compression.GZipStream(source, System.IO.Compression.CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }

    private async Task<JsonElement?> TryJsonAsync(Uri url, CancellationToken token)
    {
        try
        {
            return await GetJsonAsync(url, token);
        }
        catch (Exception ex) when (ex is RobloxApiException or HttpRequestException or JsonException)
        {
            return null;
        }
    }

    private async Task<JsonElement?> TryPostAsync(Uri url, string body, CancellationToken token)
    {
        try
        {
            using var response = await SendAsync(HttpMethod.Post, url, body, write: true, token);
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync(token)).RootElement.Clone();
        }
        catch (Exception ex) when (ex is RobloxApiException or HttpRequestException or JsonException)
        {
            return null;
        }
    }

    private static BodyPaint ReadPaint(JsonElement json)
    {
        string Hex(string name)
        {
            if (json.TryGetProperty("bodyColor3s", out var colors) && colors.ValueKind == JsonValueKind.Object && colors.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString() ?? BodyPaint.Default.Head;
            return "";
        }

        float Scale(string name, float fallback)
        {
            if (json.TryGetProperty("scales", out var scales) && scales.ValueKind == JsonValueKind.Object && scales.TryGetProperty(name, out var value) && value.TryGetSingle(out var number))
                return number;
            return fallback;
        }

        var fallback = BodyPaint.Default;
        return new BodyPaint(
            string.IsNullOrWhiteSpace(Hex("headColor3")) ? fallback.Head : Hex("headColor3"),
            string.IsNullOrWhiteSpace(Hex("torsoColor3")) ? fallback.Torso : Hex("torsoColor3"),
            string.IsNullOrWhiteSpace(Hex("leftArmColor3")) ? fallback.LeftArm : Hex("leftArmColor3"),
            string.IsNullOrWhiteSpace(Hex("rightArmColor3")) ? fallback.RightArm : Hex("rightArmColor3"),
            string.IsNullOrWhiteSpace(Hex("leftLegColor3")) ? fallback.LeftLeg : Hex("leftLegColor3"),
            string.IsNullOrWhiteSpace(Hex("rightLegColor3")) ? fallback.RightLeg : Hex("rightLegColor3"),
            Scale("height", 1),
            Scale("width", 1),
            Scale("head", 1),
            Scale("proportion", 0),
            Scale("bodyType", 0));
    }

    private static Dictionary<string, long> ReadClips(string? rig, IReadOnlyList<WornAsset> assets, IReadOnlyList<WornAsset> emotes)
    {
        var clips = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var classic = string.Equals(rig, "R6", StringComparison.OrdinalIgnoreCase);
        void Put(string name, long id)
        {
            if (id > 0 && !string.IsNullOrWhiteSpace(name))
                clips[name] = id;
        }

        if (classic)
        {
            Put("Idle", 180435571);
            Put("Walk", 180426354);
            Put("Run", 180426354);
            Put("Jump", 125750702);
            Put("Fall", 180436148);
            Put("Climb", 180436334);
            Put("Swim", 180426354);
        }
        else
        {
            Put("Idle", 507766666);
            Put("Walk", 507777826);
            Put("Run", 507767714);
            Put("Jump", 507765000);
            Put("Fall", 507767968);
            Put("Climb", 507765644);
            Put("Swim", 507784897);
        }

        foreach (var asset in assets)
        {
            var motion = asset.TypeName switch
            {
                "IdleAnimation" => "Idle",
                "WalkAnimation" => "Walk",
                "RunAnimation" => "Run",
                "JumpAnimation" => "Jump",
                "FallAnimation" => "Fall",
                "ClimbAnimation" => "Climb",
                "SwimAnimation" => "Swim",
                "MoodAnimation" => "Mood",
                _ => null
            };
            if (motion is not null)
            {
                Put(motion, asset.Id);
                Put(asset.Name, asset.Id);
            }
        }

        foreach (var emote in emotes)
            Put(emote.Name, emote.Id);

        return clips;
    }

    private static string Text(JsonElement? json, string name)
        => AsObject(json, out var element) && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static long? Number(JsonElement? json, string name)
        => AsObject(json, out var element) && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) ? number : null;

    private static bool AsObject(JsonElement? json, out JsonElement element)
    {
        if (json is { ValueKind: JsonValueKind.Object } value)
        {
            element = value;
            return true;
        }

        element = default;
        return false;
    }

    private static string Count(JsonElement? json) => Number(json, "count")?.ToString("N0", CultureInfo.CurrentCulture) ?? "0";

    private static string PresenceText(JsonElement? json)
    {
        if (!AsObject(json, out var element) || !element.TryGetProperty("userPresences", out var rows) || rows.ValueKind != JsonValueKind.Array)
            return "Offline";

        var row = rows.EnumerateArray().FirstOrDefault();
        var kind = row.ValueKind == JsonValueKind.Object && row.TryGetProperty("userPresenceType", out var type) && type.TryGetInt32(out var code) ? code : 0;
        var place = row.ValueKind == JsonValueKind.Object && row.TryGetProperty("lastLocation", out var location) && location.ValueKind == JsonValueKind.String ? location.GetString() : null;
        return kind switch
        {
            1 => "Online",
            2 => string.IsNullOrWhiteSpace(place) ? "In game" : place,
            3 => "In Studio",
            4 => "Invisible",
            _ => "Offline"
        };
    }

    private static string CountryName(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return "Unavailable";

        try
        {
            var region = new RegionInfo(code);
            return $"{region.EnglishName}  {code.ToUpperInvariant()}";
        }
        catch (ArgumentException)
        {
            return code.ToUpperInvariant();
        }
    }

    private static string AgeGroup(JsonElement? birth, JsonElement? verified)
    {
        var years = 0;
        if (AsObject(birth, out var date)
            && date.TryGetProperty("birthYear", out var yearElement) && yearElement.ValueKind == JsonValueKind.Number && yearElement.TryGetInt32(out var year)
            && date.TryGetProperty("birthMonth", out var monthElement) && monthElement.ValueKind == JsonValueKind.Number && monthElement.TryGetInt32(out var month)
            && date.TryGetProperty("birthDay", out var dayElement) && dayElement.ValueKind == JsonValueKind.Number && dayElement.TryGetInt32(out var day)
            && month is >= 1 and <= 12 && day is >= 1 and <= 31)
        {
            var born = new DateTime(year, month, Math.Min(day, DateTime.DaysInMonth(year, month)));
            years = DateTime.Today.Year - born.Year;
            if (born.Date > DateTime.Today.AddYears(-years))
                years--;
        }

        var bracket = years >= 21 ? "21+" : years >= 18 ? "18+" : years >= 13 ? "13+" : years > 0 ? "Under 13" : "Unavailable";
        if (!AsObject(verified, out var check))
            return bracket;

        var isVerified = check.TryGetProperty("isVerified", out var flag) && flag.ValueKind == JsonValueKind.True
            || check.TryGetProperty("verified", out var other) && other.ValueKind == JsonValueKind.True;
        return bracket == "Unavailable" ? (isVerified ? "Verified" : "Unverified") : $"{bracket}  {(isVerified ? "Verified" : "Unverified")}";
    }

    private static string TwoStepText(JsonElement? json)
    {
        if (!AsObject(json, out var element))
            return "Unavailable";

        if (element.TryGetProperty("methods", out var methods) && methods.ValueKind == JsonValueKind.Array)
        {
            foreach (var method in methods.EnumerateArray())
            {
                if (method.TryGetProperty("enabled", out var enabled) && enabled.ValueKind == JsonValueKind.True)
                    return "On";
            }
        }

        return "Off";
    }

    private static string VoiceText(JsonElement? json)
    {
        if (!AsObject(json, out var element))
            return "Unavailable";

        if (element.TryGetProperty("isBanned", out var banned) && banned.ValueKind == JsonValueKind.True)
            return "Suspended";

        var eligible = element.TryGetProperty("isUserEligible", out var allowed) && allowed.ValueKind == JsonValueKind.True;
        var enabled = element.TryGetProperty("isVoiceEnabled", out var on) && on.ValueKind == JsonValueKind.True;
        if (!eligible)
            return "Not eligible";

        return enabled ? "On" : "Eligible";
    }

    private static string GroupName(JsonElement? json)
    {
        if (AsObject(json, out var element) && element.TryGetProperty("group", out var group) && group.ValueKind == JsonValueKind.Object && group.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
            return string.IsNullOrWhiteSpace(name.GetString()) ? "None" : name.GetString()!;

        return "None";
    }

    private static string? HeadshotUrl(JsonElement? json)
    {
        if (!AsObject(json, out var element) || !element.TryGetProperty("data", out var rows) || rows.ValueKind != JsonValueKind.Array)
            return null;

        var row = rows.EnumerateArray().FirstOrDefault();
        return row.ValueKind == JsonValueKind.Object && row.TryGetProperty("imageUrl", out var url) && url.ValueKind == JsonValueKind.String ? url.GetString() : null;
    }

    private async Task<JsonElement> GetJsonAsync(Uri url, CancellationToken token)
    {
        using var response = await SendAsync(HttpMethod.Get, url, null, write: false, token);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(token)).RootElement.Clone();
    }

    public Task<byte[]> ReadAvatarFileAsync(long assetId, string? format, CancellationToken token)
        => DownloadAssetBytesAsync(assetId, token, format);

    public readonly record struct RenderReply(int Status, string ContentType, byte[] Body, bool AsLocation);

    public async Task<RenderReply> ReadRenderRequestAsync(Uri url, IReadOnlyDictionary<string, string>? headers, CancellationToken token)
    {
        if (!string.Equals(url.Scheme, "https", StringComparison.OrdinalIgnoreCase))
            return new RenderReply(403, "text/plain", [], false);

        var host = url.IdnHost;
        var roblox = host.Equals("roblox.com", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".roblox.com", StringComparison.OrdinalIgnoreCase);
        var cdn = host.EndsWith(".rbxcdn.com", StringComparison.OrdinalIgnoreCase);
        if (!roblox && !cdn)
            return new RenderReply(403, "text/plain", [], false);

        byte[] body;
        var status = 200;
        var type = "application/octet-stream";
        if (roblox && !cdn)
        {
            using var response = await SendAsync(HttpMethod.Get, url, null, write: false, token, json: false, headers: headers, throwOnError: false);
            status = (int)response.StatusCode;
            type = response.Content.Headers.ContentType?.MediaType ?? type;
            body = await ReadCappedAsync(response.Content, token);
            App.Logger.WriteLine("Avatar", $"Render {host} returned {status}.");
        }
        else
        {
            body = await ReadPublicCappedAsync(url, token);
        }

        body = Unpack(body);
        if (status == 200 && TryLocation(body, out var location))
        {
            var file = Unpack(await ReadPublicCappedAsync(location, token));
            return new RenderReply(200, GuessType(file), file, true);
        }

        return new RenderReply(status, type, body, false);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, Uri url, string? jsonBody, bool write, CancellationToken token, bool json = true, IReadOnlyDictionary<string, string>? headers = null, bool throwOnError = true)
    {
        var cookie = RequireCookie();
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var request = new HttpRequestMessage(method, url);
            request.Headers.TryAddWithoutValidation("Cookie", ".ROBLOSECURITY=" + cookie);
            request.Headers.TryAddWithoutValidation("Accept", json ? "application/json" : "*/*");
            if (headers is not null)
            {
                foreach (var header in headers)
                    request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            request.Headers.Referrer = new Uri("https://www.roblox.com/");
            if (_csrf is not null && method != HttpMethod.Get)
                request.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", _csrf);
            if (jsonBody is not null)
                request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            var response = await _http.SendAsync(request, token);
            if (response.StatusCode == HttpStatusCode.Forbidden &&
                response.Headers.TryGetValues("x-csrf-token", out var tokens))
            {
                _csrf = tokens.FirstOrDefault();
                response.Dispose();
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                if (!throwOnError)
                    return response;

                var message = await ReadErrorAsync(response, token);
                response.Dispose();
                throw new RobloxApiException(message);
            }

            return response;
        }

        throw new RobloxApiException("Roblox rejected the request. Sign in again and retry.");
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken token)
    {
        var body = await response.Content.ReadAsStringAsync(token);
        try
        {
            var json = JsonDocument.Parse(body).RootElement;
            if (json.TryGetProperty("errors", out var errors))
            {
                var message = errors.EnumerateArray().Select(error => error.TryGetProperty("message", out var text) ? text.GetString() : null).FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));
                if (!string.IsNullOrWhiteSpace(message))
                    return message!;
            }
        }
        catch (JsonException)
        {
        }

        return response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "The Roblox session expired. Launch Roblox while signed in, then try again.",
            HttpStatusCode.Forbidden => "Roblox refused this change. Refresh and try again.",
            HttpStatusCode.TooManyRequests => "Roblox is rate limiting requests. Wait a moment and try again.",
            _ => "Roblox could not complete that request."
        };
    }

    private static bool IsHashPath(Uri uri)
    {
        if (!uri.Host.EndsWith("rbxcdn.com", StringComparison.OrdinalIgnoreCase))
            return false;

        var path = uri.AbsolutePath.Trim('/');
        return path.Length is >= 32 and <= 80 && !path.Contains('/');
    }

    private static Uri ResolveCdn(Uri manifest, string name)
    {
        if (name.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return new Uri(name);

        _ = manifest;
        return new Uri($"https://t{CdnIndex(name)}.rbxcdn.com/{name}");
    }

    private static int CdnIndex(string hash)
    {
        var length = hash.StartsWith("30DAY-", StringComparison.Ordinal) ? Math.Min(hash.Length, 38) : Math.Min(hash.Length, 32);
        var mixed = 31;
        for (var index = 0; index < length; index++)
            mixed ^= hash[index];

        var server = mixed % 8;
        return server < 0 ? server + 8 : server;
    }

    private static string ExtensionFor(byte[] bytes, string typeName)
    {
        if (bytes.Length >= 4 && bytes[0] == 0x89 && bytes[1] == 0x50)
            return "png";
        if (bytes.Length >= 4 && bytes[0] == (byte)'O' && bytes[1] == (byte)'g' && bytes[2] == (byte)'g')
            return "ogg";
        if (bytes.Length >= 3 && bytes[0] == (byte)'I' && bytes[1] == (byte)'D' && bytes[2] == (byte)'3')
            return "mp3";
        if (bytes.Length > 8 && Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 20)).Contains("<roblox", StringComparison.OrdinalIgnoreCase))
            return "rbxm";
        if (typeName.Contains("Animation", StringComparison.OrdinalIgnoreCase) || typeName is "Model" or "Mesh")
            return "rbxm";
        if (typeName is "Audio")
            return "ogg";
        if (typeName is "Decal")
            return "png";
        return "rbxm";
    }

    public static string SafeFileName(string name, long id, string extension)
    {
        var cleaned = string.Join("_", (name ?? "asset").Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (cleaned.Length > 40)
            cleaned = cleaned[..40];
        if (cleaned.Length == 0)
            cleaned = "asset";
        return $"{cleaned}-{id}.{extension}";
    }

    private sealed class AvatarPayload
    {
        [JsonPropertyName("playerAvatarType")]
        public string? PlayerAvatarType { get; set; }

        [JsonPropertyName("assets")]
        public List<AvatarAssetPayload>? Assets { get; set; }

        [JsonPropertyName("emotes")]
        public List<AvatarEmotePayload>? Emotes { get; set; }
    }

    private sealed class AvatarAssetPayload
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("assetType")]
        public AvatarTypePayload? AssetType { get; set; }
    }

    private sealed class AvatarTypePayload
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    private sealed class AvatarEmotePayload
    {
        [JsonPropertyName("assetId")]
        public long AssetId { get; set; }

        [JsonPropertyName("assetName")]
        public string? AssetName { get; set; }
    }
}

public sealed record InventoryCategory(string Id, string Label, string? AssetTypes);

public sealed record WornAsset(long Id, string Name, string TypeName, string TypeLabel, string Group);

public sealed record BodyPaint(string Head, string Torso, string LeftArm, string RightArm, string LeftLeg, string RightLeg, float Height, float Width, float HeadScale, float Proportion, float BodyType)
{
    public static BodyPaint Default { get; } = new("EAB892", "6E6E6E", "EAB892", "EAB892", "6E6E6E", "6E6E6E", 1, 1, 1, 0, 0);
}

public sealed record AccountProfile(
    string DisplayName,
    string Username,
    long UserId,
    string About,
    string Presence,
    string Robux,
    string Friends,
    string Followers,
    string Following,
    string Joined,
    string AccountAge,
    string Country,
    string AgeGroup,
    string TwoStep,
    string Voice,
    string PrimaryGroup,
    string? HeadshotUrl);

public sealed record AvatarSnapshot(long UserId, string DisplayName, string Username, string RigType, IReadOnlyList<WornAsset> Assets, IReadOnlyList<WornAsset> Emotes, BodyPaint Paint, IReadOnlyDictionary<string, long> Clips);

public sealed record InventoryAsset(long Id, string Name, string TypeName);

public sealed record InventoryPage(IReadOnlyList<InventoryAsset> Items, string? NextCursor);

public sealed record AssetDetails(string Name, string Description, string Creator, long? Price, bool ForSale);

public sealed record DownloadedFile(string FileName, byte[] Bytes);

public sealed record ModelPackage(string Obj, byte[]? Texture, IReadOnlyList<DownloadedFile> Files, string? Material = null, IReadOnlyList<(string Name, byte[] Png)>? Textures = null)
{
    public IReadOnlyList<(string Name, byte[] Png)> Surfaces
        => Textures is { Count: > 0 } list ? list : Texture is { Length: > 0 } png ? [("texture", png)] : [];
}

public sealed record ChatConversation(string Id, string Title, string Preview, int Members, int Unread, string UpdatedAt);

public sealed record ChatLine(string Id, long SenderId, string Text, string CreatedAt, bool Mine = false)
{
    public Avalonia.Layout.HorizontalAlignment Alignment => Mine
        ? Avalonia.Layout.HorizontalAlignment.Right
        : Avalonia.Layout.HorizontalAlignment.Left;
}

public sealed record FriendCard(long Id, string DisplayName, string Username);

internal sealed record EditableAsset(long Id, string TypeName, JsonElement? Meta);

internal sealed record AvatarEmoteSlot(long Id, int Slot);

public sealed class RobloxApiException : Exception
{
    public RobloxApiException(string message) : base(message) { }
}
