using System.Globalization;
using System.Text;

namespace KTable.Core.Model;

public static class ModuleMeshes
{
    static double Turned(double v) => v == 0 ? 0 : -v;

    const int MostCorners = 4, MostPoints = 30000;
    const int None = -1;

    public static readonly SurfaceDraw Drawn = SurfaceDraw.Plain with { OneSided = true };

    public static bool KeptFace(string[] p) => p.Length - 1 <= MostCorners;

    public static string? Refusal(IEnumerable<string> obj)
    {
        var points = new HashSet<(string, string)>();
        foreach (var line in obj)
        {
            if (line.Length == 0) continue;
            if (char.IsWhiteSpace(line[0])) return "a line of it starts with a space";
            var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int numbers = p.Length - 1;
            switch (p[0])
            {
                case "v" when numbers != 3: return "a vertex of it has other than three numbers";
                case "vt" when numbers is < 2 or > 3: return "a texture point of it has one number or more than three";
                case "vn" when numbers != 3: return "a normal of it has other than three numbers";
                case "f" when KeptFace(p):
                    foreach (var corner in p.Skip(1)) points.Add((corner.Split('/')[0], corner.Split('/').ElementAtOrDefault(1) ?? ""));
                    if (points.Count > MostPoints) return $"it has more than {MostPoints} points";
                    break;
            }
        }
        return null;
    }

    public static (double X, double Z) Middle(IEnumerable<string> obj)
    {
        var points = new List<(double X, double Z)>();
        var used = new HashSet<int>();
        foreach (var line in obj)
        {
            var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length >= 4 && p[0] == "v" && Three(p, out var v)) points.Add((v.X, v.Z));
            else if (p.Length >= 4 && p[0] == "f")
                foreach (var corner in p.Skip(1))
                    if (Index(corner.Split('/')[0], points.Count) is var at and >= 0) used.Add(at);
        }
        var drawn = used.Count > 0 ? used.Where(i => i < points.Count).Select(i => points[i]).ToList() : points;
        if (drawn.Count == 0) return (0, 0);
        static double Share(double lo, double hi) => hi > lo ? -(lo + hi) / 2 / (hi - lo) : 0;
        return (Share(drawn.Min(q => q.X), drawn.Max(q => q.X)), Share(drawn.Min(q => q.Z), drawn.Max(q => q.Z)));
    }

    readonly record struct Corner(int V, int T, int N);

    sealed class Shape
    {
        public readonly List<(double X, double Y, double Z)> Points = new();
        public readonly List<(double X, double Y, double Z)> Normals = new();
        public readonly List<Corner[]> Faces = new();
        public int Textures;
    }

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    static bool Three(string[] p, out (double X, double Y, double Z) v)
    {
        v = default;
        if (p.Length < 4) return false;
        bool read = double.TryParse(p[1], NumberStyles.Float, Inv, out var x) & double.TryParse(p[2], NumberStyles.Float, Inv, out var y)
                    & double.TryParse(p[3], NumberStyles.Float, Inv, out var z);
        v = (x, y, z);
        return read;
    }

    static int Index(string part, int count) =>
        part.Length > 0 && int.TryParse(part, NumberStyles.Integer, Inv, out int k) && k != 0 ? (k < 0 ? count + k : k - 1) : None;

    static Shape Read(List<string[]> lines)
    {
        var shape = new Shape();
        foreach (var p in lines)
        {
            if (p.Length == 0) continue;
            switch (p[0])
            {
                case "v":
                    Three(p, out var v);
                    shape.Points.Add(v);
                    break;
                case "vt":
                    shape.Textures++;
                    break;
                case "vn":
                    Three(p, out var n);
                    shape.Normals.Add(n);
                    break;
                case "f" when KeptFace(p):
                    shape.Faces.Add(p.Skip(1).Select(c => c.Split('/')).Select(s => new Corner(
                        Index(s[0], shape.Points.Count),
                        s.Length > 1 ? Index(s[1], shape.Textures) : None,
                        s.Length > 2 ? Index(s[2], shape.Normals.Count) : None)).ToArray());
                    break;
            }
        }
        return shape;
    }

    static IEnumerable<(Corner A, Corner B, Corner C)> Triangles(Corner[] face)
    {
        for (int i = 1; i + 1 < face.Length; i++) yield return (face[0], face[i], face[i + 1]);
    }

    static (int V, int T) Key(Corner c) => (c.V, c.T);

    static bool Inside(Corner c, Shape shape) => c.V >= 0 && c.V < shape.Points.Count;

    static Dictionary<(int V, int T), (double X, double Y, double Z)> GameNormals(Shape shape)
    {
        var found = new Dictionary<(int V, int T), (double X, double Y, double Z)>();
        bool supplied = shape.Normals.Count > 0;
        foreach (var face in shape.Faces)
            foreach (var (a, b, c) in Triangles(face))
            {
                if (!Inside(a, shape) || !Inside(b, shape) || !Inside(c, shape)) continue;
                if (supplied)
                {
                    foreach (var corner in new[] { a, b, c })
                        found[Key(corner)] = shape.Normals[corner.N >= 0 && corner.N < shape.Normals.Count ? corner.N : 0];
                    continue;
                }
                var across = Cross(Minus(shape.Points[b.V], shape.Points[a.V]), Minus(shape.Points[c.V], shape.Points[a.V]));
                foreach (var corner in new[] { a, b, c })
                    found[Key(corner)] = found.TryGetValue(Key(corner), out var sum) ? Plus(sum, across) : across;
            }
        return found;
    }

    static (double X, double Y, double Z) Minus((double X, double Y, double Z) a, (double X, double Y, double Z) b) => (a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    static (double X, double Y, double Z) Plus((double X, double Y, double Z) a, (double X, double Y, double Z) b) => (a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    static (double X, double Y, double Z) Cross((double X, double Y, double Z) a, (double X, double Y, double Z) b) =>
        (a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

    static (double X, double Y, double Z) Leaned((double X, double Y, double Z) n, double stretch)
    {
        double x = double.IsNaN(n.X) ? 0 : n.X, y = double.IsNaN(n.Y) ? 0 : n.Y / stretch, z = double.IsNaN(n.Z) ? 0 : n.Z;
        double length = Math.Sqrt(x * x + y * y + z * z);
        if (length > 0) (x, y, z) = (x / length, y / length, z / length);
        return (Turned(x), y, Turned(z));
    }

    static string Number(double v) => v.ToString("R", Inv);

    public static string Placed(IEnumerable<string> obj, double stretch)
    {
        var raw = obj.ToList();
        var lines = raw.Select(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToList();
        var shape = Read(lines);
        var normals = GameNormals(shape);
        var normalAt = new Dictionary<(int V, int T), int>();
        var sb = new StringBuilder();
        sb.Append(Drawn.Statements());
        int face = 0;
        for (int i = 0; i < lines.Count; i++)
        {
            var p = lines[i];
            if (p.Length > 0 && p[0] == "vn") continue;
            if (p.Length > 0 && p[0] == "f")
            {
                if (!KeptFace(p)) continue;
                if (normalAt.Count == 0) Normals(sb, normals, normalAt, stretch);
                var corners = shape.Faces[face++];
                if (corners.Any(c => !Inside(c, shape)))
                {
                    sb.Append(raw[i]).Append('\n');
                    continue;
                }
                sb.Append('f');
                foreach (var c in corners)
                {
                    sb.Append(' ').Append(c.V + 1).Append('/');
                    if (c.T >= 0) sb.Append(c.T + 1);
                    if (normalAt.TryGetValue(Key(c), out int n)) sb.Append('/').Append(n);
                }
                sb.Append('\n');
                continue;
            }
            if (p.Length > 0 && p[0] == "v" && Three(p, out var v))
            {
                p[1] = Number(Turned(v.X));
                p[2] = Number(v.Y * stretch);
                p[3] = Number(Turned(v.Z));
                sb.Append(string.Join(' ', p)).Append('\n');
                continue;
            }
            sb.Append(raw[i]).Append('\n');
        }
        return sb.ToString();
    }

    static void Normals(StringBuilder sb, Dictionary<(int V, int T), (double X, double Y, double Z)> normals, Dictionary<(int V, int T), int> at, double stretch)
    {
        foreach (var (key, n) in normals)
        {
            at[key] = at.Count + 1;
            var (x, y, z) = Leaned(n, stretch);
            sb.Append("vn ").Append(Number(x)).Append(' ').Append(Number(y)).Append(' ').Append(Number(z)).Append('\n');
        }
    }
}
