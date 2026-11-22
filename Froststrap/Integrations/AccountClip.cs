using System.Buffers.Binary;
using System.Numerics;
using System.Xml.Linq;

namespace Froststrap.Integrations;

public sealed class AccountFrame
{
    public required float Time { get; init; }
    public required Dictionary<string, Matrix4x4> Bones { get; init; }
}

/// <summary>A Roblox keyframe sequence. Poses are joint transforms, in seconds.</summary>
public sealed class AccountClip
{
    public required float Length { get; init; }
    public required IReadOnlyList<AccountFrame> Frames { get; init; }

    public static IReadOnlyList<long> ReferencedAssets(byte[] bytes)
    {
        try
        {
            if (bytes.Length < 8 || bytes[7] != (byte)'!')
                return TextAssets(bytes);

            if (bytes.Length < 32 || bytes[0] != (byte)'<')
                return [];

            var chunks = Chunks(bytes);
            int? animationClass = null;
            foreach (var chunk in chunks.Where(chunk => chunk.Name == "INST"))
            {
                var cursor = 0;
                var classId = BinaryPrimitives.ReadInt32LittleEndian(chunk.Data.AsSpan(cursor));
                cursor += 4;
                var name = ReadString(chunk.Data, ref cursor);
                if (name == "Animation")
                    animationClass = classId;
            }

            if (animationClass is null)
                return [];

            foreach (var chunk in chunks.Where(chunk => chunk.Name == "PROP"))
            {
                try
                {
                    var cursor = 0;
                    var classId = BinaryPrimitives.ReadInt32LittleEndian(chunk.Data.AsSpan(cursor));
                    cursor += 4;
                    var name = ReadString(chunk.Data, ref cursor);
                    cursor += 1;
                    if (classId != animationClass || name != "AnimationId")
                        continue;

                    var ids = new List<long>();
                    foreach (var text in ReadStrings(chunk.Data[cursor..]))
                    {
                        var match = Regex.Match(text, @"(\d{5,})");
                        if (match.Success && long.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
                            ids.Add(id);
                    }

                    return ids;
                }
                catch (Exception ex) when (ex is ArgumentOutOfRangeException or RobloxApiException)
                {
                }
            }
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or RobloxApiException)
        {
        }

        return [];
    }

    public static AccountClip Read(byte[] bytes)
    {
        if (bytes.Length >= 8 && bytes[0] == (byte)'<' && bytes[1] == (byte)'r' && bytes[7] == (byte)'!')
            return ReadBinary(bytes);

        var text = Encoding.UTF8.GetString(bytes);
        if (text.Contains("<roblox", StringComparison.OrdinalIgnoreCase))
            return ReadXml(text);

        throw new RobloxApiException("Roblox did not return an animation for that choice.");
    }

    public Dictionary<string, Matrix4x4> Sample(float time)
    {
        if (Frames.Count == 0)
            return [];

        if (Frames.Count == 1 || Length <= 0.001f)
            return Frames[0].Bones;

        time %= Length;
        if (time < 0)
            time += Length;

        var next = 0;
        while (next < Frames.Count && Frames[next].Time < time)
            next++;

        if (next <= 0)
            return Frames[0].Bones;

        if (next >= Frames.Count)
            return Frames[^1].Bones;

        var before = Frames[next - 1];
        var after = Frames[next];
        var span = Math.Max(0.0001f, after.Time - before.Time);
        var blend = Math.Clamp((time - before.Time) / span, 0f, 1f);
        var names = before.Bones.Keys.Union(after.Bones.Keys);
        var posed = new Dictionary<string, Matrix4x4>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            var start = before.Bones.TryGetValue(name, out var from) ? from : Matrix4x4.Identity;
            var end = after.Bones.TryGetValue(name, out var to) ? to : start;
            posed[name] = Blend(start, end, blend);
        }

        return posed;
    }

    private static Matrix4x4 Blend(Matrix4x4 start, Matrix4x4 end, float blend)
    {
        if (!Matrix4x4.Decompose(start, out _, out var startRotation, out var startPoint)
            || !Matrix4x4.Decompose(end, out _, out var endRotation, out var endPoint))
            return blend < 0.5f ? start : end;

        var rotation = Matrix4x4.CreateFromQuaternion(Quaternion.Slerp(startRotation, endRotation, blend));
        var point = Vector3.Lerp(startPoint, endPoint, blend);
        return Matrix4x4.Multiply(rotation, Matrix4x4.CreateTranslation(point));
    }

    private static AccountClip ReadXml(string text)
    {
        var document = XDocument.Parse(text);
        var frames = new List<AccountFrame>();
        foreach (var keyframe in document.Descendants().Where(element => Attribute(element, "class") == "Keyframe"))
        {
            var time = 0f;
            var timeElement = keyframe.Elements().FirstOrDefault(element => Attribute(element, "class") == null && element.Name.LocalName == "Properties")
                ?.Elements().FirstOrDefault(element => Attribute(element, "name") == "Time");
            if (timeElement is not null && float.TryParse(timeElement.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                time = parsed;

            var bones = new Dictionary<string, Matrix4x4>(StringComparer.Ordinal);
            foreach (var pose in keyframe.Descendants().Where(element => Attribute(element, "class") == "Pose"))
            {
                var properties = pose.Elements().FirstOrDefault(element => element.Name.LocalName == "Properties");
                if (properties is null)
                    continue;

                var name = properties.Elements().FirstOrDefault(element => Attribute(element, "name") == "Name")?.Value ?? "";
                var frame = properties.Elements().FirstOrDefault(element => Attribute(element, "name") == "CFrame");
                if (string.IsNullOrWhiteSpace(name) || frame is null)
                    continue;

                bones[name] = ReadXmlFrame(frame);
            }

            if (bones.Count > 0)
                frames.Add(new AccountFrame { Time = time, Bones = bones });
        }

        return Finish(frames);
    }

    private static Matrix4x4 ReadXmlFrame(XElement frame)
    {
        float Component(string name)
        {
            var child = frame.Elements().FirstOrDefault(element => element.Name.LocalName == name);
            return child is not null && float.TryParse(child.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : name is "R00" or "R11" or "R22" ? 1f : 0f;
        }

        if (frame.Elements().Any())
            return AccountRig.FromRoblox(Component("R00"), Component("R01"), Component("R02"), Component("R10"), Component("R11"), Component("R12"), Component("R20"), Component("R21"), Component("R22"), Component("X"), Component("Y"), Component("Z"));

        var numbers = frame.Value.Split((char[])null!, StringSplitOptions.RemoveEmptyEntries);
        if (numbers.Length < 12)
            return Matrix4x4.Identity;

        float At(int index) => float.TryParse(numbers[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0f;
        return AccountRig.FromRoblox(At(3), At(4), At(5), At(6), At(7), At(8), At(9), At(10), At(11), At(0), At(1), At(2));
    }

    private static AccountClip ReadBinary(byte[] bytes)
    {
        var chunks = Chunks(bytes);
        var classes = new Dictionary<int, (string Name, int Count)>();
        foreach (var chunk in chunks.Where(chunk => chunk.Name == "INST"))
        {
            var cursor = 0;
            var classId = BinaryPrimitives.ReadInt32LittleEndian(chunk.Data.AsSpan(cursor));
            cursor += 4;
            var name = ReadString(chunk.Data, ref cursor);
            cursor += 1;
            var classCount = BinaryPrimitives.ReadInt32LittleEndian(chunk.Data.AsSpan(cursor));
            classes[classId] = (name, classCount);
        }

        if (!classes.Values.Any(item => item.Name == "Keyframe"))
        {
            App.Logger.WriteLine("Avatar", "Animation classes: " + string.Join(", ", classes.Select(pair => $"{pair.Value.Name} x{pair.Value.Count}")));
            if (classes.Values.Any(item => item.Name is "CurveAnimation" or "EulerRotationCurve" or "FloatCurve"))
                return ReadCurves(chunks);

            throw new RobloxApiException("This animation has no poses.");
        }

        var props = new Dictionary<(int ClassId, string Name), byte[]>();
        foreach (var chunk in chunks.Where(chunk => chunk.Name == "PROP"))
        {
            try
            {
                var cursor = 0;
                var classId = BinaryPrimitives.ReadInt32LittleEndian(chunk.Data.AsSpan(cursor));
                cursor += 4;
                var name = ReadString(chunk.Data, ref cursor);
                cursor += 1;
                props[(classId, name)] = chunk.Data[cursor..];
            }
            catch (Exception ex) when (ex is ArgumentOutOfRangeException or RobloxApiException)
            {
                var sample = Convert.ToHexString(chunk.Data.AsSpan(0, Math.Min(8, chunk.Data.Length)));
                App.Logger.WriteLine("Avatar", $"Skipped one property in the animation ({chunk.Data.Length} bytes, {sample}).");
            }
        }

        var keyframeId = ClassId(classes, "Keyframe");
        var poseId = ClassId(classes, "Pose");
        var keyframes = classes[keyframeId].Count;
        var poses = classes[poseId].Count;
        if (keyframes <= 0 || poses <= 0
            || !props.TryGetValue((keyframeId, "Time"), out var timeBytes)
            || !props.TryGetValue((poseId, "Name"), out var nameBytes)
            || !props.TryGetValue((poseId, "CFrame"), out var frameBytes))
            throw new RobloxApiException("This animation has no poses.");

        var times = ReadFloats(timeBytes, keyframes);
        var names = ReadStrings(nameBytes);
        var matrices = ReadCFrames(frameBytes, Math.Min(poses, names.Count));
        var groups = GroupPoses(names);
        if (groups.Count == 0)
            throw new RobloxApiException("This animation has no poses.");

        var frames = new List<AccountFrame>(groups.Count);
        var count = Math.Min(groups.Count, times.Length);
        for (var frame = 0; frame < count; frame++)
        {
            var bones = new Dictionary<string, Matrix4x4>(StringComparer.Ordinal);
            foreach (var index in groups[frame])
            {
                if ((uint)index >= (uint)matrices.Length)
                    continue;
                var joint = names[index];
                if (!string.IsNullOrWhiteSpace(joint))
                    bones[joint] = matrices[index];
            }

            if (bones.Count > 0)
                frames.Add(new AccountFrame { Time = times[frame], Bones = bones });
        }

        return Finish(frames);
    }

    private static List<List<int>> GroupPoses(List<string> names)
    {
        var groups = new List<List<int>>();
        if (names.Count == 0)
            return groups;

        var root = names[0];
        var current = new List<int>();
        for (var i = 0; i < names.Count; i++)
        {
            if (i > 0 && names[i] == root && current.Count > 0)
            {
                groups.Add(current);
                current = [];
            }

            current.Add(i);
        }

        if (current.Count > 0)
            groups.Add(current);

        return groups;
    }

    private static AccountClip Finish(List<AccountFrame> frames)
    {
        frames.Sort((left, right) => left.Time.CompareTo(right.Time));
        if (frames.Count == 0)
            throw new RobloxApiException("This animation has no keyframes.");

        var length = frames[^1].Time;
        if (length <= 0.001f)
            length = Math.Max(0.05f, 0.05f * (frames.Count - 1));

        return new AccountClip { Length = length, Frames = frames };
    }

    private static int ClassId(Dictionary<int, (string Name, int Count)> classes, string name)
    {
        foreach (var pair in classes)
        {
            if (pair.Value.Name == name)
                return pair.Key;
        }

        throw new RobloxApiException("This animation file has no " + name + ".");
    }

    private static Matrix4x4[] ReadCFrames(byte[] payload, int count)
    {
        var matrices = new Matrix4x4[count];
        var cursor = 0;
        for (var i = 0; i < count; i++)
        {
            if (cursor >= payload.Length)
                throw new RobloxApiException("The animation file ended early.");

            var id = payload[cursor++];
            var r00 = 1f;
            var r01 = 0f;
            var r02 = 0f;
            var r10 = 0f;
            var r11 = 1f;
            var r12 = 0f;
            var r20 = 0f;
            var r21 = 0f;
            var r22 = 1f;
            if (id == 0)
            {
                r00 = ReadRaw(payload, ref cursor);
                r01 = ReadRaw(payload, ref cursor);
                r02 = ReadRaw(payload, ref cursor);
                r10 = ReadRaw(payload, ref cursor);
                r11 = ReadRaw(payload, ref cursor);
                r12 = ReadRaw(payload, ref cursor);
                r20 = ReadRaw(payload, ref cursor);
                r21 = ReadRaw(payload, ref cursor);
                r22 = ReadRaw(payload, ref cursor);
            }

            matrices[i] = AccountRig.FromRoblox(r00, r01, r02, r10, r11, r12, r20, r21, r22, 0, 0, 0);
        }

        var lane = count * 4;
        if (cursor + lane * 3 > payload.Length)
            throw new RobloxApiException("The animation file ended early.");

        var x = ReadFloats(payload.AsSpan(cursor, lane).ToArray(), count);
        var y = ReadFloats(payload.AsSpan(cursor + lane, lane).ToArray(), count);
        var z = ReadFloats(payload.AsSpan(cursor + lane * 2, lane).ToArray(), count);
        for (var i = 0; i < count; i++)
        {
            var matrix = matrices[i];
            matrix.M41 = x[i];
            matrix.M42 = y[i];
            matrix.M43 = z[i];
            matrices[i] = matrix;
        }

        return matrices;
    }

    private static float[] ReadFloats(byte[] payload, int count)
    {
        if (payload.Length < count * 4)
            throw new RobloxApiException("The animation file ended early.");

        var values = new float[count];
        for (var i = 0; i < count; i++)
        {
            uint packed = ((uint)payload[i] << 24) | ((uint)payload[count + i] << 16) | ((uint)payload[count * 2 + i] << 8) | payload[count * 3 + i];
            packed = (packed >> 1) | (packed << 31);
            values[i] = BitConverter.UInt32BitsToSingle(packed);
        }

        return values;
    }

    private static List<string> ReadStrings(byte[] payload)
    {
        var names = new List<string>();
        var cursor = 0;
        while (cursor + 4 <= payload.Length)
        {
            var length = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(cursor));
            cursor += 4;
            if (length < 0 || cursor + length > payload.Length)
                break;

            names.Add(Encoding.UTF8.GetString(payload, cursor, length));
            cursor += length;
        }

        return names;
    }

    private static float ReadRaw(byte[] payload, ref int cursor)
    {
        if (cursor + 4 > payload.Length)
            throw new RobloxApiException("The animation file ended early.");

        var value = BinaryPrimitives.ReadSingleLittleEndian(payload.AsSpan(cursor));
        cursor += 4;
        return value;
    }

    private static string ReadString(byte[] payload, ref int cursor)
    {
        if (cursor < 0 || cursor + 4 > payload.Length)
            throw new RobloxApiException("The animation file ended early.");

        var length = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(cursor));
        cursor += 4;
        if (length < 0 || cursor + length > payload.Length)
            throw new RobloxApiException("The animation file ended early.");

        var text = Encoding.UTF8.GetString(payload, cursor, length);
        cursor += length;
        return text;
    }

    private static List<(string Name, byte[] Data)> Chunks(byte[] bytes)
    {
        if (bytes.Length < 32 || Encoding.ASCII.GetString(bytes, 0, 8) != "<roblox!")
            throw new RobloxApiException("Roblox did not return an animation for that choice.");

        var cursor = 32;
        var chunks = new List<(string Name, byte[] Data)>();
        while (cursor + 16 <= bytes.Length)
        {
            var name = Encoding.ASCII.GetString(bytes, cursor, 4).TrimEnd('\0');
            cursor += 4;
            var compressed = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(cursor));
            var uncompressed = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(cursor + 4));
            cursor += 12;
            if (compressed < 0 || cursor + compressed > bytes.Length)
                break;

            var payload = bytes.AsSpan(cursor, compressed).ToArray();
            cursor += compressed;
            var data = compressed == uncompressed ? payload : Inflate(payload, uncompressed);
            chunks.Add((name, data));
            if (name == "END")
                break;
        }

        return chunks;
    }

    private static byte[] Inflate(byte[] source, int size)
    {
        var output = new byte[size];
        var written = 0;
        var cursor = 0;
        while (cursor < source.Length && written < size)
        {
            var token = source[cursor++];
            var literals = token >> 4;
            if (literals == 15)
            {
                byte extra;
                do
                {
                    if (cursor >= source.Length)
                        return output;
                    extra = source[cursor++];
                    literals += extra;
                }
                while (extra == 255);
            }

            var copy = Math.Min(literals, size - written);
            if (cursor + literals > source.Length)
                return output;

            Buffer.BlockCopy(source, cursor, output, written, copy);
            cursor += literals;
            written += copy;
            if (written >= size || cursor >= source.Length)
                break;

            if (cursor + 2 > source.Length)
                break;

            var offset = source[cursor] | (source[cursor + 1] << 8);
            cursor += 2;
            if (offset <= 0 || offset > written)
                break;

            var match = (token & 15) + 4;
            if ((token & 15) == 15)
            {
                byte extra;
                do
                {
                    if (cursor >= source.Length)
                        return output;
                    extra = source[cursor++];
                    match += extra;
                }
                while (extra == 255);
            }

            var from = written - offset;
            var count = Math.Min(match, size - written);
            for (var i = 0; i < count; i++)
                output[written++] = output[from + i];
        }

        return output;
    }

    private static string? Attribute(XElement element, string name) => element.Attribute(name)?.Value;

    private static IReadOnlyList<long> TextAssets(byte[] bytes)
    {
        var ids = new List<long>();
        var text = Encoding.UTF8.GetString(bytes);
        foreach (Match match in Regex.Matches(text, @"(?:rbxassetid://|\?id=)(\d{5,})"))
        {
            if (long.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
                ids.Add(id);
        }

        return ids;
    }

    private static AccountClip ReadCurves(List<(string Name, byte[] Data)> chunks)
    {
        try
        {
            return ReadCurveClip(chunks);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            App.Logger.WriteLine("Avatar", "Curve animation could not be read: " + ex.Message);
            throw new RobloxApiException("This emote uses curve data, so it stays still here. Idle, Walk, Run, Jump, Fall, Climb, and Swim play on your avatar.");
        }
    }

    private sealed class CurveNode
    {
        public int Referent;
        public int ClassId;
        public string ClassName = "";
        public string Name = "";
        public int Parent = -1;
        public int Order;
        public List<CurveKey> Keys = [];
    }

    private readonly record struct CurveKey(float Time, float Value);

    private static AccountClip ReadCurveClip(List<(string Name, byte[] Data)> chunks)
    {
        var shared = ReadShared(chunks);
        var nodes = new Dictionary<int, CurveNode>();
        var order = new List<int>();
        foreach (var chunk in chunks)
        {
            if (chunk.Name != "INST")
                continue;

            int classId;
            string className;
            try
            {
                var cursor = 0;
                classId = ReadI32(chunk.Data, ref cursor);
                className = ReadString(chunk.Data, ref cursor);
                if (cursor >= chunk.Data.Length)
                    continue;

                var hasService = chunk.Data[cursor++] != 0;
                var count = ReadI32(chunk.Data, ref cursor);
                if (count <= 0 || cursor + count * 4 > chunk.Data.Length)
                    continue;

                var referents = ReadZInts(chunk.Data.AsSpan(cursor, count * 4).ToArray(), count, true);
                for (var i = 0; i < count; i++)
                {
                    var node = new CurveNode { Referent = referents[i], ClassName = className, ClassId = classId };
                    nodes[node.Referent] = node;
                    order.Add(node.Referent);
                }

                _ = hasService;
            }
            catch (Exception ex) when (ex is RobloxApiException or ArgumentOutOfRangeException or IndexOutOfRangeException)
            {
                continue;
            }

            continue;
        }

        foreach (var chunk in chunks)
        {
            if (chunk.Name != "PROP")
                continue;

            try
            {
            var cursor = 0;
            var classId = ReadI32(chunk.Data, ref cursor);
            var prop = ReadString(chunk.Data, ref cursor);
            if (cursor >= chunk.Data.Length)
                continue;

            var type = chunk.Data[cursor++];
            var payload = chunk.Data.AsSpan(cursor).ToArray();
            var members = new List<CurveNode>();
            foreach (var referent in order)
            {
                if (nodes.TryGetValue(referent, out var node) && node.ClassId == classId)
                    members.Add(node);
            }
            if (members.Count == 0 || prop is not ("Name" or "ValuesAndTimes" or "RotationOrder" or "Length"))
                continue;

            if (prop == "Name" && type == 0x01)
            {
                var names = ReadStrings(payload);
                for (var i = 0; i < members.Count && i < names.Count; i++)
                    members[i].Name = names[i];
            }
            else if (prop == "ValuesAndTimes" && members[0].ClassName == "FloatCurve")
            {
                var blobs = type switch
                {
                    0x01 => ReadRawStrings(payload),
                    0x1C => ReadSharedValues(payload, members.Count, shared),
                    _ => []
                };
                for (var i = 0; i < members.Count && i < blobs.Count; i++)
                    members[i].Keys = ReadCurveKeys(blobs[i]);
            }
            else if (prop == "RotationOrder" && members[0].ClassName == "EulerRotationCurve" && payload.Length >= members.Count * 4)
            {
                var values = ReadZInts(payload, members.Count, true);
                for (var i = 0; i < members.Count; i++)
                    members[i].Order = values[i];
            }
            else if (prop == "Length" && members[0].ClassName == "CurveAnimation" && type == 0x04)
            {
                var length = ReadFloats(payload, 1);
                if (length.Length > 0 && payload.Length >= 4)
                    members[0].Order = (int)MathF.Round(length[0] * 1000f);
            }
            }
            catch (Exception ex) when (ex is RobloxApiException or ArgumentOutOfRangeException or IndexOutOfRangeException)
            {
                App.Logger.WriteLine("Avatar", "Skipped one curve property: " + ex.Message);
            }
        }

        foreach (var chunk in chunks)
        {
            if (chunk.Name != "PRNT" || chunk.Data.Length < 5)
                continue;

            var cursor = 1;
            var count = ReadI32(chunk.Data, ref cursor);
            if (count <= 0 || cursor + count * 8 > chunk.Data.Length)
                continue;

            var children = ReadZInts(chunk.Data.AsSpan(cursor, count * 4).ToArray(), count, true);
            var parents = ReadZInts(chunk.Data.AsSpan(cursor + count * 4, count * 4).ToArray(), count, true);
            for (var i = 0; i < count; i++)
            {
                if (nodes.TryGetValue(children[i], out var child))
                    child.Parent = parents[i];
            }
        }

        var channels = new Dictionary<string, (List<CurveKey>[] Rotation, List<CurveKey>[] Position, int Order)>(StringComparer.Ordinal);
        foreach (var node in nodes.Values.Where(item => item.ClassName == "FloatCurve" && item.Keys.Count > 0))
        {
            var axis = Axis(node.Name);
            if (axis < 0)
                continue;

            var kind = "";
            var bone = "";
            var rotationOrder = 0;
            var parent = node.Parent;
            for (var depth = 0; depth < 6 && nodes.TryGetValue(parent, out var owner); depth++)
            {
                if (owner.ClassName == "EulerRotationCurve")
                {
                    kind = "rotation";
                    rotationOrder = owner.Order;
                }
                else if (owner.ClassName == "Vector3Curve")
                    kind = "position";
                else if (owner.ClassName == "Folder" && bone.Length == 0 && KeepBone(owner.Name))
                    bone = owner.Name;

                parent = owner.Parent;
            }

            if (bone.Length == 0 || kind.Length == 0)
                continue;

            if (!channels.TryGetValue(bone, out var channel))
            {
                channel = (new List<CurveKey>[3], new List<CurveKey>[3], rotationOrder);
                channels[bone] = channel;
            }

            var target = kind == "rotation" ? channel.Rotation : channel.Position;
            target[axis] = node.Keys;
            if (kind == "rotation")
                channels[bone] = (channel.Rotation, channel.Position, rotationOrder);
        }

        if (channels.Count == 0)
        {
            var named = nodes.Values.Count(item => item.ClassName == "FloatCurve" && item.Name.Length > 0);
            var keyed = nodes.Values.Count(item => item.ClassName == "FloatCurve" && item.Keys.Count > 0);
            var sample = string.Join(", ", nodes.Values.Where(item => item.ClassName is "FloatCurve" or "Folder").Take(12).Select(item => item.ClassName + ":" + item.Name));
            throw new InvalidOperationException($"no curve joints, named {named}, keyed {keyed}, {sample}");
        }

        var duration = 0.05f;
        foreach (var node in nodes.Values.Where(item => item.ClassName == "CurveAnimation" && item.Order > 0))
            duration = Math.Max(duration, node.Order / 1000f);
        foreach (var channel in channels.Values)
        {
            foreach (var keys in channel.Rotation.Concat(channel.Position))
            {
                if (keys is { Count: > 0 })
                    duration = Math.Max(duration, keys[^1].Time);
            }
        }

        var frames = new List<AccountFrame>();
        var step = 1f / 30f;
        for (var time = 0f; time <= duration + 0.0001f; time += step)
        {
            var bones = new Dictionary<string, Matrix4x4>(StringComparer.Ordinal);
            foreach (var (bone, channel) in channels)
            {
                float At(List<CurveKey>? keys) => keys is { Count: > 0 } ? SampleCurve(keys, time) : 0f;
                var rotation = Euler(At(channel.Rotation[0]), At(channel.Rotation[1]), At(channel.Rotation[2]), channel.Order);
                var matrix = Matrix4x4.CreateFromQuaternion(rotation);
                matrix.M41 = At(channel.Position[0]);
                matrix.M42 = At(channel.Position[1]);
                matrix.M43 = At(channel.Position[2]);
                bones[bone] = matrix;
            }

            frames.Add(new AccountFrame { Time = MathF.Min(time, duration), Bones = bones });
            if (frames.Count >= 360)
                break;
        }

        App.Logger.WriteLine("Avatar", $"Curve animation: {channels.Count} joints, {frames.Count} frames, {duration:0.00}s.");
        return Finish(frames);
    }

    private static string ClassIndex(List<(string Name, byte[] Data)> chunks, int classId)
    {
        foreach (var chunk in chunks)
        {
            if (chunk.Name != "INST" || chunk.Data.Length < 8)
                continue;

            var cursor = 0;
            var id = ReadI32(chunk.Data, ref cursor);
            if (id == classId)
                return ReadString(chunk.Data, ref cursor);
        }

        return "";
    }

    public static IReadOnlyList<byte[]> ModelBlobs(byte[] bytes)
    {
        if (bytes.Length < 32 || Encoding.ASCII.GetString(bytes, 0, Math.Min(8, bytes.Length)) != "<roblox!")
            return [];

        try
        {
            var chunks = Chunks(bytes);
            var blobs = ReadShared(chunks);
            foreach (var chunk in chunks)
            {
                if (chunk.Name != "PROP" || chunk.Data.Length < 8)
                    continue;

                try
                {
                    var cursor = 0;
                    _ = ReadI32(chunk.Data, ref cursor);
                    _ = ReadString(chunk.Data, ref cursor);
                    if (cursor >= chunk.Data.Length)
                        continue;

                    var type = chunk.Data[cursor++];
                    if (type == 0x01)
                        blobs.AddRange(ReadRawStrings(chunk.Data[cursor..]));
                }
                catch (Exception ex) when (ex is RobloxApiException or ArgumentOutOfRangeException or IndexOutOfRangeException)
                {
                }
            }

            return blobs;
        }
        catch (Exception ex) when (ex is RobloxApiException or ArgumentOutOfRangeException or IndexOutOfRangeException)
        {
            return [];
        }
    }

    private static List<byte[]> ReadShared(List<(string Name, byte[] Data)> chunks)
    {
        var values = new List<byte[]>();
        foreach (var chunk in chunks)
        {
            if (chunk.Name != "SSTR" || chunk.Data.Length < 8)
                continue;

            var cursor = 4;
            var count = ReadI32(chunk.Data, ref cursor);
            for (var i = 0; i < count && cursor + 20 <= chunk.Data.Length; i++)
            {
                cursor += 16;
                var length = ReadI32(chunk.Data, ref cursor);
                if (length < 0 || cursor + length > chunk.Data.Length)
                    break;

                values.Add(chunk.Data.AsSpan(cursor, length).ToArray());
                cursor += length;
            }
        }

        return values;
    }

    private static List<byte[]> ReadSharedValues(byte[] payload, int count, List<byte[]> shared)
    {
        var values = new List<byte[]>();
        if (payload.Length < count * 4)
            return values;

        var indices = new int[count];
        for (var i = 0; i < count; i++)
        {
            uint packed = ((uint)payload[i] << 24) | ((uint)payload[count + i] << 16) | ((uint)payload[count * 2 + i] << 8) | payload[count * 3 + i];
            indices[i] = (int)packed;
        }

        foreach (var index in indices)
            values.Add((uint)index < (uint)shared.Count ? shared[index] : []);

        return values;
    }

    private static List<CurveKey> ReadCurveKeys(byte[] blob)
    {
        var keys = new List<CurveKey>();
        if (blob.Length < 16)
            return keys;

        var cursor = 0;
        var version = ReadU32(blob, ref cursor);
        if (version != 2)
            return keys;

        var count = (int)ReadU32(blob, ref cursor);
        if (count <= 0 || count > 20000 || cursor + count * 14 > blob.Length)
            return keys;

        var values = new float[count];
        for (var i = 0; i < count; i++)
        {
            cursor += 2;
            values[i] = ReadF32(blob, ref cursor);
            cursor += 8;
        }

        if (cursor + 8 > blob.Length)
            return keys;

        _ = ReadU32(blob, ref cursor);
        var times = (int)ReadU32(blob, ref cursor);
        var used = Math.Min(count, times);
        for (var i = 0; i < used; i++)
        {
            if (cursor + 4 > blob.Length)
                break;

            keys.Add(new CurveKey(ReadU32(blob, ref cursor) / 2400f, values[i]));
        }

        keys.Sort((left, right) => left.Time.CompareTo(right.Time));
        return keys;
    }

    private static float SampleCurve(List<CurveKey> keys, float time)
    {
        if (keys.Count == 0)
            return 0f;
        if (time <= keys[0].Time)
            return keys[0].Value;
        if (time >= keys[^1].Time)
            return keys[^1].Value;

        var high = 1;
        while (high < keys.Count && keys[high].Time < time)
            high++;

        var low = high - 1;
        var span = Math.Max(0.0001f, keys[high].Time - keys[low].Time);
        return keys[low].Value + (keys[high].Value - keys[low].Value) * ((time - keys[low].Time) / span);
    }

    private static Quaternion Euler(float x, float y, float z, int order)
    {
        var qx = Quaternion.CreateFromAxisAngle(Vector3.UnitX, x);
        var qy = Quaternion.CreateFromAxisAngle(Vector3.UnitY, y);
        var qz = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, z);
        return order switch
        {
            1 => Quaternion.Concatenate(Quaternion.Concatenate(qx, qz), qy),
            2 => Quaternion.Concatenate(Quaternion.Concatenate(qy, qz), qx),
            3 => Quaternion.Concatenate(Quaternion.Concatenate(qy, qx), qz),
            4 => Quaternion.Concatenate(Quaternion.Concatenate(qz, qx), qy),
            5 => Quaternion.Concatenate(Quaternion.Concatenate(qz, qy), qx),
            _ => Quaternion.Concatenate(Quaternion.Concatenate(qx, qy), qz)
        };
    }

    private static int Axis(string name) => name.ToUpperInvariant() switch
    {
        "X" or "0" => 0,
        "Y" or "1" => 1,
        "Z" or "2" => 2,
        _ => -1
    };

    private static bool KeepBone(string name)
        => !string.IsNullOrWhiteSpace(name) && name.ToUpperInvariant() is not ("X" or "Y" or "Z" or "ROTATION" or "POSITION");

    private static int[] ReadZInts(byte[] payload, int count, bool accumulate)
    {
        var values = new int[count];
        var previous = 0;
        for (var i = 0; i < count; i++)
        {
            uint packed = ((uint)payload[i] << 24) | ((uint)payload[count + i] << 16) | ((uint)payload[count * 2 + i] << 8) | payload[count * 3 + i];
            var decoded = (int)((packed >> 1) ^ (uint)-(int)(packed & 1));
            if (accumulate)
            {
                decoded += previous;
                previous = decoded;
            }

            values[i] = decoded;
        }

        return values;
    }

    private static List<byte[]> ReadRawStrings(byte[] payload)
    {
        var values = new List<byte[]>();
        var cursor = 0;
        while (cursor + 4 <= payload.Length)
        {
            var length = ReadI32(payload, ref cursor);
            if (length < 0 || cursor + length > payload.Length)
                break;

            values.Add(payload.AsSpan(cursor, length).ToArray());
            cursor += length;
        }

        return values;
    }

    private static int ReadI32(byte[] payload, ref int cursor)
    {
        var value = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(cursor));
        cursor += 4;
        return value;
    }

    private static uint ReadU32(byte[] payload, ref int cursor)
    {
        var value = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(cursor));
        cursor += 4;
        return value;
    }

    private static float ReadF32(byte[] payload, ref int cursor)
    {
        var value = BinaryPrimitives.ReadSingleLittleEndian(payload.AsSpan(cursor));
        cursor += 4;
        return value;
    }
}
