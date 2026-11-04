using System.Globalization;
using System.Numerics;
using System.Xml;
using System.Xml.Linq;

namespace KTable.Client.Studio;

public static class ColladaFile
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    const long MostBytes = 512L * 1024 * 1024;

    public static ModelMesh Read(string path)
    {
        if (new FileInfo(path).Length > MostBytes) throw new InvalidDataException("the Collada file is too large");
        XDocument doc;
        using (var r = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
            doc = XDocument.Load(r);
        var root = doc.Root ?? throw new InvalidDataException("the Collada file is empty");
        XNamespace ns = root.Name.Namespace;
        bool zUp = string.Equals(root.Element(ns + "asset")?.Element(ns + "up_axis")?.Value.Trim(), "Z_UP", StringComparison.OrdinalIgnoreCase);
        float unit = float.TryParse(root.Element(ns + "asset")?.Element(ns + "unit")?.Attribute("meter")?.Value, NumberStyles.Float, Inv, out var u) && u > 0 ? u : 1;
        var pictures = Pictures(root, ns, Path.GetDirectoryName(path) ?? "");
        var m = new ModelMesh();
        var matIndex = new Dictionary<string, int>();
        int Material(string? symbol, Dictionary<string, string> bind)
        {
            var key = symbol != null && bind.TryGetValue(symbol, out var target) ? target : symbol ?? "";
            if (matIndex.TryGetValue(key, out var i)) return i;
            i = m.Materials.Count;
            m.Materials.Add(new ModelMaterial(Rgba.White, pictures.GetValueOrDefault(key.TrimStart('#'))));
            matIndex[key] = i;
            return i;
        }
        var geometries = root.Descendants(ns + "geometry").Where(g => g.Attribute("id") != null).ToDictionary(g => g.Attribute("id")!.Value);
        var placed = new List<(XElement geo, Matrix4x4 at, Dictionary<string, string> bind)>();
        foreach (var scene in root.Descendants(ns + "visual_scene"))
            foreach (var node in scene.Elements(ns + "node")) Walk(node, Matrix4x4.Identity, ns, geometries, placed);
        if (placed.Count == 0) placed.AddRange(geometries.Values.Select(g => (g, Matrix4x4.Identity, new Dictionary<string, string>())));
        foreach (var (geo, at, bind) in placed)
        {
            var mesh = geo.Element(ns + "mesh");
            if (mesh != null) AddMesh(m, mesh, ns, at, zUp, unit, s => Material(s, bind));
        }
        if (m.V.Count == 0) throw new InvalidDataException("the Collada file has no triangles");
        if (m.Materials.Count == 0) m.Materials.Add(new ModelMaterial(Rgba.White, null));
        return m;
    }

    static void Walk(XElement node, Matrix4x4 parent, XNamespace ns, Dictionary<string, XElement> geometries, List<(XElement, Matrix4x4, Dictionary<string, string>)> placed)
    {
        var local = Matrix4x4.Identity;
        foreach (var e in node.Elements())
        {
            var f = Floats(e.Value);
            switch (e.Name.LocalName)
            {
                case "matrix" when f.Length == 16:
                    local = Matrix4x4.Transpose(new Matrix4x4(f[0], f[1], f[2], f[3], f[4], f[5], f[6], f[7], f[8], f[9], f[10], f[11], f[12], f[13], f[14], f[15])) * local;
                    break;
                case "translate" when f.Length == 3:
                    local = Matrix4x4.CreateTranslation(f[0], f[1], f[2]) * local;
                    break;
                case "scale" when f.Length == 3:
                    local = Matrix4x4.CreateScale(f[0], f[1], f[2]) * local;
                    break;
                case "rotate" when f.Length == 4:
                    local = Matrix4x4.CreateFromAxisAngle(Vector3.Normalize(new Vector3(f[0], f[1], f[2])), f[3] * MathF.PI / 180) * local;
                    break;
            }
        }
        var world = local * parent;
        foreach (var ig in node.Elements(ns + "instance_geometry"))
        {
            var url = ig.Attribute("url")?.Value.TrimStart('#') ?? "";
            if (!geometries.TryGetValue(url, out var geo)) continue;
            var bind = ig.Descendants(ns + "instance_material").Where(im => im.Attribute("symbol") != null)
                .ToDictionary(im => im.Attribute("symbol")!.Value, im => im.Attribute("target")?.Value ?? "");
            placed.Add((geo, world, bind));
        }
        foreach (var child in node.Elements(ns + "node")) Walk(child, world, ns, geometries, placed);
    }

    static Dictionary<string, string?> Pictures(XElement root, XNamespace ns, string dir)
    {
        var images = new Dictionary<string, string>();
        foreach (var img in root.Descendants(ns + "image"))
        {
            var id = img.Attribute("id")?.Value;
            var file = img.Descendants().FirstOrDefault(e => e.Name.LocalName == "init_from")?.Value.Trim();
            if (id == null || string.IsNullOrEmpty(file)) continue;
            if (file.StartsWith("file:///", StringComparison.OrdinalIgnoreCase)) file = file[8..];
            file = Uri.UnescapeDataString(file).Replace('\\', Path.DirectorySeparatorChar);
            var p = Path.IsPathRooted(file) && File.Exists(file) ? file : Path.Combine(dir, file);
            if (!File.Exists(p)) p = Path.Combine(dir, Path.GetFileName(file));
            if (File.Exists(p)) images[id] = p;
        }
        var effects = new Dictionary<string, string?>();
        foreach (var fx in root.Descendants(ns + "effect"))
        {
            var id = fx.Attribute("id")?.Value;
            if (id == null) continue;
            var tex = fx.Descendants(ns + "diffuse").Descendants(ns + "texture").FirstOrDefault()?.Attribute("texture")?.Value;
            string? pic = null;
            if (tex != null)
            {
                var sampler = fx.Descendants(ns + "newparam").FirstOrDefault(p => p.Attribute("sid")?.Value == tex)?.Descendants(ns + "source").FirstOrDefault()?.Value.Trim();
                var surface = sampler == null ? null : fx.Descendants(ns + "newparam").FirstOrDefault(p => p.Attribute("sid")?.Value == sampler)?.Descendants(ns + "init_from").FirstOrDefault()?.Value.Trim();
                pic = images.GetValueOrDefault(surface ?? tex);
            }
            effects[id] = pic;
        }
        var materials = new Dictionary<string, string?>();
        foreach (var mat in root.Descendants(ns + "material"))
        {
            var id = mat.Attribute("id")?.Value;
            var fx = mat.Element(ns + "instance_effect")?.Attribute("url")?.Value.TrimStart('#');
            if (id != null) materials[id] = fx != null ? effects.GetValueOrDefault(fx) : null;
        }
        return materials;
    }

    static void AddMesh(ModelMesh m, XElement mesh, XNamespace ns, Matrix4x4 at, bool zUp, float unit, Func<string?, int> material)
    {
        var sources = mesh.Elements(ns + "source").Where(s => s.Attribute("id") != null).ToDictionary(s => s.Attribute("id")!.Value, s =>
        {
            var arr = Floats(s.Element(ns + "float_array")?.Value ?? "");
            var stride = int.TryParse(s.Descendants(ns + "accessor").FirstOrDefault()?.Attribute("stride")?.Value, out var st) && st > 0 ? st : 3;
            return (arr, stride);
        });
        var vertices = mesh.Element(ns + "vertices");
        var positionOf = vertices?.Elements(ns + "input").FirstOrDefault(i => i.Attribute("semantic")?.Value == "POSITION")?.Attribute("source")?.Value.TrimStart('#');
        foreach (var prim in mesh.Elements().Where(e => e.Name.LocalName is "triangles" or "polylist" or "polygons"))
        {
            var inputs = prim.Elements(ns + "input")
                .Select(i => (sem: i.Attribute("semantic")?.Value ?? "", src: i.Attribute("source")?.Value.TrimStart('#') ?? "", off: int.TryParse(i.Attribute("offset")?.Value, out var o) ? o : 0, set: i.Attribute("set")?.Value))
                .ToList();
            if (inputs.Count == 0) continue;
            int width = inputs.Max(i => i.off) + 1;
            var vin = inputs.FirstOrDefault(i => i.sem == "VERTEX");
            var tin = inputs.Where(i => i.sem == "TEXCOORD").OrderBy(i => i.set ?? "0").FirstOrDefault();
            var nin = inputs.FirstOrDefault(i => i.sem == "NORMAL");
            if (vin.sem == null || positionOf == null || !sources.TryGetValue(positionOf, out var pos)) continue;
            var uvs = tin.sem != null && sources.TryGetValue(tin.src, out var t) ? t : (Array.Empty<float>(), 2);
            var nrm = nin.sem != null && sources.TryGetValue(nin.src, out var nn) ? nn : (Array.Empty<float>(), 3);
            int mat = material(prim.Attribute("material")?.Value);
            var counts = prim.Name.LocalName == "polylist" ? Ints(prim.Element(ns + "vcount")?.Value ?? "") : null;
            var polys = prim.Name.LocalName == "polygons" ? prim.Elements(ns + "p").Select(p => Ints(p.Value)).ToList() : new List<int[]> { Ints(prim.Element(ns + "p")?.Value ?? "") };
            foreach (var p in polys)
            {
                int corners = p.Length / width;
                var sizes = counts ?? (prim.Name.LocalName == "polygons" ? new[] { corners } : Enumerable.Repeat(3, corners / 3).ToArray());
                int at0 = 0;
                foreach (var n in sizes)
                {
                    if ((at0 + n) * width > p.Length) break;
                    for (int k = 1; k + 1 < n; k++)
                        foreach (var c in new[] { at0, at0 + k + 1, at0 + k })
                        {
                            int b = c * width;
                            var v = Vec(pos.arr, pos.stride, p[b + vin.off]);
                            var w = Vector3.Transform(v, at) * unit;
                            if (zUp) w = new Vector3(w.X, w.Z, -w.Y);
                            var uv = tin.sem != null && b + tin.off < p.Length && uvs.Item1.Length > 0 ? Uv(uvs.Item1, uvs.Item2, p[b + tin.off]) : Vector2.Zero;
                            var normal = nin.sem != null && b + nin.off < p.Length && nrm.Item1.Length > 0 ? Vec(nrm.Item1, nrm.Item2, p[b + nin.off]) : Vector3.Zero;
                            m.Add(w, normal, uv, Rgba.White);
                        }
                    for (int k = 1; k + 1 < n; k++) m.Mat.Add(mat);
                    at0 += n;
                }
            }
        }
    }

    static Vector3 Vec(float[] a, int stride, int i) => i >= 0 && i * stride + 2 < a.Length ? new Vector3(a[i * stride], a[i * stride + 1], a[i * stride + 2]) : Vector3.Zero;
    static Vector2 Uv(float[] a, int stride, int i) => i >= 0 && i * stride + 1 < a.Length ? new Vector2(a[i * stride], 1 - a[i * stride + 1]) : Vector2.Zero;
    static float[] Floats(string s) => s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(x => float.TryParse(x, NumberStyles.Float, Inv, out var f) ? f : 0).ToArray();
    static int[] Ints(string s) => s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(x => int.TryParse(x, NumberStyles.Integer, Inv, out var i) ? i : -1).ToArray();
}
