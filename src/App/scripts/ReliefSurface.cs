using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

public partial class ReliefSurface : Node3D
{
    public const uint GroundLayer = 1u << 6;
    const int MostVertsAcross = 384;
    const int BlockCells = 48;
    const float PaintAbove = 0.003f;
    const float SeamProbe = 0.01f;
    const float ContourBlurUnits = 0.8f;
    static readonly Color Earth = new(0.42f, 0.33f, 0.24f);
    static Shader? _shader;

    readonly record struct Block(MeshInstance3D Node, Rect2 Box, bool Paint, int Nx, int Nz, Vector2 Uv0, Vector2 Uv1);
    readonly record struct BuiltFor(string Id, double W, double H, double CropX, double CropY, double CropW, double CropH, int Cols, int Rows, int Nx, int Nz, float Scale, int Blur);
    readonly List<Block> _blocks = new();
    MeshInstance3D? _skirt;
    Material? _base;
    ShaderMaterial? _paint;
    BuiltFor? _builtFor;
    ImageTexture? _kindsTex;
    ImageTexture? _edgesTex;
    byte[]? _kindTexels;
    byte[]? _fields;
    byte[]? _edgesOf;
    byte[]? _detailOf;
    byte[]? _axes;
    ImageTexture? _axesTex;
    byte[]? _roads;
    bool _edgesBusy;
    int _edgesVersion;
    (int Cols, int Rows, byte[] Kinds, byte[] Detail)? _edgesNext;
    ReliefGrid? _grid;
    ImageTexture? _heightsTex;
    float[]? _heightsOf;
    float[]? _soft;
    float[]? _heightTexels;
    float _heightsScale;
    int _heightsBlur;
    bool _heightsStreams;
    byte[]? _flowKinds;
    GroundFlow? _flow;
    bool _flowBusy;
    bool _flowAgain;
    (int Cols, int Rows, float[] Soft, byte[] Kinds, double CellMetres, double MetresPerUnit)? _flowNext;
    float _cellWorld = 1;
    GroundTiles.Sheet? _tiles;
    ShaderMaterial? _bake;
    ImageTexture? _linesTex;
    ImageTexture? _sitesTex;
    bool _networkBusy;
    NetworkJob? _networkNext;
    static ImageTexture? _noNetwork;
    const int SettledKept = 8;
    static readonly Dictionary<string, (long Key, int Cols, int Rows, GroundSettlement Plan)> Settled = new();

    sealed record NetworkJob(string Id, int Cols, int Rows, long Key, byte[] Kinds, byte[] Salt, byte[] Detail, float[] Soft, double CellMetres, double MetresPerHeight);
    static bool _contours, _streams;
    static double _unitsPerMetre;
    static double _contourStep = -1;

    const string StyleParameter = "ground_style", ContourParameter = "contour_interval", ScaleParameter = "units_per_metre", SnowParameter = "snow_line";

    public static void ShowScale(Calibration c)
    {
        _unitsPerMetre = c.UnitsPerMeter is { } upm && upm > 0 ? upm : 0;
        RenderingServer.GlobalShaderParameterSet(ScaleParameter, (float)_unitsPerMetre);
        GroundTiles.StaleEverything();
    }

    public static bool ShowStreams(bool on)
    {
        if (_streams == on) return false;
        _streams = on;
        GroundTiles.StaleEverything();
        return true;
    }

    static Shader GroundShader => _shader ??= new Shader { Code = FileAccess.GetFileAsString("res://shaders/ground.gdshader") };

    public static void ShowStyle(string? style)
    {
        RenderingServer.GlobalShaderParameterSet(StyleParameter, MapStyles.Index(style));
        GroundTiles.StaleEverything();
    }

    public static bool ShowContours(bool on)
    {
        if (_contours == on) return false;
        _contours = on;
        GroundTiles.StaleEverything();
        return true;
    }

    public static void SnowLine(double height)
    {
        RenderingServer.GlobalShaderParameterSet(SnowParameter, (float)height);
        GroundTiles.StaleEverything();
    }

    public static void ContourStep(double step)
    {
        if (step == _contourStep) return;
        _contourStep = step;
        RenderingServer.GlobalShaderParameterSet(ContourParameter, (float)step);
        GroundTiles.StaleEverything();
    }

    public static Vector4[] Palette()
    {
        int kinds = GroundKinds.All.Count;
        var p = new Vector4[kinds * MapStyles.ShaderOrder.Count];
        for (int s = 0; s < MapStyles.ShaderOrder.Count; s++)
            for (int i = 0; i < kinds; i++)
            {
                var c = Color.FromHtml(GroundKinds.Color((byte)i, MapStyles.ShaderOrder[s]));
                p[s * kinds + i] = new Vector4(c.R, c.G, c.B, c.A);
            }
        return p;
    }

    public void Build(SheetDef def, ReliefGrid grid, Ground ground, Transform3D sheetXf, Func<Vector3[], bool>? covered, Material? picture)
    {
        Clear();
        float level = -sheetXf.Origin.Y;
        var c = def.Crop ?? SheetGeometry.Whole;
        float W = (float)def.Size.W, H = (float)def.Size.H;
        float x0 = -W / 2, x1 = W / 2, z0 = -H / 2, z1 = H / 2;
        int cellsX = Math.Max(1, (int)Math.Ceiling(c.W * (grid.Cols - 1))), cellsZ = Math.Max(1, (int)Math.Ceiling(c.H * (grid.Rows - 1)));
        int step = Math.Max(1, (int)Math.Ceiling(Math.Max(cellsX, cellsZ) / (double)MostVertsAcross));
        int nx = Math.Max(1, cellsX / step), nz = Math.Max(1, cellsZ / step);
        var h = GroundMesh.WeldedHeights(ground, sheetXf, W / 2, H / 2);
        _cellWorld = (float)(Ground.FullWidth(def) / Math.Max(1, grid.Cols - 1));
        float scale = (float)(Ground.FullWidth(def) * (def.Relief is { Length: > 0 } ? def.Height : 1));
        int blur = Math.Max(step, (int)Math.Round(ContourBlurUnits / _cellWorld));
        UpdateKinds(grid);
        UpdateHeights(grid, scale, blur);
        UpdateNetwork(def.Id, grid);
        _builtFor = new BuiltFor(def.Id, def.Size.W, def.Size.H, c.X, c.Y, c.W, c.H, grid.Cols, grid.Rows, nx, nz, scale, blur);
        var cells = new Vector2(grid.Cols, grid.Rows);
        bool drawsGround = picture == null && !def.HasPicture;
        bool tiled = GroundTiles.Enabled && IsInsideTree();
        _base = picture ?? (!drawsGround ? null : tiled ? Tiled(def, grid, sheetXf, level, scale, step, cells, false) : Material(false, cells));
        if (grid.Painted || (_contours && def.Relief is { Length: > 0 }))
            _paint = tiled && !drawsGround ? Tiled(def, grid, sheetXf, level, scale, step, cells, true) : Material(true, cells);
        for (int bz = 0; bz < nz; bz += BlockCells)
            for (int bx = 0; bx < nx; bx += BlockCells)
            {
                int ex = Math.Min(nx, bx + BlockCells), ez = Math.Min(nz, bz + BlockCells);
                float ax = Mathf.Lerp(x0, x1, bx / (float)nx), bxw = Mathf.Lerp(x0, x1, ex / (float)nx);
                float az = Mathf.Lerp(z0, z1, bz / (float)nz), bzw = Mathf.Lerp(z0, z1, ez / (float)nz);
                var uv0 = new Vector2((float)(c.X + c.W * bx / nx), (float)(c.Y + c.H * bz / nz));
                var uv1 = new Vector2((float)(c.X + c.W * ex / nx), (float)(c.Y + c.H * ez / nz));
                var box = new Rect2(ax, az, bxw - ax, bzw - az);
                var corners = new[] { sheetXf * new Vector3(ax, 0, az), sheetXf * new Vector3(bxw, 0, az), sheetXf * new Vector3(bxw, 0, bzw), sheetXf * new Vector3(ax, 0, bzw) };
                bool hidden = covered?.Invoke(corners) == true;
                if (_base != null && !hidden) Add(new Block(Node(_base, level), box, false, ex - bx, ez - bz, uv0, uv1), h);
                if (_paint != null && (hidden || !drawsGround)) Add(new Block(Node(_paint, level + PaintAbove), box, true, ex - bx, ez - bz, uv0, uv1), h);
            }
        _skirt = new MeshInstance3D
        {
            Mesh = SkirtMesh(h, ground, sheetXf, W, H, nx, nz),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = Earth, Roughness = 1, CullMode = BaseMaterial3D.CullModeEnum.Disabled, SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Position = new Vector3(0, level, 0),
        };
        AddChild(_skirt);
    }

    static ArrayMesh SkirtMesh(Func<float, float, float> h, Ground ground, Transform3D sheetXf, float W, float H, int nx, int nz) =>
        GroundMesh.Skirt(h, -W / 2, -H / 2, W / 2, H / 2, nx, nz, 0, (x, z) => ground.ReliefAt(Coords.FromWorld(sheetXf * new Vector3(x, 0, z))) == null, Math.Min(W, H) * SeamProbe);

    static MeshInstance3D Node(Material mat, float lift) => new()
    {
        MaterialOverride = mat,
        Position = new Vector3(0, lift, 0),
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        Layers = Table.WorldLayer | GroundLayer,
    };

    void Add(Block b, Func<float, float, float> h)
    {
        Shape(b, h);
        AddChild(b.Node);
        _blocks.Add(b);
    }

    static void Shape(Block b, Func<float, float, float> h) =>
        b.Node.Mesh = GroundMesh.Grid(h, b.Box.Position.X, b.Box.Position.Y, b.Box.End.X, b.Box.End.Y, b.Nx, b.Nz, b.Uv0, b.Uv1);

    public bool Patch(SheetDef def, ReliefGrid grid, Ground ground, Transform3D sheetXf, Rect2 where)
    {
        if (_builtFor is not { } built || !Patchable) return false;
        var crop = def.Crop ?? SheetGeometry.Whole;
        bool sameSheet = built.Id == def.Id && built.W == def.Size.W && built.H == def.Size.H && built.Cols == grid.Cols && built.Rows == grid.Rows;
        bool sameCrop = built.CropX == crop.X && built.CropY == crop.Y && built.CropW == crop.W && built.CropH == crop.H;
        if (!sameSheet || !sameCrop) return false;
        if (_paint == null && grid.Painted) return false;
        var box = CellBoxOf(def, grid, sheetXf, where);
        if (box.Empty) return true;
        bool kindsChanged = false, heightsChanged = false;
        for (int y = box.Y0; y <= box.Y1; y++)
            for (int x = box.X0; x <= box.X1; x++)
            {
                int i = y * grid.Cols + x;
                kindsChanged |= _edgesOf![i] != grid.Kind[i] || _kindTexels![4 * i + 1] != grid.Salt[i] || _kindTexels[4 * i + 2] != grid.Detail[i];
                heightsChanged |= _heightsOf![i] != grid.H[i];
            }
        if (!kindsChanged && !heightsChanged) return true;
        _grid = grid;
        if (kindsChanged) PatchKinds(grid, box);
        if (heightsChanged) PatchHeights(def, grid, ground, sheetXf, box, built);
        if (_unitsPerMetre > 0) FlowAgain(grid);
        return true;
    }

    bool Patchable => KindsKept && _heightsOf != null && _soft != null && _heightTexels != null;

    bool KindsKept => _grid != null && _kindTexels != null && _edgesOf != null && _detailOf != null && _roads != null && _fields != null && _axes != null;

    CellBox CellBoxOf(SheetDef def, ReliefGrid grid, Transform3D sheetXf, Rect2 where)
    {
        var inv = sheetXf.AffineInverse();
        var c = def.Crop ?? SheetGeometry.Whole;
        float W = (float)def.Size.W, H = (float)def.Size.H;
        var box = CellBox.None;
        foreach (var corner in new[] { where.Position, where.Position + new Vector2(where.Size.X, 0), where.End, where.Position + new Vector2(0, where.Size.Y) })
        {
            var l = inv * new Vector3(corner.X, 0, corner.Y);
            double u = c.X + c.W * (l.X / W + 0.5), v = c.Y + c.H * (l.Z / H + 0.5);
            int cx = (int)Math.Floor(u * (grid.Cols - 1)), cy = (int)Math.Floor(v * (grid.Rows - 1));
            box = box.Union(new CellBox(cx, cy, cx + 1, cy + 1));
        }
        return box.Grow(1).Clip(grid.Cols, grid.Rows);
    }

    Rect2 LocalRect(BuiltFor built, CellBox box)
    {
        float X(int cx) => (float)(((cx / (double)Math.Max(1, built.Cols - 1) - built.CropX) / built.CropW - 0.5) * built.W);
        float Z(int cy) => (float)(((cy / (double)Math.Max(1, built.Rows - 1) - built.CropY) / built.CropH - 0.5) * built.H);
        return new Rect2(X(box.X0), Z(box.Y0), X(box.X1) - X(box.X0), Z(box.Y1) - Z(box.Y0)).Grow(_cellWorld * 2);
    }

    void PatchKinds(ReliefGrid grid, CellBox box)
    {
        for (int y = box.Y0; y <= box.Y1; y++)
            for (int x = box.X0; x <= box.X1; x++)
            {
                int i = y * grid.Cols + x;
                _edgesOf![i] = grid.Kind[i];
                _kindTexels![4 * i] = grid.Kind[i];
                _kindTexels[4 * i + 1] = grid.Salt[i];
                _kindTexels[4 * i + 2] = grid.Detail[i];
                _detailOf![i] = grid.Detail[i];
            }
        _edgesVersion++;
        GroundEdges.Derive(grid.Cols, grid.Rows, _edgesOf!, box, _roads!, _fields!);
        GroundRoads.Axes(grid.Cols, grid.Rows, _edgesOf!, _detailOf!, box, _axes!);
        Upload(ref _axesTex, grid.Cols, grid.Rows, _axes!);
        var reach = box.Grow(GroundEdges.Reach).Clip(grid.Cols, grid.Rows);
        for (int y = reach.Y0; y <= reach.Y1; y++)
            for (int x = reach.X0; x <= reach.X1; x++)
            {
                int i = y * grid.Cols + x;
                _kindTexels![4 * i + 3] = _roads![i];
            }
        Upload(ref _kindsTex, grid.Cols, grid.Rows, _kindTexels!);
        Upload(ref _edgesTex, grid.Cols, grid.Rows, _fields!);
        _tiles?.StaleUv(UvOf(box.Grow(GroundRoads.Influence + GroundEdges.Reach), grid));
    }

    void PatchHeights(SheetDef def, ReliefGrid grid, Ground ground, Transform3D sheetXf, CellBox box, BuiltFor built)
    {
        for (int y = box.Y0; y <= box.Y1; y++)
            for (int x = box.X0; x <= box.X1; x++)
            {
                int i = y * grid.Cols + x;
                _heightsOf![i] = grid.H[i];
                _heightTexels![4 * i] = grid.H[i] * built.Scale;
            }
        GroundHeights.Blurred(grid.H, grid.Cols, grid.Rows, built.Blur, box, _soft!);
        var softened = box.Grow(built.Blur).Clip(grid.Cols, grid.Rows);
        for (int y = softened.Y0; y <= softened.Y1; y++)
            for (int x = softened.X0; x <= softened.X1; x++)
            {
                int i = y * grid.Cols + x;
                _heightTexels![4 * i + 1] = _soft![i] * built.Scale;
            }
        UploadHeights(grid.Cols, grid.Rows);
        _tiles?.StaleUv(UvOf(softened.Grow(2), grid));
        float W = (float)def.Size.W, H = (float)def.Size.H;
        var h = GroundMesh.WeldedHeights(ground, sheetXf, W / 2, H / 2);
        var local = LocalRect(built, box);
        foreach (var b in _blocks)
            if (b.Box.Intersects(local)) Shape(b, h);
        bool atEdge = local.Position.X <= -W / 2 || local.Position.Y <= -H / 2 || local.End.X >= W / 2 || local.End.Y >= H / 2;
        if (_skirt != null && atEdge) _skirt.Mesh = SkirtMesh(h, ground, sheetXf, W, H, built.Nx, built.Nz);
    }

    void FlowAgain(ReliefGrid grid)
    {
        if (_flowBusy) { _flowAgain = true; return; }
        _flowKinds = (byte[])grid.Kind.Clone();
        DeriveFlow(grid.Cols, grid.Rows, (float[])_soft!.Clone(), _flowKinds, _cellWorld / _unitsPerMetre, _heightsScale / _unitsPerMetre);
    }

    ShaderMaterial Tiled(SheetDef def, ReliefGrid grid, Transform3D sheetXf, float level, float scale, int step, Vector2 cells, bool paintedOnly)
    {
        var c = def.Crop ?? SheetGeometry.Whole;
        float W = (float)def.Size.W, H = (float)def.Size.H;
        float perU = (float)(W / c.W), perV = (float)(H / c.H);
        float u0 = (float)(-W * c.X / c.W - W / 2), v0 = (float)(-H * c.Y / c.H - H / 2);
        Vector3 World(Vector2 uv, float y) => sheetXf * new Vector3(u0 + perU * uv.X, level + y, v0 + perV * uv.Y);
        var origin = World(Vector2.Zero, 0);
        var alongU = sheetXf.Basis * new Vector3(perU, 0, 0);
        var alongV = sheetXf.Basis * new Vector3(0, 0, perV);
        _bake = Material(paintedOnly, cells, GroundTiles.BakeShader);
        _bake.SetShaderParameter("world_origin", new Vector2(origin.X, origin.Z));
        _bake.SetShaderParameter("world_u", new Vector2(alongU.X, alongU.Z));
        _bake.SetShaderParameter("world_v", new Vector2(alongV.X, alongV.Z));
        _bake.SetShaderParameter("sheet_level", origin.Y);
        _bake.SetShaderParameter("normal_step", (float)step);
        float high = 0;
        foreach (float v in grid.H) high = Math.Max(high, v);
        _tiles = GroundTiles.For(this).Track(def.Id, this, _bake, World, (float)Ground.FullWidth(def), 0, high * scale, paintedOnly);
        return _tiles.View;
    }

    public override void _ExitTree() => Untile();

    void Untile()
    {
        if (_tiles != null) GroundTiles.Release(_tiles);
        _tiles = null;
        _bake = null;
    }

    Rect2 UvOf(CellBox box, ReliefGrid grid)
    {
        float cx = Math.Max(1, grid.Cols - 1), cy = Math.Max(1, grid.Rows - 1);
        return new Rect2(box.X0 / cx, box.Y0 / cy, (box.X1 - box.X0 + 1) / cx, (box.Y1 - box.Y0 + 1) / cy);
    }

    ShaderMaterial Material(bool paintedOnly, Vector2 cells, Shader? shader = null)
    {
        var m = new ShaderMaterial { Shader = shader ?? GroundShader };
        m.SetShaderParameter("kinds", _kindsTex!);
        m.SetShaderParameter("edges", _edgesTex!);
        m.SetShaderParameter("heights", _heightsTex!);
        m.SetShaderParameter("network", _linesTex ?? NoNetwork);
        m.SetShaderParameter("road_axes", _axesTex ?? NoNetwork);
        m.SetShaderParameter("farm_sites", _sitesTex ?? NoNetwork);
        m.SetShaderParameter("cell_world", _cellWorld);
        m.SetShaderParameter("palette", Palette());
        m.SetShaderParameter("painted_only", paintedOnly);
        m.SetShaderParameter("cells", cells);
        if (paintedOnly) m.RenderPriority = 1;
        return m;
    }

    static ImageTexture NoNetwork => _noNetwork ??= ImageTexture.CreateFromImage(Image.CreateEmpty(1, 1, false, Image.Format.Rgba8));

    void UpdateNetwork(string id, ReliefGrid grid)
    {
        if (_unitsPerMetre <= 0 || _soft == null || _soft.Length != grid.H.Length) return;
        double cellMetres = _cellWorld / _unitsPerMetre;
        long key = NetworkKey(grid, cellMetres, _heightsScale);
        if (Settled.TryGetValue(id, out var last) && last.Cols == grid.Cols && last.Rows == grid.Rows)
        {
            ShowNetwork(last.Plan, grid.Cols, grid.Rows);
            if (last.Key == key) return;
        }
        var job = new NetworkJob(id, grid.Cols, grid.Rows, key, (byte[])grid.Kind.Clone(), (byte[])grid.Salt.Clone(), (byte[])grid.Detail.Clone(),
            (float[])_soft.Clone(), cellMetres, _heightsScale / _unitsPerMetre);
        if (_networkBusy) { _networkNext = job; return; }
        PlanNetwork(job);
    }

    static long NetworkKey(ReliefGrid grid, double cellMetres, float heightScale)
    {
        const long Prime = 1099511628211;
        long h = unchecked((long)14695981039346656037UL);
        foreach (var arr in new[] { grid.Kind, grid.Salt, grid.Detail })
            foreach (byte b in arr) h = unchecked((h ^ b) * Prime);
        foreach (float f in grid.H) h = unchecked((h ^ BitConverter.SingleToInt32Bits(f)) * Prime);
        h = unchecked((h ^ BitConverter.DoubleToInt64Bits(cellMetres)) * Prime);
        return unchecked((h ^ BitConverter.SingleToInt32Bits(heightScale)) * Prime);
    }

    void PlanNetwork(NetworkJob job)
    {
        _networkBusy = true;
        Task.Run(() =>
        {
            try
            {
                var plan = GroundNetwork.Plan(job.Cols, job.Rows, job.Kinds, job.Salt, job.Detail, job.Soft, job.CellMetres, job.MetresPerHeight);
                Callable.From(() => NetworkPlanned(job, plan)).CallDeferred();
            }
            catch (Exception e)
            {
                GD.PrintErr("[relief] planning farms and lanes: " + e.Message);
                Callable.From(() => NetworkPlanned(job, null)).CallDeferred();
            }
        });
    }

    void NetworkPlanned(NetworkJob job, GroundSettlement? plan)
    {
        if (plan != null)
        {
            if (Settled.Count >= SettledKept && !Settled.ContainsKey(job.Id)) Settled.Clear();
            Settled[job.Id] = (job.Key, job.Cols, job.Rows, plan);
        }
        if (!GodotObject.IsInstanceValid(this)) return;
        _networkBusy = false;
        if (_networkNext is { } next)
        {
            _networkNext = null;
            PlanNetwork(next);
        }
        if (plan == null || _grid == null || _grid.Cols != job.Cols || _grid.Rows != job.Rows) return;
        ShowNetwork(plan, job.Cols, job.Rows);
    }

    void ShowNetwork(GroundSettlement plan, int cols, int rows)
    {
        bool fresh = _linesTex == null || _linesTex.GetWidth() != cols || _linesTex.GetHeight() != rows;
        Upload(ref _linesTex, cols, rows, plan.Lines);
        Upload(ref _sitesTex, cols, rows, plan.Sites);
        _tiles?.StaleAll();
        if (!fresh) return;
        foreach (var m in new[] { _base as ShaderMaterial, _paint, _bake })
        {
            m?.SetShaderParameter("network", _linesTex!);
            m?.SetShaderParameter("farm_sites", _sitesTex!);
        }
    }

    void UpdateKinds(ReliefGrid grid)
    {
        _grid = grid;
        bool changed = _edgesOf == null || _detailOf == null || !_edgesOf.AsSpan().SequenceEqual(grid.Kind) || !_detailOf.AsSpan().SequenceEqual(grid.Detail);
        bool sized = _kindsTex != null && _edgesTex != null && _kindsTex.GetWidth() == grid.Cols && _kindsTex.GetHeight() == grid.Rows;
        if (sized && !changed) return;
        _edgesOf = (byte[])grid.Kind.Clone();
        _detailOf = (byte[])grid.Detail.Clone();
        if (!sized)
        {
            var (roads, fields) = GroundEdges.Derive(grid.Cols, grid.Rows, grid.Kind);
            _roads = roads;
            UploadKinds(grid);
            UploadEdges(grid.Cols, grid.Rows, fields);
            UploadAxes(grid.Cols, grid.Rows, GroundRoads.Axes(grid.Cols, grid.Rows, grid.Kind, grid.Detail));
            return;
        }
        UploadKinds(grid);
        _edgesVersion++;
        if (_edgesBusy) { _edgesNext = (grid.Cols, grid.Rows, (byte[])grid.Kind.Clone(), (byte[])grid.Detail.Clone()); return; }
        DeriveEdges(grid.Cols, grid.Rows, (byte[])grid.Kind.Clone(), (byte[])grid.Detail.Clone());
    }

    void DeriveEdges(int cols, int rows, byte[] kinds, byte[] detail)
    {
        _edgesBusy = true;
        int version = _edgesVersion;
        Task.Run(() =>
        {
            try
            {
                var derived = GroundEdges.Derive(cols, rows, kinds);
                var axes = GroundRoads.Axes(cols, rows, kinds, detail);
                Callable.From(() => EdgesDerived(cols, rows, version, derived.Roads, derived.Fields, axes)).CallDeferred();
            }
            catch (Exception e)
            {
                GD.PrintErr("[relief] deriving the ground's edges: " + e.Message);
                Callable.From(() => EdgesDerived(cols, rows, version, null, null, null)).CallDeferred();
            }
        });
    }

    void EdgesDerived(int cols, int rows, int version, byte[]? roads, byte[]? fields, byte[]? axes)
    {
        _edgesBusy = false;
        if (_edgesNext is { } next)
        {
            _edgesNext = null;
            DeriveEdges(next.Cols, next.Rows, next.Kinds, next.Detail);
        }
        if (roads == null || fields == null || axes == null || version != _edgesVersion || !GodotObject.IsInstanceValid(this) || _grid == null || _grid.Cols != cols || _grid.Rows != rows) return;
        _roads = roads;
        UploadKinds(_grid);
        UploadEdges(cols, rows, fields);
        UploadAxes(cols, rows, axes);
        _tiles?.StaleAll();
    }

    void UploadAxes(int cols, int rows, byte[] axes)
    {
        bool fresh = _axesTex == null || _axesTex.GetWidth() != cols || _axesTex.GetHeight() != rows;
        _axes = axes;
        Upload(ref _axesTex, cols, rows, axes);
        if (!fresh) return;
        (_base as ShaderMaterial)?.SetShaderParameter("road_axes", _axesTex!);
        _paint?.SetShaderParameter("road_axes", _axesTex!);
        _bake?.SetShaderParameter("road_axes", _axesTex!);
    }

    void UploadKinds(ReliefGrid grid)
    {
        var roads = _roads is { } r && r.Length == grid.Kind.Length ? r : null;
        if (_kindTexels == null || _kindTexels.Length != grid.Kind.Length * 4) _kindTexels = new byte[grid.Kind.Length * 4];
        var texels = _kindTexels;
        for (int i = 0; i < grid.Kind.Length; i++)
        {
            texels[4 * i] = grid.Kind[i];
            texels[4 * i + 1] = grid.Salt[i];
            texels[4 * i + 2] = grid.Detail[i];
            texels[4 * i + 3] = roads?[i] ?? GroundEdges.Far;
        }
        Upload(ref _kindsTex, grid.Cols, grid.Rows, texels);
    }

    void UploadEdges(int cols, int rows, byte[] fields)
    {
        _fields = fields;
        Upload(ref _edgesTex, cols, rows, fields);
    }

    static void Upload(ref ImageTexture? tex, int cols, int rows, byte[] texels)
    {
        var img = Image.CreateFromData(cols, rows, false, Image.Format.Rgba8, texels);
        if (tex != null && tex.GetWidth() == cols && tex.GetHeight() == rows) tex.Update(img);
        else tex = ImageTexture.CreateFromImage(img);
    }

    void UpdateHeights(ReliefGrid grid, float scale, int blur)
    {
        bool calibrated = _unitsPerMetre > 0;
        bool streams = _streams && calibrated;
        bool sized = _heightsTex != null && _heightTexels != null && _heightsTex.GetWidth() == grid.Cols && _heightsTex.GetHeight() == grid.Rows;
        bool heightsChanged = !sized || _heightsOf == null || _heightsScale != scale || _heightsBlur != blur || !_heightsOf.AsSpan().SequenceEqual(grid.H);
        bool kindsChanged = calibrated && (_flowKinds == null || !_flowKinds.AsSpan().SequenceEqual(grid.Kind));
        bool streamsChanged = _heightsStreams != streams;
        if (!heightsChanged && !kindsChanged && !streamsChanged) return;
        _heightsStreams = streams;
        float[]? soft = null;
        if (heightsChanged)
        {
            _heightsOf = (float[])grid.H.Clone();
            _heightsScale = scale;
            _heightsBlur = blur;
            soft = GroundHeights.Blurred(grid, blur);
            _soft = soft;
            var texels = new float[grid.H.Length * 4];
            var old = sized ? _heightTexels : null;
            for (int i = 0; i < grid.H.Length; i++)
            {
                texels[4 * i] = grid.H[i] * scale;
                texels[4 * i + 1] = soft[i] * scale;
                texels[4 * i + 2] = old?[4 * i + 2] ?? GroundStreams.NoFlow;
                texels[4 * i + 3] = old?[4 * i + 3] ?? 0;
            }
            _heightTexels = texels;
        }
        if (!calibrated)
        {
            _flowKinds = null;
            _flow = null;
            ClearFlow();
            UploadHeights(grid.Cols, grid.Rows);
            return;
        }
        if (!heightsChanged && !kindsChanged)
        {
            if (_flow != null && _flow.Order.Length * 4 == _heightTexels!.Length) WriteFlow(_flow);
            UploadHeights(grid.Cols, grid.Rows);
            return;
        }
        _flowKinds = (byte[])grid.Kind.Clone();
        soft ??= _soft is { } kept && kept.Length == grid.H.Length ? kept : GroundHeights.Blurred(grid, blur);
        _soft = soft;
        double cellMetres = _cellWorld / _unitsPerMetre, metresPerUnit = scale / _unitsPerMetre;
        if (heightsChanged) UploadHeights(grid.Cols, grid.Rows);
        if (_flowBusy) { _flowNext = (grid.Cols, grid.Rows, (float[])soft.Clone(), _flowKinds, cellMetres, metresPerUnit); return; }
        DeriveFlow(grid.Cols, grid.Rows, (float[])soft.Clone(), _flowKinds, cellMetres, metresPerUnit);
    }

    void DeriveFlow(int cols, int rows, float[] soft, byte[] kinds, double cellMetres, double metresPerUnit)
    {
        _flowBusy = true;
        Task.Run(() =>
        {
            try
            {
                var flow = GroundStreams.Flow(cols, rows, soft, kinds, cellMetres, metresPerUnit);
                Callable.From(() => FlowDerived(cols, rows, flow)).CallDeferred();
            }
            catch (Exception e)
            {
                GD.PrintErr("[relief] deriving the streams: " + e.Message);
                Callable.From(() => FlowDerived(cols, rows, null)).CallDeferred();
            }
        });
    }

    void FlowDerived(int cols, int rows, GroundFlow? flow)
    {
        _flowBusy = false;
        if (!GodotObject.IsInstanceValid(this)) return;
        if (_flowNext is { } next)
        {
            _flowNext = null;
            _flowAgain = false;
            DeriveFlow(next.Cols, next.Rows, next.Soft, next.Kinds, next.CellMetres, next.MetresPerUnit);
        }
        else if (_flowAgain && _grid != null && _unitsPerMetre > 0)
        {
            _flowAgain = false;
            FlowAgain(_grid);
        }
        if (flow == null) return;
        if (_heightTexels == null || _heightTexels.Length != flow.Order.Length * 4 || _heightsTex == null || _heightsTex.GetWidth() != cols || _heightsTex.GetHeight() != rows) return;
        _flow = flow;
        WriteFlow(flow);
        UploadHeights(cols, rows);
        _tiles?.StaleAll();
    }

    void WriteFlow(GroundFlow flow)
    {
        var texels = _heightTexels!;
        for (int i = 0; i < flow.Order.Length; i++)
        {
            texels[4 * i + 2] = GroundStreams.Packed(flow, i);
            texels[4 * i + 3] = _heightsStreams ? flow.Order[i] : 0;
        }
    }

    void ClearFlow()
    {
        var texels = _heightTexels!;
        for (int i = 0; i < texels.Length / 4; i++)
        {
            texels[4 * i + 2] = GroundStreams.NoFlow;
            texels[4 * i + 3] = 0;
        }
    }

    void UploadHeights(int cols, int rows)
    {
        var img = Image.CreateFromData(cols, rows, false, Image.Format.Rgbaf, System.Runtime.InteropServices.MemoryMarshal.AsBytes(_heightTexels.AsSpan()).ToArray());
        if (_heightsTex != null && _heightsTex.GetWidth() == cols && _heightsTex.GetHeight() == rows) _heightsTex.Update(img);
        else _heightsTex = ImageTexture.CreateFromImage(img);
    }

    public void Clear()
    {
        foreach (var b in _blocks) b.Node.QueueFree();
        _blocks.Clear();
        _skirt?.QueueFree();
        _skirt = null;
        _base = null;
        _paint = null;
        _builtFor = null;
        Untile();
    }
}
