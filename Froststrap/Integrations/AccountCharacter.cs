using System.Buffers.Binary;
using System.Numerics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Froststrap.Integrations;

/// <summary>
/// A Roblox R15 body made of separate parts. Clothing is the classic shirt and pants
/// templates, and hats and heads are their own meshes welded to a joint. Playing an
/// animation turns those parts. It does not bend the single thumbnail photo.
/// </summary>
public static class AccountCharacter
{
    private const int Width = 585;
    private const int Height = 559;

    public static async Task<AccountMesh> CreateAsync(AccountStudioClient client, IReadOnlyList<WornAsset> assets, BodyPaint paint, CancellationToken token)
    {
        App.Logger.WriteLine("Avatar", "Worn " + string.Join(", ", assets.Select(asset => asset.TypeName).Take(24)));
        var shirtTask = ClothingAsync(client, assets, "Shirt", token);
        var pantsTask = ClothingAsync(client, assets, "Pants", token);
        var faceTask = ClothingAsync(client, assets, "Face", token);
        var piecesTask = Task.WhenAll(assets.Where(IsMeshItem).Take(8).Select(asset => PieceAsync(client, asset, token)));
        await Task.WhenAll(shirtTask, pantsTask, faceTask, piecesTask);

        var shirt = await shirtTask;
        var pants = await pantsTask;
        var face = await faceTask;
        var pieces = (await piecesTask).Where(piece => piece is not null).Cast<Piece>().ToList();
        var mesh = Assemble(paint, shirt, pants, face, pieces);
        App.Logger.WriteLine("Avatar", $"Character ready: {mesh.Faces.Count} triangles, clothes {(shirt is null ? 0 : 1) + (pants is null ? 0 : 1)}, accessories {pieces.Count}.");
        shirt?.Dispose();
        pants?.Dispose();
        face?.Dispose();
        foreach (var piece in pieces)
            piece.Texture?.Dispose();
        return mesh;
    }

    private static AccountMesh Assemble(BodyPaint paint, Image<Rgba32>? shirt, Image<Rgba32>? pants, Image<Rgba32>? face, List<Piece> pieces)
    {
        var surfaces = new List<Surface>();
        var shirtIndex = AddSurface(surfaces, shirt);
        var pantsIndex = AddSurface(surfaces, pants);
        var faceIndex = AddSurface(surfaces, face);
        var y = Math.Clamp(paint.Height <= 0 ? 1f : paint.Height, 0.7f, 1.25f);
        var x = Math.Clamp(paint.Width <= 0 ? 1f : paint.Width, 0.7f, 1.25f);
        var head = Math.Clamp(paint.HeadScale <= 0 ? 1f : paint.HeadScale, 0.75f, 1.5f);
        var legs = 1f + Math.Clamp(paint.Proportion, 0f, 1f) * 0.4f;
        var torso = 1f + Math.Clamp(paint.BodyType, 0f, 1f) * 0.35f;
        var coveredHead = pieces.Any(piece => piece.Anchor is "head" or "hat");
        var colors = new Dictionary<string, Rgba32>(StringComparer.Ordinal)
        {
            ["head"] = Rgb(paint.Head),
            ["torso"] = Rgb(paint.Torso),
            ["left"] = Rgb(paint.LeftArm),
            ["right"] = Rgb(paint.RightArm),
            ["legs"] = Rgb(paint.LeftLeg)
        };
        var builder = new Body();
        var pivots = new Vector3[16];
        void Joint(int bone, int parent, Vector3 rest) => pivots[bone] = parent < 0 ? rest : pivots[parent] + rest;
        Joint(0, -1, Vector3.Zero);
        Joint(1, 0, Vector3.Zero);
        Joint(2, 1, new Vector3(0, 0.72f * y, 0));
        Joint(3, 2, new Vector3(0, 1.12f * y, 0));
        Joint(4, 2, new Vector3(-1.2f * x * torso, 0.85f * y, 0));
        Joint(5, 4, new Vector3(0, -1f * y, 0));
        Joint(6, 5, new Vector3(0, -0.9f * y * legs, 0));
        Joint(7, 2, new Vector3(1.2f * x * torso, 0.85f * y, 0));
        Joint(8, 7, new Vector3(0, -1f * y, 0));
        Joint(9, 8, new Vector3(0, -0.9f * y * legs, 0));
        Joint(10, 1, new Vector3(-0.42f * x, -0.08f * y, 0));
        Joint(11, 10, new Vector3(0, -1.1f * y * legs, 0));
        Joint(12, 11, new Vector3(0, -1f * y * legs, 0));
        Joint(13, 1, new Vector3(0.42f * x, -0.08f * y, 0));
        Joint(14, 13, new Vector3(0, -1.1f * y * legs, 0));
        Joint(15, 14, new Vector3(0, -1f * y * legs, 0));

        var lowerShirt = shirt is not null && Covers(shirt, 231, 150, 128, 52);
        Box(1, pivots[1] + new Vector3(0, 0.32f * y, 0), new Vector3(1.6f * x * torso, 0.78f * y, 0.95f), "torso", lowerShirt ? shirtIndex : pantsIndex >= 0 ? pantsIndex : shirtIndex, 0.55f, 1f, colors["torso"]);
        Box(2, pivots[2] + new Vector3(0, 0.5f * y, 0), new Vector3(2f * x * torso, 1.12f * y, 1.02f), "torso", shirtIndex >= 0 ? shirtIndex : pantsIndex, 0f, 0.62f, colors["torso"]);
        if (!coveredHead)
            Box(3, pivots[3] + new Vector3(0, 0.55f * head, 0), new Vector3(1.15f * head, 1.1f * head, 1.15f * head), "head", -1, 0f, 1f, colors["head"]);
        Limb(4, pivots[4] + new Vector3(0, -0.5f * y, 0), new Vector3(0.85f * x, 1.05f * y, 0.85f), "left", shirtIndex, 0f, 0.5f, colors["left"]);
        Limb(5, pivots[5] + new Vector3(0, -0.42f * y * legs, 0), new Vector3(0.75f * x, 0.95f * y * legs, 0.75f), "left", shirtIndex, 0.42f, 0.82f, colors["left"]);
        Limb(6, pivots[6] + new Vector3(0, -0.16f * y, 0), new Vector3(0.6f * x, 0.4f * y, 0.75f), "left", shirtIndex, 0.75f, 1f, colors["left"]);
        Limb(7, pivots[7] + new Vector3(0, -0.5f * y, 0), new Vector3(0.85f * x, 1.05f * y, 0.85f), "right", shirtIndex, 0f, 0.5f, colors["right"]);
        Limb(8, pivots[8] + new Vector3(0, -0.42f * y * legs, 0), new Vector3(0.75f * x, 0.95f * y * legs, 0.75f), "right", shirtIndex, 0.42f, 0.82f, colors["right"]);
        Limb(9, pivots[9] + new Vector3(0, -0.16f * y, 0), new Vector3(0.6f * x, 0.4f * y, 0.75f), "right", shirtIndex, 0.75f, 1f, colors["right"]);
        Limb(10, pivots[10] + new Vector3(0, -0.55f * y * legs, 0), new Vector3(0.88f * x, 1.15f * y * legs, 0.88f), "left", pantsIndex, 0f, 0.52f, colors["legs"]);
        Limb(11, pivots[11] + new Vector3(0, -0.48f * y * legs, 0), new Vector3(0.75f * x, 1.05f * y * legs, 0.75f), "left", pantsIndex, 0.45f, 0.86f, colors["legs"]);
        Limb(12, pivots[12] + new Vector3(0, -0.1f * y, 0.08f), new Vector3(0.8f * x, 0.34f * y, 1.2f), "left", pantsIndex, 0.8f, 1f, colors["legs"]);
        Limb(13, pivots[13] + new Vector3(0, -0.55f * y * legs, 0), new Vector3(0.88f * x, 1.15f * y * legs, 0.88f), "right", pantsIndex, 0f, 0.52f, colors["legs"]);
        Limb(14, pivots[14] + new Vector3(0, -0.48f * y * legs, 0), new Vector3(0.75f * x, 1.05f * y * legs, 0.75f), "right", pantsIndex, 0.45f, 0.86f, colors["legs"]);
        Limb(15, pivots[15] + new Vector3(0, -0.1f * y, 0.08f), new Vector3(0.8f * x, 0.34f * y, 1.2f), "right", pantsIndex, 0.8f, 1f, colors["legs"]);

        if (faceIndex >= 0)
            builder.RetextureHeadFront(faceIndex);

        var bodyCenter = new Vector3(0, 1.5f * y, 0);
        foreach (var piece in pieces)
        {
            var surface = AddSurface(surfaces, piece.Texture);
            var anchor = piece.Anchor == "torso" ? new Vector3(0, 1.6f * y, 0) : AnchorOf(piece.Anchor, pivots, y, head);
            builder.AddMesh(piece, anchor, surface, colors["head"]);
        }

        var positions = builder.Positions;
        var center = bodyCenter;
        var fit = 4f;
        if (positions.Count > 0)
        {
            float Band(Func<Vector3, float> axis, float t)
            {
                var values = positions.Select(axis).OrderBy(value => value).ToArray();
                return values[(int)Math.Clamp(t * (values.Length - 1), 0, values.Length - 1)];
            }

            var min = new Vector3(Band(point => point.X, 0.02f), Band(point => point.Y, 0.02f), Band(point => point.Z, 0.02f));
            var max = new Vector3(Band(point => point.X, 0.98f), Band(point => point.Y, 0.98f), Band(point => point.Z, 0.98f));
            center = (min + max) * 0.5f;
            fit = Math.Max(1f, (max - min).Length());
        }

        return new AccountMesh
        {
            Positions = positions,
            Uvs = builder.Uvs,
            Faces = builder.Faces,
            FaceColors = builder.Colors.ToArray(),
            Surfaces = surfaces.Count > 0 ? surfaces.ToArray() : null,
            FaceSurface = builder.Surfaces.ToArray(),
            VertexBone = builder.Bones.ToArray(),
            Bind = positions.ToArray(),
            Pivots = pivots,
            Center = center,
            Fit = Math.Max(1f, fit),
            PoseScale = 1f,
            CanPose = positions.Count > 0,
            RigidParts = true
        };

        void Box(int bone, Vector3 center, Vector3 size, string slot, int surface, float v0, float v1, Rgba32 color)
            => builder.AddBox(bone, center, size, slot, surface, v0, v1, color);

        void Limb(int bone, Vector3 center, Vector3 size, string side, int surface, float v0, float v1, Rgba32 color)
            => builder.AddBox(bone, center, size, side, surface, v0, v1, color);
    }

    private static Vector3 AnchorOf(string anchor, Vector3[] pivots, float y, float head) => anchor switch
    {
        "head" => pivots[3] + new Vector3(0, 0.5f * head, 0),
        "hat" => pivots[3],
        "neck" => pivots[2] + new Vector3(0, 0.9f * y, 0),
        "back" => pivots[2] + new Vector3(0, 0.4f * y, 0.55f),
        "front" => pivots[2] + new Vector3(0, 0.4f * y, -0.55f),
        "waist" => pivots[1] + new Vector3(0, 0.2f * y, 0),
        "left-foot" => pivots[12],
        "right-foot" => pivots[15],
        "left-arm" => pivots[4],
        "right-arm" => pivots[7],
        _ => pivots[2] + new Vector3(0, 0.35f * y, 0)
    };

    private static async Task<Image<Rgba32>?> ClothingAsync(AccountStudioClient client, IReadOnlyList<WornAsset> assets, string type, CancellationToken token)
    {
        var asset = assets.FirstOrDefault(item => item.TypeName == type);
        if (asset is null)
            return null;

        try
        {
            var image = await ImageOfAsync(client, await client.ReadAvatarFileAsync(asset.Id, null, token), token);
            return type == "Face" ? image : Template(image);
        }
        catch (Exception ex) when (ex is RobloxApiException or HttpRequestException or UnknownImageFormatException or InvalidImageContentException)
        {
            App.Logger.WriteLine("Avatar", $"Skipped the {type} image.");
            return null;
        }
    }

    private static async Task<Piece?> PieceAsync(AccountStudioClient client, WornAsset asset, CancellationToken token)
    {
        try
        {
            var format = asset.TypeName is "Head" or "DynamicHead" ? "avatar_meshpart_head" : "avatar_meshpart_accessory";
            var bytes = await client.ReadAvatarFileAsync(asset.Id, format, token);
            if (!(TryMesh(bytes, out var mesh) || TryEmbeddedMesh(bytes, out mesh)) || mesh.Indices.Count < 24)
            {
                mesh = new Mesh([], [], []);
                Image<Rgba32>? texture = null;
                var meshId = IdAfter(bytes, "MeshId");
                var textureId = IdAfter(bytes, "TextureID") ?? IdAfter(bytes, "TextureId");
                var candidates = new List<long>();
                if (meshId is long meshAsset)
                    candidates.Add(meshAsset);
                if (textureId is long textureAsset)
                    candidates.Add(textureAsset);
                candidates.AddRange(Ids(bytes).Where(id => !candidates.Contains(id)).Take(4));
                foreach (var id in candidates)
                {
                    var child = await client.ReadAvatarFileAsync(id, null, token);
                    if (mesh.Positions.Count == 0 && (TryMesh(child, out var nested) || TryEmbeddedMesh(child, out nested)))
                    {
                        if (nested.Indices.Count < 24)
                            App.Logger.WriteLine("Avatar", $"Weak {asset.TypeName} {Describe(child)}");
                        else
                            mesh = nested;
                    }
                    else if (id == meshId)
                        App.Logger.WriteLine("Avatar", $"Mesh file {child.Length} bytes, {Encoding.ASCII.GetString(child, 0, Math.Min(16, child.Length)).Replace('\n', ' ')}");
                    else if (texture is null)
                        texture = await ImageOfAsync(client, child, token);
                    if (mesh.Positions.Count > 0 && texture is not null)
                        break;
                }

                if (mesh.Positions.Count == 0)
                {
                    foreach (var blob in AccountClip.ModelBlobs(bytes))
                    {
                        if ((TryMesh(blob, out var nested) || TryEmbeddedMesh(blob, out nested)) && nested.Indices.Count >= 24)
                        {
                            mesh = nested;
                            break;
                        }
                    }
                }

                if (mesh.Positions.Count == 0)
                {
                    texture?.Dispose();
                    var blobs = AccountClip.ModelBlobs(bytes);
                    App.Logger.WriteLine("Avatar", $"Accessory {asset.TypeName} {bytes.Length} bytes, no mesh. blobs {string.Join(",", blobs.Select(blob => blob.Length).Take(6))}. {Around(bytes, "MeshId")}");
                    return null;
                }

                App.Logger.WriteLine("Avatar", $"{asset.TypeName} {mesh.Indices.Count / 3} triangles.");
                return new Piece(mesh, texture, Anchor(asset.TypeName));
            }

            App.Logger.WriteLine("Avatar", $"{asset.TypeName} {mesh.Indices.Count / 3} triangles.");
            return new Piece(mesh, null, Anchor(asset.TypeName));
        }
        catch (Exception ex) when (ex is RobloxApiException or HttpRequestException or UnknownImageFormatException or InvalidImageContentException)
        {
            App.Logger.WriteLine("Avatar", $"Skipped accessory {asset.Id}.");
            return null;
        }
    }

    private static bool IsMeshItem(WornAsset asset) => asset.TypeName is "Hat" or "HairAccessory" or "FaceAccessory" or "NeckAccessory" or "ShoulderAccessory" or "FrontAccessory" or "BackAccessory" or "WaistAccessory" or "Head" or "DynamicHead" or "LeftShoeAccessory" or "RightShoeAccessory" or "ShirtAccessory" or "PantsAccessory" or "JacketAccessory" or "SweaterAccessory" or "ShortsAccessory" or "DressSkirtAccessory" or "TShirtAccessory";

    private static string Anchor(string type) => type switch
    {
        "Hat" or "HairAccessory" => "hat",
        "FaceAccessory" or "Head" or "DynamicHead" => "head",
        "NeckAccessory" => "neck",
        "BackAccessory" => "back",
        "FrontAccessory" => "front",
        "WaistAccessory" or "PantsAccessory" or "ShortsAccessory" or "DressSkirtAccessory" => "waist",
        "LeftShoeAccessory" => "left-foot",
        "RightShoeAccessory" => "right-foot",
        "ShoulderAccessory" => "left-arm",
        _ => "torso"
    };

    private static async Task<Image<Rgba32>?> ImageOfAsync(AccountStudioClient client, byte[] bytes, CancellationToken token)
    {
        if (IsImage(bytes))
            return Image.Load<Rgba32>(bytes);

        foreach (var id in Ids(bytes).Take(4))
        {
            var child = await client.ReadAvatarFileAsync(id, null, token);
            if (IsImage(child))
                return Image.Load<Rgba32>(child);
        }

        return null;
    }

    private static Image<Rgba32>? Template(Image<Rgba32>? source)
    {
        if (source is null || source.Width == Width && source.Height == Height)
            return source;

        var canvas = new Image<Rgba32>(Width, Height);
        for (var y = 0; y < Height; y++)
        {
            var sy = Math.Clamp(y * source.Height / Height, 0, source.Height - 1);
            for (var x = 0; x < Width; x++)
                canvas[x, y] = source[Math.Clamp(x * source.Width / Width, 0, source.Width - 1), sy];
        }

        source.Dispose();
        return canvas;
    }

    private static string Describe(byte[] bytes)
    {
        var newline = Array.IndexOf(bytes, (byte)'\n');
        var head = newline is > 0 and < 20 ? Encoding.ASCII.GetString(bytes, 0, newline) : Encoding.ASCII.GetString(bytes, 0, Math.Min(12, bytes.Length)).Replace('\0', ' ');
        if (newline < 10 || newline + 16 >= bytes.Length)
            return $"{bytes.Length} bytes, {head}";

        var start = newline + 1;
        return $"{bytes.Length} bytes, {head}, {ReadUInt16(bytes, start)}/{ReadUInt16(bytes, start + 2)}/{ReadInt32(bytes, start + 4)}/{ReadInt32(bytes, start + 8)}/{ReadUInt16(bytes, start + 12)}/{ReadUInt16(bytes, start + 14)}";
    }

    private static long? IdAfter(byte[] bytes, string token)
    {
        var name = Encoding.ASCII.GetBytes(token);
        var url = "rbxassetid://"u8;
        for (var index = 0; index + name.Length < bytes.Length; index++)
        {
            if (!bytes.AsSpan(index, name.Length).SequenceEqual(name))
                continue;

            var window = Math.Min(bytes.Length - url.Length, index + name.Length + 48);
            for (var at = index + name.Length; at <= window; at++)
            {
                if (!bytes.AsSpan(at, url.Length).SequenceEqual(url))
                    continue;

                long id = 0;
                var digits = 0;
                for (var cursor = at + url.Length; cursor < bytes.Length && bytes[cursor] is >= (byte)'0' and <= (byte)'9' && digits < 18; cursor++)
                {
                    id = id * 10 + (bytes[cursor] - (byte)'0');
                    digits++;
                }

                if (digits >= 5)
                    return id;
            }
        }

        return null;
    }

    private static string Around(byte[] bytes, string token)
    {
        var text = Encoding.Latin1.GetString(bytes);
        var at = text.IndexOf(token, StringComparison.Ordinal);
        if (at < 0)
            return token + " missing";

        var slice = text.Substring(at, Math.Min(80, text.Length - at));
        return new string(slice.Select(ch => ch is >= ' ' and < (char)127 ? ch : '.').ToArray());
    }

    private static bool IsImage(byte[] bytes)
        => bytes.Length > 8 && (bytes[0] == 0x89 && bytes[1] == 0x50 || bytes[0] == 0xFF && bytes[1] == 0xD8);

    private static IEnumerable<long> Ids(byte[] bytes)
    {
        var text = Encoding.Latin1.GetString(bytes);
        var seen = new HashSet<long>();
        foreach (Match match in Regex.Matches(text, @"(?:rbxassetid://|\?id=|asset/\?id=)(\d{5,})"))
        {
            if (long.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && seen.Add(id))
                yield return id;
        }
    }

    private static int AddSurface(List<Surface> surfaces, Image<Rgba32>? image)
    {
        if (image is null || image.Width == 0 || image.Height == 0)
            return -1;

        var pixels = new Rgba32[image.Width * image.Height];
        image.CopyPixelDataTo(pixels);
        surfaces.Add(new Surface(pixels, image.Width, image.Height));
        return surfaces.Count - 1;
    }

    private static bool Covers(Image<Rgba32> image, int x, int y, int w, int h)
    {
        var hits = 0;
        var samples = 0;
        for (var yy = y; yy < y + h && yy < image.Height; yy += 8)
        {
            for (var xx = x; xx < x + w && xx < image.Width; xx += 8)
            {
                samples++;
                if (image[xx, yy].A > 24)
                    hits++;
            }
        }

        return samples > 0 && hits * 3 > samples;
    }

    private static bool TryEmbeddedMesh(byte[] bytes, out Mesh mesh)
    {
        mesh = new Mesh([], [], []);
        var marker = "version "u8;
        for (var index = 0; index + 16 < bytes.Length; index++)
        {
            if (!bytes.AsSpan(index, marker.Length).SequenceEqual(marker))
                continue;
            if (TryMesh(bytes[index..], out mesh))
                return true;
        }

        return false;
    }

    private static bool TryMesh(byte[] bytes, out Mesh mesh)
    {
        mesh = new Mesh([], [], []);
        if (bytes.Length < 20)
            return false;

        var header = Encoding.ASCII.GetString(bytes, 0, Math.Min(20, bytes.Length));
        if (!header.StartsWith("version ", StringComparison.Ordinal))
            return false;

        return header.StartsWith("version 1.", StringComparison.Ordinal) ? TryTextMesh(bytes, out mesh) : TryBinaryMesh(bytes, out mesh);
    }

    private static bool TryTextMesh(byte[] bytes, out Mesh mesh)
    {
        mesh = new Mesh([], [], []);
        var text = Encoding.ASCII.GetString(bytes);
        var values = new List<float>();
        foreach (Match match in Regex.Matches(text, @"\[([-+0-9.eE]+),([-+0-9.eE]+),([-+0-9.eE]+)\]"))
        {
            if (float.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var a) &&
                float.TryParse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var b) &&
                float.TryParse(match.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var c))
            {
                values.Add(a);
                values.Add(b);
                values.Add(c);
            }
        }

        if (values.Count < 27)
            return false;

        for (var index = 0; index + 8 < values.Count; index += 9)
        {
            mesh.Positions.Add(new Vector3(values[index], values[index + 1], values[index + 2]));
            mesh.Uvs.Add(new Vector2(values[index + 6], values[index + 7]));
        }

        for (var index = 0; index + 2 < mesh.Positions.Count; index += 3)
            mesh.Indices.AddRange([index, index + 1, index + 2]);
        return mesh.Indices.Count >= 3;
    }

    private static bool TryBinaryMesh(byte[] bytes, out Mesh mesh)
    {
        mesh = new Mesh([], [], []);
        var newline = Array.IndexOf(bytes, (byte)'\n');
        if (newline < 10 || newline + 14 >= bytes.Length || bytes[0] != (byte)'v')
            return false;

        var major = bytes[8] is >= (byte)'2' and <= (byte)'7' ? bytes[8] - (byte)'0' : 0;
        var start = newline + 1;
        return major switch
        {
            2 => ReadSized(bytes, start, false, out mesh),
            3 => ReadSized(bytes, start, true, out mesh),
            4 or 5 => ReadModern(bytes, start, out mesh),
            6 or 7 => ReadChunks(bytes, start, major == 7, out mesh),
            _ => false
        };
    }

    private static bool ReadSized(byte[] bytes, int start, bool lod, out Mesh mesh)
    {
        mesh = new Mesh([], [], []);
        if (start + (lod ? 16 : 12) > bytes.Length)
            return false;

        var headerSize = ReadUInt16(bytes, start);
        var stride = bytes[start + 2];
        var faceStride = bytes[start + 3];
        var lods = lod ? ReadUInt16(bytes, start + 6) : 0;
        var countAt = lod ? start + 8 : start + 4;
        var vertices = ReadInt32(bytes, countAt);
        var faces = ReadInt32(bytes, countAt + 4);
        if (headerSize < 12 || stride < 12 || faceStride < 12 || vertices is < 1 or > 60000 || faces is < 1 or > 120000)
            return false;

        var origin = start + headerSize;
        if (!Fits(origin, vertices, stride, 0, faces, faceStride, bytes.Length))
            return false;

        CopyVertices(mesh, bytes, origin, vertices, stride);
        var facesAt = origin + vertices * stride;
        var (from, count) = lods >= 2 ? LodBand(bytes, facesAt + faces * faceStride, lods, faces) : (0, Math.Min(faces, 9000));
        CopyFaces(mesh, bytes, facesAt + from * faceStride, count, faceStride, vertices);
        return mesh.Indices.Count >= 3;
    }

    private static bool ReadModern(byte[] bytes, int start, out Mesh mesh)
    {
        mesh = new Mesh([], [], []);
        if (start + 16 > bytes.Length)
            return false;

        var headerSize = ReadUInt16(bytes, start);
        var vertices = ReadInt32(bytes, start + 4);
        var faces = ReadInt32(bytes, start + 8);
        var lods = ReadUInt16(bytes, start + 12);
        var bones = ReadUInt16(bytes, start + 14);
        if (headerSize < 16 || start + headerSize > bytes.Length || vertices is < 1 or > 60000 || faces is < 1 or > 120000)
            return false;

        var skin = bones > 0 ? vertices * 8 : 0;
        var origin = start + headerSize;
        var stride = Fits(origin, vertices, 40, skin, faces, 12, bytes.Length) ? 40 : 36;
        if (!Fits(origin, vertices, stride, skin, faces, 12, bytes.Length))
            return false;

        CopyVertices(mesh, bytes, origin, vertices, stride);
        var facesAt = origin + vertices * stride + skin;
        var (from, count) = lods >= 2 ? LodBand(bytes, facesAt + faces * 12, lods, faces) : (0, Math.Min(faces, 9000));
        CopyFaces(mesh, bytes, facesAt + from * 12, count, 12, vertices);
        return mesh.Indices.Count >= 3;
    }

    private static bool ReadChunks(byte[] bytes, int start, bool draco, out Mesh mesh)
    {
        mesh = new Mesh([], [], []);
        var cursor = start;
        var vertices = 0;
        var vertAt = -1;
        var stride = 40;
        var faces = 0;
        var faceAt = -1;
        var lodAt = -1;
        var lods = 0;
        while (cursor + 16 <= bytes.Length)
        {
            var type = Encoding.ASCII.GetString(bytes, cursor, 8);
            var size = ReadInt32(bytes, cursor + 12);
            var data = cursor + 16;
            if (size < 0 || data < 0 || (long)data + size > bytes.Length)
                return false;

            if (type == "COREMESH")
            {
                if (draco || size < 8)
                    return false;

                vertices = ReadInt32(bytes, data);
                if (vertices is < 1 or > 60000)
                    return false;

                vertAt = data + 4;
                var faceCountAt = vertAt + vertices * 40;
                if (faceCountAt + 4 > data + size)
                {
                    stride = 36;
                    faceCountAt = vertAt + vertices * 36;
                }

                if (faceCountAt + 4 > data + size)
                    return false;

                faces = ReadInt32(bytes, faceCountAt);
                faceAt = faceCountAt + 4;
                if (faces is < 1 or > 120000 || (long)faceAt + (long)faces * 12 > data + size)
                    return false;
            }
            else if (type == "LODS" && size >= 12)
            {
                lods = ReadInt32(bytes, data + 4);
                lodAt = data + 8;
            }

            cursor = data + size;
        }

        if (vertAt < 0 || faceAt < 0)
            return false;

        CopyVertices(mesh, bytes, vertAt, vertices, stride);
        var (from, count) = lods >= 2 && lodAt >= 0 ? LodBand(bytes, lodAt, lods, faces) : (0, Math.Min(faces, 9000));
        CopyFaces(mesh, bytes, faceAt + from * 12, count, 12, vertices);
        return mesh.Indices.Count >= 3;
    }

    private static bool Fits(int origin, int vertices, int stride, int skin, int faces, int faceStride, int length)
    {
        var end = (long)origin + (long)vertices * stride + skin + (long)faces * faceStride;
        return origin >= 0 && stride > 0 && end >= 0 && end <= length;
    }

    private static (int From, int Count) LodBand(byte[] bytes, int at, int lods, int faces)
    {
        var marks = new List<int>();
        var count = Math.Min(lods, 8);
        for (var index = 0; index < count; index++)
        {
            var pos = at + index * 4;
            if (pos < 0 || pos + 4 > bytes.Length)
                break;

            marks.Add(ReadInt32(bytes, pos));
        }

        var bestFrom = 0;
        var bestCount = 0;
        for (var index = 0; index + 1 < marks.Count; index++)
        {
            var begin = marks[index];
            var end = marks[index + 1];
            if (begin < 0 || end <= begin || end > faces)
                continue;

            var band = end - begin;
            if (band <= 9000 && band > bestCount)
            {
                bestFrom = begin;
                bestCount = band;
            }
        }

        if (bestCount > 0)
            return (bestFrom, bestCount);

        return (0, Math.Min(faces, 9000));
    }

    private static void CopyVertices(Mesh mesh, byte[] bytes, int origin, int vertices, int stride)
    {
        for (var index = 0; index < vertices; index++)
        {
            var at = origin + index * stride;
            mesh.Positions.Add(new Vector3(ReadSingle(bytes, at), ReadSingle(bytes, at + 4), ReadSingle(bytes, at + 8)));
            mesh.Uvs.Add(stride >= 32 ? new Vector2(ReadSingle(bytes, at + 24), ReadSingle(bytes, at + 28)) : Vector2.Zero);
        }
    }

    private static void CopyFaces(Mesh mesh, byte[] bytes, int at, int faces, int stride, int vertices)
    {
        for (var index = 0; index < faces; index++)
        {
            var cursor = at + index * stride;
            if (cursor < 0 || cursor + 12 > bytes.Length)
                return;

            var a = ReadInt32(bytes, cursor);
            var b = ReadInt32(bytes, cursor + 4);
            var c = ReadInt32(bytes, cursor + 8);
            if ((uint)a < (uint)vertices && (uint)b < (uint)vertices && (uint)c < (uint)vertices)
                mesh.Indices.AddRange([a, b, c]);
        }
    }

    private static ushort ReadUInt16(byte[] bytes, int offset)
        => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset));

    private static int ReadInt32(byte[] bytes, int offset)
        => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset));

    private static float ReadSingle(byte[] bytes, int offset)
        => BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(offset));

    private static Rgba32 Rgb(string hex)
    {
        var text = (hex ?? "").Trim().TrimStart('#');
        if (text.Length >= 6 && uint.TryParse(text[..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
            return new Rgba32((byte)(value >> 16), (byte)(value >> 8), (byte)value);

        return new Rgba32(196, 196, 196);
    }

    private sealed class Body
    {
        public List<Vector3> Positions { get; } = [];
        public List<Vector2> Uvs { get; } = [];
        public List<int[]> Faces { get; } = [];
        public List<int> Surfaces { get; } = [];
        public List<Rgba32> Colors { get; } = [];
        public List<int> Bones { get; } = [];

        public void AddBox(int bone, Vector3 center, Vector3 size, string slot, int surface, float v0, float v1, Rgba32 color)
        {
            var half = size * 0.5f;
            Vector3 At(int corner) => center + new Vector3((corner & 1) == 0 ? -half.X : half.X, (corner & 2) == 0 ? -half.Y : half.Y, (corner & 4) == 0 ? -half.Z : half.Z);
            var point = Enumerable.Range(0, 8).Select(At).ToArray();
            Face(bone, point[0], point[1], point[3], point[2], Region(slot, "front", v0, v1), surface, color);
            Face(bone, point[5], point[4], point[6], point[7], Region(slot, "back", v0, v1), surface, color);
            Face(bone, point[4], point[0], point[2], point[6], Region(slot, "left", v0, v1), surface, color);
            Face(bone, point[1], point[5], point[7], point[3], Region(slot, "right", v0, v1), surface, color);
            Face(bone, point[2], point[3], point[7], point[6], Region(slot, "up", 0f, 1f), surface, color);
            Face(bone, point[4], point[5], point[1], point[0], Region(slot, "down", 0f, 1f), surface, color);
        }

        public void RetextureHeadFront(int surface)
        {
            const int triangle = 24;
            const int vertex = 48;
            if (Surfaces.Count <= triangle + 1 || Uvs.Count < vertex + 4)
                return;

            Surfaces[triangle] = surface;
            Surfaces[triangle + 1] = surface;
            Uvs[vertex] = new Vector2(1, 0);
            Uvs[vertex + 1] = new Vector2(0, 0);
            Uvs[vertex + 2] = new Vector2(0, 1);
            Uvs[vertex + 3] = new Vector2(1, 1);
        }

        public void AddMesh(Piece piece, Vector3 anchor, int surface, Rgba32 color)
        {
            var source = piece.Geometry;
            if (source.Positions.Count == 0)
                return;

            float At(float t, Func<Vector3, float> axis)
            {
                var values = source.Positions.Select(axis).OrderBy(value => value).ToArray();
                return values[Math.Clamp((int)(t * (values.Length - 1)), 0, values.Length - 1)];
            }

            var min = new Vector3(At(0.02f, point => point.X), At(0.02f, point => point.Y), At(0.02f, point => point.Z));
            var max = new Vector3(At(0.98f, point => point.X), At(0.98f, point => point.Y), At(0.98f, point => point.Z));
            var size = Vector3.Max(max - min, new Vector3(0.05f));
            var native = MathF.Max(size.X, MathF.Max(size.Y, size.Z));
            var onHead = piece.Anchor is "hat" or "head";
            var scale = onHead ? 1.7f / native : native is >= 0.45f and <= 6f ? 1f : 1.4f / native;
            var mid = (min + max) * 0.5f;
            var margin = size * 0.35f + new Vector3(0.05f);
            var map = new int[source.Positions.Count];
            var bone = Bone(piece.Anchor);
            for (var index = 0; index < source.Positions.Count; index++)
            {
                var point = source.Positions[index];
                var inside = point.X >= min.X - margin.X && point.X <= max.X + margin.X
                    && point.Y >= min.Y - margin.Y && point.Y <= max.Y + margin.Y
                    && point.Z >= min.Z - margin.Z && point.Z <= max.Z + margin.Z;
                if (!inside)
                {
                    map[index] = -1;
                    continue;
                }

                map[index] = Positions.Count;
                Positions.Add(anchor + (point - mid) * scale);
                Bones.Add(bone);
                Uvs.Add(index < source.Uvs.Count ? source.Uvs[index] : Vector2.Zero);
            }

            for (var index = 0; index + 2 < source.Indices.Count; index += 3)
            {
                var a = map[source.Indices[index]];
                var b = map[source.Indices[index + 1]];
                var c = map[source.Indices[index + 2]];
                if (a < 0 || b < 0 || c < 0)
                    continue;

                Faces.Add([a, a, b, b, c, c]);
                Surfaces.Add(surface);
                Colors.Add(color);
            }
        }

        private void Face(int bone, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Rect region, int surface, Rgba32 color)
        {
            var start = Positions.Count;
            Positions.AddRange([a, b, c, d]);
            Bones.AddRange([bone, bone, bone, bone]);
            if (surface < 0 || region.W <= 0)
            {
                Uvs.AddRange([Vector2.Zero, Vector2.Zero, Vector2.Zero, Vector2.Zero]);
                surface = -1;
            }
            else
            {
                Vector2 Map(float u, float v)
                {
                    var px = region.X + u * region.W;
                    var py = region.Y + v * region.H;
                    return new Vector2(px / Width, 1f - py / Height);
                }

                Uvs.AddRange([Map(0, 1), Map(1, 1), Map(1, 0), Map(0, 0)]);
            }

            Faces.Add([start, start, start + 1, start + 1, start + 2, start + 2]);
            Faces.Add([start, start, start + 2, start + 2, start + 3, start + 3]);
            Surfaces.Add(surface);
            Surfaces.Add(surface);
            Colors.Add(color);
            Colors.Add(color);
        }

        private static Rect Region(string slot, string face, float v0, float v1)
        {
            if (slot is not ("torso" or "left" or "right"))
                return default;

            var (x, y, w, h) = (slot, face) switch
            {
                ("torso", "front") => (231, 74, 128, 128),
                ("torso", "back") => (427, 74, 128, 128),
                ("torso", "left") => (361, 74, 64, 128),
                ("torso", "right") => (165, 74, 64, 128),
                ("torso", "up") => (231, 8, 128, 64),
                ("torso", "down") => (231, 204, 128, 64),
                ("right", "front") => (217, 355, 64, 128),
                ("right", "back") => (85, 355, 64, 128),
                ("right", "left") => (19, 355, 64, 128),
                ("right", "right") => (151, 355, 64, 128),
                ("right", "up") => (217, 289, 64, 64),
                ("right", "down") => (217, 485, 64, 64),
                ("left", "front") => (308, 355, 64, 128),
                ("left", "left") => (374, 355, 64, 128),
                ("left", "back") => (440, 355, 64, 128),
                ("left", "right") => (506, 355, 64, 128),
                ("left", "up") => (308, 289, 64, 64),
                ("left", "down") => (308, 485, 64, 64),
                _ => (0, 0, 0, 0)
            };
            if (w == 0 || face is "up" or "down")
                return new Rect(x, y, w, h);

            var top = y + v0 * h;
            return new Rect(x, (int)top, w, Math.Max(1, (int)((v1 - v0) * h)));
        }

        private static int Bone(string anchor) => anchor switch
        {
            "head" or "hat" or "neck" => 3,
            "waist" => 1,
            "left-foot" => 12,
            "right-foot" => 15,
            "left-arm" => 4,
            "right-arm" => 7,
            _ => 2
        };
    }

    private readonly record struct Rect(int X, int Y, int W, int H);

    private sealed class Mesh
    {
        public Mesh(List<Vector3> positions, List<Vector2> uvs, List<int> indices)
        {
            Positions = positions;
            Uvs = uvs;
            Indices = indices;
        }

        public List<Vector3> Positions { get; }
        public List<Vector2> Uvs { get; }
        public List<int> Indices { get; }
    }

    private sealed record Piece(Mesh Geometry, Image<Rgba32>? Texture, string Anchor);
}
