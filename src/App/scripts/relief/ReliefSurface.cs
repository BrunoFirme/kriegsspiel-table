using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

public partial class ReliefSurface : Node3D
{
    public const uint GroundLayer = 1u << 6;
    const int BlockCells = 48;
    const float PaintAbove = 0.003f;
    const float SeamProbe = 0.01f;
    const float ContourBlurUnits = 0.8f;
    const int KindBytes = 3, DrawnAround = 2, UnderTheBrush = 6;
    static readonly Color Earth = new(0.42f, 0.33f, 0.24f);
    static Shader? _shader;

    readonly record struct Block(MeshInstance3D Node, Rect2 Box, bool Paint, int Nx, int Nz, Vector2 Uv0, Vector2 Uv1);
    readonly record struct BuiltFor(string Id, double W, double H, double CropX, double CropY, double CropW, double CropH, int Cols, int Rows, int Nx, int Nz, float Scale, int Blur);
    readonly List<Block> _blocks = new();
    MeshInstance3D? _skirt;
    Material? _base;
    ShaderMaterial? _paint;
    BuiltFor? _builtFor, _lastBuiltFor;
    Transform3D _lastXf;
    bool _redrawn = true;
    ImageTexture? _kindsTex;
    ImageTexture? _edgesTex;
    ImageTexture? _nearTex;
    byte[]? _kindTexels;
    byte[]? _fields;
    byte[]? _edgesOf;
    byte[]? _detailOf;
    bool _edgesBusy;
    int _edgesVersion;
    (int Cols, int Rows, byte[] Kinds)? _edgesNext;
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
    static bool _contours, _streams;
    static double _unitsPerMetre;
    static double _contourStep = -1;

    const string LevelParameter = "relief_level", MetreParameter = "relief_metre", HighParameter = "relief_high";
    public const string HeightsParameter = "heights_view";
    const string StyleParameter = "ground_style", ContourParameter = "contour_interval", ScaleParameter = "units_per_metre", SnowParameter = "snow_line", GainParameter = "ground_gain";
    const string PaintedParameter = "painted";

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
        GroundTiles.Style = MapStyles.Index(style);
        RenderingServer.GlobalShaderParameterSet(StyleParameter, MapStyles.Index(style));
        GroundTiles.StaleEverything();
    }

    public static void ShowLight(double share, Color tint)
    {
        var light = tint.SrgbToLinear();
        RenderingServer.GlobalShaderParameterSet(GainParameter, new Vector3((float)share / light.R, (float)share / light.G, (float)share / light.B));
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

    const string NearParameter = "built_near";
    readonly BuiltPainter _painter = new();

    public int PaintBlocks => _blocks.FindAll(b => b.Paint).Count;

    public bool Rebuilt(SheetDef def, ReliefGrid grid, IReadOnlyList<BuiltThing> built)
    {
        if (!GroundTiles.Enabled || !IsInsideTree()) return true;
        bool had = _painter.Any;
        if (_painter.Set(built, Ground.FullWidth(def), BuiltGeometry.FullHeight(def), _unitsPerMetre) is not { } changed) return true;
        UploadNear();
        if (_tiles == null || had != _painter.Any) return false;
        _tiles.StaleUv(changed, urgent: true);
        GroundTiles.For(this).Hurry();
        return true;
    }

    public void Build(SheetDef def, ReliefGrid grid, Ground ground, Transform3D sheetXf, Func<Vector3[], bool>? covered, Material? picture, IReadOnlyList<BuiltThing>? built = null)
    {
        Clear();
        bool builtRedrawn = _painter.Set(built ?? [], Ground.FullWidth(def), BuiltGeometry.FullHeight(def), _unitsPerMetre) != null;
        if (builtRedrawn || _nearTex == null) UploadNear();
        float level = -sheetXf.Origin.Y;
        var c = def.Crop ?? SheetGeometry.Whole;
        float W = (float)def.Size.W, H = (float)def.Size.H;
        float x0 = -W / 2, x1 = W / 2, z0 = -H / 2, z1 = H / 2;
        var (nx, nz, step) = Ground.LatticeOf(def, grid);
        var h = GroundMesh.WeldedHeights(ground, def);
        _cellWorld = (float)(Ground.FullWidth(def) / Math.Max(1, grid.Cols - 1));
        float scale = (float)(Ground.FullWidth(def) * (def.Relief is { Length: > 0 } ? def.Height : 1));
        int blur = Math.Max(step, (int)Math.Round(ContourBlurUnits / _cellWorld));
        var builtFor = new BuiltFor(def.Id, def.Size.W, def.Size.H, c.X, c.Y, c.W, c.H, grid.Cols, grid.Rows, nx, nz, scale, blur);
        bool kindsRedrawn = UpdateKinds(grid), heightsRedrawn = UpdateHeights(grid, scale, blur);
        _redrawn = kindsRedrawn || heightsRedrawn || builtRedrawn || builtFor != _lastBuiltFor || sheetXf != _lastXf;
        _lastBuiltFor = builtFor;
        _lastXf = sheetXf;
        _builtFor = builtFor;
        var cells = new Vector2(grid.Cols, grid.Rows);
        bool drawsGround = picture == null && !def.HasPicture;
        bool tiled = GroundTiles.Enabled && IsInsideTree();
        _base = picture ?? (!drawsGround ? null : tiled ? Tiled(def, grid, sheetXf, level, scale, step, cells, false) : Material(false, cells));
        if (grid.Painted || (_painter.Any && tiled) || (_contours && def.Relief is { Length: > 0 }))
            _paint = tiled && !drawsGround ? Tiled(def, grid, sheetXf, level, scale, step, cells, true) : Material(true, cells);
        bool mixed = _paint != null && _tiles != null && picture is ShaderMaterial;
        if (mixed) _tiles!.Lay((ShaderMaterial)picture!);
        (picture as ShaderMaterial)?.SetShaderParameter(PaintedParameter, mixed);
        float metre = (float)(_unitsPerMetre * (def.Relief is { Length: > 0 } ? def.Height : 1)), highest = _unitsPerMetre > 0 ? (float)(grid.Highest * Ground.FullWidth(def) / _unitsPerMetre) : 0;
        foreach (var laid in new[] { _base, _paint, picture })
        {
            if (laid is not ShaderMaterial seen) continue;
            seen.SetShaderParameter(LevelParameter, (sheetXf * new Vector3(0, level, 0)).Y);
            seen.SetShaderParameter(MetreParameter, metre);
            seen.SetShaderParameter(HighParameter, highest);
        }
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
                if (_base != null && !hidden) Add(new Block(Node(_base, level, casts: true), box, false, ex - bx, ez - bz, uv0, uv1), h);
                if (_paint != null && (hidden || (!drawsGround && !mixed))) Add(new Block(Node(_paint, level + PaintAbove, casts: hidden), box, true, ex - bx, ez - bz, uv0, uv1), h);
            }
        _skirt = new MeshInstance3D
        {
            Mesh = SkirtMesh(h, ground, sheetXf, W, H, nx, nz),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = Earth, Roughness = 1, CullMode = BaseMaterial3D.CullModeEnum.Disabled, SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled },
            Position = new Vector3(0, level, 0),
        };
        AddChild(_skirt);
    }

    static ArrayMesh SkirtMesh(Func<float, float, float> h, Ground ground, Transform3D sheetXf, float W, float H, int nx, int nz) =>
        GroundMesh.Skirt(h, -W / 2, -H / 2, W / 2, H / 2, nx, nz, 0, (x, z) => ground.ReliefAt(Coords.FromWorld(sheetXf * new Vector3(x, 0, z))) == null, Math.Min(W, H) * SeamProbe);

    static MeshInstance3D Node(Material mat, float lift, bool casts) => new()
    {
        MaterialOverride = mat,
        Position = new Vector3(0, lift, 0),
        CastShadow = casts ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
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
                kindsChanged |= _edgesOf![i] != grid.Kind[i] || _kindTexels![KindBytes * i + 1] != grid.Salt[i] || _kindTexels[KindBytes * i + 2] != grid.Detail[i];
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

    bool KindsKept => _grid != null && _kindTexels != null && _edgesOf != null && _detailOf != null && _fields != null;

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
                _kindTexels![KindBytes * i] = grid.Kind[i];
                _kindTexels[KindBytes * i + 1] = grid.Salt[i];
                _kindTexels[KindBytes * i + 2] = grid.Detail[i];
                _detailOf![i] = grid.Detail[i];
            }
        _edgesVersion++;
        GroundEdges.Derive(grid.Cols, grid.Rows, _edgesOf!, box, _fields!);
        Upload(ref _kindsTex, grid.Cols, grid.Rows, _kindTexels!, Image.Format.Rgb8);
        Upload(ref _edgesTex, grid.Cols, grid.Rows, _fields!, Image.Format.Rg8);
        _tiles?.StaleUv(UvOf(box.Grow(GroundEdges.Reach), grid));
        _tiles?.StaleUv(UvOf(box.Grow(UnderTheBrush), grid), urgent: true);
        if (_tiles != null) GroundTiles.For(this).Hurry();
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
        _tiles?.StaleUv(UvOf(softened.Grow(2), grid), urgent: true);
        if (_tiles != null) GroundTiles.For(this).Hurry();
        float W = (float)def.Size.W, H = (float)def.Size.H;
        var h = GroundMesh.WeldedHeights(ground, def);
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
        _tiles = GroundTiles.For(this).Track(def.Id, this, _bake, World, (float)Ground.FullWidth(def), 0, high * scale, paintedOnly, _redrawn, _painter, paintedOnly ? null : GroundCard.For(def));
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

    void UploadNear()
    {
        var (cols, rows, texels) = _painter.Near();
        bool fresh = _nearTex == null || _nearTex.GetWidth() != cols || _nearTex.GetHeight() != rows;
        Upload(ref _nearTex, cols, rows, texels, Image.Format.Rg8);
        if (!fresh) return;
        foreach (var laid in new[] { _base, _paint, _bake }) (laid as ShaderMaterial)?.SetShaderParameter(NearParameter, _nearTex!);
    }

    ShaderMaterial Material(bool paintedOnly, Vector2 cells, Shader? shader = null)
    {
        var m = new ShaderMaterial { Shader = shader ?? GroundShader };
        m.SetShaderParameter(NearParameter, _nearTex!);
        m.SetShaderParameter("kinds", _kindsTex!);
        m.SetShaderParameter("edges", _edgesTex!);
        m.SetShaderParameter("heights", _heightsTex!);
        m.SetShaderParameter("cell_world", _cellWorld);
        m.SetShaderParameter("palette", Palette());
        m.SetShaderParameter("painted_only", paintedOnly);
        m.SetShaderParameter("cells", cells);
        if (paintedOnly) m.RenderPriority = 1;
        return m;
    }

    bool Salted(ReliefGrid grid)
    {
        if (_kindTexels == null || _kindTexels.Length != grid.Salt.Length * KindBytes) return false;
        for (int i = 0; i < grid.Salt.Length; i++)
            if (_kindTexels[KindBytes * i + 1] != grid.Salt[i]) return false;
        return true;
    }

    bool UpdateKinds(ReliefGrid grid)
    {
        _grid = grid;
        bool changed = _edgesOf == null || _detailOf == null || !_edgesOf.AsSpan().SequenceEqual(grid.Kind) || !_detailOf.AsSpan().SequenceEqual(grid.Detail) || !Salted(grid);
        bool sized = _kindsTex != null && _edgesTex != null && _kindsTex.GetWidth() == grid.Cols && _kindsTex.GetHeight() == grid.Rows;
        if (sized && !changed) return false;
        _edgesOf = (byte[])grid.Kind.Clone();
        _detailOf = (byte[])grid.Detail.Clone();
        if (!sized)
        {
            UploadKinds(grid);
            UploadEdges(grid.Cols, grid.Rows, GroundEdges.Derive(grid.Cols, grid.Rows, grid.Kind));
            return true;
        }
        UploadKinds(grid);
        _edgesVersion++;
        if (_edgesBusy) { _edgesNext = (grid.Cols, grid.Rows, (byte[])grid.Kind.Clone()); return true; }
        DeriveEdges(grid.Cols, grid.Rows, (byte[])grid.Kind.Clone());
        return true;
    }

    void DeriveEdges(int cols, int rows, byte[] kinds)
    {
        _edgesBusy = true;
        int version = _edgesVersion;
        Task.Run(() =>
        {
            try
            {
                var fields = GroundEdges.Derive(cols, rows, kinds);
                Callable.From(() => EdgesDerived(cols, rows, version, fields)).CallDeferred();
            }
            catch (Exception e)
            {
                GD.PrintErr("[relief] deriving the ground's edges: " + e.Message);
                Callable.From(() => EdgesDerived(cols, rows, version, null)).CallDeferred();
            }
        });
    }

    void EdgesDerived(int cols, int rows, int version, byte[]? fields)
    {
        _edgesBusy = false;
        if (_edgesNext is { } next)
        {
            _edgesNext = null;
            DeriveEdges(next.Cols, next.Rows, next.Kinds);
        }
        if (fields == null || version != _edgesVersion || !GodotObject.IsInstanceValid(this) || _grid == null || _grid.Cols != cols || _grid.Rows != rows) return;
        UploadEdges(cols, rows, fields);
        _tiles?.StaleAll();
    }

    void UploadKinds(ReliefGrid grid)
    {
        if (_kindTexels == null || _kindTexels.Length != grid.Kind.Length * KindBytes) _kindTexels = new byte[grid.Kind.Length * KindBytes];
        var texels = _kindTexels;
        for (int i = 0; i < grid.Kind.Length; i++)
        {
            texels[KindBytes * i] = grid.Kind[i];
            texels[KindBytes * i + 1] = grid.Salt[i];
            texels[KindBytes * i + 2] = grid.Detail[i];
        }
        Upload(ref _kindsTex, grid.Cols, grid.Rows, texels, Image.Format.Rgb8);
    }

    void UploadEdges(int cols, int rows, byte[] fields)
    {
        _fields = fields;
        Upload(ref _edgesTex, cols, rows, fields, Image.Format.Rg8);
    }

    static void Upload(ref ImageTexture? tex, int cols, int rows, byte[] texels, Image.Format format)
    {
        var img = Image.CreateFromData(cols, rows, false, format, texels);
        if (tex != null && tex.GetWidth() == cols && tex.GetHeight() == rows) tex.Update(img);
        else tex = ImageTexture.CreateFromImage(img);
    }

    bool UpdateHeights(ReliefGrid grid, float scale, int blur)
    {
        bool calibrated = _unitsPerMetre > 0;
        bool streams = _streams && calibrated;
        bool sized = _heightsTex != null && _heightTexels != null && _heightsTex.GetWidth() == grid.Cols && _heightsTex.GetHeight() == grid.Rows;
        bool heightsChanged = !sized || _heightsOf == null || _heightsScale != scale || _heightsBlur != blur || !_heightsOf.AsSpan().SequenceEqual(grid.H);
        bool kindsChanged = calibrated && (_flowKinds == null || !_flowKinds.AsSpan().SequenceEqual(grid.Kind));
        bool streamsChanged = _heightsStreams != streams;
        if (!heightsChanged && !kindsChanged && !streamsChanged) return false;
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
            return true;
        }
        if (!heightsChanged && !kindsChanged)
        {
            if (_flow != null && _flow.Order.Length * 4 == _heightTexels!.Length) WriteFlow(_flow);
            UploadHeights(grid.Cols, grid.Rows);
            return true;
        }
        _flowKinds = (byte[])grid.Kind.Clone();
        soft ??= _soft is { } kept && kept.Length == grid.H.Length ? kept : GroundHeights.Blurred(grid, blur);
        _soft = soft;
        double cellMetres = _cellWorld / _unitsPerMetre, metresPerUnit = scale / _unitsPerMetre;
        if (heightsChanged) UploadHeights(grid.Cols, grid.Rows);
        if (_flowBusy) { _flowNext = (grid.Cols, grid.Rows, (float[])soft.Clone(), _flowKinds, cellMetres, metresPerUnit); return heightsChanged || streamsChanged; }
        DeriveFlow(grid.Cols, grid.Rows, (float[])soft.Clone(), _flowKinds, cellMetres, metresPerUnit);
        return heightsChanged || streamsChanged;
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
        var changed = WriteFlow(flow);
        if (changed.Empty) return;
        UploadHeights(cols, rows);
        if (_grid != null) _tiles?.StaleUv(UvOf(changed.Grow(DrawnAround).Clip(cols, rows), _grid));
    }

    CellBox WriteFlow(GroundFlow flow)
    {
        var texels = _heightTexels!;
        var changed = CellBox.None;
        int cols = _heightsTex?.GetWidth() ?? 0;
        for (int i = 0; i < flow.Order.Length; i++)
        {
            float packed = GroundStreams.Packed(flow, i), order = _heightsStreams ? flow.Order[i] : 0;
            if (texels[4 * i + 2] == packed && texels[4 * i + 3] == order) continue;
            texels[4 * i + 2] = packed;
            texels[4 * i + 3] = order;
            if (cols > 0) changed = changed.Union(new CellBox(i % cols, i / cols, i % cols, i / cols));
        }
        return changed;
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
