using System;
using System.Collections.Generic;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

public partial class ReliefSurface : Node3D
{
    const int MostVertsAcross = 384;
    const int BlockCells = 48;
    const float PaintAbove = 0.003f;
    const float SeamProbe = 0.01f;
    static readonly Color Earth = new(0.42f, 0.33f, 0.24f);
    static Shader? _shader;

    readonly List<(MeshInstance3D node, Rect2 box, bool paint)> _blocks = new();
    MeshInstance3D? _skirt;
    ShaderMaterial? _base, _paint;
    ImageTexture? _kindsTex;

    static Shader GroundShader => _shader ??= new Shader { Code = FileAccess.GetFileAsString("res://shaders/ground.gdshader") };

    public static Vector4[] Palette()
    {
        var p = new Vector4[GroundKinds.All.Count];
        for (int i = 0; i < p.Length; i++) { var c = Color.FromHtml(GroundKinds.All[i].Color); p[i] = new Vector4(c.R, c.G, c.B, c.A); }
        return p;
    }

    public void Build(SheetDef def, ReliefGrid grid, Ground ground, Transform3D sheetXf, float paintLift, Func<Vector3[], bool>? covered = null)
    {
        Clear();
        var c = def.Crop ?? SheetGeometry.Whole;
        float W = (float)def.Size.W, H = (float)def.Size.H;
        float x0 = -W / 2, x1 = W / 2, z0 = -H / 2, z1 = H / 2;
        int cellsX = Math.Max(1, (int)Math.Ceiling(c.W * (grid.Cols - 1))), cellsZ = Math.Max(1, (int)Math.Ceiling(c.H * (grid.Rows - 1)));
        int step = Math.Max(1, (int)Math.Ceiling(Math.Max(cellsX, cellsZ) / (double)MostVertsAcross));
        int nx = Math.Max(1, cellsX / step), nz = Math.Max(1, cellsZ / step);
        var h = GroundMesh.Heights(ground, sheetXf);
        UpdateKinds(grid);
        var cells = new Vector2(grid.Cols, grid.Rows);
        if (!def.HasPicture) _base = Material(false, cells);
        if (grid.Painted) _paint = Material(true, cells);
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
                if (_base != null && covered?.Invoke(corners) != true) Add(GroundMesh.Grid(h, ax, az, bxw, bzw, ex - bx, ez - bz, uv0, uv1), _base, box, false, 0);
                if (_paint != null) Add(GroundMesh.Grid(h, ax, az, bxw, bzw, ex - bx, ez - bz, uv0, uv1), _paint, box, true, paintLift + PaintAbove);
            }
        _skirt = new MeshInstance3D
        {
            Mesh = GroundMesh.Skirt(h, x0, z0, x1, z1, nx, nz, -sheetXf.Origin.Y, (x, z) => ground.ReliefAt(Coords.FromWorld(sheetXf * new Vector3(x, 0, z))) == null, Math.Min(W, H) * SeamProbe),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = Earth, Roughness = 1, CullMode = BaseMaterial3D.CullModeEnum.Disabled, SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_skirt);
    }

    void Add(ArrayMesh mesh, Material mat, Rect2 box, bool paint, float lift)
    {
        var node = new MeshInstance3D { Mesh = mesh, MaterialOverride = mat, Position = new Vector3(0, lift, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(node);
        _blocks.Add((node, box, paint));
    }

    ShaderMaterial Material(bool paintedOnly, Vector2 cells)
    {
        var m = new ShaderMaterial { Shader = GroundShader };
        m.SetShaderParameter("kinds", _kindsTex!);
        m.SetShaderParameter("palette", Palette());
        m.SetShaderParameter("painted_only", paintedOnly);
        m.SetShaderParameter("cells", cells);
        if (paintedOnly) m.RenderPriority = 1;
        return m;
    }

    void UpdateKinds(ReliefGrid grid)
    {
        var img = Image.CreateFromData(grid.Cols, grid.Rows, false, Image.Format.R8, grid.Kind);
        if (_kindsTex != null && _kindsTex.GetWidth() == grid.Cols && _kindsTex.GetHeight() == grid.Rows) _kindsTex.Update(img);
        else _kindsTex = ImageTexture.CreateFromImage(img);
    }

    public void Clear()
    {
        foreach (var (node, _, _) in _blocks) node.QueueFree();
        _blocks.Clear();
        _skirt?.QueueFree(); _skirt = null;
        _base = null; _paint = null;
    }
}
