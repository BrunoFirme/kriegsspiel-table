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
    ReliefSurface _surface = null!;
    public int TopZ;
    public Func<SheetNode, Vector3[], bool>? Covered;
    internal SheetPicture? Picture { get; private set; }
    public event Action? IndexKnown;
    ShaderMaterial? _mat;
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
        n._mat = new ShaderMaterial { Shader = Shading(Look.Solid) };
        n._mat.SetShaderParameter(TintParameter, Unpictured);
        n.Fade(def.Seen);
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
        _mat.SetShaderParameter(PartsParameter, tex);
        _mat.SetShaderParameter(AcrossParameter, new Vector2I(Picture.Shape.Across, Picture.Shape.Down));
        _mat.SetShaderParameter(SpanParameter, Picture.Shape.Span(Picture.Shown));
        _mat.SetShaderParameter(ShareParameter, Picture.Shape.Share);
        _mat.SetShaderParameter(InsetParameter, Picture.Shape.Inset);
        _mat.SetShaderParameter(PicturedParameter, true);
        _mat.SetShaderParameter(TintParameter, Colors.White);
        Fade(Def.Seen);
    }

    public void Fade(double opacity)
    {
        if (_mat == null) return;
        bool faded = opacity < 1;
        var tint = _mat.GetShaderParameter(TintParameter).AsColor();
        _mat.SetShaderParameter(TintParameter, tint with { A = faded ? (float)opacity : 1 });
        _look = faded ? Look.SeenThrough : Picture?.HasAlpha == true ? Look.CutOut : Look.Solid;
        _mat.Shader = Shading(_look);
    }

    public float Opacity => _mat?.GetShaderParameter(TintParameter).AsColor().A ?? 1;
    public bool SeenThrough => _mat != null && _look == Look.SeenThrough;

    enum Look { Solid, CutOut, SeenThrough }

    const string ShaderPath = "res://shaders/sheet.gdshader", ShaderHead = "shader_type spatial;";
    const string PartsParameter = "parts", AcrossParameter = "parts_across", SpanParameter = "span", ShareParameter = "share", InsetParameter = "inset", TintParameter = "tint", PicturedParameter = "pictured";
    static readonly System.Collections.Generic.Dictionary<Look, Shader> Shaders = new();
    Look _look;

    static Shader Shading(Look look)
    {
        if (Shaders.TryGetValue(look, out var made)) return made;
        var code = Godot.FileAccess.GetFileAsString(ShaderPath);
        string? define = look switch { Look.CutOut => "CUT_OUT", Look.SeenThrough => "SEEN_THROUGH", _ => null };
        if (define != null) code = code.Replace(ShaderHead, $"{ShaderHead}\n#define {define}");
        return Shaders[look] = new Shader { Code = code };
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
        if (on) return;
        _outline?.QueueFree();
        _outline = null;
        _drawnThick = 0;
    }

    public void RefreshOutline() => _drawnThick = 0;

    public void DrawOutline(float thick, bool cornersOnly)
    {
        if (!_selected) return;
        if (_outline != null && cornersOnly == _drawnCorners && Mathf.Abs(thick - _drawnThick) <= _drawnThick * OutlineSlack) return;
        _drawnThick = thick;
        _drawnCorners = cornersOnly;
        var im = new ImmediateMesh();
        float w = (float)Def.Size.W / 2, h = (float)Def.Size.H / 2;
        var lift = Lift();
        Band(im, SelectionAura.Casing, w, h, thick, thick * CasingShare, cornersOnly, lift);
        Band(im, SelectionAura.Picked, w, h, 0, thick, cornersOnly, lift);
        if (_outline == null)
        {
            _outline = new MeshInstance3D { Layers = Table.LiveOnlyLayer };
            AddChild(_outline);
        }
        _outline.Mesh = im;
    }

    void Band(ImmediateMesh im, Color colour, float w, float h, float from, float wide, bool cornersOnly, Func<float, float, float> lift)
    {
        var mat = new StandardMaterial3D
        {
            AlbedoColor = colour, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, NoDepthTest = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha, RenderPriority = OutlinePriority, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        var corners = new[] { (-w, -h, -1f, -1f), (w, -h, 1f, -1f), (w, h, 1f, 1f), (-w, h, -1f, 1f) };
        int along = cornersOnly ? 1 : _relief.Ground.Any ? OutlineSteps : 1;
        float mark = Math.Min(_drawnThick * CornerMarkLength, Math.Min(w, h));
        im.SurfaceBegin(Mesh.PrimitiveType.Triangles, mat);
        for (int k = 0; k < corners.Length; k++)
        {
            var (ax, az, asx, asz) = corners[k];
            var (bx, bz, bsx, bsz) = corners[(k + 1) % corners.Length];
            float length = Mathf.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az));
            float share = cornersOnly ? Math.Min(0.5f, mark / Math.Max(length, 1e-6f)) : 1f / along;
            var spans = cornersOnly ? new[] { (0f, share), (1 - share, 1f) } : Enumerable.Range(0, along).Select(i => (i * share, (i + 1) * share)).ToArray();
            foreach (var (f0, f1) in spans)
            {
                Vector3 At(float f, float off)
                {
                    float x = Mathf.Lerp(ax, bx, f), z = Mathf.Lerp(az, bz, f), sx = Mathf.Lerp(asx, bsx, f), sz = Mathf.Lerp(asz, bsz, f);
                    return new Vector3(x + sx * off, lift(x, z) + OutlineAbove, z + sz * off);
                }
                Vector3 a0 = At(f0, from), a1 = At(f0, from + wide), b0 = At(f1, from), b1 = At(f1, from + wide);
                foreach (var v in new[] { a0, a1, b1, a0, b1, b0 }) im.SurfaceAddVertex(v);
            }
        }
        im.SurfaceEnd();
    }

    float _drawnThick;
    bool _drawnCorners;
    const float OutlineSlack = 0.04f, CasingShare = 0.5f, CornerMarkLength = 9;
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
        var grid = _relief.Shaped(Def);
        _works = _relief.Works(Def);
        if (where is { } box && grid != null && _surface.Patch(Def, grid, _relief.Ground, Transform, box)) { Patched++; return; }
        Built++;
        _surface.Build(Def, grid ?? Bare, _relief.Ground, Transform, Covered == null ? null : corners => Covered(this, corners), _mat, BuiltHere?.Invoke());
        RefreshOutline();
    }

    public Func<IReadOnlyList<BuiltThing>>? BuiltHere { get; set; }

    long _works;

    public void Rebuilt()
    {
        if (_relief.Works(Def) != _works) _relief.Invalidate();
        else if (!_surface.Rebuilt(Def, _relief.Shaped(Def) ?? Bare, BuiltHere?.Invoke() ?? [])) Reground();
    }

    static readonly ReliefGrid Bare = new(2, 2);

    public bool ContainsWorld(Vector3 world)
    {
        var l = ToLocal(world);
        return Mathf.Abs(l.X) <= Def.Size.W / 2 && Mathf.Abs(l.Z) <= Def.Size.H / 2;
    }
}
