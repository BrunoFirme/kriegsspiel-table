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

    readonly List<(MeshInstance3D node, Rect2 box, bool paint)> _blocks = new();
    MeshInstance3D? _skirt;
    Material? _base;
    ShaderMaterial? _paint;
    ImageTexture? _kindsTex;
    ImageTexture? _edgesTex;
    byte[]? _edgesOf;
    byte[]? _roads;
    bool _edgesBusy;
    (int Cols, int Rows, byte[] Kinds)? _edgesNext;
    ReliefGrid? _grid;
    ImageTexture? _heightsTex;
    float[]? _heightsOf;
    float[]? _heightTexels;
    float _heightsScale;
    int _heightsBlur;
    bool _heightsStreams;
    byte[]? _flowKinds;
    GroundFlow? _flow;
    bool _flowBusy;
    (int Cols, int Rows, float[] Soft, byte[] Kinds, double CellMetres, double MetresPerUnit)? _flowNext;
    float _cellWorld = 1;
    static bool _contours, _streams;
    static double _unitsPerMetre;
    static double _contourStep = -1;

    const string StyleParameter = "ground_style", ContourParameter = "contour_interval", ScaleParameter = "units_per_metre", SnowParameter = "snow_line";

    public static void ShowScale(Calibration c)
    {
        _unitsPerMetre = c.UnitsPerMeter is { } upm && upm > 0 ? upm : 0;
        RenderingServer.GlobalShaderParameterSet(ScaleParameter, (float)_unitsPerMetre);
    }

    public static bool ShowStreams(bool on)
    {
        if (_streams == on) return false;
        _streams = on;
        return true;
    }

    static Shader GroundShader => _shader ??= new Shader { Code = FileAccess.GetFileAsString("res://shaders/ground.gdshader") };

    public static void ShowStyle(string? style) => RenderingServer.GlobalShaderParameterSet(StyleParameter, MapStyles.Index(style));

    public static bool ShowContours(bool on)
    {
        if (_contours == on) return false;
        _contours = on;
        return true;
    }

    public static void SnowLine(double height) => RenderingServer.GlobalShaderParameterSet(SnowParameter, (float)height);

    public static void ContourStep(double step)
    {
        if (step == _contourStep) return;
        _contourStep = step;
        RenderingServer.GlobalShaderParameterSet(ContourParameter, (float)step);
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
        UpdateKinds(grid);
        UpdateHeights(grid, (float)(Ground.FullWidth(def) * (def.Relief is { Length: > 0 } ? def.Height : 1)), Math.Max(step, (int)Math.Round(ContourBlurUnits / _cellWorld)));
        var cells = new Vector2(grid.Cols, grid.Rows);
        bool drawsGround = picture == null && !def.HasPicture;
        _base = picture ?? (drawsGround ? Material(false, cells) : null);
        if (grid.Painted || (_contours && def.Relief is { Length: > 0 })) _paint = Material(true, cells);
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
                if (_base != null && !hidden) Add(GroundMesh.Grid(h, ax, az, bxw, bzw, ex - bx, ez - bz, uv0, uv1), _base, box, false, level);
                if (_paint != null && (hidden || !drawsGround)) Add(GroundMesh.Grid(h, ax, az, bxw, bzw, ex - bx, ez - bz, uv0, uv1), _paint, box, true, level + PaintAbove);
            }
        _skirt = new MeshInstance3D
        {
            Mesh = GroundMesh.Skirt(h, x0, z0, x1, z1, nx, nz, 0, (x, z) => ground.ReliefAt(Coords.FromWorld(sheetXf * new Vector3(x, 0, z))) == null, Math.Min(W, H) * SeamProbe),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = Earth, Roughness = 1, CullMode = BaseMaterial3D.CullModeEnum.Disabled, SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Position = new Vector3(0, level, 0),
        };
        AddChild(_skirt);
    }

    void Add(ArrayMesh mesh, Material mat, Rect2 box, bool paint, float lift)
    {
        var node = new MeshInstance3D
        {
            Mesh = mesh,
            MaterialOverride = mat,
            Position = new Vector3(0, lift, 0),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Layers = Table.WorldLayer | GroundLayer,
        };
        AddChild(node);
        _blocks.Add((node, box, paint));
    }

    ShaderMaterial Material(bool paintedOnly, Vector2 cells)
    {
        var m = new ShaderMaterial { Shader = GroundShader };
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

    void UpdateKinds(ReliefGrid grid)
    {
        _grid = grid;
        bool changed = _edgesOf == null || !_edgesOf.AsSpan().SequenceEqual(grid.Kind);
        bool sized = _kindsTex != null && _edgesTex != null && _kindsTex.GetWidth() == grid.Cols && _kindsTex.GetHeight() == grid.Rows;
        if (sized && !changed) return;
        _edgesOf = (byte[])grid.Kind.Clone();
        if (!sized)
        {
            var (roads, fields) = GroundEdges.Derive(grid.Cols, grid.Rows, grid.Kind);
            _roads = roads;
            UploadKinds(grid);
            UploadEdges(grid.Cols, grid.Rows, fields);
            return;
        }
        UploadKinds(grid);
        if (_edgesBusy) { _edgesNext = (grid.Cols, grid.Rows, _edgesOf); return; }
        DeriveEdges(grid.Cols, grid.Rows, _edgesOf);
    }

    void DeriveEdges(int cols, int rows, byte[] kinds)
    {
        _edgesBusy = true;
        Task.Run(() =>
        {
            try
            {
                var derived = GroundEdges.Derive(cols, rows, kinds);
                Callable.From(() => EdgesDerived(cols, rows, derived.Roads, derived.Fields)).CallDeferred();
            }
            catch (Exception e)
            {
                GD.PrintErr("[relief] deriving the ground's edges: " + e.Message);
                Callable.From(() => EdgesDerived(cols, rows, null, null)).CallDeferred();
            }
        });
    }

    void EdgesDerived(int cols, int rows, byte[]? roads, byte[]? fields)
    {
        _edgesBusy = false;
        if (_edgesNext is { } next)
        {
            _edgesNext = null;
            DeriveEdges(next.Cols, next.Rows, next.Kinds);
        }
        if (roads == null || fields == null || !GodotObject.IsInstanceValid(this) || _grid == null || _grid.Cols != cols || _grid.Rows != rows) return;
        _roads = roads;
        UploadKinds(_grid);
        UploadEdges(cols, rows, fields);
    }

    void UploadKinds(ReliefGrid grid)
    {
        var roads = _roads is { } r && r.Length == grid.Kind.Length ? r : null;
        var texels = new byte[grid.Kind.Length * 4];
        for (int i = 0; i < grid.Kind.Length; i++)
        {
            texels[4 * i] = grid.Kind[i];
            texels[4 * i + 1] = grid.Salt[i];
            texels[4 * i + 2] = grid.Detail[i];
            texels[4 * i + 3] = roads?[i] ?? GroundEdges.Far;
        }
        var img = Image.CreateFromData(grid.Cols, grid.Rows, false, Image.Format.Rgba8, texels);
        if (_kindsTex != null && _kindsTex.GetWidth() == grid.Cols && _kindsTex.GetHeight() == grid.Rows) _kindsTex.Update(img);
        else _kindsTex = ImageTexture.CreateFromImage(img);
    }

    void UploadEdges(int cols, int rows, byte[] fields)
    {
        var img = Image.CreateFromData(cols, rows, false, Image.Format.Rgba8, fields);
        if (_edgesTex != null && _edgesTex.GetWidth() == cols && _edgesTex.GetHeight() == rows) _edgesTex.Update(img);
        else _edgesTex = ImageTexture.CreateFromImage(img);
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
        soft ??= GroundHeights.Blurred(grid, blur);
        double cellMetres = _cellWorld / _unitsPerMetre, metresPerUnit = scale / _unitsPerMetre;
        if (heightsChanged) UploadHeights(grid.Cols, grid.Rows);
        if (_flowBusy) { _flowNext = (grid.Cols, grid.Rows, soft, _flowKinds, cellMetres, metresPerUnit); return; }
        DeriveFlow(grid.Cols, grid.Rows, soft, _flowKinds, cellMetres, metresPerUnit);
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
        if (_flowNext is { } next)
        {
            _flowNext = null;
            DeriveFlow(next.Cols, next.Rows, next.Soft, next.Kinds, next.CellMetres, next.MetresPerUnit);
        }
        if (flow == null || !GodotObject.IsInstanceValid(this)) return;
        if (_heightTexels == null || _heightTexels.Length != flow.Order.Length * 4 || _heightsTex == null || _heightsTex.GetWidth() != cols || _heightsTex.GetHeight() != rows) return;
        _flow = flow;
        WriteFlow(flow);
        UploadHeights(cols, rows);
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
        foreach (var (node, _, _) in _blocks) node.QueueFree();
        _blocks.Clear();
        _skirt?.QueueFree();
        _skirt = null;
        _base = null;
        _paint = null;
    }
}
