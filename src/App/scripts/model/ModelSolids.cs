using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;

namespace KTable.App;

internal sealed class ModelSolids
{
    readonly ModelStudio _m;
    readonly Dictionary<string, string> _built = new();
    readonly Dictionary<string, ModelPartView> _shelf = new();
    HashSet<string> _consumed = new();
    List<ModelTree.Chain> _chains = new();
    bool _building, _touched;
    double _since;
    int _gen, _builds;
    const double Throttle = 0.12;
    const int ShelfKept = 8;
    public const string SolidKind = "solid";

    public ModelSolids(ModelStudio m) => _m = m;

    public bool IsConsumed(string id) => _consumed.Contains(id);
    public IReadOnlyList<ModelTree.Chain> Chains => _chains;
    public bool Busy => _building;

    public void Reset()
    {
        foreach (var v in _shelf.Values) if (GodotObject.IsInstanceValid(v.Node)) v.Node.QueueFree();
        _built.Clear();
        _shelf.Clear();
        _consumed.Clear();
        _chains.Clear();
        _gen++;
        _touched = true;
    }

    public void Touch() => _touched = true;

    public void Refresh()
    {
        _chains = ModelTree.Solids(_m.Doc);
        _consumed = ModelTree.Consumed(_m.Doc, _chains);
        var keys = _chains.Select(c => c.Key).ToHashSet();
        foreach (var k in _m.Views.Keys.Where(ModelTree.IsSolidKey).Where(k => !keys.Contains(k)).ToList())
        {
            var v = _m.Views[k];
            _m.Views.Remove(k);
            _built.Remove(k);
            v.Node.Visible = false;
            Shelve(k, v);
        }
        foreach (var c in _chains)
            if (_m.Views.TryGetValue(c.Key, out var sv))
            {
                sv.Xf = SpaceOf(c);
                sv.Node.Transform = sv.Xf;
                sv.Node.Visible = true;
            }
        _touched = true;
    }

    void Shelve(string key, ModelPartView v)
    {
        if (_shelf.Remove(key, out var older) && older != v && GodotObject.IsInstanceValid(older.Node)) older.Node.QueueFree();
        _shelf[key] = v;
        while (_shelf.Count > ShelfKept)
        {
            var first = _shelf.Keys.First();
            if (GodotObject.IsInstanceValid(_shelf[first].Node)) _shelf[first].Node.QueueFree();
            _shelf.Remove(first);
        }
    }

    Transform3D SpaceOf(ModelTree.Chain c) => ModelTree.ParentOf(_m.Doc, c.Head) is { } g ? ModelTree.WorldXf(_m.Doc, g) : Transform3D.Identity;

    string Sig(ModelTree.Chain c)
    {
        var sb = new StringBuilder();
        foreach (var m in c.Members)
            foreach (var p in m.IsGroup ? ModelTree.Descendants(_m.Doc, m).Prepend(m) : new[] { m })
            {
                sb.Append(JsonSerializer.Serialize(new { p.Id, p.Kind, p.Mesh, p.Tex, p.Pos, p.Rot, p.Scl, p.Visible, p.Parent, p.Join, p.Blend, p.Twin, p.Pivot, p.WorldScale, p.Tint }));
                if (_m.Views.TryGetValue(p.Id, out var v)) sb.Append('#').Append(v.Gen);
            }
        return sb.ToString();
    }

    public bool Pending => _chains.Any(c => _built.GetValueOrDefault(c.Key) != Sig(c));

    public void Tick(double delta)
    {
        if (_building) return;
        _since += delta;
        if (!_touched || _since < Throttle) return;
        foreach (var c in _chains)
        {
            var sig = Sig(c);
            if (_built.GetValueOrDefault(c.Key) == sig) continue;
            _ = Build(c, sig);
            return;
        }
        _touched = false;
    }

    public async Task Catch()
    {
        for (int round = 0; round < 64; round++)
        {
            while (_building) await _m.ToSignal(_m.GetTree(), SceneTree.SignalName.ProcessFrame);
            var next = _chains.Select(c => (c, sig: Sig(c))).FirstOrDefault(x => _built.GetValueOrDefault(x.c.Key) != x.sig);
            if (next.c == null) return;
            await Build(next.c, next.sig);
        }
    }

    async Task Build(ModelTree.Chain c, string sig)
    {
        _building = true;
        int gen = _gen;
        try
        {
            bool smooth = c.Members.Skip(1).Any(m => m.Blend > 0) && c.Members.All(ShapeSdf.Fits);
            MeshData data;
            if (smooth)
            {
                var (ops, reach) = SdfOperands(c);
                data = await Task.Run(() => ShapeSdf.Mesh(ops, reach));
            }
            else data = await Csg(c);
            if (gen != _gen) return;
            var img = ModelImport.Unwrap(data, ModelStudio.TexSizeFor(data));
            Install(c, data, img);
            _built[c.Key] = sig;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            _built[c.Key] = sig;
            GD.PrintErr($"[studio] joining {c.Head.Name}: {e.Message}");
            _m.Studio.Say($"Could not join the shapes from {c.Head.Name}: {e.Message}");
        }
        finally
        {
            _building = false;
            _since = 0;
            _touched = true;
        }
    }

    void Install(ModelTree.Chain c, MeshData data, Image img)
    {
        var key = c.Key;
        var head = c.Head;
        var old = _m.Views.GetValueOrDefault(key);
        if (old == null && _shelf.Remove(key, out var shelved)) old = shelved;
        if (old != null) _m.Views.Remove(key);
        var part = new ModelPart { Id = key, Name = head.Name, Kind = SolidKind, Region = head.SolidRegion };
        var v = _m.AddView(part, data, img);
        v.Build = ++_builds;
        v.Xf = SpaceOf(c);
        v.Node.Transform = v.Xf;
        if (old != null)
        {
            if (PaintCarry.Carry(old.Data, old.Paint, old.W, old.H, v.Data, v.Paint, v.W, v.H)) v.Repaint();
            old.Node.QueueFree();
        }
        else if (head.SolidMesh != null && head.SolidPaint != null && Saved(head) is var (mesh, paint, w, h))
        {
            if (PaintCarry.Carry(mesh, paint, w, h, v.Data, v.Paint, v.W, v.H)) v.Repaint();
            v.PaintDirty = false;
        }
        _m.TakeBackInto(v);
        _m.RefreshSelection();
        _m.Changed(mesh: true);
    }

    (MeshData, byte[], int, int)? Saved(ModelPart head)
    {
        try
        {
            var mesh = ModelImport.Obj(ArtStore.SrcPath(_m.Store, _m.Pack.Id, head.SolidMesh!));
            if (ArtStore.LoadSrc(_m.Store, _m.Pack.Id, head.SolidPaint!) is not { } pimg) return null;
            pimg.Convert(Image.Format.Rgba8);
            var d = pimg.GetData();
            for (int i = 0; i < d.Length; i += 4)
            {
                int a = d[i + 3];
                d[i] = (byte)(d[i] * a / 255);
                d[i + 1] = (byte)(d[i + 1] * a / 255);
                d[i + 2] = (byte)(d[i + 2] * a / 255);
            }
            return (mesh, d, pimg.GetWidth(), pimg.GetHeight());
        }
        catch (Exception e) when (KTable.Core.SaveStore.IsFileTrouble(e))
        {
            GD.PrintErr($"[studio] the paint on {head.Name}'s joined shape: {e.Message}");
            return null;
        }
    }

    public void Keep()
    {
        foreach (var c in _chains)
        {
            if (!_m.Views.TryGetValue(c.Key, out var v)) continue;
            c.Head.SolidRegion = v.Part.Region;
            if (!v.Paint.AsSpan().ContainsAnyExcept((byte)0)) { c.Head.SolidPaint = c.Head.SolidMesh = null; continue; }
            if (!v.PaintDirty && c.Head.SolidPaint != null && v.SavedBuild == v.Build) continue;
            v.PaintDirty = false;
            v.SavedBuild = v.Build;
            c.Head.SolidPaint = ArtStore.AddSrc(_m.Store, _m.Pack.Id, ModelBake.Straight(v.W, v.H, v.Paint));
            c.Head.SolidMesh = _m.SaveSrcText(ModelImport.ToObj(v.Data), ".obj");
        }
    }

    async Task<MeshData> Csg(ModelTree.Chain c)
    {
        var holder = new Node3D { Visible = false, Name = "Joining" };
        var sources = new List<ModelPartView>();
        var root = ChainNode(c, CsgShape3D.OperationEnum.Union, sources);
        holder.AddChild(root);
        _m.Vp.AddChild(holder);
        try
        {
            for (int i = 0; i < 2; i++) await _m.ToSignal(_m.GetTree(), SceneTree.SignalName.ProcessFrame);
            var baked = root.BakeStaticMesh();
            var m = new MeshData();
            foreach (var v in sources)
            {
                m.Tex.Add(ModelBake.Composite(v));
                m.MatColor.Add(Colors.White);
            }
            if (baked == null) return m;
            for (int s = 0; s < baked.GetSurfaceCount(); s++)
            {
                int src = baked.SurfaceGetMaterial(s) is { } mat && mat.HasMeta("src") ? mat.GetMeta("src").AsInt32() : 0;
                var a = baked.SurfaceGetArrays(s);
                var vs = a[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                var ns = a[(int)Mesh.ArrayType.Normal].VariantType == Variant.Type.Nil ? Array.Empty<Vector3>() : a[(int)Mesh.ArrayType.Normal].AsVector3Array();
                var uvs = a[(int)Mesh.ArrayType.TexUV].VariantType == Variant.Type.Nil ? Array.Empty<Vector2>() : a[(int)Mesh.ArrayType.TexUV].AsVector2Array();
                var idx = a[(int)Mesh.ArrayType.Index].VariantType == Variant.Type.Nil ? Enumerable.Range(0, vs.Length).ToArray() : a[(int)Mesh.ArrayType.Index].AsInt32Array();
                for (int i = 0; i + 2 < idx.Length; i += 3)
                {
                    for (int k = 0; k < 3; k++)
                    {
                        int q = idx[i + k];
                        m.Add(vs[q], ns.Length > q ? ns[q] : Vector3.Up, uvs.Length > q ? uvs[q] : Vector2.Zero, Colors.White);
                    }
                    m.Mat.Add(src);
                }
            }
            if (m.N.All(n => n == Vector3.Zero)) m.FaceNormals();
            return m;
        }
        finally { holder.QueueFree(); }
    }

    CsgCombiner3D ChainNode(ModelTree.Chain c, CsgShape3D.OperationEnum op, List<ModelPartView> sources)
    {
        var node = new CsgCombiner3D { Operation = op };
        for (int i = 0; i < c.Members.Count; i++)
        {
            var member = c.Members[i];
            var mop = i == 0 ? CsgShape3D.OperationEnum.Union : member.Join switch
            {
                ModelTree.Cut => CsgShape3D.OperationEnum.Subtraction,
                ModelTree.Overlap => CsgShape3D.OperationEnum.Intersection,
                _ => CsgShape3D.OperationEnum.Union,
            };
            if (MemberNode(member, mop, sources) is { } n) node.AddChild(n);
        }
        return node;
    }

    CsgShape3D? MemberNode(ModelPart p, CsgShape3D.OperationEnum op, List<ModelPartView> sources)
    {
        if (p.IsGroup)
        {
            var g = new CsgCombiner3D { Operation = op, Transform = p.Xf() };
            foreach (var inner in ModelTree.ChainsIn(_m.Doc, p)) g.AddChild(ChainNode(inner, CsgShape3D.OperationEnum.Union, sources));
            return g;
        }
        if (!_m.Views.TryGetValue(p.Id, out var v)) return null;
        var data = v.Data;
        if (p.Twin)
        {
            var twin = new MeshData { V = data.V.ToList(), N = data.N.ToList(), UV = data.UV.ToList(), C = data.C.ToList(), Mat = data.Mat.ToList(), Tex = { null }, MatColor = { Colors.White } };
            twin.Transform(p.Xf().AffineInverse() * ModelTree.Mirror * p.Xf());
            var both = new MeshData { V = data.V.ToList(), N = data.N.ToList(), UV = data.UV.ToList(), C = data.C.ToList(), Mat = data.Mat.ToList(), Tex = { null }, MatColor = { Colors.White } };
            both.V.AddRange(twin.V);
            both.N.AddRange(twin.N);
            both.UV.AddRange(twin.UV);
            both.C.AddRange(twin.C);
            both.Mat.AddRange(twin.Mat);
            data = both;
        }
        var mat = new StandardMaterial3D();
        mat.SetMeta("src", sources.Count);
        sources.Add(v);
        return new CsgMesh3D { Mesh = data.ToMesh(), Operation = op, Transform = p.Xf(), Material = mat };
    }

    (List<SdfOperand>, Aabb) SdfOperands(ModelTree.Chain c)
    {
        var ops = new List<SdfOperand>();
        Aabb? reach = null;
        foreach (var p in c.Members)
        {
            if (!_m.Views.TryGetValue(p.Id, out var v) || ShapeSdf.Unit(p) is not { } dist) continue;
            var places = p.Twin ? new[] { p.Xf(), ModelTree.Mirror * p.Xf() } : new[] { p.Xf() };
            var centre = v.Data.Bounds().GetCenter();
            var shapes = places.Select(xf => new SdfShape
            {
                ToLocal = xf.AffineInverse(), Dist = dist, Centre = centre,
                Unit = Mathf.Min(xf.Basis.Column0.Length(), Mathf.Min(xf.Basis.Column1.Length(), xf.Basis.Column2.Length())),
            }).ToList();
            foreach (var xf in places)
            {
                var box = xf * v.Data.Bounds();
                reach = reach is { } r ? r.Merge(box) : box;
            }
            var avg = ModelBake.Composite(v);
            avg.Resize(1, 1, Image.Interpolation.Bilinear);
            ops.Add(new SdfOperand { Shapes = shapes, Join = p == c.Head ? ModelTree.Add : p.Join ?? ModelTree.Add, Blend = p == c.Head ? 0 : p.Blend, Colour = avg.GetPixel(0, 0) with { A = 1 } });
        }
        if (ops.Count == 0) throw new InvalidOperationException("nothing to join");
        return (ops, reach ?? new Aabb(Vector3.Zero, Vector3.One));
    }
}
