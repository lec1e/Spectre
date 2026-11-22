using System.Numerics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Froststrap.Integrations;

public readonly record struct Surface(Rgba32[] Pixels, int Width, int Height);

public sealed class AccountMesh
{
    private static readonly string[] BoneNames =
    [
        "HumanoidRootPart", "LowerTorso", "UpperTorso", "Head",
        "LeftUpperArm", "LeftLowerArm", "LeftHand",
        "RightUpperArm", "RightLowerArm", "RightHand",
        "LeftUpperLeg", "LeftLowerLeg", "LeftFoot",
        "RightUpperLeg", "RightLowerLeg", "RightFoot"
    ];

    private static readonly int[] BoneParents = [-1, 0, 1, 2, 2, 4, 5, 2, 7, 8, 1, 10, 11, 1, 13, 14];

    public required List<Vector3> Positions { get; init; }
    public required List<Vector2> Uvs { get; init; }
    public required List<int[]> Faces { get; init; }
    public Rgba32[]? Texture { get; init; }
    public Rgba32[]? FaceColors { get; init; }
    public int TextureWidth { get; init; }
    public int TextureHeight { get; init; }
    public Surface[]? Surfaces { get; init; }
    public int[]? FaceSurface { get; init; }
    public int[]? VertexBone { get; init; }
    public int[]? VertexBoneB { get; init; }
    public float[]? VertexWeight { get; init; }
    public Vector3[]? Bind { get; init; }
    public Vector3[]? Pivots { get; init; }
    public Vector3 Center { get; init; }
    public float Fit { get; init; }
    public float PoseScale { get; init; } = 1f;
    public bool CanPose { get; init; }
    public bool RigidParts { get; init; }

    public static AccountMesh Parse(string obj, byte[]? texturePng)
        => Parse(obj, null, texturePng is { Length: > 0 } png ? [("texture", png)] : []);

    public static AccountMesh Parse(string obj, string? material, IReadOnlyList<(string Name, byte[] Png)> textures)
    {
        var positions = new List<Vector3>();
        var uvs = new List<Vector2>();
        var faces = new List<int[]>();
        var faceSurface = new List<int>();
        var faceLabel = new List<string>();
        var surfaces = new List<Surface>();
        var lookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, png) in textures)
        {
            if (!TryLoad(png, out var surface))
                continue;

            var index = surfaces.Count;
            surfaces.Add(surface);
            Remember(lookup, name, index);
        }

        var materials = ReadMaterials(material, lookup, surfaces.Count);
        var currentSurface = surfaces.Count > 0 ? 0 : -1;
        var currentLabel = "";

        foreach (var raw in obj.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length < 2)
                continue;

            if (line.StartsWith("usemtl ", StringComparison.Ordinal))
            {
                var materialName = line[7..].Trim();
                if (materials.TryGetValue(materialName, out var mapped) || lookup.TryGetValue(materialName, out mapped) || lookup.TryGetValue(Path.GetFileName(materialName), out mapped))
                    currentSurface = mapped;
                currentLabel = materialName;
            }
            else if (line.StartsWith("o ", StringComparison.Ordinal) || line.StartsWith("g ", StringComparison.Ordinal))
            {
                currentLabel = line[2..].Trim();
            }
            else if (line.StartsWith("v ", StringComparison.Ordinal))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 4 &&
                    float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
                    float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) &&
                    float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
                    positions.Add(new Vector3(x, y, z));
            }
            else if (line.StartsWith("vt ", StringComparison.Ordinal))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3 &&
                    float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var u) &&
                    float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                    uvs.Add(new Vector2(u, v));
            }
            else if (line.StartsWith("f ", StringComparison.Ordinal))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var corners = new List<int>();
                for (var i = 1; i < parts.Length; i++)
                {
                    var bits = parts[i].Split('/');
                    if (!int.TryParse(bits[0], out var position) || position == 0)
                        continue;

                    if (position < 0)
                        position = positions.Count + position + 1;
                    var uv = -1;
                    if (bits.Length > 1 && int.TryParse(bits[1], out var parsedUv) && parsedUv != 0)
                    {
                        if (parsedUv < 0)
                            parsedUv = uvs.Count + parsedUv + 1;
                        uv = parsedUv - 1;
                    }

                    corners.Add(position - 1);
                    corners.Add(uv);
                }

                for (var i = 2; i < corners.Count / 2; i++)
                {
                    faces.Add([corners[0], corners[1], corners[(i - 1) * 2], corners[(i - 1) * 2 + 1], corners[i * 2], corners[i * 2 + 1]]);
                    faceSurface.Add(currentSurface);
                    faceLabel.Add(currentLabel);
                }
            }
        }

        var first = surfaces.Count > 0 ? surfaces[0] : default;
        var (center, fit) = Frame(positions);
        var skin = Skin(positions, faces, faceLabel, center, fit);
        return new AccountMesh
        {
            Positions = positions,
            Uvs = uvs,
            Faces = faces,
            Texture = first.Pixels,
            TextureWidth = first.Width,
            TextureHeight = first.Height,
            Surfaces = surfaces.Count > 0 ? surfaces.ToArray() : null,
            FaceSurface = faceSurface.ToArray(),
            VertexBone = skin.Bones,
            VertexBoneB = skin.BonesB,
            VertexWeight = skin.Weights,
            Bind = skin.Bind,
            Pivots = skin.Pivots,
            Center = center,
            Fit = fit,
            PoseScale = skin.Scale,
            CanPose = skin.CanPose
        };
    }

    public Vector3[]? Pose(IReadOnlyDictionary<string, Matrix4x4> bones)
    {
        if (!CanPose || VertexBone is null || Bind is null || Pivots is null)
            return null;

        var classic = bones.ContainsKey("Torso") || bones.ContainsKey("Left Arm");
        var rotation = new Quaternion[BoneNames.Length];
        var posedPivot = new Vector3[BoneNames.Length];
        var ready = new bool[BoneNames.Length];
        Walk(0, Quaternion.Identity, Pivots[0], Pivots[0]);

        var shift = Vector3.Zero;
        if (TryShift(bones, "HumanoidRootPart", out var rootShift))
            shift += rootShift;
        if (TryShift(bones, classic ? "Torso" : "LowerTorso", out var bodyShift))
            shift += bodyShift;
        shift *= PoseScale;
        var cap = RigidParts ? 2.5f : 0.55f;
        if (shift.Length() > cap)
            shift *= cap / shift.Length();

        var posed = new Vector3[Bind.Length];
        for (var i = 0; i < Bind.Length; i++)
        {
            var placed = Placed(VertexBone[i], i);
            var weight = RigidParts || VertexWeight is null ? 1f : VertexWeight[i];
            if (weight < 0.999f && VertexBoneB is not null)
                placed = Vector3.Lerp(Placed(VertexBoneB[i], i), placed, weight);
            posed[i] = placed + shift;
        }

        return posed;

        Vector3 Placed(int bone, int index)
        {
            if ((uint)bone >= (uint)BoneNames.Length || !ready[bone])
                return Bind[index];

            return posedPivot[bone] + Vector3.Transform(Bind[index] - Pivots[bone], rotation[bone]);
        }

        void Walk(int bone, Quaternion parentRotation, Vector3 parentPosed, Vector3 parentBind)
        {
            var local = RigidParts
                ? Rotation(bones, classic ? ClassicName(BoneNames[bone]) : BoneNames[bone])
                : LocalRotation(BoneNames[bone], bones, classic);
            var world = Quaternion.Concatenate(local, parentRotation);
            if (!RigidParts && bone != 0 && ready[0])
                world = ClampTowards(world, rotation[0], 0.45f);
            var bindPivot = Pivots[bone];
            var pivot = parentPosed + Vector3.Transform(bindPivot - parentBind, parentRotation);
            rotation[bone] = world;
            posedPivot[bone] = pivot;
            ready[bone] = true;
            for (var child = 0; child < BoneParents.Length; child++)
            {
                if (BoneParents[child] == bone)
                    Walk(child, world, pivot, bindPivot);
            }
        }
    }

    private static Quaternion LocalRotation(string bone, IReadOnlyDictionary<string, Matrix4x4> bones, bool classic)
    {
        var rotation = Rotation(bones, classic ? ClassicName(bone) : bone);
        var limit = bone is "HumanoidRootPart" or "LowerTorso" or "UpperTorso" ? 0.42f : bone is "Head" ? 0.65f : 0.9f;
        return Limit(rotation, limit);
    }

    private static string ClassicName(string bone) => bone switch
    {
        "LeftLowerArm" or "LeftHand" or "RightLowerArm" or "RightHand" or "LeftLowerLeg" or "LeftFoot" or "RightLowerLeg" or "RightFoot" or "UpperTorso" => "",
        "LowerTorso" => "Torso",
        "LeftUpperArm" => "Left Arm",
        "RightUpperArm" => "Right Arm",
        "LeftUpperLeg" => "Left Leg",
        "RightUpperLeg" => "Right Leg",
        _ => bone
    };

    private static Quaternion Limit(Quaternion rotation, float maxRadians)
    {
        rotation = Quaternion.Normalize(rotation);
        var cosine = Math.Clamp(Math.Abs(rotation.W), 0f, 1f);
        var angle = 2f * MathF.Acos(cosine);
        if (angle <= maxRadians || angle < 0.0001f)
            return rotation;

        var sine = MathF.Sqrt(Math.Max(0.000001f, 1f - rotation.W * rotation.W));
        var axis = MathF.Sin(maxRadians * 0.5f) / sine;
        var sign = rotation.W < 0f ? -1f : 1f;
        return Quaternion.Normalize(new Quaternion(rotation.X * axis, rotation.Y * axis, rotation.Z * axis, sign * MathF.Cos(maxRadians * 0.5f)));
    }

    private static Quaternion ClampTowards(Quaternion world, Quaternion root, float maxRadians)
        => Quaternion.Concatenate(Limit(Quaternion.Concatenate(world, Quaternion.Inverse(root)), maxRadians), root);

    private static Quaternion Rotation(IReadOnlyDictionary<string, Matrix4x4> bones, string name)
        => bones.TryGetValue(name, out var matrix) && Matrix4x4.Decompose(matrix, out _, out var rotation, out _)
            ? rotation
            : Quaternion.Identity;

    private static bool TryShift(IReadOnlyDictionary<string, Matrix4x4> bones, string name, out Vector3 shift)
    {
        shift = Vector3.Zero;
        if (!bones.TryGetValue(name, out var matrix) || !Matrix4x4.Decompose(matrix, out _, out _, out var point))
            return false;

        shift = point;
        return true;
    }

    private static (int[] Bones, int[] BonesB, float[] Weights, Vector3[] Bind, Vector3[] Pivots, float Scale, bool CanPose) Skin(List<Vector3> positions, List<int[]> faces, List<string> labels, Vector3 center, float fit)
    {
        var bones = new int[positions.Count];
        var bonesB = new int[positions.Count];
        var weights = new float[positions.Count];
        Array.Fill(bones, 1);
        Array.Fill(bonesB, 1);
        Array.Fill(weights, 1f);
        var pivots = new Vector3[BoneNames.Length];
        for (var i = 0; i < pivots.Length; i++)
            pivots[i] = center;
        if (positions.Count == 0 || faces.Count == 0)
            return (bones, bonesB, weights, positions.ToArray(), pivots, 1f, false);

        var ys = positions.Select(point => point.Y).OrderBy(value => value).ToArray();
        var y0 = ys[Math.Clamp(ys.Length / 25, 0, ys.Length - 1)];
        var y1 = ys[Math.Clamp(ys.Length - 1 - ys.Length / 25, 0, ys.Length - 1)];
        var height = Math.Max(0.001f, y1 - y0);
        var chestX = new List<float>();
        var spineZ = new List<float>();
        var headZ = new List<float>();
        var leftArm = new List<float>();
        var rightArm = new List<float>();
        var leftLeg = new List<Vector3>();
        var rightLeg = new List<Vector3>();
        foreach (var point in positions)
        {
            var y = (point.Y - y0) / height;
            if (y is > 0.50f and < 0.72f)
                chestX.Add(MathF.Abs(point.X - center.X));
            if (y is > 0.48f and < 0.74f)
                spineZ.Add(point.Z);
            if (y > 0.76f)
                headZ.Add(point.Z);
            if (y < 0.46f)
            {
                if (point.X < center.X)
                    leftLeg.Add(point);
                else
                    rightLeg.Add(point);
            }
        }

        var torsoHalf = Math.Max(height * 0.08f, chestX.Count > 0 ? Percentile(chestX, 0.62f) : fit * 0.12f);
        foreach (var point in positions)
        {
            var y = (point.Y - y0) / height;
            if (y is > 0.36f and < 0.74f && MathF.Abs(point.X - center.X) > torsoHalf * 0.78f)
            {
                if (point.X < center.X)
                    leftArm.Add(point.X);
                else
                    rightArm.Add(point.X);
            }
        }

        var zSpine = spineZ.Count > 0 ? Percentile(spineZ, 0.5f) : center.Z;
        var zHead = headZ.Count > 0 ? Percentile(headZ, 0.5f) : zSpine;
        float Y(float t) => y0 + height * t;
        var hip = new Vector3(center.X, Y(0.47f), zSpine);
        var waist = new Vector3(center.X, Y(0.58f), zSpine);
        var neck = new Vector3(center.X, Y(0.73f), zSpine);
        var crown = new Vector3(center.X, Y(0.96f), zHead);
        var leftArmX = leftArm.Count > 8 ? Percentile(leftArm, 0.5f) : center.X - torsoHalf * 0.92f;
        var rightArmX = rightArm.Count > 8 ? Percentile(rightArm, 0.5f) : center.X + torsoHalf * 0.92f;
        var leftHip = LimbPoint(leftLeg, new Vector3(center.X - torsoHalf * 0.45f, Y(0.47f), zSpine));
        var rightHip = LimbPoint(rightLeg, new Vector3(center.X + torsoHalf * 0.45f, Y(0.47f), zSpine));

        var tips = new Vector3[BoneNames.Length];
        pivots[0] = hip;
        tips[0] = hip;
        pivots[1] = hip;
        tips[1] = waist;
        pivots[2] = waist;
        tips[2] = neck;
        pivots[3] = neck;
        tips[3] = crown;
        Arm(4, leftArmX);
        Arm(7, rightArmX);
        Leg(10, leftHip);
        Leg(13, rightHip);

        var named = new int[positions.Count];
        Array.Fill(named, -1);
        for (var face = 0; face < faces.Count && face < labels.Count; face++)
        {
            var bone = ExactBone(labels[face]);
            if (bone is not int chosen)
                continue;
            var corners = faces[face];
            for (var corner = 0; corner < corners.Length; corner += 2)
            {
                var index = corners[corner];
                if ((uint)index < (uint)named.Length)
                    named[index] = chosen;
            }
        }

        var armLimit = torsoHalf * 1.75f;
        const float band = 0.055f;
        for (var i = 0; i < positions.Count; i++)
        {
            if (named[i] >= 0)
            {
                bones[i] = named[i];
                bonesB[i] = named[i];
                weights[i] = 1f;
                continue;
            }

            var point = positions[i];
            var y = (point.Y - y0) / height;
            var reach = MathF.Abs(point.X - center.X);
            var left = point.X < center.X;
            var region = 2;
            var neighbor = 2;
            var weight = 1f;
            if (y >= 0.76f)
            {
                region = 3;
                if (y < 0.76f + band)
                {
                    neighbor = 2;
                    weight = Math.Clamp((y - 0.76f) / band, 0.4f, 1f);
                }
            }
            else if (y <= 0.40f)
            {
                region = left ? 10 : 13;
                if (y > 0.40f - band)
                {
                    neighbor = 2;
                    weight = Math.Clamp((0.40f - y) / band, 0.4f, 1f);
                }
            }
            else if (reach > armLimit && y < 0.72f)
            {
                region = left ? 4 : 7;
                var span = Math.Max(height * 0.08f, torsoHalf * 0.4f);
                if (reach < armLimit + span)
                {
                    neighbor = 2;
                    weight = Math.Clamp((reach - armLimit) / span, 0.4f, 1f);
                }
            }

            bones[i] = region;
            bonesB[i] = neighbor;
            weights[i] = weight;
        }

        foreach (var face in faces)
        {
            if (face.Length < 6)
                continue;
            var corners = new[] { face[0], face[2], face[4] };
            if ((uint)corners[0] >= (uint)bones.Length || (uint)corners[1] >= (uint)bones.Length || (uint)corners[2] >= (uint)bones.Length)
                continue;
            if (weights[corners[0]] < 0.999f || weights[corners[1]] < 0.999f || weights[corners[2]] < 0.999f)
                continue;

            var chosen = corners
                .GroupBy(index => bones[index])
                .OrderByDescending(group => group.Count())
                .First().Key;
            foreach (var index in corners)
            {
                bones[index] = chosen;
                bonesB[index] = chosen;
                weights[index] = 1f;
            }
        }

        return (bones, bonesB, weights, positions.ToArray(), pivots, height / 5f, true);

        void Arm(int bone, float x)
        {
            var shoulder = new Vector3(x, Y(0.68f), zSpine);
            var elbow = new Vector3(x, Y(0.52f), zSpine);
            var wrist = new Vector3(x, Y(0.40f), zSpine);
            var hand = new Vector3(x, Y(0.34f), zSpine);
            pivots[bone] = shoulder;
            tips[bone] = elbow;
            pivots[bone + 1] = elbow;
            tips[bone + 1] = wrist;
            pivots[bone + 2] = wrist;
            tips[bone + 2] = hand;
        }

        void Leg(int bone, Vector3 hipPoint)
        {
            var x = hipPoint.X;
            var z = hipPoint.Z;
            var hipJoint = new Vector3(x, Y(0.47f), z);
            var knee = new Vector3(x, Y(0.27f), z);
            var ankle = new Vector3(x, Y(0.09f), z);
            var foot = new Vector3(x, Y(0.03f), z);
            pivots[bone] = hipJoint;
            tips[bone] = knee;
            pivots[bone + 1] = knee;
            tips[bone + 1] = ankle;
            pivots[bone + 2] = ankle;
            tips[bone + 2] = foot;
        }
    }

    private static Vector3 LimbPoint(List<Vector3> points, Vector3 fallback)
    {
        if (points.Count < 8)
            return fallback;

        var xs = points.Select(point => point.X).ToList();
        var zs = points.Select(point => point.Z).ToList();
        return new Vector3(Percentile(xs, 0.5f), fallback.Y, Percentile(zs, 0.5f));
    }

    private static float Percentile(List<float> values, float place)
    {
        if (values.Count == 0)
            return 0f;

        values.Sort();
        var index = (int)MathF.Round((values.Count - 1) * Math.Clamp(place, 0f, 1f));
        return values[Math.Clamp(index, 0, values.Count - 1)];
    }

    private static int? ExactBone(string label)
    {
        for (var i = 0; i < BoneNames.Length; i++)
        {
            if (string.Equals(BoneNames[i], label, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return label.ToLowerInvariant() switch
        {
            "left arm" or "leftarm" => 4,
            "right arm" or "rightarm" => 7,
            "left leg" or "leftleg" => 10,
            "right leg" or "rightleg" => 13,
            "torso" => 2,
            _ => null
        };
    }

    private static (Vector3 Center, float Fit) Frame(List<Vector3> positions)
    {
        if (positions.Count == 0)
            return (Vector3.Zero, 1f);

        static float Pick(float[] values, int slot) => values[Math.Clamp(slot, 0, values.Length - 1)];
        var xs = positions.Select(point => point.X).OrderBy(value => value).ToArray();
        var ys = positions.Select(point => point.Y).OrderBy(value => value).ToArray();
        var zs = positions.Select(point => point.Z).OrderBy(value => value).ToArray();
        var low = Math.Max(0, positions.Count / 40);
        var high = positions.Count - 1 - low;
        var min = new Vector3(Pick(xs, low), Pick(ys, low), Pick(zs, low));
        var max = new Vector3(Pick(xs, high), Pick(ys, high), Pick(zs, high));
        var size = Vector3.Max(max - min, new Vector3(0.001f));
        return ((min + max) * 0.5f, MathF.Max(size.Y, MathF.Max(size.X, size.Z)));
    }

    private static Dictionary<string, int> ReadMaterials(string? material, Dictionary<string, int> textures, int count)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(material))
        {
            if (count == 1)
                map[""] = 0;
            return map;
        }

        string? current = null;
        var order = 0;
        var mapped = false;
        foreach (var raw in material.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("newmtl ", StringComparison.Ordinal))
            {
                if (current is not null && !mapped && order < count)
                    map[current] = order;
                current = line[7..].Trim();
                order++;
                mapped = false;
            }
            else if (current is not null && line.StartsWith("map_Kd ", StringComparison.Ordinal))
            {
                var file = line[7..].Trim().Trim('"');
                if (textures.TryGetValue(file, out var index) || textures.TryGetValue(Path.GetFileName(file), out index))
                {
                    map[current] = index;
                    mapped = true;
                }
            }
        }

        if (current is not null && !mapped && order - 1 < count)
            map[current] = Math.Max(0, order - 1);

        return map;
    }

    private static void Remember(Dictionary<string, int> lookup, string name, int index)
    {
        lookup[name] = index;
        lookup[Path.GetFileName(name)] = index;
    }

    private static bool TryLoad(byte[] png, out Surface surface)
    {
        try
        {
            using var image = Image.Load<Rgba32>(png);
            var pixels = new Rgba32[image.Width * image.Height];
            image.CopyPixelDataTo(pixels);
            surface = new Surface(pixels, image.Width, image.Height);
            return image.Width > 0 && image.Height > 0;
        }
        catch (UnknownImageFormatException)
        {
            surface = default;
            return false;
        }
    }
}

public static class AccountMeshPreview
{
    private static readonly object Raster = new();
    private static float[] _depth = [];
    public static byte[] RenderPng(AccountMesh mesh, float yaw, float pitch, int width = 420, int height = 520)
    {
        var color = RenderBgra(mesh, null, yaw, pitch, width, height);
        using var image = Image.LoadPixelData<Bgra32>(color, width, height);
        var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    public static byte[] RenderBgra(AccountMesh mesh, Vector3[]? posed, float yaw, float pitch, int width, int height, byte[]? reuse = null, bool coarse = false, float zoom = 1f)
    {
        if (mesh.Positions.Count == 0 || mesh.Faces.Count == 0)
            throw new RobloxApiException("The 3D file has no triangles.");

        pitch = Math.Clamp(pitch, -1.2f, 1.2f);
        var source = posed is { Length: > 0 } && posed.Length == mesh.Positions.Count ? posed : null;
        var positions = source ?? (IReadOnlyList<Vector3>)mesh.Positions;
        var center = mesh.Fit > 0.001f ? mesh.Center : CenterOf(positions);
        var fit = mesh.Fit > 0.001f ? mesh.Fit : 1f;
        var scale = 0.82f * MathF.Min(width, height) / fit * Math.Clamp(zoom, 0.4f, 3.6f);
        var cy = MathF.Cos(yaw);
        var sy = MathF.Sin(yaw);
        var cp = MathF.Cos(pitch);
        var sp = MathF.Sin(pitch);
        var light = Vector3.Normalize(new Vector3(-0.2f, 0.72f, -0.66f));
        var pixels = width * height;
        var color = reuse is { Length: > 0 } && reuse.Length == pixels * 4 ? reuse : new byte[pixels * 4];
        lock (Raster)
        {
        if (_depth.Length != pixels)
            _depth = new float[pixels];
        var depth = _depth;
        Array.Fill(depth, float.PositiveInfinity);
        for (var i = 0; i < color.Length; i += 4)
        {
            color[i] = 12;
            color[i + 1] = 12;
            color[i + 2] = 14;
            color[i + 3] = 255;
        }

        for (var faceIndex = 0; faceIndex < mesh.Faces.Count; faceIndex++)
        {
            var face = mesh.Faces[faceIndex];
            if (!Project(positions, mesh, face[0], face[1], center, cy, sy, cp, sp, scale, width, height, out var a) ||
                !Project(positions, mesh, face[2], face[3], center, cy, sy, cp, sp, scale, width, height, out var b) ||
                !Project(positions, mesh, face[4], face[5], center, cy, sy, cp, sp, scale, width, height, out var c))
                continue;

            var normal = Vector3.Cross(b.View - a.View, c.View - a.View);
            var length = normal.Length();
            if (length < 0.000001f)
                continue;

            var shade = Math.Clamp(MathF.Abs(Vector3.Dot(normal / length, light)), 0.58f, 1f);
            Fill(color, depth, width, height, a, b, c, mesh, faceIndex, shade, coarse);
        }

        return color;
        }
    }

    public static WriteableBitmap CopyTo(WriteableBitmap? bitmap, byte[] pixels, int width, int height)
    {
        if (bitmap is null || bitmap.PixelSize.Width != width || bitmap.PixelSize.Height != height)
            bitmap = new WriteableBitmap(new PixelSize(width, height), new Avalonia.Vector(96, 96), PixelFormats.Bgra8888, AlphaFormat.Opaque);

        using var frame = bitmap.Lock();
        var row = width * 4;
        if (frame.RowBytes == row)
            Marshal.Copy(pixels, 0, frame.Address, Math.Min(pixels.Length, row * height));
        else
        {
            for (var y = 0; y < height; y++)
                Marshal.Copy(pixels, y * row, frame.Address + y * frame.RowBytes, row);
        }

        return bitmap;
    }

    private static Vector3 CenterOf(IReadOnlyList<Vector3> positions)
    {
        var sum = Vector3.Zero;
        foreach (var position in positions)
            sum += position;
        return sum / Math.Max(1, positions.Count);
    }

    private static bool Project(IReadOnlyList<Vector3> positions, AccountMesh mesh, int positionIndex, int uvIndex, Vector3 center, float cy, float sy, float cp, float sp, float scale, int width, int height, out Vert vert)
    {
        vert = default;
        if ((uint)positionIndex >= (uint)positions.Count)
            return false;

        var point = positions[positionIndex] - center;
        var x1 = point.X * cy + point.Z * sy;
        var z1 = -point.X * sy + point.Z * cy;
        var y2 = point.Y * cp - z1 * sp;
        var z2 = point.Y * sp + z1 * cp;
        var uv = uvIndex >= 0 && uvIndex < mesh.Uvs.Count ? mesh.Uvs[uvIndex] : Vector2.Zero;
        vert = new Vert(width * 0.5f + x1 * scale, height * 0.5f - y2 * scale, z2, uv.X, uv.Y, new Vector3(x1, y2, z2));
        return true;
    }

    private static void Fill(byte[] color, float[] depth, int width, int height, Vert a, Vert b, Vert c, AccountMesh mesh, int face, float shade, bool coarse)
    {
        if (a.Y > b.Y) (a, b) = (b, a);
        if (b.Y > c.Y) (b, c) = (c, b);
        if (a.Y > b.Y) (a, b) = (b, a);

        var flat = coarse ? Sample(mesh, face, (a.U + b.U + c.U) / 3f, (a.V + b.V + c.V) / 3f, shade) : default;
        Band(color, depth, width, height, a, b, a, c, mesh, face, shade, coarse, flat);
        Band(color, depth, width, height, b, c, a, c, mesh, face, shade, coarse, flat);
    }

    private static void Band(byte[] color, float[] depth, int width, int height, Vert from, Vert to, Vert farFrom, Vert farTo, AccountMesh mesh, int face, float shade, bool coarse, Rgba32 flat)
    {
        var top = Math.Clamp((int)MathF.Ceiling(MathF.Min(from.Y, to.Y) - 0.5f), 0, height - 1);
        var bottom = Math.Clamp((int)MathF.Floor(MathF.Max(from.Y, to.Y) - 0.5f), 0, height - 1);
        for (var y = top; y <= bottom; y++)
        {
            var scan = y + 0.5f;
            if (!Cross(from, to, scan, out var left) || !Cross(farFrom, farTo, scan, out var right))
                continue;
            if (left.X > right.X)
                (left, right) = (right, left);

            var start = Math.Clamp((int)MathF.Ceiling(left.X - 0.5f), 0, width - 1);
            var end = Math.Clamp((int)MathF.Floor(right.X - 0.5f), 0, width - 1);
            var span = MathF.Max(0.0001f, right.X - left.X);
            for (var x = start; x <= end; x++)
            {
                var t = Math.Clamp((x + 0.5f - left.X) / span, 0f, 1f);
                var z = left.Z + (right.Z - left.Z) * t;
                var index = y * width + x;
                if (z >= depth[index])
                    continue;

                depth[index] = z;
                var pixel = coarse ? flat : Sample(mesh, face, left.U + (right.U - left.U) * t, left.V + (right.V - left.V) * t, shade);
                var offset = index * 4;
                color[offset] = pixel.B;
                color[offset + 1] = pixel.G;
                color[offset + 2] = pixel.R;
                color[offset + 3] = 255;
            }
        }
    }

    private static bool Cross(Vert from, Vert to, float y, out SpanHit hit)
    {
        var dy = to.Y - from.Y;
        if (MathF.Abs(dy) < 0.0001f)
        {
            hit = new SpanHit(from.X, from.Z, from.U, from.V);
            return y >= MathF.Min(from.Y, to.Y) - 0.5f && y <= MathF.Max(from.Y, to.Y) + 0.5f;
        }

        var t = (y - from.Y) / dy;
        if (t < -0.02f || t > 1.02f)
        {
            hit = default;
            return false;
        }

        t = Math.Clamp(t, 0f, 1f);
        hit = new SpanHit(
            from.X + (to.X - from.X) * t,
            from.Z + (to.Z - from.Z) * t,
            from.U + (to.U - from.U) * t,
            from.V + (to.V - from.V) * t);
        return true;
    }

    private static bool Textured(AccountMesh mesh, int face, out Surface surface)
    {
        surface = default;
        if (mesh.FaceSurface is { } map && (uint)face < (uint)map.Length && map[face] < 0)
            return false;

        if (mesh.Surfaces is { Length: > 0 } surfaces)
        {
            var index = mesh.FaceSurface is { } mapped && (uint)face < (uint)mapped.Length ? mapped[face] : 0;
            if ((uint)index < (uint)surfaces.Length && surfaces[index].Pixels is { Length: > 0 })
            {
                surface = surfaces[index];
                return true;
            }
        }

        if (mesh.Texture is { Length: > 0 } && mesh.TextureWidth > 0)
        {
            surface = new Surface(mesh.Texture, mesh.TextureWidth, mesh.TextureHeight);
            return true;
        }

        return false;
    }

    private static Rgba32 Sample(AccountMesh mesh, int face, float u, float v, float shade)
    {
        if (Textured(mesh, face, out var surface))
        {
            var uu = Math.Clamp(u, 0f, 1f);
            var vv = Math.Clamp(1f - v, 0f, 1f);
            var x = Math.Clamp((int)(uu * (surface.Width - 1)), 0, surface.Width - 1);
            var y = Math.Clamp((int)(vv * (surface.Height - 1)), 0, surface.Height - 1);
            var texel = surface.Pixels[y * surface.Width + x];
            return new Rgba32((byte)(texel.R * shade), (byte)(texel.G * shade), (byte)(texel.B * shade));
        }

        if (mesh.FaceColors is { } colors && (uint)face < (uint)colors.Length)
        {
            var tint = colors[face];
            return new Rgba32((byte)(tint.R * shade), (byte)(tint.G * shade), (byte)(tint.B * shade));
        }

        var gray = (byte)(196 * shade);
        return new Rgba32(gray, gray, gray);
    }

    private readonly record struct Vert(float X, float Y, float Z, float U, float V, Vector3 View);

    private readonly record struct SpanHit(float X, float Z, float U, float V);
}
