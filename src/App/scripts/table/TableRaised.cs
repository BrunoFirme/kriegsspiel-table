using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Godot;
using KTable.Client;
using KTable.Core.Model;

namespace KTable.App;

sealed class TableRaised
{
    const double SettleSeconds = 0.3;
    const float ChunkM = 160, FadeShare = 0.12f, FloorM = 0.15f, SinkM = 0.3f, TurnStep = 10, TurnProbe = 0.01f, BlockCellM = 40, CameraMarginM = 1.5f;
    const double BudgetMs = 4, LayoutEvery = 0.5;
    const float NearBarePx = 2, BareSpanPx = 12000, GateNearM = 20, LampPick = 0.37f;

    readonly TableContext _c;
    readonly SheetLayer _sheets;
    readonly TableLamps _lamps;
    readonly Dictionary<(int, int), RaisedChunk> _chunks = new();
    IEnumerator<bool>? _work;
    Node3D? _next;
    bool _due = true, _shown;
    double _wait, _layoutWait;
    float _reachShown = -1, _glowShown = -1;
    int _laidOn;
    Stopwatch? _took;
    double _workedMs;
    int _joined;

    static ShaderMaterial? _material, _plainMaterial, _treeMaterial;
    readonly List<RaisedChunk.Drawn> _drawn = new();
    readonly Dictionary<(int, int), List<(Vector3[] Ring, float Top)>> _blockers = new();
    readonly Dictionary<(int, int), List<(Vector3[] Ring, float Top)>> _nextBlockers = new();
    readonly List<RaisedChunk.Drawn> _nextDrawn = new();

    public readonly Node3D Root = new() { Name = "Raised" };

    public static int Buildings { get; private set; }

    public static int Trees { get; private set; }

    public static bool Busy { get; private set; }

    public TableRaised(TableContext c, SheetLayer sheets, TableLamps lamps) => (_c, _sheets, _lamps) = (c, sheets, lamps);

    public static ShaderMaterial Material => _material ??= Textured(new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/raised.gdshader") });

    static ShaderMaterial PlainMaterial => _plainMaterial ??= Plained((ShaderMaterial)Material.Duplicate());

    static ShaderMaterial Plained(ShaderMaterial material)
    {
        material.SetShaderParameter("plain", true);
        return material;
    }

    static ShaderMaterial Textured(ShaderMaterial material)
    {
        RaisedTextures.Start();
        return material;
    }

    public static ShaderMaterial TreeMaterial => _treeMaterial ??= new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/raised_tree.gdshader") };

    public void Due() => _due = true;

    public void Reground(Rect2? where) => Due();

    public void Tick(double delta)
    {
        _wait -= delta;
        if (_due && _wait <= 0)
        {
            _due = false;
            _wait = SettleSeconds;
            Start();
        }
        if (_work != null) Step();
        if (_material != null) RaisedTextures.Lay(_material, PlainMaterial);
        if (GroundPhotos.Lay() && GroundTiles.Raised.Photo) GroundTiles.StaleEverything();
        _layoutWait -= delta;
        if (_layoutWait <= 0 && _work == null)
        {
            _layoutWait = LayoutEvery;
            if (_c.S.View.Lighting.Raised && Layout() != _laidOn) Due();
        }
        float reach = Reach(), glow = _lamps.Glow;
        if (reach != _reachShown) Show(reach);
        if (glow != _glowShown)
        {
            _glowShown = glow;
            Material.SetShaderParameter("glow", glow);
            PlainMaterial.SetShaderParameter("glow", glow);
        }
    }

    float Reach()
    {
        var view = _c.S.View;
        float host = view.Lighting.RaisedReachM > 0 ? (float)view.Lighting.RaisedReachM : float.PositiveInfinity;
        float mine = _c.Settings.RaisedReachM >= Settings.RaisedReachOff ? float.PositiveInfinity : _c.Settings.RaisedReachM;
        return Mathf.Min(host, mine);
    }

    void Show(float reach)
    {
        _reachShown = reach;
        float upm = Upm;
        foreach (var drawn in _drawn)
        {
            if (!GodotObject.IsInstanceValid(drawn.Node)) continue;
            float end = Mathf.Min(reach, drawn.EndM);
            bool handedOn = drawn.Handed && end >= drawn.EndM;
            drawn.Node.VisibilityRangeBegin = drawn.BeginM * upm;
            drawn.Node.VisibilityRangeBeginMargin = drawn.Handed ? 0 : drawn.BeginM * upm * FadeShare;
            drawn.Node.VisibilityRangeEnd = float.IsPositiveInfinity(end) ? 0 : end * upm;
            drawn.Node.VisibilityRangeEndMargin = float.IsPositiveInfinity(end) || handedOn ? 0 : end * upm * FadeShare;
            drawn.Node.VisibilityRangeFadeMode = drawn.Handed ? GeometryInstance3D.VisibilityRangeFadeModeEnum.Disabled : GeometryInstance3D.VisibilityRangeFadeModeEnum.Self;
        }
        Underfoot(reach);
    }

    void Underfoot(float reach)
    {
        if (!_shown)
        {
            GroundTiles.Raise(BuiltLayer.Raising.None);
            return;
        }
        GroundPhotos.Start();
        GroundTiles.Raise(new BuiltLayer.Raising(float.IsPositiveInfinity(reach) ? NearBarePx : Mathf.Max(NearBarePx, BareSpanPx / reach), true));
    }

    float Upm => (float)(_c.S.View.Calibration.UnitsPerMeter ?? 0);

    int Layout()
    {
        var hash = new System.HashCode();
        var hidden = _sheets.Hidden;
        var view = _c.S.View;
        hash.Add(view.Lighting.Raised);
        hash.Add(view.Lighting.MapStyle);
        hash.Add(view.Lighting.SnowLine);
        foreach (var sheet in view.Sheets)
        {
            hash.Add(sheet.Id);
            hash.Add(hidden.Contains(sheet.Id));
            hash.Add(BuiltGeometry.ToWorld(sheet, new SheetPoint(0, 0)));
            hash.Add(BuiltGeometry.ToWorld(sheet, new SheetPoint(BuiltGeometry.PerUnit, BuiltGeometry.PerUnit)));
        }
        return hash.ToHashCode();
    }

    void Start()
    {
        _laidOn = Layout();
        _next?.QueueFree();
        _next = null;
        _chunks.Clear();
        _nextDrawn.Clear();
        _nextBlockers.Clear();
        _work = null;
        var view = _c.S.View;
        if (!view.Lighting.Raised || Upm <= 0)
        {
            Swap(null);
            Buildings = 0;
            Trees = 0;
            return;
        }
        _next = new Node3D { Name = "Raised" };
        _took = Stopwatch.StartNew();
        _workedMs = 0;
        _joined = 0;
        _work = Work().GetEnumerator();
        Busy = true;
    }

    void Step()
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed.TotalMilliseconds < BudgetMs)
        {
            if (_work!.MoveNext()) continue;
            _workedMs += clock.Elapsed.TotalMilliseconds;
            _work = null;
            Finish();
            return;
        }
        _workedMs += clock.Elapsed.TotalMilliseconds;
    }

    void Finish()
    {
        var upload = Stopwatch.StartNew();
        Buildings = _chunks.Values.Sum(c => c.Buildings);
        foreach (var chunk in _chunks.Values)
            foreach (var blocker in chunk.Blockers) Block(_nextBlockers, blocker);
        Trees = _chunks.Values.Sum(c => c.Trees);
        int near = _chunks.Values.Sum(c => c.NearTriangles), plain = _chunks.Values.Sum(c => c.PlainTriangles);
        Swap(_next);
        _next = null;
        GD.Print($"[raised] {Buildings} buildings ({_joined} walls joined to a neighbour, {near} triangles near, {plain} far) and {Trees} trees in {_chunks.Count} pieces, "
            + $"{_took?.ElapsedMilliseconds ?? 0} ms ({_workedMs:0} ms working), {upload.ElapsedMilliseconds} ms of it in one frame");
        _chunks.Clear();
        _reachShown = -1;
        Busy = false;
    }

    void Swap(Node3D? next)
    {
        foreach (var old in Root.GetChildren()) old.QueueFree();
        _drawn.Clear();
        _blockers.Clear();
        foreach (var (key, list) in _nextBlockers) _blockers[key] = list;
        _nextBlockers.Clear();
        _shown = next != null;
        if (next != null)
        {
            Root.AddChild(next);
            _drawn.AddRange(_nextDrawn);
        }
        _nextDrawn.Clear();
        _reachShown = -1;
    }

    IEnumerable<bool> Work()
    {
        var s = _c.S;
        var view = s.View;
        float upm = Upm;
        var sheets = view.Sheets.ToDictionary(sh => sh.Id);
        var hidden = _sheets.Hidden;
        string style = view.Lighting.MapStyle;
        float chunk = ChunkM * upm, snow = (float)ContourLines.SnowLineFor(view, view.Lighting);
        int epoch = Manners.Age(style);
        RaisedChunk.SnowY = snow > 0 ? _c.SheetsTop + snow : float.PositiveInfinity;
        foreach (var material in new[] { Material, PlainMaterial, TreeMaterial }) material.SetShaderParameter("ground_top", _c.SheetsTop);
        var bySheet = new Dictionary<string, List<BuiltPainter.Shape>>();
        foreach (var thing in view.Sheets.Where(sh => !hidden.Contains(sh.Id)).SelectMany(sh => s.Relief.Seen(sh)).ToList())
        {
            if (!sheets.TryGetValue(thing.Sheet, out var sheet)) continue;
            var shapes = BuiltPainter.ShapesOf(thing, Ground.FullWidth(sheet), BuiltGeometry.FullHeight(sheet), upm);
            if (!bySheet.TryGetValue(sheet.Id, out var kept)) bySheet[sheet.Id] = kept = new();
            kept.AddRange(shapes);
            var worlds = shapes.Select(shape => shape.Uv.Select(uv => Ground.FromUv(sheet, uv.X, uv.Y)).Select(p => Coords.ToWorld(p, _c.SheetsTop + (float)s.Ground.DrawnAt(p))).ToArray()).ToList();
            var gaps = RaisedRows.Of(shapes.Select((shape, i) => shape.Kind == PartKinds.Block && worlds[i].Length == 4 ? worlds[i] : null).ToList(), upm);
            _joined += gaps.Sum(g => g?.Count(e => !float.IsNaN(e)) ?? 0);
            var opens = RaisedRows.Open(shapes.Select((shape, i) => shape.Kind == PartKinds.Block && worlds[i].Length == 4 ? worlds[i] : null).ToList(), upm);
            var gates = shapes.Select((shape, i) => (shape, i)).Where(g => g.shape.Kind == PartKinds.Block && g.shape.Class == "gatehouse").Select(g => RaisedFrame.Middle(worlds[g.i])).ToList();
            for (int i = 0; i < shapes.Count; i++)
            {
                var world = worlds[i];
                bool gated = shapes[i].Class == "bridge" && epoch is Manners.Medieval or Manners.Later && gates.Any(g => ((g - world[0]) with { Y = 0 }).Length() < GateNearM * upm);
                Into(world[0], chunk).Add(shapes[i], world, style, s.Ground, _c.SheetsTop, upm, Across(sheet, shapes[i]), gaps[i], opens[i], gated);
                yield return true;
            }
            foreach (var lamp in Lamps.Of(thing, epoch, upm))
            {
                var on = BuiltGeometry.ToWorld(sheet, lamp.At.Stored);
                var at = Coords.ToWorld(on, _c.SheetsTop + (float)s.Ground.DrawnAt(on));
                Into(at, chunk).Lamp(lamp.Kind, at, upm, Mathf.PosMod(at.X * LampPick + at.Z, 1));
            }
        }
        foreach (var sheet in view.Sheets)
        {
            if (hidden.Contains(sheet.Id)) continue;
            var shapes = bySheet.TryGetValue(sheet.Id, out var kept) ? kept : new List<BuiltPainter.Shape>();
            foreach (var done in RaisedForest.Plant(sheet, s.Ground, _c.SheetsTop, upm, chunk, _chunks, shapes, Dominant(shapes, style))) yield return done;
        }
        foreach (var piece in _chunks.Values)
            foreach (var drawn in piece.Nodes(Material, PlainMaterial, TreeMaterial, upm))
            {
                _next!.AddChild(drawn.Node);
                _nextDrawn.Add(drawn);
                yield return true;
            }
    }

    RaisedChunk Into(Vector3 at, float chunk)
    {
        var key = ((int)Mathf.Floor(at.X / chunk), (int)Mathf.Floor(at.Z / chunk));
        if (!_chunks.TryGetValue(key, out var into)) _chunks[key] = into = new RaisedChunk(new Vector3((key.Item1 + 0.5f) * chunk, 0, (key.Item2 + 0.5f) * chunk));
        return into;
    }

    static int Dominant(List<BuiltPainter.Shape> shapes, string? style)
    {
        var counts = shapes.Where(sh => sh.Kind == PartKinds.Block).GroupBy(sh => RaisedLooks.Aged(style, sh.Manner)).OrderByDescending(g => g.Count()).FirstOrDefault();
        return counts?.Key ?? Manners.For(Manners.Age(style), Manners.Continental);
    }

    static Vector3 Across(SheetDef sheet, BuiltPainter.Shape shape)
    {
        if (shape.Kind != PartKinds.Sign) return Vector3.Right;
        float a = Mathf.DegToRad(shape.Turn / TurnStep);
        double w = Ground.FullWidth(sheet), h = BuiltGeometry.FullHeight(sheet);
        var uv = shape.Uv[0];
        var from = Coords.ToWorld(Ground.FromUv(sheet, uv.X, uv.Y));
        var to = Coords.ToWorld(Ground.FromUv(sheet, uv.X + Mathf.Cos(a) / w * TurnProbe, uv.Y + Mathf.Sin(a) / h * TurnProbe));
        return (to - from).Normalized();
    }

    (int, int) Cell(float x, float z)
    {
        float cell = BlockCellM * Mathf.Max(Upm, 1e-6f);
        return ((int)Mathf.Floor(x / cell), (int)Mathf.Floor(z / cell));
    }

    void Block(Dictionary<(int, int), List<(Vector3[] Ring, float Top)>> into, (Vector3[] Ring, float Top) blocker)
    {
        float margin = CameraMarginM * Upm;
        float x0 = blocker.Ring.Min(p => p.X) - margin, x1 = blocker.Ring.Max(p => p.X) + margin, z0 = blocker.Ring.Min(p => p.Z) - margin, z1 = blocker.Ring.Max(p => p.Z) + margin;
        var (a, b) = Cell(x0, z0);
        var (c, d) = Cell(x1, z1);
        for (int x = a; x <= c; x++)
            for (int z = b; z <= d; z++)
            {
                if (!into.TryGetValue((x, z), out var list)) into[(x, z)] = list = new();
                list.Add(blocker);
            }
    }

    public float? Ceiling(Vector3 at)
    {
        if (_blockers.Count == 0 || !_blockers.TryGetValue(Cell(at.X, at.Z), out var list)) return null;
        float margin = CameraMarginM * Upm;
        float? top = null;
        foreach (var (ring, roof) in list)
            if (Near(ring, at, margin) && (top == null || roof > top)) top = roof;
        return top + CameraMarginM * Upm;
    }

    static bool Near(Vector3[] ring, Vector3 at, float margin)
    {
        bool inside = false;
        float nearest = float.MaxValue;
        for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
        {
            Vector2 a = new(ring[i].X, ring[i].Z), b = new(ring[j].X, ring[j].Z), p = new(at.X, at.Z);
            if (a.Y > p.Y != b.Y > p.Y && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
            var ab = b - a;
            float t = Mathf.Clamp((p - a).Dot(ab) / Mathf.Max(ab.LengthSquared(), 1e-12f), 0, 1);
            nearest = Mathf.Min(nearest, (a + ab * t - p).Length());
        }
        return inside || nearest <= margin;
    }

    public static Vector3 Footing(Vector3[] world, float upm, out float bottom, int foot = -1)
    {
        float high = world.Max(p => p.Y), low = world.Min(p => p.Y);
        float level = foot switch
        {
            (int)KTable.Core.Model.Footing.Pad => low + (high - low) * (float)Footings.IntoSlope,
            (int)KTable.Core.Model.Footing.RoofYard => low,
            _ => high,
        };
        bottom = foot == (int)KTable.Core.Model.Footing.Stilts ? level : low - SinkM * upm;
        return new Vector3(0, level + FloorM * upm, 0);
    }
}
