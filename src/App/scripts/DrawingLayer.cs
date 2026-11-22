using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Protocol;
using KTable.Core.Model;

namespace KTable.App;

sealed class DrawingLayer
{
    public const float StrokeLift = 0.025f, FillLift = 0.02f;
    const float UsualMetresToAUnit = 50, LeastOfTheLift = 0.1f;

    public float Lifted(float lift) =>
        lift * (_c.S.View.Calibration.UnitsPerMeter is > 0 and var perMetre ? Mathf.Clamp((float)(1 / perMetre) / UsualMetresToAUnit, LeastOfTheLift, 1) : 1);

    public void Raise(float clear) => Root.Position = Root.Position with { Y = Mathf.Max(0, clear - Lifted(DecalLift)) };

    const float AreaWash = 0.35f;

    public static Color Washed(Color ink) => new(ink, ink.A * AreaWash);

    public static StandardMaterial3D Ink(Color color) => new()
    {
        AlbedoColor = color, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = color.A < 1 ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled,
    };

    public const int GridPriority = (int)Material.RenderPriorityMin, TableDecalPriority = GridPriority + 1, LowestInk = TableDecalPriority + 1, InkSteps = -LowestInk;

    static StandardMaterial3D Laid(Color color, int step) => new()
    {
        AlbedoColor = color, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, RenderPriority = LowestInk + step,
    };

    readonly TableContext _c;
    readonly List<MeshInstance3D> _drawings = new();
    readonly List<MeshInstance3D> _decals = new();
    public Node3D Root = null!;

    readonly MapLettering _lettering;

    public DrawingLayer(TableContext c, MapLettering lettering)
    {
        _c = c;
        _lettering = lettering;
    }

    string Scenario => _c.S.View.ScenarioId;

    public string Board
    {
        get => _c.Settings.DrawLayer.GetValueOrDefault(Scenario, "");
        set
        {
            if (Board == value) return;
            if (value.Length > 0) _c.Settings.DrawLayer[Scenario] = value;
            else _c.Settings.DrawLayer.Remove(Scenario);
            _c.Settings.Save();
        }
    }

    static readonly HashSet<string> NoneHidden = new();

    public IReadOnlySet<string> HiddenBoards => _c.Settings.HiddenLayers.GetValueOrDefault(Scenario) ?? NoneHidden;

    public void ShowBoard(string id, bool shown)
    {
        var hidden = _c.Settings.HiddenLayers;
        if (!hidden.TryGetValue(Scenario, out var set)) hidden[Scenario] = set = new();
        if (!(shown ? set.Remove(id) : set.Add(id))) return;
        if (set.Count == 0) hidden.Remove(Scenario);
        _c.Settings.Save();
    }

    public string BoardName(string id) => _c.S.View.Boards.FirstOrDefault(b => b.Id == id)?.Name ?? "That layer";

    public System.Func<bool> SightOpen { get; set; } = static () => false;

    public bool SightLocked => SightOpen() && _c.S.View.Boards.Any(b => b.Sight);

    public string DrawBoard()
    {
        var boards = _c.S.View.Boards;
        if (SightOpen() && boards.FirstOrDefault(b => b.Sight) is { } sight) return sight.Id;
        if (Board.Length > 0 && boards.Any(b => b.Id == Board)) return Board;
        return (boards.FirstOrDefault(b => !b.Sight) ?? boards.FirstOrDefault())?.Id ?? "";
    }

    public bool BoardShown(string board) => board.Length == 0 || !HiddenBoards.Contains(board);

    public bool Locked => !BoardShown(DrawBoard());

    public bool OnDrawBoard(string board) => BoardShown(board) && (board.Length == 0 || board == DrawBoard());

    public bool DrawsSight
    {
        get
        {
            var board = DrawBoard();
            return _c.S.View.Boards.Any(b => b.Id == board && b.Sight);
        }
    }

    static List<Vec2> Ring(Stroke st) => st.Sight == Stroke.SightView && st.Points.Count > 2 && st.Points[0] != st.Points[^1] ? [.. st.Points, st.Points[0]] : st.Points;

    public void Sync()
    {
        foreach (var d in _drawings) d.QueueFree();
        _drawings.Clear();
        SyncDecals();
        var boards = _c.S.View.Boards;
        var sightBoards = boards.Where(b => b.Sight).Select(b => b.Id).ToHashSet();
        var strokes = _c.S.View.Drawings.Where(d => _c.LayerShown(d.Key)).SelectMany(d => d.Value)
            .Where(st => st.Text == null && st.Points.Count >= 2 && BoardShown(st.Board));
        var runs = KTable.Client.InkOrder.Runs(boards, strokes, st => (Ink: Coords.ParseColor(st.Color, Colors.Black), Aid: st.Sight != null || sightBoards.Contains(st.Board)), InkSteps);
        foreach (var run in runs)
        {
            var tris = new List<Vector3>();
            foreach (var st in run.Strokes)
                if (run.Wash) Ribbon.AddFill(tris, st.Points, _c.SheetsTop + Lifted(FillLift), _c.S.Ground);
                else Ribbon.AddLine(tris, Ring(st), st.Width, _c.SheetsTop + Lifted(StrokeLift), _c.S.Ground, st.Round);
            if (tris.Count < 3) continue;
            var mat = Laid(run.Wash ? Washed(run.Key.Ink) : run.Key.Ink, run.Step);
            if (run.Wash) mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
            Ribbon.Surface((ImmediateMesh)Holder(run.Key.Aid).Mesh, mat, tris);
        }
        _lettering.Sync(BoardShown);
    }

    public const int MostSurfaces = 256;

    MeshInstance3D Holder(bool aid)
    {
        uint layers = aid ? Table.LiveOnlyLayer : Table.WorldLayer;
        if (_drawings.LastOrDefault(m => m.Layers == layers) is { } open && open.Mesh.GetSurfaceCount() < MostSurfaces) return open;
        var mi = new MeshInstance3D { Mesh = new ImmediateMesh(), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Layers = layers };
        Root.AddChild(mi);
        _drawings.Add(mi);
        return mi;
    }

    public void Reletter() => _lettering.Sync(BoardShown);

    public MapLettering Lettering => _lettering;

    public void Reground(Rect2? where)
    {
        if (where is { } box && !_c.S.View.Drawings.Values.Any(strokes => strokes.Any(st => st.Points.Any(p => box.HasPoint(new Vector2((float)p.X, (float)-p.Z)))))
            && !_c.S.View.Decals.Values.Any(l => l.Any(d => box.HasPoint(new Vector2((float)d.X, (float)-d.Z))))) return;
        Sync();
    }

    public const float DecalLift = 0.015f;
    const int DecalSteps = 8, LargestDecal = 1024;

    void SyncDecals()
    {
        foreach (var d in _decals) if (GodotObject.IsInstanceValid(d)) d.QueueFree();
        _decals.Clear();
        foreach (var (layer, decals) in _c.S.View.Decals)
        {
            if (!_c.LayerShown(layer)) continue;
            foreach (var d in decals)
                if (KTable.Client.PieceFiles.DecalFile(_c.S.View, _c.S.Store, d.Picture) is { } file) _decals.Add(Decal(d, file.Package, file.Path));
        }
    }

    MeshInstance3D Decal(DecalDef d, string package, string file)
    {
        var mat = new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha, CullMode = BaseMaterial3D.CullModeEnum.Disabled, RenderPriority = TableDecalPriority, AlbedoColor = Colors.White,
        };
        var mi = new MeshInstance3D { Mesh = DecalMesh(d, _c.SheetsTop + Lifted(DecalLift), _c.S.Ground), MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        var tex = _c.S.Assets.Texture(package, file, t => { if (GodotObject.IsInstanceValid(mi)) mat.AlbedoTexture = t; }, maxSize: LargestDecal);
        if (tex != null) mat.AlbedoTexture = tex;
        Root.AddChild(mi);
        return mi;
    }

    public static Vec2 DecalPoint(DecalDef d, double s, double t)
    {
        double lx = (s - 0.5) * d.W, lz = (0.5 - t) * d.H, a = d.Rot * System.Math.PI / 180, c = System.Math.Cos(a), n = System.Math.Sin(a);
        return new Vec2(d.X + lx * c + lz * n, d.Z - lx * n + lz * c);
    }

    public static ImmediateMesh DecalMesh(DecalDef d, float lift, Ground ground, ImmediateMesh? into = null, Material? material = null)
    {
        var im = into ?? new ImmediateMesh();
        im.SurfaceBegin(Mesh.PrimitiveType.Triangles, material);
        Vector3 At(int i, int j)
        {
            double s = i / (double)DecalSteps, t = j / (double)DecalSteps;
            var p = DecalPoint(d, s, t);
            return Coords.ToWorld(p, lift + (float)ground.At(p));
        }
        void V(int i, int j)
        {
            im.SurfaceSetNormal(Vector3.Up);
            im.SurfaceSetUV(new Vector2(i / (float)DecalSteps, j / (float)DecalSteps));
            im.SurfaceAddVertex(At(i, j));
        }
        for (int i = 0; i < DecalSteps; i++)
            for (int j = 0; j < DecalSteps; j++)
            {
                V(i, j);
                V(i + 1, j);
                V(i + 1, j + 1);
                V(i, j);
                V(i + 1, j + 1);
                V(i, j + 1);
            }
        im.SurfaceEnd();
        return im;
    }

    public DecalDef? TableDecalAt(Vec2 point)
    {
        foreach (var (layer, decals) in _c.S.View.Decals)
        {
            if (!_c.LayerShown(layer) || !_c.S.CanEdit(layer)) continue;
            for (int i = decals.Count - 1; i >= 0; i--)
                if (Covers(decals[i], point)) return decals[i];
        }
        return null;
    }

    public string? LayerOf(DecalDef d) => _c.S.View.Decals.FirstOrDefault(kv => kv.Value.Contains(d)).Key;

    public static bool Covers(DecalDef d, Vec2 point)
    {
        double a = d.Rot * System.Math.PI / 180, c = System.Math.Cos(a), n = System.Math.Sin(a), dx = point.X - d.X, dz = point.Z - d.Z;
        double lx = dx * c - dz * n, lz = dx * n + dz * c;
        return System.Math.Abs(lx) <= d.W / 2 && System.Math.Abs(lz) <= d.H / 2;
    }

    public int DrawnSurfaces() => _drawings.Sum(mi => mi.Mesh.GetSurfaceCount());

    public int DrawingCount() => _c.S.View.Drawings.Where(d => _c.S.CanEdit(d.Key)).Sum(d => d.Value.Count(s => OnDrawBoard(s.Board)));

    public void EraseAll()
    {
        var cmds = new List<Command>();
        var board = DrawBoard();
        foreach (var (layer, strokes) in _c.S.View.Drawings)
        {
            if (!_c.S.CanEdit(layer)) continue;
            foreach (var st in strokes.Where(x => OnDrawBoard(x.Board)).ToList()) cmds.Add(new EraseStroke { Layer = layer, StrokeId = st.Id });
        }
        _c.DoAll(cmds);
    }
}
