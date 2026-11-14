using System.Globalization;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace KTable.Core.Model;

public static class PackedModel
{
    public const string Type = "kmesh", Ext = "." + Type;
    const int Corners = 3, PlainPoint = 3, ColouredPoint = 6, Normal = 3, Coord = 2, Word = 4, MostArity = 3;
    const int Head = 0, Points = 1, Normals = 2, Coords = 3, Faces = 4;
    const long SectionWeighs = 64, HeadWeighs = 16;
    static readonly byte[] Magic = "KTMESH1\n"u8.ToArray();
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    sealed class Section
    {
        public List<string> Head = new();
        public List<float> Points = new(), Normals = new(), Coords = new();
        public List<int> Faces = new();
        public int PointWidth, NormalWidth, CoordWidth, Arity;
    }

    sealed record Read(List<string> Head, float[] Points, int PointWidth, float[] Normals, float[] Coords, int[] Faces, int Arity);

    public static bool Is(string path) => path.EndsWith(Ext, StringComparison.OrdinalIgnoreCase);

    public static IEnumerable<string> Lines(string path, long most = ContentLimits.MaxModelBytes) =>
        Is(path) ? Unpacked(File.ReadAllBytes(path), most) : File.ReadLines(path);

    public static byte[]? Pack(IEnumerable<string> lines)
    {
        var sections = new List<Section>();
        Section? now = null;
        int at = Head;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            var p = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            int kind = p[0] switch { "v" => Points, "vn" => Normals, "vt" => Coords, "f" => Faces, _ => Head };
            if (now == null || kind < at) sections.Add(now = new Section());
            at = kind;
            bool taken = kind switch
            {
                Points => Numbers(p, now.Points, ref now.PointWidth, PlainPoint, ColouredPoint),
                Normals => Numbers(p, now.Normals, ref now.NormalWidth, Normal, Normal),
                Coords => Numbers(p, now.Coords, ref now.CoordWidth, Coord, Coord),
                Faces => Face(p, now),
                _ => Kept(now, line, p[0]),
            };
            if (!taken) return null;
        }
        return sections.Count == 0 ? null : Written(sections);
    }

    static bool Kept(Section s, string line, string word)
    {
        if (word == "mtllib") return false;
        s.Head.Add(line);
        return true;
    }

    static bool Numbers(string[] p, List<float> into, ref int width, int plain, int full)
    {
        int count = p.Length - 1;
        if (width == 0) width = count;
        if (count != width || count != plain && count != full) return false;
        for (int i = 1; i < p.Length; i++)
        {
            if (!float.TryParse(p[i], NumberStyles.Float, Inv, out float f) || !float.IsFinite(f)) return false;
            if (double.Parse(Spelled(f), Inv) != double.Parse(p[i], NumberStyles.Float, Inv)) return false;
            into.Add(f);
        }
        return true;
    }

    static bool Face(string[] p, Section s)
    {
        if (p.Length != Corners + 1) return false;
        for (int i = 1; i < p.Length; i++)
        {
            var parts = p[i].Split('/');
            if (s.Arity == 0) s.Arity = parts.Length;
            if (parts.Length != s.Arity || parts.Length > MostArity) return false;
            foreach (var part in parts)
            {
                if (!int.TryParse(part, NumberStyles.None, Inv, out int index) || index < 1) return false;
                s.Faces.Add(index);
            }
        }
        return true;
    }

    static string Spelled(float f) => f.ToString("R", Inv);

    static byte[] Written(List<Section> sections)
    {
        using var file = new MemoryStream();
        file.Write(Magic);
        using (var packed = new ZLibStream(file, CompressionLevel.SmallestSize, leaveOpen: true))
        using (var w = new BinaryWriter(packed, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(sections.Count);
            foreach (var s in sections)
            {
                w.Write(s.Head.Count);
                foreach (var line in s.Head)
                {
                    var text = Encoding.UTF8.GetBytes(line);
                    w.Write(text.Length);
                    w.Write(text);
                }
                w.Write((byte)s.PointWidth);
                w.Write((byte)s.Arity);
                Planes(w, MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(s.Points)));
                Planes(w, MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(s.Normals)));
                Planes(w, MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(s.Coords)));
                Planes(w, MemoryMarshal.AsBytes(Stepped(s.Faces, s.Arity).AsSpan()));
            }
        }
        return file.ToArray();
    }

    static int[] Stepped(List<int> faces, int arity)
    {
        var stepped = new int[faces.Count];
        int corners = arity == 0 ? 0 : faces.Count / arity, before = 0;
        for (int c = 0; c < corners; c++)
        {
            int point = faces[c * arity];
            stepped[c] = unchecked(point - before);
            for (int k = 1; k < arity; k++) stepped[k * corners + c] = unchecked(faces[c * arity + k] - point);
            before = point;
        }
        return stepped;
    }

    static int[] Walked(int[] stepped, int arity)
    {
        var faces = new int[stepped.Length];
        int corners = arity == 0 ? 0 : stepped.Length / arity, before = 0;
        for (int c = 0; c < corners; c++)
        {
            int point = unchecked(before + stepped[c]);
            faces[c * arity] = point;
            for (int k = 1; k < arity; k++) faces[c * arity + k] = unchecked(point + stepped[k * corners + c]);
            before = point;
        }
        return faces;
    }

    static void Planes(BinaryWriter w, ReadOnlySpan<byte> words)
    {
        int count = words.Length / Word;
        w.Write(count);
        var planes = new byte[words.Length];
        for (int i = 0; i < count; i++)
            for (int b = 0; b < Word; b++)
                planes[b * count + i] = words[i * Word + b];
        w.Write(planes);
    }

    static byte[] Words(BinaryReader r, ref long left)
    {
        int count = r.ReadInt32();
        var planes = Bytes(r, count, Word, ref left);
        var words = new byte[planes.Length];
        for (int i = 0; i < count; i++)
            for (int b = 0; b < Word; b++)
                words[i * Word + b] = planes[b * count + i];
        return words;
    }

    static byte[] Bytes(BinaryReader r, int count, int each, ref long left)
    {
        Spend(ref left, count < 0 ? long.MaxValue : (long)count * each);
        var bytes = r.ReadBytes(count * each);
        return bytes.Length == count * each ? bytes : throw new EndOfStreamException("the model file ends early");
    }

    static void Spend(ref long left, long bytes)
    {
        if (bytes > left) throw new TooLargeException("the model unpacks to more than a model may be");
        left -= bytes;
    }

    static IEnumerable<string> Unpacked(byte[] file, long most)
    {
        if (!file.AsSpan(0, Math.Min(file.Length, Magic.Length)).SequenceEqual(Magic)) throw new FormatException("not a packed model file");
        var sections = new List<Read>();
        long left = most;
        try
        {
            using var packed = new ZLibStream(new MemoryStream(file, Magic.Length, file.Length - Magic.Length), CompressionMode.Decompress);
            using var r = new BinaryReader(packed, Encoding.UTF8);
            int count = r.ReadInt32();
            for (int n = 0; n < count; n++)
            {
                Spend(ref left, SectionWeighs);
                var head = new List<string>();
                int lines = r.ReadInt32();
                for (int i = 0; i < lines; i++)
                {
                    Spend(ref left, HeadWeighs);
                    head.Add(Encoding.UTF8.GetString(Bytes(r, r.ReadInt32(), 1, ref left)));
                }
                int pointWidth = r.ReadByte(), arity = r.ReadByte();
                if (pointWidth is not (0 or PlainPoint or ColouredPoint) || arity > MostArity) throw new FormatException("not a readable packed model");
                var points = MemoryMarshal.Cast<byte, float>(Words(r, ref left)).ToArray();
                var normals = MemoryMarshal.Cast<byte, float>(Words(r, ref left)).ToArray();
                var coords = MemoryMarshal.Cast<byte, float>(Words(r, ref left)).ToArray();
                var faces = Walked(MemoryMarshal.Cast<byte, int>(Words(r, ref left)).ToArray(), arity);
                if (pointWidth == 0 && points.Length > 0 || arity == 0 && faces.Length > 0) throw new FormatException("not a readable packed model");
                sections.Add(new Read(head, points, pointWidth, normals, coords, faces, arity));
            }
        }
        catch (InvalidDataException e) { throw new FormatException($"not a readable packed model ({e.Message})", e); }
        return Spoken(sections);
    }

    static IEnumerable<string> Spoken(List<Read> sections)
    {
        var line = new StringBuilder();
        foreach (var s in sections)
        {
            foreach (var head in s.Head) yield return head;
            foreach (var said in Said(line, "v", s.Points, s.PointWidth)) yield return said;
            foreach (var said in Said(line, "vn", s.Normals, Normal)) yield return said;
            foreach (var said in Said(line, "vt", s.Coords, Coord)) yield return said;
            int each = Corners * s.Arity;
            for (int f = 0; each > 0 && f + each <= s.Faces.Length; f += each)
            {
                line.Clear().Append('f');
                for (int c = 0; c < Corners; c++)
                    for (int k = 0; k < s.Arity; k++)
                        line.Append(k == 0 ? ' ' : '/').Append(s.Faces[f + c * s.Arity + k]);
                yield return line.ToString();
            }
        }
    }

    static IEnumerable<string> Said(StringBuilder line, string word, float[] numbers, int width)
    {
        for (int i = 0; width > 0 && i + width <= numbers.Length; i += width)
        {
            line.Clear().Append(word);
            for (int k = 0; k < width; k++) line.Append(' ').Append(Spelled(numbers[i + k]));
            yield return line.ToString();
        }
    }
}
