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
    const float ChunkM = 160, FadeShare = 0.12f, FloorM = 0.15f, SinkM = 0.3f, TurnStep = 10, TurnProbe = 0.01f, BlockCellM = 40, CameraMarginM = 1.5f, HitStepM = 0.5f, HitGrownM = 0.5f;
    const double BudgetMs = 4, LayoutEvery = 0.5;
    const float DirtyReachM = 48, DirtyCells = 2;
    const int MostAgain = 4096;
    const float NearBarePx = 2, BareSpanPx = 12000, LampPick = 0.37f;

    readonly TableContext _c;
    readonly SheetLayer _sheets;
    readonly TableLamps _lamps;
    readonly Dictionary<(int, int), RaisedChunk> _chunks = new();
    IEnumerator<bool>? _work;
    Node3D? _next, _showingRoot;
    bool _due = true, _shown, _whole = true, _changed;
    double _wait, _layoutWait;
    float _reachShown = -1, _detailShown = -1, _glowShown = -1;
    int _laidOn;
    Stopwatch? _took;
    double _workedMs;
    int _joined;

    static ShaderMaterial? _material, _plainMaterial, _treeMaterial;
    readonly RaisedShown _showing = new();
    readonly RaisedChanges _changes = new();
    readonly Dictionary<string, int> _dominant = new();
    readonly Dictionary<(int, int), List<(Vector3[] Ring, float Top)>> _blockers = new();
    readonly Dictionary<(int, int), List<RaisedChunk.Drawn>> _nextPieces = new();
    readonly Dictionary<(int, int), List<RaisedChunk.Drawn>> _nextGroves = new();
    readonly List<Node> _loose = new();
    HashSet<(int, int)>? _only;
    int _nextSpan = 1;
    List<RaisedChanges.Grid> _nextGrids = new();
    Dictionary<string, (long Key, Rect2 Box)> _nextThings = new();
    Dictionary<string, int> _nextDominant = new();

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
        float reach = Reach(), detail = Detail(), glow = _lamps.Glow;
        if (reach != _reachShown || detail != _detailShown) Show(reach, detail);
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

    float Detail() => Mathf.Clamp(_c.Settings.RaisedDetailM, Settings.RaisedDetailLeast, Settings.RaisedDetailMost) / Settings.RaisedDetailUsual;

    internal readonly record struct Shown(float BeginM, float BeginMarginM, float EndM, float EndMarginM);

    internal static Shown ShownOf(RaisedChunk.Drawn drawn, float reach, float detail)
    {
        float begin = drawn.BeginM * detail, last = drawn.EndM * detail, end = Mathf.Min(reach, last);
        bool handedOn = drawn.Handed && end >= last;
        return new Shown(begin, drawn.Handed ? 0 : begin * FadeShare, float.IsPositiveInfinity(end) ? 0 : end, float.IsPositiveInfinity(end) || handedOn ? 0 : end * FadeShare);
    }

    void Show(float reach, float detail)
    {
        _reachShown = reach;
        _detailShown = detail;
        float upm = Upm;
        foreach (var drawn in _showing.Drawn)
        {
            if (!GodotObject.IsInstanceValid(drawn.Node)) continue;
            var shown = ShownOf(drawn, reach, detail);
            drawn.Node.VisibilityRangeBegin = shown.BeginM * upm;
            drawn.Node.VisibilityRangeBeginMargin = shown.BeginMarginM * upm;
            drawn.Node.VisibilityRangeEnd = shown.EndM * upm;
            drawn.Node.VisibilityRangeEndMargin = shown.EndMarginM * upm;
            drawn.Node.VisibilityRangeFadeMode = drawn.Handed ? GeometryInstance3D.VisibilityRangeFadeModeEnum.Disabled : GeometryInstance3D.VisibilityRangeFadeModeEnum.Self;
            HandOver(drawn, shown, reach, detail, upm);
        }
        Underfoot(reach);
    }

    static void HandOver(RaisedChunk.Drawn drawn, Shown shown, float reach, float detail, float upm)
    {
        if (!drawn.Handed) return;
        if (drawn.BeginM > 0)
        {
            if (reach < shown.BeginM) return;
            drawn.Node.VisibilityRangeBeginMargin = shown.BeginM * FadeShare * upm;
            drawn.Node.VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Dependencies;
            return;
        }
        bool crossed = drawn.Under is { } far && GodotObject.IsInstanceValid(far) && far.IsInsideTree() && drawn.Node.IsInsideTree() && reach >= drawn.EndM * detail;
        drawn.Node.VisibilityParent = crossed ? drawn.Node.GetPathTo(drawn.Under!) : new NodePath();
        if (!crossed) return;
        drawn.Node.VisibilityRangeEnd = 0;
        drawn.Node.VisibilityRangeEndMargin = 0;
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

    float Upm => (float)(_c.S.View.BuiltPerMetre ?? 0);

    const float MostChunksAcross = 128;

    float Chunk => Mathf.Max(ChunkM * Upm, _c.S.View.Sheets.Select(s => (float)System.Math.Max(s.Size.W, s.Size.H)).DefaultIfEmpty(0).Max() / MostChunksAcross);

    int Layout() => LayoutOf(_c.S.View, _sheets.Hidden);

    internal static int LayoutOf(GameState view, IReadOnlySet<string> hidden)
    {
        var hash = new System.HashCode();
        hash.Add(view.Lighting.Raised);
        hash.Add(view.BuiltPerMetre);
        hash.Add(view.Lighting.MapStyle);
        hash.Add(view.Lighting.SnowLine);
        foreach (var sheet in view.Sheets)
        {
            hash.Add(sheet.Id);
            hash.Add(hidden.Contains(sheet.Id));
            hash.Add(sheet.Relief);
            hash.Add(sheet.Height);
            hash.Add(BuiltGeometry.ToWorld(sheet, new SheetPoint(0, 0)));
            hash.Add(BuiltGeometry.ToWorld(sheet, new SheetPoint(BuiltGeometry.PerUnit, BuiltGeometry.PerUnit)));
        }
        return hash.ToHashCode();
    }

    void Start()
    {
        int layout = Layout();
        if (layout != _laidOn) _whole = true;
        _laidOn = layout;
        Discard();
        var view = _c.S.View;
        if (!view.Lighting.Raised || Upm <= 0)
        {
            Swap(null);
            _changes.Forget();
            _whole = true;
            Buildings = 0;
            Trees = 0;
            return;
        }
        bool whole = _whole || !_shown || !_changes.Known;
        _whole = false;
        if (whole) _next = new Node3D { Name = "Raised" };
        _took = Stopwatch.StartNew();
        _workedMs = 0;
        _joined = 0;
        _work = Work().GetEnumerator();
        Busy = true;
    }

    void Discard()
    {
        _next?.QueueFree();
        _next = null;
        foreach (var node in _loose)
            if (GodotObject.IsInstanceValid(node)) node.QueueFree();
        _loose.Clear();
        _chunks.Clear();
        _nextPieces.Clear();
        _nextGroves.Clear();
        _only = null;
        _changed = false;
        _work = null;
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
        Busy = false;
        if (!_changed)
        {
            Discard();
            return;
        }
        var upload = Stopwatch.StartNew();
        bool whole = _next != null;
        if (whole)
        {
            Swap(_next);
            _next = null;
            _showing.Span = _nextSpan;
        }
        else
        {
            foreach (var key in _only!) _showing.Drop(key);
            foreach (var group in _only.Select(k => RaisedGroves.Group(k, _showing.Span)).Distinct()) _showing.DropGrove(group);
            foreach (var node in _loose) _showingRoot!.AddChild(node);
        }
        foreach (var (key, drawn) in _nextPieces)
        {
            var chunk = _chunks.GetValueOrDefault(key);
            _showing.Put(key, new RaisedShown.Piece(drawn, chunk?.Buildings ?? 0, chunk?.Trees ?? 0, chunk?.NearTriangles ?? 0, chunk?.PlainTriangles ?? 0, chunk?.Blockers ?? new()));
        }
        foreach (var (group, drawn) in _nextGroves) _showing.PutGrove(group, drawn);
        _blockers.Clear();
        foreach (var blocker in _showing.Blockers) Block(_blockers, blocker);
        _changes.Keep(_nextGrids, _nextThings);
        _dominant.Clear();
        foreach (var (sheet, culture) in _nextDominant) _dominant[sheet] = culture;
        (Buildings, Trees) = (_showing.Buildings, _showing.Trees);
        GD.Print($"[raised] {Buildings} buildings ({_joined} walls joined to a neighbour, {_showing.Near} triangles near, {_showing.Plain} far) and {Trees} trees in {_showing.Pieces} pieces, "
            + $"{(whole ? "all" : _only!.Count + " pieces")} laid again, {_showing.Drawn.Count()} drawn, {_took?.ElapsedMilliseconds ?? 0} ms ({_workedMs:0} ms working), "
            + $"{upload.ElapsedMilliseconds} ms of it in one frame");
        _loose.Clear();
        Discard();
        _reachShown = -1;
        if (_showing.Batches > RaisedGroves.Budget) Again();
    }

    void Again()
    {
        _whole = true;
        _due = true;
    }

    void Swap(Node3D? next)
    {
        foreach (var old in Root.GetChildren()) old.QueueFree();
        _showing.Clear();
        _blockers.Clear();
        _shown = next != null;
        _showingRoot = next;
        if (next != null) Root.AddChild(next);
        _reachShown = -1;
    }

    IEnumerable<bool> Work()
    {
        var s = _c.S;
        var view = s.View;
        float upm = Upm;
        var hidden = _sheets.Hidden;
        string style = view.Lighting.MapStyle;
        float chunk = Chunk, snow = (float)ContourLines.SnowLineFor(view, view.Lighting);
        int epoch = Manners.Age(style);
        RaisedChunk.SnowY = snow > 0 ? _c.SheetsTop + snow : float.PositiveInfinity;
        foreach (var material in new[] { Material, PlainMaterial, TreeMaterial }) material.SetShaderParameter("ground_top", _c.SheetsTop);
        var shown = view.Sheets.Where(sh => !hidden.Contains(sh.Id)).ToList();
        _nextGrids = new List<RaisedChanges.Grid>();
        foreach (var sheet in shown)
        {
            if (s.Ground.GridOf(sheet.Id) is { } grid) _nextGrids.Add(new RaisedChanges.Grid(sheet.Id, grid.Cols, grid.Rows, RaisedChanges.Blocks(grid)));
            yield return true;
        }
        var laid = new List<Laid>();
        _nextThings = new Dictionary<string, (long Key, Rect2 Box)>();
        var bySheet = shown.ToDictionary(sh => sh.Id, _ => new List<BuiltPainter.Shape>());
        foreach (var sheet in shown)
            foreach (var thing in s.Relief.Seen(sheet))
            {
                var shapes = BuiltPainter.ShapesOf(thing, Ground.FullWidth(sheet), BuiltGeometry.FullHeight(sheet), upm);
                bySheet[sheet.Id].AddRange(shapes);
                var worlds = shapes.Select(shape => shape.Uv.Select(uv => Ground.FromUv(sheet, uv.X, uv.Y)).Select(p => OnGround(p, s.Ground)).ToArray()).ToList();
                laid.Add(new Laid(thing, sheet, shapes, worlds));
                _nextThings[thing.Id] = (BuiltPainter.KeyOf(thing), Flat(worlds.SelectMany(w => w)));
                yield return true;
            }
        _nextDominant = shown.ToDictionary(sh => sh.Id, sh => Dominant(bySheet[sh.Id], style));
        if (_next == null)
        {
            _only = Changed(laid, chunk);
            if (_only == null) _next = new Node3D { Name = "Raised" };
            else if (_only.Count == 0)
            {
                _changes.Keep(_nextGrids, _nextThings);
                yield break;
            }
        }
        _changed = true;
        foreach (var (thing, sheet, shapes, worlds) in laid)
        {
            var anchors = worlds.Select(w => KeyOf(w[0], chunk)).ToList();
            if (_only == null || anchors.Any(_only.Contains))
            {
                var blocks = shapes.Select((shape, i) => shape.Kind == PartKinds.Block && worlds[i].Length == 4 ? worlds[i] : null).ToList();
                var gaps = RaisedRows.Of(blocks, upm);
                _joined += gaps.Sum(g => g?.Count(e => !float.IsNaN(e)) ?? 0);
                var opens = RaisedRows.Open(blocks, upm);
                var rings = Enumerable.Range(0, shapes.Count).Where(i => shapes[i].Kind == PartKinds.Block && worlds[i].Length >= 3).Select(i => worlds[i]).ToList();
                bool Stands(Vector3 p) => rings.Any(ring => Near(ring, p, 0));
                for (int i = 0; i < shapes.Count; i++)
                {
                    if (_only != null && !_only.Contains(anchors[i])) continue;
                    Into(worlds[i][0], chunk).Add(shapes[i], worlds[i], style, s.Ground, _c.SheetsTop, upm, Across(sheet, shapes[i]), gaps[i], opens[i], Stands);
                    yield return true;
                }
            }
            foreach (var lamp in Lamps.Of(thing, epoch, upm))
            {
                var on = BuiltGeometry.ToWorld(sheet, lamp.At.Stored);
                var at = Coords.ToWorld(on, _c.SheetsTop + (float)s.Ground.DrawnAt(on));
                if (_only == null || _only.Contains(KeyOf(at, chunk))) Into(at, chunk).Lamp(lamp.Kind, at, upm, Mathf.PosMod(at.X * LampPick + at.Z, 1));
            }
        }
        foreach (var sheet in shown)
            foreach (var done in RaisedForest.Plant(sheet, s.Ground, _c.SheetsTop, upm, chunk, _chunks, bySheet[sheet.Id], _nextDominant[sheet.Id], _only)) yield return done;
        _nextSpan = _only == null ? RaisedGroves.Span(_chunks) : _showing.Span;
        foreach (var (key, piece) in _chunks)
        {
            var drawn = new List<RaisedChunk.Drawn>();
            foreach (var d in piece.Nodes(Material, PlainMaterial, upm))
            {
                Hold(d.Node);
                drawn.Add(d);
                yield return true;
            }
            _nextPieces[key] = drawn;
        }
        foreach (var made in RaisedGroves.Nodes(_chunks, Chunk / upm, upm, TreeMaterial, _nextSpan))
        {
            if (made is { } placed)
            {
                Hold(placed.Drawn.Node);
                if (!_nextGroves.TryGetValue(placed.Group, out var list)) _nextGroves[placed.Group] = list = new();
                list.Add(placed.Drawn);
            }
            yield return true;
        }
    }

    readonly record struct Laid(BuiltThing Thing, SheetDef Sheet, List<BuiltPainter.Shape> Shapes, List<Vector3[]> Worlds);

    void Hold(Node node)
    {
        if (_next != null) _next.AddChild(node);
        else _loose.Add(node);
    }

    HashSet<(int, int)>? Changed(List<Laid> laid, float chunk)
    {
        if (_nextDominant.Any(d => _dominant.GetValueOrDefault(d.Key, int.MinValue) != d.Value) || _dominant.Keys.Any(k => !_nextDominant.ContainsKey(k))) return null;
        if (!_changes.Covers(_nextGrids)) return null;
        var sheets = _c.S.View.Sheets.ToDictionary(sh => sh.Id);
        var rects = _changes.ThingsChanged(_nextThings).ToList();
        float reach = DirtyReachM * Upm;
        foreach (var grid in _nextGrids)
        {
            var cells = _changes.GroundChanged(grid, out bool unknown);
            if (unknown || !sheets.TryGetValue(grid.Sheet, out var sheet)) return null;
            reach = Mathf.Max(reach, DirtyReachM * Upm + DirtyCells * (float)Ground.FullWidth(sheet) / grid.Cols);
            rects.AddRange(cells.Select(c => Flat(new[] { (c.Position.X, c.Position.Y), (c.End.X, c.Position.Y), (c.Position.X, c.End.Y), (c.End.X, c.End.Y) }
                .Select(uv => Coords.ToWorld(Ground.FromUv(sheet, uv.Item1 / (double)grid.Cols, uv.Item2 / (double)grid.Rows))))));
        }
        var grown = rects.Select(r => r.Grow(reach)).ToList();
        var only = new HashSet<(int, int)>();
        foreach (var r in grown)
            for (int x = Mathf.FloorToInt(r.Position.X / chunk); x <= Mathf.FloorToInt(r.End.X / chunk); x++)
                for (int z = Mathf.FloorToInt(r.Position.Y / chunk); z <= Mathf.FloorToInt(r.End.Y / chunk); z++) only.Add((x, z));
        if (grown.Count > 0)
            foreach (var (_, _, _, worlds) in laid)
                foreach (var world in worlds)
                    if (grown.Any(r => r.Intersects(Flat(world), true))) only.Add(KeyOf(world[0], chunk));
        int span = _showing.Span;
        var grouped = only.Select(k => RaisedGroves.Group(k, span)).Distinct()
            .SelectMany(g => Enumerable.Range(0, span * span).Select(i => (g.Item1 * span + i % span, g.Item2 * span + i / span))).ToHashSet();
        return grouped.Count > MostAgain ? null : grouped;
    }

    static Rect2 Flat(IEnumerable<Vector3> points)
    {
        Rect2? box = null;
        foreach (var p in points) box = box?.Expand(new Vector2(p.X, p.Z)) ?? new Rect2(p.X, p.Z, 0, 0);
        return box ?? new Rect2();
    }

    Vector3 OnGround(Vec2 p, Ground ground) => Coords.ToWorld(p, _c.SheetsTop + (float)ground.DrawnAt(p));

    static (int, int) KeyOf(Vector3 at, float chunk) => ((int)Mathf.Floor(at.X / chunk), (int)Mathf.Floor(at.Z / chunk));

    RaisedChunk Into(Vector3 at, float chunk)
    {
        var key = KeyOf(at, chunk);
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

    public IEnumerable<MeshInstance3D> Standing(Vector3 around, float reach)
    {
        float within = Chunk / 2 + reach;
        foreach (var drawn in _showing.Drawn)
            if (drawn.Stands && drawn.Node is MeshInstance3D piece && GodotObject.IsInstanceValid(piece)
                && Mathf.Abs(piece.Position.X - around.X) <= within && Mathf.Abs(piece.Position.Z - around.Z) <= within) yield return piece;
    }

    public Vec2? Under(Vector3 from, Vector3 way, float reach)
    {
        if (_blockers.Count == 0 || Upm <= 0) return null;
        float step = HitStepM * Upm;
        for (float t = 0; t <= reach; t += step)
        {
            var p = from + way * t;
            if (!_blockers.TryGetValue(Cell(p.X, p.Z), out var list)) continue;
            foreach (var (ring, top) in list)
                if (p.Y <= top && Near(ring, p, HitGrownM * Upm)) return Coords.FromWorld(p);
        }
        return null;
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
        float high = world.Max(p => p.Y), low = world.Min(p => p.Y), front = world.Length > 1 ? (world[0].Y + world[1].Y) / 2 : high;
        float level = foot < 0 ? high : (float)Footings.Floor((KTable.Core.Model.Footing)foot, low, high, front, Footings.StoreyM * upm);
        bottom = foot == (int)KTable.Core.Model.Footing.Stilts ? level : low - SinkM * upm;
        return new Vector3(0, level + FloorM * upm, 0);
    }
}
