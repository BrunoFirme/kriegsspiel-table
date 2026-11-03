using System.Globalization;
using System.Numerics;
using System.Text;
using KTable.Client.Geometry;
using KTable.Core;

namespace KTable.Client.Studio;

public sealed record ModelMaterial(Rgba Color, string? Picture);

public sealed class ModelMesh
{
    public readonly List<Vector3> V = new();
    public readonly List<Vector3> N = new();
    public readonly List<Vector2> UV = new();
    public readonly List<Rgba> C = new();
    public readonly List<int> Mat = new();
    public readonly List<ModelMaterial> Materials = new();

    public void Add(Vector3 v, Vector3 n, Vector2 uv, Rgba c) { V.Add(v); N.Add(n); UV.Add(uv); C.Add(c); }
}

public static class ModelFiles
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static readonly Rgba Plain = new(0.75f, 0.74f, 0.70f);

    public static byte[] Bytes(string path) =>
        ContentLimits.ModelAllowed(path) ? File.ReadAllBytes(path) : throw TooLarge(path);

    static TooLargeException TooLarge(string path) =>
        new($"{Path.GetFileName(path)} is larger than the {ContentLimits.Describe(ContentLimits.MaxModelBytes)} a model file may be");

    public static ModelMesh Obj(string path) =>
        ContentLimits.ModelAllowed(path) ? Refusing("OBJ", () => ReadObj(path)) : throw TooLarge(path);

    public static ModelMesh Stl(byte[] bytes) => Refusing("STL", () => ReadStl(bytes));

    public static ModelMesh Ply(byte[] bytes) => Refusing("PLY", () => ReadPly(bytes));

    static ModelMesh Refusing(string format, Func<ModelMesh> read)
    {
        try { return read(); }
        catch (Exception e) when (e is FormatException or OverflowException or IndexOutOfRangeException or ArgumentException or EndOfStreamException or KeyNotFoundException)
        {
            throw new InvalidDataException($"not a readable {format} file ({e.Message})", e);
        }
    }

    static ModelMesh ReadObj(string path)
    {
        var v = new List<Vector3>(); var vt = new List<Vector2>(); var vn = new List<Vector3>(); var vc = new List<Rgba>();
        var m = new ModelMesh();
        var mats = new Dictionary<string, int>();
        var lib = new Dictionary<string, (Rgba kd, string? map)>();
        int cur = -1;
        int Material(string name)
        {
            if (mats.TryGetValue(name, out var i)) return i;
            i = m.Materials.Count;
            var (kd, map) = lib.TryGetValue(name, out var e) ? e : (Rgba.White, null);
            string? picture = null;
            if (map != null)
            {
                var p = Path.Combine(Path.GetDirectoryName(path) ?? "", map.Replace('\\', Path.DirectorySeparatorChar));
                if (!File.Exists(p)) p = Path.Combine(Path.GetDirectoryName(path) ?? "", Path.GetFileName(map));
                if (File.Exists(p)) picture = p;
            }
            m.Materials.Add(new ModelMaterial(kd, picture));
            mats[name] = i;
            return i;
        }
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length < 2 || line[0] == '#') continue;
            var p = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            switch (p[0])
            {
                case "v":
                    v.Add(new Vector3(F(p[1]), F(p[2]), F(p[3])));
                    vc.Add(p.Length >= 7 ? new Rgba(F(p[4]), F(p[5]), F(p[6])) : Rgba.White);
                    break;
                case "vt": vt.Add(new Vector2(F(p[1]), p.Length > 2 ? F(p[2]) : 0)); break;
                case "vn": vn.Add(new Vector3(F(p[1]), F(p[2]), F(p[3]))); break;
                case "mtllib":
                {
                    var mp = Path.Combine(Path.GetDirectoryName(path) ?? "", line[7..].Trim());
                    if (File.Exists(mp)) ReadMtl(mp, lib);
                    break;
                }
                case "usemtl": cur = Material(line[6..].Trim()); break;
                case "f":
                {
                    if (cur < 0) cur = Material("");
                    var idx = new List<(int v, int t, int n)>();
                    for (int i = 1; i < p.Length; i++)
                    {
                        var s = p[i].Split('/');
                        int Fix(string x, int count) => string.IsNullOrEmpty(x) ? -1 : (int.Parse(x, Inv) is var k && k < 0 ? count + k : k - 1);
                        idx.Add((Fix(s[0], v.Count), s.Length > 1 ? Fix(s[1], vt.Count) : -1, s.Length > 2 ? Fix(s[2], vn.Count) : -1));
                    }
                    for (int i = 1; i + 1 < idx.Count; i++)
                    {
                        foreach (var q in new[] { idx[0], idx[i + 1], idx[i] })
                            m.Add(q.v >= 0 && q.v < v.Count ? v[q.v] : Vector3.Zero, q.n >= 0 && q.n < vn.Count ? vn[q.n] : Vector3.Zero,
                                  q.t >= 0 && q.t < vt.Count ? new Vector2(vt[q.t].X, 1 - vt[q.t].Y) : Vector2.Zero, q.v >= 0 && q.v < vc.Count ? vc[q.v] : Rgba.White);
                        m.Mat.Add(cur);
                    }
                    break;
                }
            }
        }
        if (m.Materials.Count == 0) m.Materials.Add(new ModelMaterial(Rgba.White, null));
        return m;
    }

    static void ReadMtl(string path, Dictionary<string, (Rgba kd, string? map)> lib)
    {
        string? name = null;
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            var p = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            switch (p[0])
            {
                case "newmtl": name = line[6..].Trim(); lib[name] = (Rgba.White, null); break;
                case "Kd" when name != null && p.Length >= 4: lib[name] = (new Rgba(F(p[1]), F(p[2]), F(p[3])), lib[name].map); break;
                case "map_Kd" when name != null: lib[name] = (lib[name].kd, p[^1]); break;
            }
        }
    }

    static float F(string s) => float.Parse(s, NumberStyles.Float, Inv);

    static ModelMesh ReadStl(byte[] bytes)
    {
        var m = new ModelMesh { Materials = { new ModelMaterial(Plain, null) } };
        bool ascii = bytes.Length > 6 && Encoding.ASCII.GetString(bytes, 0, 6).StartsWith("solid", StringComparison.Ordinal) && Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 512)).Contains("facet", StringComparison.Ordinal);
        static Vector3 Up(Vector3 z) => new(z.X, z.Z, -z.Y);
        if (ascii)
        {
            var corners = new List<Vector3>();
            foreach (var raw in Encoding.ASCII.GetString(bytes).Split('\n'))
            {
                var p = raw.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length >= 4 && p[0] == "vertex") corners.Add(Up(new Vector3(F(p[1]), F(p[2]), F(p[3]))));
                if (corners.Count == 3) { AddTri(m, corners[0], corners[2], corners[1]); corners.Clear(); }
            }
        }
        else
        {
            using var br = new BinaryReader(new MemoryStream(bytes));
            br.ReadBytes(80);
            uint n = br.ReadUInt32();
            for (uint i = 0; i < n && br.BaseStream.Position + 50 <= br.BaseStream.Length; i++)
            {
                br.ReadBytes(12);
                var a = Up(new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle()));
                var b = Up(new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle()));
                var c = Up(new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle()));
                br.ReadUInt16();
                AddTri(m, a, c, b);
            }
        }
        return m;
    }

    static void AddTri(ModelMesh m, Vector3 a, Vector3 b, Vector3 c)
    {
        var n = (c - a).Cross(b - a).Normalized();
        m.Add(a, n, Vector2.Zero, Rgba.White); m.Add(b, n, Vector2.Zero, Rgba.White); m.Add(c, n, Vector2.Zero, Rgba.White);
        m.Mat.Add(0);
    }

    static ModelMesh ReadPly(byte[] bytes)
    {
        int headerEnd = IndexOf(bytes, "end_header");
        if (headerEnd < 0) throw new InvalidDataException("not a PLY file");
        int dataStart = headerEnd + "end_header".Length;
        while (dataStart < bytes.Length && bytes[dataStart] != '\n') dataStart++;
        dataStart++;
        var header = Encoding.ASCII.GetString(bytes, 0, headerEnd).Split('\n').Select(l => l.Trim()).ToList();
        string format = "ascii";
        var elements = new List<(string name, int count, List<(string type, string name, string? countType)> props)>();
        foreach (var l in header)
        {
            var p = l.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length == 0) continue;
            if (p[0] == "format") format = p[1];
            else if (p[0] == "element") elements.Add((p[1], int.Parse(p[2], Inv), new()));
            else if (p[0] == "property" && elements.Count > 0)
            {
                if (p[1] == "list") elements[^1].props.Add((p[3], p[4], p[2]));
                else elements[^1].props.Add((p[1], p[2], null));
            }
        }
        if (format == "binary_big_endian") throw new NotSupportedException("big-endian PLY is not supported");
        int room = bytes.Length - dataStart;
        foreach (var (name, count, props) in elements)
            if (count < 0 || count > room || count > 0 && props.Count == 0)
                throw new InvalidDataException($"the PLY file says it holds {count} {name}(s), more than it can");
        bool bin = format.StartsWith("binary", StringComparison.Ordinal);
        var pos = new List<Vector3>(); var nor = new List<Vector3>(); var col = new List<Rgba>(); var uv = new List<Vector2>();
        var faces = new List<int[]>();
        int at = dataStart;
        string[]? tokens = bin ? null : Encoding.ASCII.GetString(bytes, dataStart, bytes.Length - dataStart).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        int tk = 0;
        double Read(string type)
        {
            if (!bin) return double.Parse(tokens![tk++], Inv);
            double r;
            switch (type)
            {
                case "char": case "int8": r = (sbyte)bytes[at]; at += 1; break;
                case "uchar": case "uint8": r = bytes[at]; at += 1; break;
                case "short": case "int16": r = BitConverter.ToInt16(bytes, at); at += 2; break;
                case "ushort": case "uint16": r = BitConverter.ToUInt16(bytes, at); at += 2; break;
                case "int": case "int32": r = BitConverter.ToInt32(bytes, at); at += 4; break;
                case "uint": case "uint32": r = BitConverter.ToUInt32(bytes, at); at += 4; break;
                case "float": case "float32": r = BitConverter.ToSingle(bytes, at); at += 4; break;
                default: r = BitConverter.ToDouble(bytes, at); at += 8; break;
            }
            return r;
        }
        foreach (var (name, count, props) in elements)
            for (int i = 0; i < count; i++)
            {
                var vals = new Dictionary<string, double>();
                int[]? list = null;
                foreach (var (type, pname, countType) in props)
                {
                    if (countType != null)
                    {
                        int n = (int)Read(countType);
                        if (n < 0 || n > room) throw new InvalidDataException($"the PLY file says a list holds {n} items, more than it can");
                        var l = new int[n];
                        for (int k = 0; k < n; k++) l[k] = (int)Read(type);
                        if (pname is "vertex_indices" or "vertex_index") list = l;
                    }
                    else vals[pname] = Read(type);
                }
                if (name == "vertex")
                {
                    pos.Add(new Vector3((float)vals.GetValueOrDefault("x"), (float)vals.GetValueOrDefault("y"), (float)vals.GetValueOrDefault("z")));
                    nor.Add(vals.ContainsKey("nx") ? new Vector3((float)vals["nx"], (float)vals["ny"], (float)vals["nz"]) : Vector3.Zero);
                    float k = props.FirstOrDefault(p => p.name == "red").type is "uchar" or "uint8" ? 1 / 255f : 1;
                    col.Add(vals.ContainsKey("red") ? new Rgba((float)vals["red"] * k, (float)vals["green"] * k, (float)vals["blue"] * k) : Rgba.White);
                    uv.Add(vals.ContainsKey("s") ? new Vector2((float)vals["s"], 1 - (float)vals["t"]) : vals.ContainsKey("u") ? new Vector2((float)vals["u"], 1 - (float)vals["v"]) : Vector2.Zero);
                }
                else if (name == "face" && list != null) faces.Add(list);
            }
        var m = new ModelMesh { Materials = { new ModelMaterial(col.Any(c => c != Rgba.White) ? Rgba.White : Plain, null) } };
        foreach (var f in faces)
            for (int i = 1; i + 1 < f.Length; i++)
            {
                foreach (var k in new[] { f[0], f[i + 1], f[i] })
                    m.Add(pos[k], nor[k], uv[k], col[k]);
                m.Mat.Add(0);
            }
        return m;
    }

    static int IndexOf(byte[] b, string s)
    {
        var t = Encoding.ASCII.GetBytes(s);
        for (int i = 0; i + t.Length <= Math.Min(b.Length, 1 << 16); i++)
        {
            int k = 0;
            while (k < t.Length && b[i + k] == t[k]) k++;
            if (k == t.Length) return i;
        }
        return -1;
    }
}
