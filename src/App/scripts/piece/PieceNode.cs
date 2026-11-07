using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

public partial class PieceNode : Node3D
{
    public string Layer { get; private set; } = "";
    public PieceInstance Piece { get; private set; } = null!;
    public PieceDef? Def { get; private set; }
    public bool Ghost { get; private set; }
    public bool PrintingOnly { get; private set; }
    public Vector2 Footprint { get; private set; } = Vector2.One;
    public string Shape { get; private set; } = "box";
    public double BuiltScale { get; private set; } = 1;
    internal (int x, int z)? Cell;

    Session _s = null!;
    internal TableSounds? Sounds;
    MeshInstance3D? _sel, _model, _body, _addOn;
    StandardMaterial3D? _modelMat;
    StandardMaterial3D? _sideMat;
    ShaderMaterial? _selMat;
    float _thickness = 0.2f;
    Color _layerTint = Colors.White;
    int _shownState = -1;
    string? _shownPicture;
    string? _shownDef;
    string? _shownTint;
    string? _texPackage, _texRel;
    int _texSize;
    bool _texLoaded;
    Quaternion _modelRest = Quaternion.Identity;
    Vector3 _modelRestPos;
    const float CutoutFraction = 0.2f;

    static Shader? _outlineShader;
    static Shader OutlineShader => _outlineShader ??= new Shader
    {
        Code = """
            shader_type spatial;
            render_mode unshaded, cull_disabled, shadows_disabled;
            uniform sampler2D tex : source_color, filter_linear_mipmap;
            uniform vec4 color : source_color = vec4(1.0, 0.85, 0.1, 1.0);
            uniform bool use_alpha = false;
            void fragment() {
                ALBEDO = color.rgb;
                ALPHA = use_alpha ? texture(tex, UV).a : 1.0;
                ALPHA_SCISSOR_THRESHOLD = 0.5;
            }
            """,
    };

    readonly PieceLettering _lettering;
    readonly PieceBack _back;
    readonly PieceWidgets _widgets;
    readonly PieceUi _plates;
    readonly PieceLines _lines;
    readonly PieceDecals _decals;
    readonly PiecePrints _prints;
    readonly NoteFace _note;

    public PieceNode()
    {
        _lettering = new PieceLettering(this);
        _back = new PieceBack(this);
        _widgets = new PieceWidgets(this);
        _plates = new PieceUi(this);
        _lines = new PieceLines(this);
        _decals = new PieceDecals(this);
        _prints = new PiecePrints(this);
        _note = new NoteFace(this);
    }

    internal Session Session => _s;

    public static PieceNode Create(Session s, string layer, PieceInstance p, bool ghost = false, Color? layerTint = null, bool lettered = true, bool printingOnly = false)
    {
        var n = new PieceNode { _s = s, Layer = layer, Piece = p, Ghost = ghost, _layerTint = layerTint ?? Colors.White, Lettered = lettered, PrintingOnly = printingOnly };
        n.Build();
        return n;
    }

    PieceStateDef? CurState => Def != null && Piece.State >= 0 && Piece.State < Def.States.Count ? Def.States[Piece.State] : null;

    PieceArt Art => _s.Art;

    readonly List<(MeshInstance3D node, int surface)> _edgeSlots = new();
    Color _edgeColor = Colors.Transparent;

    void PaintEdges(Color c)
    {
        _edgeColor = c;
        _sideMat = Art.Edge(c);
        foreach (var (node, surface) in _edgeSlots)
            if (IsInstanceValid(node))
            {
                if (surface < 0) node.MaterialOverride = _sideMat;
                else node.SetSurfaceOverrideMaterial(surface, _sideMat);
            }
    }

    StandardMaterial3D Print(bool scissor)
    {
        var (pkg, rel, size) = TexKey();
        return Art.Print(pkg, rel, size, PrintTint(), scissor);
    }

    void Build()
    {
        Def = _s.Def(Piece.Def);
        var worn = WornModel;
        var shown = worn ?? Def;
        var size = shown?.Size ?? new Size2(1.5, 0.75);
        if (worn == null && CurState?.Size is { } ss) size = ss;
        float w = (float)(size.W * Piece.Scale), h = (float)(size.H * Piece.Scale);
        Footprint = new Vector2(w, h);
        BuiltScale = Piece.Scale;
        _thickness = (float)Mathf.Clamp((shown?.Thickness ?? 0.2) * Piece.Scale, 0.02, 2);
        Shape = worn?.Shape ?? CurState?.Shape ?? Def?.Shape ?? "box";
        if (Shape == "box" && shown?.Kind != "model" && !IsEffect && TexKey() is { pkg: not null } k && _s.Assets.Mask(k.pkg, k.rel!, k.size) is { } mk && mk.TransparentFraction > 0.01f)
            Shape = mk.TransparentFraction > CutoutFraction ? "alpha" : "rounded";
        _shownState = Piece.State;
        _shownDef = Piece.Def;
        _shownPicture = Piece.Picture;
        _shownTint = Piece.TintOverride;
        var tint = Tint();

        if (IsEffect) { Shape = Def!.Effect != null ? "box" : "circle"; BuildEffect(); }
        else if (PrintingOnly) { if (Def?.Kind != "model") LoadTexture(); }
        else if (shown?.Kind == "model" && CurrentMesh() is { } meshHash)
        {
            BuildModel(meshHash, w, h, tint);
            if (Def?.Effect != null) BuildEffect();
        }
        else if (Shape == "alpha")
            BuildCutout(tint);
        else
            BuildPolygon();
        if (!PrintingOnly && !IsEffect && Def is { Kind: not "model", AddOnMesh: { } addOn }) BuildAddOn(addOn, tint);

        if (Lettered) _lettering.Add();
        BuildLiveText();
        _shownFaceDown = Piece.FaceDown;
        Refresh(Piece);
    }

    public bool Lettered { get; private set; } = true;

    public void LetterForPicture(bool on)
    {
        _lettering.ForPicture(on);
        _widgets.ForPicture(on);
        _prints.ForPicture(on);
        _note.ForPicture(on);
    }

    public void SetLettered(bool on)
    {
        if (on == Lettered) return;
        Lettered = on;
        if (on)
        {
            _lettering.Add();
            _lettering.Relayout();
            SyncWidgets(force: true);
            if (_sel != null) SetMaskLayer(true);
            if (_hovered) SetAuraLayer(SelectionAura.HoverLayer, true);
        }
        else
        {
            _lettering.Drop();
            SyncWidgets(force: true);
        }
    }

    public float LetteringRange() =>
        Mathf.Clamp(Mathf.Max(Footprint.X, Footprint.Y) * Look.LetteringReach, 12f, Mathf.Min(3000f, Look.LetteringReach * 12f));

    internal PieceLook Look => _s.Art.Look;

    public int LetteringWeight => 2 + Piece.Widgets.Count;

    public float BodyRange() => Mathf.Clamp(Mathf.Max(Footprint.X, Footprint.Y) * 80f, 30f, 1500f);

    MeshInstance3D? _far;

    void AddFarQuad(Material face)
    {
        _far = new MeshInstance3D
        {
            Mesh = Art.Plane(Footprint), Position = new Vector3(0, _thickness, 0),
            MaterialOverride = face, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            VisibilityRangeBegin = BodyRange(),
        };
        AddChild(_far);
    }

    public bool ScriptDrawingShows => Piece.State == Piece.DrawnOnState;

    public void SyncWidgets(bool force = false)
    {
        SyncUi(force);
        _widgets.Sync(force);
        _prints.Sync();
        _decals.Sync(force);
        _note.Sync(force);
    }

    public void SyncUi(bool force = false) => _plates.Sync(force);
    public void PanelChanged(KTable.Core.Protocol.Command c) => _plates.Changed(c);
    public (string Id, Control Control, Vector2 Pixel)? PanelAt(Vec2 point) => _flipping ? null : _plates.ControlAt(Coords.ToLocal(point, Piece.Pos, Piece.RotDeg));
    public void PressPanel((string Id, Control Control, Vector2 Pixel) at) => _plates.Press(at.Id, at.Control, at.Pixel);

    public void SyncLines() => _lines.Sync();

    public int DrawnWidgets => _widgets.Drawn;

    public void SetWidgetHover(int index) => _widgets.SetHover(index);

    public void FlashWidget(int index) => _widgets.Flash(index);

    public void SetWidgetText(int index, string text) { _widgets.SetEditText(index, text); SyncWidgets(force: true); }
    public void ClearWidgetText()
    {
        if (!_widgets.Editing) return;
        _widgets.ClearEditText();
        SyncWidgets(force: true);
    }

    public (int index, Widget widget)? WidgetAt(Vec2 point) => _flipping ? null : _widgets.At(Coords.ToLocal(point, Piece.Pos, Piece.RotDeg));

    public bool? NotePartAt(Vec2 point) => _flipping ? null : _note.PartAt(Coords.ToLocal(point, Piece.Pos, Piece.RotDeg));

    public string NoteLine(bool body) => _note.Line(body);

    public void SetNoteText(bool body, string text) => _note.SetEditing(body, text);

    public void ClearNoteText() => _note.StopEditing();

    public Vec2? FacePoint(Vector2 screen)
    {
        var cam = GetViewport()?.GetCamera3D();
        if (cam == null) return null;
        var o = cam.ProjectRayOrigin(screen);
        var d = cam.ProjectRayNormal(screen);
        var up = Up;
        var face = Position + up * ((Piece.FaceDown ? 0 : _thickness) + (Piece.FaceDown ? PieceBack.Below : 0.006f));
        float facing = d.Dot(up);
        if (Mathf.Abs(facing) < 1e-5f) return null;
        float t = (face - o).Dot(up) / facing;
        if (t < 0) return null;
        var level = _tilt.Inverse() * (o + d * t - Position);
        return Coords.FromWorld(Position + new Vector3(level.X, 0, level.Z));
    }

    Color Tint() => Spoken ? Colors.Transparent : Coords.ParseColor(Piece.TintOverride ?? Def?.Tint, Colors.White) * _layerTint;
    Color SideColor() => (Tint() * 0.82f) with { A = Tint().A };
    Color PrintTint() => Def is { TintsPictures: true } ? Tint() : _layerTint with { A = _layerTint.A * Tint().A };

    (string? pkg, string? rel, int size) TexKey()
    {
        if (Spoken) return (null, null, 0);
        if (Piece.FacePicture is { } fp && !Piece.FaceDown && KTable.Client.PieceFiles.DecalFile(_s.View, _s.Store, fp, Piece.Def) is { } own)
            return (own.Package, own.Path, Assets.PieceTexSize(Mathf.Max(Footprint.X, Footprint.Y)));
        var pack = _s.Store.PackOfDef(PictureDefId);
        var img = CurrentImage();
        if (pack == null || img == null || !pack.Assets.TryGetValue(img, out var a)) return (null, null, 0);
        return (pack.Id, a.File, Assets.PieceTexSize(Mathf.Max(Footprint.X, Footprint.Y)));
    }

    public float Thickness => _thickness;
    public float ModelTop => _plates.ModelTop;
    public float BaseY(PieceInstance p) => BaseAt(p.Pos, p.RotDeg, p.Y, p.Stack);

    float BaseAt(Vec2 pos, double rotDeg, double y, int stack)
    {
        float ground = GroundUnder(pos, rotDeg);
        return 0.03f + (y > 0 ? Math.Max((float)y, ground) : ground + stack * (_thickness + 0.01f));
    }

    public float LandingY(Vec2 pos, double rotDeg, double storedY) => RestAt(pos, rotDeg, storedY, 0).y;

    Basis _tilt = Basis.Identity;
    public Vector3 Up => _tilt * Vector3.Up;
    public bool Tilted => _tilt != Basis.Identity;
    public float Heading => IsDie ? Rotation.Y : _poseYaw;
    public Func<PieceNode, Vec2, double, double, int, (float y, Vector3 up)?>? Seating;

    (float y, Vector3 up) RestAt(Vec2 pos, double rotDeg, double y, int stack) =>
        Seating?.Invoke(this, pos, rotDeg, y, stack) ?? (BaseAt(pos, rotDeg, y, stack), Vector3.Up);

    static Basis TiltTo(Vector3 up) => up.IsEqualApprox(Vector3.Up) ? Basis.Identity : new Basis(ArcTo(Vector3.Up, up.Normalized()));

    void Rest(Vec2 pos, double rotDeg, double y, int stack)
    {
        var (baseY, up) = RestAt(pos, rotDeg, y, stack);
        var euler = Pose(rotDeg);
        _tilt = TiltTo(up);
        Transform = new Transform3D(_tilt * Basis.FromEuler(euler), Coords.ToWorld(pos, baseY) + Up * LiftY);
    }

    public Quaternion RestingTurn(Vec2 pos, double rotDeg, double storedY)
    {
        var (_, up) = RestAt(pos, rotDeg, storedY, 0);
        return new Quaternion((TiltTo(up) * Basis.FromEuler(new Vector3(0, Coords.Yaw(rotDeg), Piece.FaceDown ? Mathf.Pi : 0))).Orthonormalized());
    }

    float GroundUnder(Vec2 pos, double rotDeg) => _s.Ground is { Any: true } g ? (float)g.Under(pos, rotDeg, Footprint.X, Footprint.Y) : 0;

    public float RestY => BaseY(Piece) - 0.03f;

    public void Resettle()
    {
        if (IsDie) { Position = Coords.ToWorld(Piece.Pos, BaseY(Piece)); return; }
        if (!_flipping && !Placed && !Held) Rest(Piece.Pos, Piece.RotDeg, Piece.Y, Piece.Stack);
    }
    public float TopY => (Position + Up * (Piece.FaceDown ? 0 : _thickness)).Y;

    public float TopAt(Vector3 world)
    {
        if (!Tilted) return TopY;
        var up = Up;
        var top = Position + up * (Piece.FaceDown ? 0 : _thickness);
        return top.Y - (up.X * (world.X - top.X) + up.Z * (world.Z - top.Z)) / Mathf.Max(0.2f, up.Y);
    }

    string PictureDefId => Piece.Picture is { Length: > 0 } pic && _s.Def(pic) != null ? pic : Piece.Def;
    PieceDef? PictureDef => Piece.Picture is { Length: > 0 } pic ? _s.Def(pic) ?? Def : Def;

    string? CurrentImage()
    {
        if (PictureDef is { } other && !ReferenceEquals(other, Def)) return other.Image ?? other.Texture;
        if (Def == null) return null;
        if (CurState is { } st && (st.Image ?? st.Texture) is { } si) return si;
        return Def.Image ?? Def.Texture;
    }

    string? CurrentMesh() => WornModel?.Mesh ?? CurState?.Mesh ?? Def?.Mesh;

    PieceDef? WornModel => PictureDef is { Kind: "model", Mesh.Length: > 0 } worn && !ReferenceEquals(worn, Def) ? worn : null;

    string? CurrentBack() => PictureDef is { } other && !ReferenceEquals(other, Def) ? other.Back
        : CurState is { } st && Piece.State > 0 ? st.Back : Def?.Back;

    bool SamePrintUnderside => CurrentBack() is not { } back || back == CurrentImage();

    public static List<Vector2> Outline(string shape, float w, float h) =>
        KTable.Client.Geometry.PieceShapes.Outline(shape, w, h).ConvertAll(p => new Vector2(p.X, p.Y));

    void BuildPolygon()
    {
        var face = Print(scissor: true);
        _body = new MeshInstance3D
        {
            Mesh = Art.Slab(Shape, Footprint.X, Footprint.Y, _thickness, SamePrintUnderside),
            VisibilityRangeEnd = BodyRange(),
        };
        _body.SetSurfaceOverrideMaterial(0, face);
        _body.SetSurfaceOverrideMaterial(2, BackMat(scissor: false));
        AddChild(_body);
        _edgeSlots.Add((_body, 1));
        PaintEdges(SideColor());
        AddFarQuad(face);
        LoadTexture();
    }

    void BuildCutout(Color tint)
    {
        float t = _thickness;
        var faceMat = Print(scissor: true);
        AddChild(new MeshInstance3D
        {
            Mesh = Art.Plane(Footprint), Position = new Vector3(0, t, 0),
            MaterialOverride = faceMat, VisibilityRangeEnd = BodyRange(),
        });
        AddFarQuad(faceMat);
        var (pkg, rel, size) = TexKey();
        var mask = pkg == null ? null : _s.Assets.Mask(pkg, rel!, size);
        _wallsPending = mask == null;
        if (mask != null && Art.Walls($"{pkg}|{rel}|{size}", mask, Footprint.X, Footprint.Y, t) is { } walls)
        {
            var node = new MeshInstance3D { Mesh = walls, VisibilityRangeEnd = BodyRange() };
            AddChild(node);
            _edgeSlots.Add((node, -1));
        }
        PaintEdges(SideColor());
        bool same = SamePrintUnderside;
        AddChild(new MeshInstance3D { Mesh = Art.Plane(Footprint, flip: same), RotationDegrees = same ? Vector3.Zero : new Vector3(180, 0, 0), Position = new Vector3(0, 0, 0),
            MaterialOverride = BackMat(scissor: true), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        LoadTexture();
    }

    bool _wallsPending;

    internal static ArrayMesh? SilhouetteWalls(AlphaMask mask, float w, float h, float t) => PieceArt.SilhouetteWalls(mask, w, h, t);

    void LoadTexture()
    {
        var (pkg, rel, size) = TexKey();
        if (pkg == null) return;
        _texPackage = pkg;
        _texRel = rel;
        _texSize = size;
        var tex = _s.Assets.Texture(pkg, rel!, t => { if (IsInstanceValid(this)) ApplyTexture(t); }, maxSize: size);
        if (tex != null) ApplyTexture(tex);
    }

    void ApplyTexture(Texture2D t)
    {
        _texLoaded = true;
        _selMat?.SetShaderParameter("tex", t);
        if (_lettering.Built) _lettering.Relayout();
        if (Def?.Kind != "model" && _texPackage != null && _s.Assets.Mask(_texPackage, _texRel!, _texSize) is { } mk
            && (Shape == "box" ? mk.TransparentFraction > 0.01f : Shape == "alpha" && _wallsPending))
            Callable.From(Rebuild).CallDeferred();
    }

    void Bumps(string pack, string file, params StandardMaterial3D[] mats)
    {
        void Apply(Texture2D t)
        {
            foreach (var m in mats) { m.NormalEnabled = true; m.NormalTexture = t; }
        }
        var tex = _s.Assets.Texture(pack, file, t => { if (IsInstanceValid(this)) Apply(t); }, maxSize: 2048);
        if (tex != null) Apply(tex);
    }

    void BuildModel(string meshHash, float w, float h, Color tint)
    {
        var pack = _s.Store.PackOfDef(PictureDefId)!;
        if (!pack.Assets.TryGetValue(meshHash, out var ma)) return;
        bool die = Def!.Tags.Contains("die");
        StandardMaterial3D Body(Color c) => die
            ? new StandardMaterial3D { AlbedoColor = c, Roughness = 0.32f, Metallic = 0f, MetallicSpecular = 0.6f, CullMode = BaseMaterial3D.CullModeEnum.Back,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic }
            : new StandardMaterial3D { AlbedoColor = c, CullMode = BaseMaterial3D.CullModeEnum.Disabled, Transparency = PieceArt.SeeThrough(c) ? BaseMaterial3D.TransparencyEnum.Alpha
                : BaseMaterial3D.TransparencyEnum.AlphaScissor };
        var mat = _modelMat = Body(tint);
        var untinted = Body(_layerTint);
        if (!die)
        {
            PieceFinish.Apply(mat, Def!);
            PieceFinish.Apply(untinted, Def!);
        }
        var shadow = Def!.Shadowless ? GeometryInstance3D.ShadowCastingSetting.Off : GeometryInstance3D.ShadowCastingSetting.On;
        _model = new MeshInstance3D { RotationDegrees = new Vector3(0, 180, 0), CastShadow = shadow };
        AddChild(_model);
        var texHash = WornModel?.Texture ?? CurState?.Texture ?? Def!.Texture;
        if (texHash != null && pack.Assets.TryGetValue(texHash, out var ta))
        {
            var tex = _s.Assets.Texture(pack.Id, ta.File, t => { if (IsInstanceValid(this)) mat.AlbedoTexture = untinted.AlbedoTexture = t; }, maxSize: die ? 1024 : 2048);
            if (tex != null) mat.AlbedoTexture = untinted.AlbedoTexture = tex;
        }
        if (Def!.Normal != null && pack.Assets.TryGetValue(Def.Normal, out var na)) Bumps(pack.Id, na.File, mat, untinted);
        var model = _model;
        _s.Assets.EnsureFile(pack.Id, ma.File, () =>
        {
            if (!IsInstanceValid(this)) return;
            var mesh = ModelCache.Get(_s.Assets.Abs(pack.Id, ma.File));
            if (mesh == null) return;
            model.Mesh = mesh;
            for (int s = 0; s < mesh.GetSurfaceCount(); s++) model.SetSurfaceOverrideMaterial(s, s == 0 ? mat : untinted);
            if (!die && (mesh.SurfaceGetFormat(0) & Mesh.ArrayFormat.FormatColor) != 0) foreach (var m in new[] { mat, untinted }) { m.VertexColorUseAsAlbedo = true; m.VertexColorIsSrgb = true; }
            var aabb = mesh.GetAabb();
            float sx = aabb.Size.X > 0 ? w / aabb.Size.X : 1, sz = aabb.Size.Z > 0 ? h / aabb.Size.Z : 1;
            float sy = (sx + sz) / 2;
            model.Scale = new Vector3(sx, sy, sz);
            _plates.ModelTop = aabb.Size.Y * sy;
            if (Piece.Ui.Count > 0) SyncUi(force: true);
            if (Piece.Widgets.Count > 0) SyncWidgets(force: true);
            if (IsDie)
            {
                var verts = Enumerable.Range(0, mesh.GetSurfaceCount()).SelectMany(s => mesh.SurfaceGetArrays(s)[(int)Mesh.ArrayType.Vertex].AsVector3Array());
                _hull = verts.Select(vv => vv * model.Scale).ToArray();
                _meshReady = true;
                ApplyFace(Piece.State);
            }
            else
            {
                model.Position = new Vector3(0, -aabb.Position.Y * sy, 0);
                _thickness = aabb.Size.Y * sy;
                _modelRest = model.Quaternion;
                _modelRestPos = model.Position;
            }
        });
    }

    void BuildAddOn(string meshHash, Color tint)
    {
        if (_s.Store.PackOfDef(Piece.Def) is not { } pack || !pack.Assets.TryGetValue(meshHash, out var ma)) return;
        var mat = new StandardMaterial3D { AlbedoColor = tint, CullMode = BaseMaterial3D.CullModeEnum.Disabled, Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor };
        var node = _addOn = new MeshInstance3D { MaterialOverride = mat, Scale = Vector3.One * (float)Piece.Scale, VisibilityRangeEnd = BodyRange() };
        AddChild(node);
        if (Def!.AddOnTexture is { } texHash && pack.Assets.TryGetValue(texHash, out var ta))
        {
            var tex = _s.Assets.Texture(pack.Id, ta.File, t => { if (IsInstanceValid(this)) mat.AlbedoTexture = t; }, maxSize: 2048);
            if (tex != null) mat.AlbedoTexture = tex;
        }
        _s.Assets.EnsureFile(pack.Id, ma.File, () =>
        {
            if (!IsInstanceValid(this) || !IsInstanceValid(node)) return;
            if (ModelCache.Get(_s.Assets.Abs(pack.Id, ma.File)) is { } mesh) node.Mesh = mesh;
        });
    }

    static Quaternion ArcTo(Vector3 from, Vector3 to)
    {
        var d = from.Dot(to);
        if (d > 0.9999f) return Quaternion.Identity;
        if (d < -0.9999f) return new Quaternion(Vector3.Right, Mathf.Pi);
        return new Quaternion(from.Cross(to).Normalized(), Mathf.Acos(Mathf.Clamp(d, -1, 1)));
    }

    public void Refresh(PieceInstance p)
    {
        if (IsDie) { RefreshDie(p); return; }
        bool rebuild = p.State != _shownState || p.Picture != _shownPicture || p.TintOverride != _shownTint || p.Def != _shownDef || LooksChanged(p);
        Piece = p;
        if (rebuild || EffectSwitched) { Rebuild(); return; }
        SyncWidgets();
        SyncLines();
        if (!Placed) Scale = Vector3.One;
        if (!_flipping && !Placed) Rest(p.Pos, p.RotDeg, p.Y, p.Stack);
        var edge = SideColor();
        if (_edgeSlots.Count > 0 && edge != _edgeColor) PaintEdges(edge);

        var lines = new List<string>();
        var placed = Def?.TextLines.Where(t => t.Place != null).Select(t => t.Name).ToHashSet() ?? new HashSet<string>();
        void Line(string key, string v)
        {
            v = v.Trim();
            if (placed.Contains(key) || v.Length == 0 || lines.Contains(v) || lines.Count >= 2) return;
            lines.Add(v);
        }
        Line("Name", p.Text.GetValueOrDefault("Name") ?? Def?.TextLines.FirstOrDefault(t => t.Name == "Name")?.Default ?? "");
        if (Def != null) foreach (var t in Def.TextLines) if (t.Name != "Name") Line(t.Name, TextOf(p, t));
        foreach (var (k, v) in p.Text) if (k != "Name") Line(k, v);
        if (!Look.ShowFaceText || NoteCard.Is(Def)) lines.Clear();
        _lettering.Layout(lines);
        SyncSide();
    }

    public static FaceBox DefaultNameBox(bool boxy) => KTable.Client.Studio.PiecePrinting.Band(boxy);

    public static string TextOf(PieceInstance p, TextLineDef t) => (p.Text.TryGetValue(t.Name, out var v) ? v : t.Default).Trim();
    internal bool Boxy => Shape is "box" or "rounded" && Def?.Kind != "model";

    internal FaceBox? DetectedNameBox()
    {
        if (_texPackage == null) return null;
        if (_s.Assets.Mask(_texPackage, _texRel!, _texSize)?.DarkBand is not { } b) return null;
        return new FaceBox { X = 0.07, Y = b.y + b.h * 0.08, W = 0.86, H = b.h * 0.84 };
    }

    public string HoverText()
    {
        var parts = new List<string>();
        var title = Piece.Label is { Length: > 0 } lb ? lb : Def?.Name ?? Piece.Def;
        if (IsDie && Def is { States.Count: > 0 } die) title += ": " + die.States[Math.Clamp(Piece.State, 0, die.States.Count - 1)].Name;
        parts.Add(title);
        if (Piece.Text.GetValueOrDefault("Name") is { Length: > 0 } nm && nm != title) parts.Add(nm);
        foreach (var (k, v) in Piece.Text) if (k != "Name" && v.Length > 0) parts.Add($"{k}: {v}");
        if (Def != null) foreach (var c in Def.Counters) parts.Add($"{c.Name} {Piece.Counters.GetValueOrDefault(c.Name, c.Default)}");
        if (Def != null && Def.States.Count > 1 && Def.States[Math.Clamp(Piece.State, 0, Def.States.Count - 1)].Name is { Length: > 0 } st
            && !double.TryParse(st, out _)
            && !System.Text.RegularExpressions.Regex.IsMatch(st.Trim(), @"^state\s*\d+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            && !string.Equals(st, title, StringComparison.OrdinalIgnoreCase))
            parts.Add(st);
        if (Piece.Note is { Length: > 0 } note) parts.Add(note.Trim());
        if (Piece.Stowed is { Count: > 0 } held) parts.Add($"holds {held.Count}");
        return string.Join("\n", parts);
    }

    void Rebuild()
    {
        bool selected = _sel != null, hovered = _hovered;
        _hovered = false;
        foreach (var c in GetChildren()) { RemoveChild(c); c.QueueFree(); }
        _sel = _model = _body = _far = _addOn = null;
        _selMat = null;
        _sideMat = null;
        _modelMat = null;
        _lettering.Forget();
        _edgeSlots.Clear();
        _edgeColor = Colors.Transparent;
        _back.Forget();
        _backMat = null;
        _flipGen++;
        _flipping = false;
        _widgets.Forget();
        _note.Forget();
        _plates.Forget();
        _lines.Forget();
        _prints.Forget();
        _decals.Forget();
        _hull = null;
        _meshReady = false;
        _spinning = false;
        _animGen++;
        ResetEffect();
        _built = false;
        Scale = Vector3.One;
        Build();
        if (selected) SetSelected(true);
        if (hovered) SetHovered(true);
    }

    public void Preview(Vec2 pos, double rotDeg, double scale, float? y = null)
    {
        _tilt = Basis.Identity;
        Position = Coords.ToWorld(pos, (y ?? RestAt(pos, rotDeg, Piece.Y, Piece.Stack).y + 0.05f) + LiftY);
        Rotation = Pose(rotDeg);
        Scale = Vector3.One * (float)(scale / Math.Max(1e-6, BuiltScale));
    }

    public void SetSelected(bool on)
    {
        if (on && _sel == null)
        {
            float pad = Mathf.Clamp(Footprint.Length() * 0.035f, 0.04f, 0.3f);
            var c = new Color(1, 0.93f, 0.5f);
            _selMat = new ShaderMaterial { Shader = OutlineShader };
            _selMat.SetShaderParameter("color", c);
            Mesh mesh;
            if (Shape == "alpha" && Def?.Kind != "model")
            {
                mesh = new PlaneMesh { Size = Footprint * (1 + 2 * pad / Mathf.Max(0.01f, Mathf.Min(Footprint.X, Footprint.Y))) };
                _selMat.SetShaderParameter("use_alpha", true);
                if (_texPackage != null && _s.Assets.Texture(_texPackage, _texRel!, null, maxSize: _texSize) is { } t) _selMat.SetShaderParameter("tex", t);
            }
            else
            {
                bool model = Def?.Kind == "model";
                var shape = model ? "box" : Shape;
                float rim = Mathf.Clamp(Mathf.Min(Footprint.X, Footprint.Y) * 0.05f, 0.025f, 0.2f);
                var inner = Outline(shape, Footprint.X, Footprint.Y);
                var outer = Outline(shape, Footprint.X + rim * 2, Footprint.Y + rim * 2);
                float y = model ? 0.006f : _thickness + 0.004f;
                var st = new SurfaceTool();
                st.Begin(Mesh.PrimitiveType.Triangles);
                for (int i = 0; i < inner.Count; i++)
                {
                    int j = (i + 1) % inner.Count;
                    Vector3 a0 = new(inner[i].X, y, inner[i].Y), a1 = new(outer[i].X, y, outer[i].Y), b0 = new(inner[j].X, y, inner[j].Y), b1 = new(outer[j].X, y, outer[j].Y);
                    foreach (var v in new[] { a0, a1, b1, a0, b1, b0 }) st.AddVertex(v);
                }
                mesh = st.Commit();
            }
            bool halo = Shape == "alpha" && Def?.Kind != "model";
            _sel =
                new MeshInstance3D { Mesh = mesh, Position = new Vector3(0, halo ? -0.015f : 0, 0), MaterialOverride = _selMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                    Visible = Look.FlatSelection || IsEffect, Layers = Table.LiveOnlyLayer };
            AddChild(_sel);
        }
        else if (!on && _sel != null)
        {
            _sel.QueueFree();
            _sel = null;
            _selMat = null;
        }
        SetMaskLayer(on);
    }

    public bool Ringed => _sel != null;

    public void RefreshSelectionLook() { if (_sel != null) _sel.Visible = Look.FlatSelection || IsEffect; }

    public void HideFromPicture(bool on)
    {
        foreach (var child in FindChildren("*", "VisualInstance3D", recursive: true, owned: false))
            if (child is VisualInstance3D vi && vi != _sel)
            {
                uint shown = vi is Label3D ? Table.TextLayer : Table.WorldLayer;
                vi.Layers = on ? (vi.Layers & ~shown) | Table.OutOfPictureLayer : (vi.Layers | shown) & ~Table.OutOfPictureLayer;
            }
    }

    public bool Masked => FindChildren("*", "VisualInstance3D", recursive: true, owned: false)
        .Any(c => c is VisualInstance3D vi && vi != _sel && (vi.Layers & SelectionAura.MaskLayer) != 0);

    void SetMaskLayer(bool on) => SetAuraLayer(SelectionAura.MaskLayer, on);

    bool _hovered;

    public void SetHovered(bool on)
    {
        if (on == _hovered) return;
        _hovered = on;
        SetAuraLayer(SelectionAura.HoverLayer, on);
    }

    void SetAuraLayer(uint layer, bool on)
    {
        if (IsEffect) on = false;
        foreach (var child in FindChildren("*", "VisualInstance3D", recursive: true, owned: false))
            if (child is VisualInstance3D vi && vi != _sel)
                vi.Layers = on && !InsideOutline(vi) ? vi.Layers | layer : vi.Layers & ~layer;
    }

    bool InsideOutline(VisualInstance3D vi)
    {
        if (vi is not Label3D || vi.GetParent() != this) return false;
        var drawn = vi.GetAabb();
        if (drawn.Size.X <= 0) return false;
        var box = vi.Transform * drawn;
        var half = Footprint / 2;
        return box.Position.X >= -half.X && box.End.X <= half.X && box.Position.Z >= -half.Y && box.End.Z <= half.Y;
    }

    public bool Contains(Vec2 point) => ContainsAt(point, Piece.Pos, Piece.RotDeg);

    public bool ContainsAt(Vec2 point, Vec2 at, double rotDeg)
    {
        var l = Coords.ToLocal(point, at, rotDeg);
        if (!KTable.Client.Geometry.PieceShapes.Inside(Shape, Footprint.X, Footprint.Y, new System.Numerics.Vector2(l.X, l.Y))) return false;
        switch (Shape)
        {
            case "alpha":
                if (_texPackage == null) return true;
                var m = _s.Assets.Mask(_texPackage, _texRel!, _texSize);
                if (m == null) return true;
                float u = l.X / Footprint.X + 0.5f, v = Piece.FaceDown ? 0.5f - l.Y / Footprint.Y : l.Y / Footprint.Y + 0.5f, e = 1.5f / AlphaMask.N;
                return m.At(u, v) || m.At(u + e, v) || m.At(u - e, v) || m.At(u, v + e) || m.At(u, v - e);
            default:
                return true;
        }
    }
}

public static class ModelCache
{
    static readonly Dictionary<string, ArrayMesh?> Cache = new();
    public static ArrayMesh? Get(string path)
    {
        if (!Cache.TryGetValue(path, out var m)) Cache[path] = m = ObjLoader.Load(path);
        return m;
    }
}
