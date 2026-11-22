using System.Numerics;
using SixLabors.ImageSharp.PixelFormats;

namespace Froststrap.Integrations;

/// <summary>Poses a block body with a Roblox animation clip and the account's body colors.</summary>
public static class AccountRig
{
    public static Matrix4x4 FromRoblox(float r00, float r01, float r02, float r10, float r11, float r12, float r20, float r21, float r22, float x, float y, float z)
        => new(r00, r10, r20, 0, r01, r11, r21, 0, r02, r12, r22, 0, x, y, z, 1);

    public static AccountMesh Build(AccountClip clip, float time, BodyPaint paint)
    {
        var posed = clip.Sample(time);
        var classic = posed.ContainsKey("Torso") || posed.ContainsKey("Left Arm");
        var height = Math.Clamp(paint.Height <= 0 ? 1f : paint.Height, 0.7f, 1.25f);
        var width = Math.Clamp(paint.Width <= 0 ? 1f : paint.Width, 0.7f, 1.25f);
        var head = Math.Clamp(paint.HeadScale <= 0 ? 1f : paint.HeadScale, 0.75f, 1.5f);
        var legs = 1f + Math.Clamp(paint.Proportion, 0f, 1f) * 0.4f;
        var torso = 1f + Math.Clamp(paint.BodyType, 0f, 1f) * 0.35f;
        var limbs = classic ? Classic(height, width, head) : Modern(height, width, head, legs, torso);
        var colors = new Dictionary<string, Rgba32>(StringComparer.Ordinal)
        {
            ["head"] = Rgb(paint.Head),
            ["torso"] = Rgb(paint.Torso),
            ["left"] = Rgb(paint.LeftArm),
            ["right"] = Rgb(paint.RightArm),
            ["legs"] = Rgb(paint.LeftLeg)
        };
        var world = new Dictionary<string, Matrix4x4>(StringComparer.Ordinal) { ["HumanoidRootPart"] = Bone(posed, "HumanoidRootPart") };
        var positions = new List<Vector3>();
        var faces = new List<int[]>();
        var faceColors = new List<Rgba32>();
        foreach (var limb in limbs)
        {
            var parent = world.TryGetValue(limb.Parent, out var attached) ? attached : Matrix4x4.Identity;
            var joint = Matrix4x4.Multiply(Bone(posed, limb.Name), Matrix4x4.Multiply(Matrix4x4.CreateTranslation(limb.Rest), parent));
            world[limb.Name] = joint;
            AddBox(positions, faces, faceColors, joint, limb.Center, limb.Size, colors[limb.Paint]);
        }

        return new AccountMesh
        {
            Positions = positions,
            Uvs = [],
            Faces = faces,
            FaceColors = faceColors.ToArray()
        };
    }

    private static Matrix4x4 Bone(Dictionary<string, Matrix4x4> posed, string name)
        => posed.TryGetValue(name, out var transform) ? transform : Matrix4x4.Identity;

    private static IEnumerable<Limb> Modern(float height, float width, float head, float legs, float torso)
    {
        var y = height;
        var x = width;
        yield return new Limb("LowerTorso", "HumanoidRootPart", new Vector3(0, 0, 0), new Vector3(0, 0.32f * y, 0), new Vector3(1.55f * x * torso, 0.7f * y, 0.9f), "torso");
        yield return new Limb("UpperTorso", "LowerTorso", new Vector3(0, 0.72f * y, 0), new Vector3(0, 0.52f * y, 0), new Vector3(2f * x * torso, 1.05f * y, 1f), "torso");
        yield return new Limb("Head", "UpperTorso", new Vector3(0, 1.12f * y, 0), new Vector3(0, 0.58f * head, 0), new Vector3(1.2f * head, 1.15f * head, 1.2f * head), "head");
        yield return new Limb("LeftUpperArm", "UpperTorso", new Vector3(-1.2f * x * torso, 0.85f * y, 0), new Vector3(0, -0.5f * y, 0), new Vector3(0.8f * x, 1f * y, 0.8f), "left");
        yield return new Limb("LeftLowerArm", "LeftUpperArm", new Vector3(0, -1f * y, 0), new Vector3(0, -0.42f * y * legs, 0), new Vector3(0.7f * x, 0.9f * y * legs, 0.7f), "left");
        yield return new Limb("LeftHand", "LeftLowerArm", new Vector3(0, -0.9f * y * legs, 0), new Vector3(0, -0.18f * y, 0), new Vector3(0.55f * x, 0.38f * y, 0.7f), "left");
        yield return new Limb("RightUpperArm", "UpperTorso", new Vector3(1.2f * x * torso, 0.85f * y, 0), new Vector3(0, -0.5f * y, 0), new Vector3(0.8f * x, 1f * y, 0.8f), "right");
        yield return new Limb("RightLowerArm", "RightUpperArm", new Vector3(0, -1f * y, 0), new Vector3(0, -0.42f * y * legs, 0), new Vector3(0.7f * x, 0.9f * y * legs, 0.7f), "right");
        yield return new Limb("RightHand", "RightLowerArm", new Vector3(0, -0.9f * y * legs, 0), new Vector3(0, -0.18f * y, 0), new Vector3(0.55f * x, 0.38f * y, 0.7f), "right");
        yield return new Limb("LeftUpperLeg", "LowerTorso", new Vector3(-0.42f * x, -0.08f * y, 0), new Vector3(0, -0.55f * y * legs, 0), new Vector3(0.82f * x, 1.1f * y * legs, 0.82f), "legs");
        yield return new Limb("LeftLowerLeg", "LeftUpperLeg", new Vector3(0, -1.1f * y * legs, 0), new Vector3(0, -0.48f * y * legs, 0), new Vector3(0.7f * x, 1f * y * legs, 0.7f), "legs");
        yield return new Limb("LeftFoot", "LeftLowerLeg", new Vector3(0, -1f * y * legs, 0), new Vector3(0, -0.12f * y, 0.12f), new Vector3(0.75f * x, 0.32f * y, 1.15f), "legs");
        yield return new Limb("RightUpperLeg", "LowerTorso", new Vector3(0.42f * x, -0.08f * y, 0), new Vector3(0, -0.55f * y * legs, 0), new Vector3(0.82f * x, 1.1f * y * legs, 0.82f), "legs");
        yield return new Limb("RightLowerLeg", "RightUpperLeg", new Vector3(0, -1.1f * y * legs, 0), new Vector3(0, -0.48f * y * legs, 0), new Vector3(0.7f * x, 1f * y * legs, 0.7f), "legs");
        yield return new Limb("RightFoot", "RightLowerLeg", new Vector3(0, -1f * y * legs, 0), new Vector3(0, -0.12f * y, 0.12f), new Vector3(0.75f * x, 0.32f * y, 1.15f), "legs");
    }

    private static IEnumerable<Limb> Classic(float height, float width, float head)
    {
        var y = height;
        var x = width;
        yield return new Limb("Torso", "HumanoidRootPart", Vector3.Zero, new Vector3(0, 0.5f * y, 0), new Vector3(2f * x, 2f * y, 1f), "torso");
        yield return new Limb("Head", "Torso", new Vector3(0, 1.5f * y, 0), new Vector3(0, 0.6f * head, 0), new Vector3(1.25f * head, 1.2f * head, 1.25f * head), "head");
        yield return new Limb("Left Arm", "Torso", new Vector3(-1.5f * x, 1f * y, 0), new Vector3(0, -1f * y, 0), new Vector3(1f * x, 2f * y, 1f), "left");
        yield return new Limb("Right Arm", "Torso", new Vector3(1.5f * x, 1f * y, 0), new Vector3(0, -1f * y, 0), new Vector3(1f * x, 2f * y, 1f), "right");
        yield return new Limb("Left Leg", "Torso", new Vector3(-0.5f * x, -1f * y, 0), new Vector3(0, -1f * y, 0), new Vector3(1f * x, 2f * y, 1f), "legs");
        yield return new Limb("Right Leg", "Torso", new Vector3(0.5f * x, -1f * y, 0), new Vector3(0, -1f * y, 0), new Vector3(1f * x, 2f * y, 1f), "legs");
    }

    private static void AddBox(List<Vector3> positions, List<int[]> faces, List<Rgba32> colors, Matrix4x4 world, Vector3 center, Vector3 size, Rgba32 color)
    {
        var half = size * 0.5f;
        var start = positions.Count;
        for (var corner = 0; corner < 8; corner++)
        {
            var local = center + new Vector3((corner & 1) == 0 ? -half.X : half.X, (corner & 2) == 0 ? -half.Y : half.Y, (corner & 4) == 0 ? -half.Z : half.Z);
            positions.Add(Vector3.Transform(local, world));
        }

        int[] quads = [0, 1, 3, 2, 4, 6, 7, 5, 0, 4, 5, 1, 2, 3, 7, 6, 0, 2, 6, 4, 1, 5, 7, 3];
        for (var quad = 0; quad < quads.Length; quad += 4)
        {
            var a = start + quads[quad];
            var b = start + quads[quad + 1];
            var c = start + quads[quad + 2];
            var d = start + quads[quad + 3];
            faces.Add([a, -1, b, -1, c, -1]);
            faces.Add([a, -1, c, -1, d, -1]);
            colors.Add(color);
            colors.Add(color);
        }
    }

    private static Rgba32 Rgb(string hex)
    {
        var text = (hex ?? "").Trim().TrimStart('#');
        if (text.Length >= 6 && uint.TryParse(text[..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
            return new Rgba32((byte)(value >> 16), (byte)(value >> 8), (byte)value);

        return new Rgba32(196, 196, 196);
    }

    private readonly record struct Limb(string Name, string Parent, Vector3 Rest, Vector3 Center, Vector3 Size, string Paint);
}
