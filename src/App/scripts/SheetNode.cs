using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Json = KTable.Core.Json;
using KTable.Core.Model;

namespace KTable.App;

public partial class SheetNode : Node3D
{
    public SheetDef Def { get; private set; } = null!;
    Assets _assets = null!;
    ReliefCache _relief = null!;
    ReliefSurface? _surface;
    int _surfaceVersion = -1;
    public int TopZ;
    public Func<SheetNode, Vector3[], bool>? Covered;
    readonly Dictionary<(int l, int x, int y), (float cx, float cz, float w, float h, float lift)> _quadBoxes = new();
    TileIndex? _index;
    string _tilesRel = "";
    int _baseLevel = -1, _detailLevel = -1;
    readonly Dictionary<(int l, int x, int y), MeshInstance3D> _quads = new();
    MeshInstance3D? _fallback, _outline;
    bool _selected;
    public int TilesLoaded { get; private set; }
    public int TilesTotal { get; private set; }
    public bool Loaded => !Def.HasPicture || _index != null && TilesLoaded >= TilesTotal;
    public int QuadCount => _quads.Count;
    public int DetailLevel => _detailLevel;
    public int CoarsestLevel => _coarsestLevel;
    int _coarsestLevel = -1;

    const int BaseSide = 2048, LeastBaseSide = 256;
    const long BasePixelsForAllSheets = 4L * BaseSide * BaseSide;
    int _baseSide = BaseSide;

    public static int BaseSideFor(int sheets)
    {
        int side = BaseSide;
        while (side > LeastBaseSide && (long)side * side * Math.Max(1, sheets) > BasePixelsForAllSheets) side /= 2;
        return side;
    }

    public static SheetNode Create(SheetDef def, Assets assets, ReliefCache relief, int sheets)
    {
        var n = new SheetNode { _assets = assets, _relief = relief, _baseSide = BaseSideFor(sheets) };
        n.UpdateDef(def);
        n._surface = new ReliefSurface();
        n.AddChild(n._surface);
        if (!def.HasPicture) return n;
        n._tilesRel = Path.Combine("tiles", def.Image);
        assets.EnsureFile(def.Package, Path.Combine(n._tilesRel, "index.json"), n.OnIndex);
        n._fallback = new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2((float)def.Size.W, (float)def.Size.H) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.82f, 0.78f, 0.68f), Roughness = 1f, Metallic = 0f, SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled },
            Position = new Vector3(0, -0.01f, 0),
        };
        n.AddChild(n._fallback);
        return n;
    }

    public void UpdateDef(SheetDef def)
    {
        bool relay = _index != null && (Math.Abs(def.Size.W - _laidW) > 1e-9 || Math.Abs(def.Size.H - _laidH) > 1e-9 || !SameCrop(def.Crop, _laidCrop));
        Def = def;
        Position = Coords.ToWorld(def.Pos, def.Z * ZStep);
        Rotation = new Vector3(0, Coords.Yaw(def.RotDeg), 0);
        if (relay) RebuildQuads();
    }

    static bool SameCrop(FaceBox? a, FaceBox? b) => a == null && b == null || a != null && b != null && a.X == b.X && a.Y == b.Y && a.W == b.W && a.H == b.H;

    double _laidW, _laidH;
    FaceBox? _laidCrop;
    void MarkLaidOut() { _laidW = Def.Size.W; _laidH = Def.Size.H; _laidCrop = Def.Crop == null ? null : new FaceBox { X = Def.Crop.X, Y = Def.Crop.Y, W = Def.Crop.W, H = Def.Crop.H }; }

    void RebuildQuads()
    {
        foreach (var q in _quads.Values) q.QueueFree();
        _quads.Clear();
        _quadBoxes.Clear();
        _textured.Clear();
        _detailLevel = -1;
        TilesLoaded = 0; TilesTotal = 0;
        if (_fallback != null) _fallback.Mesh = new PlaneMesh { Size = new Vector2((float)Def.Size.W, (float)Def.Size.H) };
        if (_index == null) return;
        var lv = _index.Levels[_baseLevel];
        for (int y = 0; y < lv.Rows; y++)
            for (int x = 0; x < lv.Cols; x++)
                EnsureQuad(_baseLevel, x, y, 0f);
        TilesTotal = _quads.Keys.Count(k => k.l == _baseLevel);
        MarkLaidOut();
        RefreshOutline();
    }

    public void SetSelected(bool on)
    {
        _selected = on;
        if (on && _outline == null)
        {
            var im = new ImmediateMesh();
            float w = (float)Def.Size.W / 2, h = (float)Def.Size.H / 2;
            var mat = new StandardMaterial3D
            {
                AlbedoColor = Def.Locked ? new Color(1, 0.8f, 0.2f) : new Color(0.2f, 1, 0.4f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                NoDepthTest = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, RenderPriority = OutlinePriority, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            };
            float t = Math.Max(0.12f, Math.Min(w, h) * 0.012f);
            var lift = Lift();
            im.SurfaceBegin(Mesh.PrimitiveType.TriangleStrip, mat);
            var corners = new[] { (-w, -h, -1f, -1f), (w, -h, 1f, -1f), (w, h, 1f, 1f), (-w, h, -1f, 1f), (-w, -h, -1f, -1f) };
            int along = _relief.Ground.Any ? OutlineSteps : 1;
            for (int k = 0; k + 1 < corners.Length; k++)
                for (int i = 0; i < along; i++)
                {
                    float f = i / (float)along;
                    var (ax, az, asx, asz) = corners[k];
                    var (bx, bz, bsx, bsz) = corners[k + 1];
                    float x = Mathf.Lerp(ax, bx, f), z = Mathf.Lerp(az, bz, f), sx = Mathf.Lerp(asx, bsx, f), sz = Mathf.Lerp(asz, bsz, f);
                    float y = lift(x, z) + OutlineAbove;
                    im.SurfaceAddVertex(new Vector3(x + sx * t, y, z + sz * t));
                    im.SurfaceAddVertex(new Vector3(x - sx * t, y, z - sz * t));
                }
            var (lx, lz, lsx, lsz) = corners[^1];
            im.SurfaceAddVertex(new Vector3(lx + lsx * t, lift(lx, lz) + OutlineAbove, lz + lsz * t));
            im.SurfaceAddVertex(new Vector3(lx - lsx * t, lift(lx, lz) + OutlineAbove, lz - lsz * t));
            im.SurfaceEnd();
            _outline = new MeshInstance3D { Mesh = im };
            AddChild(_outline);
        }
        else if (!on && _outline != null) { _outline.QueueFree(); _outline = null; }
    }

    public void RefreshOutline() { if (_selected) { SetSelected(false); SetSelected(true); } }

    const int OutlineSteps = 32;
    const float OutlineAbove = 0.05f;
    const int OutlinePriority = 10;
    public const float ZStep = 0.004f;

    Func<float, float, float> Lift() => _relief.Ground.Any ? GroundMesh.Heights(_relief.Ground, Transform) : (_, _) => 0f;

    public void Reground(Rect2? where)
    {
        if (where is { } box && !WorldBox().Intersects(box)) return;
        foreach (var (key, b) in _quadBoxes)
            if (_quads.TryGetValue(key, out var q) && (where == null || QuadBox(b).Intersects(where.Value)))
                Shape(q, b);
        RefreshSurface(true);
        RefreshOutline();
    }

    Rect2 WorldBox() => QuadBox((0, 0, (float)Def.Size.W, (float)Def.Size.H, 0));

    Rect2 QuadBox((float cx, float cz, float w, float h, float lift) b)
    {
        var xf = Transform;
        var first = xf * new Vector3(b.cx - b.w / 2, 0, b.cz - b.h / 2);
        var r = new Rect2(new Vector2(first.X, first.Z), Vector2.Zero);
        foreach (var (sx, sz) in new[] { (1, -1), (1, 1), (-1, 1) })
        {
            var p = xf * new Vector3(b.cx + sx * b.w / 2, 0, b.cz + sz * b.h / 2);
            r = r.Expand(new Vector2(p.X, p.Z));
        }
        return r;
    }

    public void RefreshSurface(bool force = false)
    {
        if (_surface == null) return;
        var grid = _relief.Grid(Def);
        if (grid == null && Def.HasPicture) { _surface.Clear(); _surfaceVersion = -1; return; }
        if (!force && _surfaceVersion == _relief.Version) return;
        _surfaceVersion = _relief.Version;
        _surface.Build(Def, grid ?? Bare, _relief.Ground, Transform, (TopZ + 1 - Def.Z) * ZStep, Covered == null ? null : corners => Covered(this, corners));
    }

    static readonly ReliefGrid Bare = new(2, 2);

    void Shape(MeshInstance3D q, (float cx, float cz, float w, float h, float lift) b)
    {
        var g = _relief.Ground;
        if (!g.Any)
        {
            q.Mesh = new PlaneMesh { Size = new Vector2(b.w, b.h) };
            q.Position = new Vector3(b.cx, b.lift, b.cz);
            return;
        }
        var xf = Transform;
        double cell = double.PositiveInfinity;
        foreach (var (sx, sz) in new[] { (0f, 0f), (-0.5f, -0.5f), (0.5f, -0.5f), (0.5f, 0.5f), (-0.5f, 0.5f) })
            cell = Math.Min(cell, g.CellSize(Coords.FromWorld(xf * new Vector3(b.cx + sx * b.w, 0, b.cz + sz * b.h))));
        int n = double.IsFinite(cell) ? Math.Clamp((int)Math.Ceiling(Math.Max(b.w, b.h) / cell), 1, GroundMesh.MostCellsPerTile) : 1;
        float longest = Math.Max(b.w, b.h);
        int nx = Math.Max(1, (int)Math.Round(n * b.w / longest)), nz = Math.Max(1, (int)Math.Round(n * b.h / longest));
        q.Mesh = GroundMesh.Grid(GroundMesh.Heights(g, xf), b.cx - b.w / 2, b.cz - b.h / 2, b.cx + b.w / 2, b.cz + b.h / 2, nx, nz, Vector2.Zero, Vector2.One, b.lift, longest / Math.Max(nx, nz) * SkirtCells);
        q.Position = Vector3.Zero;
    }

    void OnIndex()
    {
        try { _index = Json.Load<TileIndex>(_assets.Abs(Def.Package, Path.Combine(_tilesRel, "index.json"))); }
        catch (Exception e) { GD.PushError($"sheet {Def.Id}: bad tile index: {e.Message}"); return; }
        _baseLevel = _index.Levels.Count - 1;
        for (int i = 0; i < _index.Levels.Count; i++)
            if (_index.Levels[i].W <= _baseSide && _index.Levels[i].H <= _baseSide) { _baseLevel = i; break; }
        var lv = _index.Levels[_baseLevel];
        for (int y = 0; y < lv.Rows; y++)
            for (int x = 0; x < lv.Cols; x++)
                EnsureQuad(_baseLevel, x, y, 0f);
        TilesTotal = _quads.Keys.Count(k => k.l == _baseLevel);
        MarkLaidOut();
    }

    FaceBox Crop => Def.Crop ?? SheetGeometry.Whole;

    const int MaxLeaves = 600;

    float Density(int level)
    {
        var c = Crop;
        return (float)(_index!.Levels[level].W * c.W / Def.Size.W);
    }

    const float DetailLift = 0.002f;
    const float SkirtCells = 1.5f;
    readonly HashSet<(int l, int x, int y)> _textured = new();
    public bool LodDue { get; private set; }

    static (int l, int x, int y) Parent((int l, int x, int y) t) => (t.l + 1, t.x >> 1, t.y >> 1);

    internal void UpdateLod(LodView view, ref int fresh)
    {
        if (_index == null || _baseLevel < 0) return;
        LodDue = false;
        var xf = GlobalTransform;
        var leaves = new HashSet<(int l, int x, int y)>();
        var refined = new HashSet<(int l, int x, int y)>();
        var open = new PriorityQueue<(int l, int x, int y), float>();
        int count = 0;

        void Consider(int l, int x, int y)
        {
            if (!TileBox(l, x, y, out var lo, out var hi)) return;
            if (!InView(view, xf, lo, hi)) return;
            count++;
            if (l > 0)
            {
                var mid = xf * new Vector3((lo.X + hi.X) / 2, 0, (lo.Y + hi.Y) / 2);
                float need = view.PixelsPerUnitAt(mid), have = Density(l);
                if (need > have) { open.Enqueue((l, x, y), -need / have); return; }
            }
            leaves.Add((l, x, y));
        }

        var bl = _index.Levels[_baseLevel];
        for (int y = 0; y < bl.Rows; y++)
            for (int x = 0; x < bl.Cols; x++)
                Consider(_baseLevel, x, y);
        while (open.Count > 0 && count + 3 <= MaxLeaves)
        {
            var t = open.Dequeue();
            count--;
            refined.Add(t);
            for (int dy = 0; dy < 2; dy++)
                for (int dx = 0; dx < 2; dx++)
                    Consider(t.l - 1, t.x * 2 + dx, t.y * 2 + dy);
        }
        foreach (var t in open.UnorderedItems) leaves.Add(t.Element);

        var keep = new HashSet<(int, int, int)>();
        int finest = _baseLevel, coarsest = -1;
        foreach (var t in leaves)
        {
            if (t.l >= _baseLevel) continue;
            if (!_quads.ContainsKey(t))
            {
                if (fresh <= 0) { LodDue = true; continue; }
                fresh--;
            }
            keep.Add(t);
            finest = Math.Min(finest, t.l);
            coarsest = Math.Max(coarsest, t.l);
            EnsureQuad(t.l, t.x, t.y, DetailLift * (_baseLevel - t.l));
            _assets.Touch(Def.Package, TileRel(t.l, t.x, t.y));
        }
        var unfinished = new HashSet<(int l, int x, int y)>();
        foreach (var t in leaves)
            if (!_textured.Contains(t))
                for (var a = Parent(t); a.l <= _baseLevel && unfinished.Add(a); a = Parent(a)) { }
        foreach (var a in unfinished)
            if (a.l < _baseLevel && _quads.ContainsKey(a)) keep.Add(a);
        _detailLevel = finest < _baseLevel ? finest : -1;
        _coarsestLevel = coarsest;
        var drop = new List<(int, int, int)>();
        foreach (var k in _quads.Keys) if (k.l != _baseLevel && !keep.Contains(k)) drop.Add(k);
        foreach (var k in drop) { _quads[k].QueueFree(); _quads.Remove(k); _quadBoxes.Remove(k); _textured.Remove(k); }
        foreach (var (k, q) in _quads)
            if (k.l == _baseLevel) q.Visible = !refined.Contains(k) || unfinished.Contains(k);
        for (int y = 0; y < bl.Rows; y++)
            for (int x = 0; x < bl.Cols; x++)
                _assets.Touch(Def.Package, TileRel(_baseLevel, x, y));
    }

    bool TileBox(int l, int x, int y, out Vector2 lo, out Vector2 hi)
    {
        lo = hi = Vector2.Zero;
        if (_index == null || l < 0 || l >= _index.Levels.Count) return false;
        var lv = _index.Levels[l];
        if (x < 0 || y < 0 || x >= lv.Cols || y >= lv.Rows) return false;
        int ts = _index.TileSize;
        int pw = Math.Min(ts, lv.W - x * ts), ph = Math.Min(ts, lv.H - y * ts);
        if (pw <= 0 || ph <= 0) return false;
        var c = Crop;
        double u0 = Math.Max(x * ts / (double)lv.W, c.X), u1 = Math.Min((x * ts + pw) / (double)lv.W, c.X + c.W);
        double v0 = Math.Max(y * ts / (double)lv.H, c.Y), v1 = Math.Min((y * ts + ph) / (double)lv.H, c.Y + c.H);
        if (u1 - u0 <= 1e-9 || v1 - v0 <= 1e-9) return false;
        float W = (float)Def.Size.W, H = (float)Def.Size.H;
        lo = new Vector2((float)((u0 - c.X) / c.W * W - W / 2), (float)((v0 - c.Y) / c.H * H - H / 2));
        hi = new Vector2((float)((u1 - c.X) / c.W * W - W / 2), (float)((v1 - c.Y) / c.H * H - H / 2));
        return true;
    }

    bool InView(LodView view, Transform3D xf, Vector2 lo, Vector2 hi)
    {
        var box = new Aabb(xf * new Vector3(lo.X, 0, lo.Y), Vector3.Zero)
            .Expand(xf * new Vector3(hi.X, 0, lo.Y))
            .Expand(xf * new Vector3(hi.X, 0, hi.Y))
            .Expand(xf * new Vector3(lo.X, 0, hi.Y))
            .Grow((hi - lo).Length() * 0.5f + (float)_relief.Ground.Highest);
        return view.Sees(box);
    }

    string TileRel(int l, int x, int y) => Path.Combine(_tilesRel, l.ToString(), $"{x}_{y}.{_index!.Ext}");

    void EnsureQuad(int l, int x, int y, float yOff)
    {
        if (_quads.ContainsKey((l, x, y)) || _index == null) return;
        var lv = _index.Levels[l];
        int ts = _index.TileSize;
        int pw = Math.Min(ts, lv.W - x * ts), ph = Math.Min(ts, lv.H - y * ts);
        var c = Crop;
        double u0 = x * ts / (double)lv.W, u1 = (x * ts + pw) / (double)lv.W, v0 = y * ts / (double)lv.H, v1 = (y * ts + ph) / (double)lv.H;
        double ku0 = Math.Max(u0, c.X), ku1 = Math.Min(u1, c.X + c.W), kv0 = Math.Max(v0, c.Y), kv1 = Math.Min(v1, c.Y + c.H);
        if (ku1 - ku0 <= 1e-6 || kv1 - kv0 <= 1e-6) return;
        float W = (float)Def.Size.W, H = (float)Def.Size.H;
        float qw = (float)((ku1 - ku0) / c.W) * W, qh = (float)((kv1 - kv0) / c.H) * H;
        float cx = (float)(((ku0 + ku1) / 2 - c.X) / c.W) * W - W / 2, cz = (float)(((kv0 + kv1) / 2 - c.Y) / c.H) * H - H / 2;
        var mat = new StandardMaterial3D
        {
            Roughness = 1f, Metallic = 0f, SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
            TextureRepeat = false,
            AlbedoColor = new Color(1, 1, 1, 0),
            Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor,
            Uv1Scale = new Vector3((float)((ku1 - ku0) / (u1 - u0)), (float)((kv1 - kv0) / (v1 - v0)), 1),
            Uv1Offset = new Vector3((float)((ku0 - u0) / (u1 - u0)), (float)((kv0 - v0) / (v1 - v0)), 0),
        };
        var q = new MeshInstance3D { MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        var box = (cx, cz, qw, qh, yOff);
        Shape(q, box);
        AddChild(q);
        _quads[(l, x, y)] = q;
        _quadBoxes[(l, x, y)] = box;
        var tex = _assets.Texture(Def.Package, TileRel(l, x, y), t =>
        {
            if (!_quads.TryGetValue((l, x, y), out var now) || now != q) return;
            Textured(l, x, y, mat, t);
            if (l == _baseLevel && _fallback != null) { _fallback.QueueFree(); _fallback = null; }
        }, quiet: l != _baseLevel);
        if (tex != null) Textured(l, x, y, mat, tex);
    }

    void Textured(int l, int x, int y, StandardMaterial3D mat, Texture2D tex)
    {
        mat.AlbedoTexture = tex;
        mat.AlbedoColor = Colors.White;
        _textured.Add((l, x, y));
        if (l == _baseLevel) TilesLoaded++;
        else LodDue = true;
    }

    public bool ContainsWorld(Vector3 world)
    {
        var l = ToLocal(world);
        return Mathf.Abs(l.X) <= Def.Size.W / 2 && Mathf.Abs(l.Z) <= Def.Size.H / 2;
    }
}
