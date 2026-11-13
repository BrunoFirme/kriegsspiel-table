using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Protocol;
using KTable.Core.Model;

namespace KTable.App;

sealed class DrawingLayer
{
    public const float StrokeLift = 0.025f, FillLift = 0.02f;

    const float AreaWash = 0.35f;

    public static Color Washed(Color ink) => new(ink, ink.A * AreaWash);

    public static StandardMaterial3D Ink(Color color) => new()
    {
        AlbedoColor = color, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = color.A < 1 ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled,
    };

    readonly TableContext _c;
    readonly Dictionary<string, MeshInstance3D> _drawings = new();
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

    public string DrawBoard()
    {
        var boards = _c.S.View.Boards;
        if (Board.Length > 0 && boards.Any(b => b.Id == Board)) return Board;
        return boards.Count > 0 ? boards[0].Id : "";
    }

    public bool BoardShown(string board) => board.Length == 0 || !HiddenBoards.Contains(board);

    public bool Locked => !BoardShown(DrawBoard());

    public bool OnDrawBoard(string board) => BoardShown(board) && (board.Length == 0 || board == DrawBoard());

    public void Sync()
    {
        foreach (var d in _drawings.Values) d.QueueFree();
        _drawings.Clear();
        SyncDecals();
        foreach (var (layer, strokes) in _c.S.View.Drawings)
        {
            if (!_c.LayerShown(layer)) continue;
            if (strokes.Count == 0) continue;
            var im = new ImmediateMesh();
            foreach (var st in strokes)
            {
                if (st.Text != null || st.Points.Count < 2 || !BoardShown(st.Board)) continue;
                var color = Coords.ParseColor(st.Color, Colors.Black);
                if (st.Fill) Ribbon.Fill(im, Ink(Washed(color)), st.Points, _c.SheetsTop + FillLift, _c.S.Ground);
                Ribbon.Draw(im, Ink(color), st.Points, st.Width, _c.SheetsTop + StrokeLift, _c.S.Ground, st.Round);
            }
            var mi = new MeshInstance3D { Mesh = im, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            Root.AddChild(mi);
            _drawings[layer] = mi;
        }
        _lettering.Sync(BoardShown);
    }

    public void Reletter() => _lettering.Sync(BoardShown);

    public void Reground(Rect2? where)
    {
        if (where is { } box && !_c.S.View.Drawings.Values.Any(strokes => strokes.Any(st => st.Points.Any(p => box.HasPoint(new Vector2((float)p.X, (float)-p.Z)))))
            && !_c.S.View.Decals.Values.Any(l => l.Any(d => box.HasPoint(new Vector2((float)d.X, (float)-d.Z))))) return;
        Sync();
    }

    public const float DecalLift = 0.015f;
    const int DecalSteps = 8, DecalPriority = 1, LargestDecal = 1024;

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
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha, CullMode = BaseMaterial3D.CullModeEnum.Disabled, RenderPriority = DecalPriority, AlbedoColor = Colors.White,
        };
        var mi = new MeshInstance3D { Mesh = DecalMesh(d, _c.SheetsTop + DecalLift, _c.S.Ground), MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
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

    public int DrawnSurfaces() => _drawings.Values.Sum(mi => mi.Mesh is ImmediateMesh im ? im.GetSurfaceCount() : 0);

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
