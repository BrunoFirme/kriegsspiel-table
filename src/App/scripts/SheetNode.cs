using System;
using System.IO;
using Godot;
using Json = KTable.Core.Json;
using KTable.Core.Model;

namespace KTable.App;

public partial class SheetNode : Node3D
{
    public SheetDef Def { get; private set; } = null!;
    Assets _assets = null!;
    ReliefCache _relief = null!;
    ReliefSurface _surface = null!;
    public int TopZ;
    public Func<SheetNode, Vector3[], bool>? Covered;
    internal SheetPicture? Picture { get; private set; }
    public event Action? IndexKnown;
    StandardMaterial3D? _mat;
    MeshInstance3D? _outline;
    bool _selected;
    static readonly Color Unpictured = new(0.82f, 0.78f, 0.68f);

    public bool Loaded => Picture?.Done ?? !Def.HasPicture;
    public int TilesLoaded => Picture?.TilesLoaded ?? 0;
    public int TilesTotal => Picture?.TilesTotal ?? 0;

    public static SheetNode Create(SheetDef def, Assets assets, ReliefCache relief)
    {
        var n = new SheetNode { _assets = assets, _relief = relief };
        n.UpdateDef(def);
        n._surface = new ReliefSurface();
        n.AddChild(n._surface);
        if (!def.HasPicture) return n;
        n._mat = new StandardMaterial3D
        {
            Roughness = 1f, Metallic = 0f, SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
            TextureRepeat = false,
            AlbedoColor = Unpictured,
        };
        var tilesRel = Path.Combine("tiles", def.Image);
        assets.EnsureFile(def.Package, Path.Combine(tilesRel, "index.json"), () => n.OnIndex(tilesRel));
        return n;
    }

    void OnIndex(string tilesRel)
    {
        TileIndex index;
        try { index = Json.Load<TileIndex>(_assets.Abs(Def.Package, Path.Combine(tilesRel, "index.json"))); }
        catch (Exception e) when (e is IOException or System.Text.Json.JsonException or UnauthorizedAccessException) { GD.PushError($"sheet {Def.Id}: bad tile index: {e.Message}"); return; }
        if (index.Levels.Count == 0) return;
        Picture = new SheetPicture(_assets, Def.Package, tilesRel, index);
        Picture.Changed += ShowPicture;
        IndexKnown?.Invoke();
    }

    void ShowPicture()
    {
        if (_mat == null || Picture?.Texture is not { } tex) return;
        _mat.AlbedoTexture = tex;
        _mat.AlbedoColor = Colors.White;
        _mat.Transparency = Picture.HasAlpha ? BaseMaterial3D.TransparencyEnum.AlphaScissor : BaseMaterial3D.TransparencyEnum.Disabled;
    }

    public override void _ExitTree() => Picture?.Drop();

    public void UpdateDef(SheetDef def)
    {
        Def = def;
        Position = Coords.ToWorld(def.Pos, def.Z * ZStep);
        Rotation = new Vector3(0, Coords.Yaw(def.RotDeg), 0);
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

    public bool Touches(Rect2? where) => where is not { } box || WorldBox().Intersects(box);

    Rect2 WorldBox()
    {
        var xf = Transform;
        float w = (float)Def.Size.W / 2, h = (float)Def.Size.H / 2;
        var first = xf * new Vector3(-w, 0, -h);
        var r = new Rect2(new Vector2(first.X, first.Z), Vector2.Zero);
        foreach (var (sx, sz) in new[] { (1, -1), (1, 1), (-1, 1) })
        {
            var p = xf * new Vector3(sx * w, 0, sz * h);
            r = r.Expand(new Vector2(p.X, p.Z));
        }
        return r;
    }

    public int Built { get; private set; }
    public int Patched { get; private set; }

    public void Reground(Rect2? where = null)
    {
        var grid = _relief.Grid(Def);
        if (where is { } box && grid != null && _surface.Patch(Def, grid, _relief.Ground, Transform, box)) { Patched++; return; }
        Built++;
        _surface.Build(Def, grid ?? Bare, _relief.Ground, Transform, Covered == null ? null : corners => Covered(this, corners), _mat);
        RefreshOutline();
    }

    static readonly ReliefGrid Bare = new(2, 2);

    public bool ContainsWorld(Vector3 world)
    {
        var l = ToLocal(world);
        return Mathf.Abs(l.X) <= Def.Size.W / 2 && Mathf.Abs(l.Z) <= Def.Size.H / 2;
    }
}
