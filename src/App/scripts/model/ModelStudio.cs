using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Godot;
using KTable.Client.Studio;
using KTable.Core;
using KTable.Core.Model;

namespace KTable.App;

public sealed class ModelDoc
{
    public const int CurrentVersion = 2;
    public int Version { get; set; } = CurrentVersion;
    public float Scale { get; set; } = 1;
    public List<ModelPart> Parts { get; set; } = new();
    public string? Baked { get; set; }
    public int BakedSize { get; set; }
}

public sealed class ModelPart
{
    public string Id { get; set; } = ArtDoc.NewId();
    public string Name { get; set; } = "Part";
    public string Kind { get; set; } = "mesh";
    public string Mesh { get; set; } = "";
    public string Tex { get; set; } = "";
    public string? Paint { get; set; }
    public float[] Pos { get; set; } = { 0, 0, 0 };
    public float[] Rot { get; set; } = { 0, 0, 0 };
    public float[] Scl { get; set; } = { 1, 1, 1 };
    public string Tint { get; set; } = "#ffffffff";
    public bool Visible { get; set; } = true;
    public int[]? Region { get; set; }
    public Dictionary<string, string[]>? FaceDocs { get; set; }
    public string? Groups { get; set; }
    public string? OwnLayout { get; set; }
    public string? Parent { get; set; }
    public string? Join { get; set; }
    public float Blend { get; set; }
    public bool Twin { get; set; }
    public float[]? Pivot { get; set; }
    public bool WorldScale { get; set; }
    public ShapeSpec? Spec { get; set; }
    public string? SolidMesh { get; set; }
    public string? SolidPaint { get; set; }
    public int[]? SolidRegion { get; set; }

    [JsonIgnore] public bool IsGroup => Kind == ModelTree.GroupKind;

    public Transform3D Xf()
    {
        var turn = Basis.FromEuler(new Vector3(Mathf.DegToRad(Rot[0]), Mathf.DegToRad(Rot[1]), Mathf.DegToRad(Rot[2])));
        var size = new Vector3(Scl[0], Scl[1], Scl[2]);
        var at = new Vector3(Pos[0], Pos[1], Pos[2]);
        if (WorldScale) return new Transform3D(turn.Scaled(size), at);
        var b = turn * Basis.FromScale(size);
        var pivot = Pivot is { Length: 3 } p ? new Vector3(p[0], p[1], p[2]) : Vector3.Zero;
        return new Transform3D(b, at - b * pivot);
    }
}

public partial class ModelStudio : Control
{
    PieceStudio _studio = null!;
    PackageStore _store = null!;
    PackManifest _pack = null!;
    PieceDef? _def;
    ModelDoc _doc = new();
    internal StudioPrefs P = null!;

    SubViewportContainer _vc = null!;
    SubViewport _vp = null!;
    Camera3D _cam = null!;
    Node3D _root = null!;
    MeshInstance3D _grid = null!;
    readonly Dictionary<string, ModelPartView> _views = new();
    string? _selPart;
    public string Tool = "orbit";
    public Color Ink = new(0.75f, 0.15f, 0.12f);

    bool _dirtyTex, _dirtyMesh;
    readonly Debounce _bake = new();
    const double BakeDelay = 1.2, BakeMaxDelay = 5;
    Task<bool>? _bakeTask;
    int _openGen;
    string? _openedTex, _openedMesh;
    float _spotsScale = 1;
    public string? Problem { get; private set; }
    internal static readonly JsonSerializerOptions Opts = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    readonly ModelHistory _history = new();
    ModelBake _baker = null!;
    ModelTools _tools = null!;
    ModelSolids _solids = null!;
    Image? _takeBack;
    Control? _overlay;

    internal ModelCamera Camera { get; private set; } = null!;
    internal DecalTool Decal { get; private set; } = null!;
    internal ModelPanels Panels { get; private set; } = null!;
    internal ShapeFaceMap ShapeFaces { get; private set; } = null!;

    internal PieceStudio Studio => _studio;
    internal PackageStore Store => _store;
    internal PackManifest Pack => _pack;
    internal PieceDef? Def => _def;
    internal ModelDoc Doc => _doc;
    internal Dictionary<string, ModelPartView> Views => _views;
    internal string? SelPartId { get => _selPart; set => _selPart = value; }
    internal ModelPart? SelPart => _doc.Parts.FirstOrDefault(p => p.Id == _selPart);
    internal bool IsPart(string id) => _doc.Parts.Any(p => p.Id == id);
    internal SubViewportContainer Vc => _vc;
    internal SubViewport Vp => _vp;
    internal Camera3D Cam => _cam;
    internal Node3D Stand => _root;
    internal ModelHistory History => _history;
    internal ModelTools Tools => _tools;
    internal ModelSolids Solids => _solids;
    internal ModelEdits Edits { get; private set; } = null!;
    internal IEnumerable<ModelPartView> Drawn => _views.Values.Where(v => v.Node.Visible && !IsGhost(v));
    internal bool DirtyMesh => _dirtyMesh;
    internal bool DirtyTex => _dirtyTex;
    internal float SnapStep => P.ModelSnap;

    public string ToolTitle => Tool switch { "move" => "Move shapes", "paint" => "Paint on the model", "erase" => "Rub out paint", "fill" => "Fill", "pick" => "Colour picker", "decal" => "Decal", _ => "Look round" };

    public void Setup(PieceStudio studio, PackageStore store, PackManifest pack, StudioPrefs prefs)
    {
        _studio = studio;
        _store = store;
        _pack = pack;
        P = prefs;
        _baker = new ModelBake(store, pack);
        _tools = new ModelTools(this);
        _solids = new ModelSolids(this);
        Edits = new ModelEdits(this);
        Decal = new DecalTool(this);
        Panels = new ModelPanels(this);
        ShapeFaces = new ShapeFaceMap(this);
        Ink = Hex.To(prefs.Ink, Ink);
        MouseFilter = MouseFilterEnum.Ignore;
        _vc = new SubViewportContainer { Stretch = true, MouseFilter = MouseFilterEnum.Stop, FocusMode = FocusModeEnum.Click };
        _vc.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_vc);
        _vp = new SubViewport { OwnWorld3D = true, Msaa3D = Viewport.Msaa.Msaa4X, HandleInputLocally = false };
        _vc.AddChild(_vp);
        var env = _env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = Ui.Raised,
            AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(0.85f, 0.85f, 0.88f), AmbientLightEnergy = 0.55f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
        };
        _vp.AddChild(new WorldEnvironment { Environment = env });
        _vp.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-55, 35, 0), LightEnergy = 1.1f, ShadowEnabled = true });
        _vp.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-30, -150, 0), LightEnergy = 0.35f });
        _cam = new Camera3D { Fov = 40, Near = 0.01f, Far = 500 };
        _vp.AddChild(_cam);
        Camera = new ModelCamera(_cam) { Ortho = prefs.ModelOrtho };
        _grid = new MeshInstance3D { CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Layers = 2 };
        _vp.AddChild(_grid);
        BuildGrid(LeastGridHalf);
        _root = new Node3D();
        _vp.AddChild(_root);
        _vc.GuiInput += _tools.OnInput;
        MakeOverlay();
        Camera.Apply();
    }

    Godot.Environment _env = null!;

    internal void Backdrop(bool on)
    {
        _vp.TransparentBg = on;
        _env.BackgroundMode = on ? Godot.Environment.BGMode.ClearColor : Godot.Environment.BGMode.Color;
        _grid.Visible = !on;
    }

    const float LeastGridHalf = 10, GridMargin = 1.25f;
    const int GridLinesAside = 10;
    float _gridHalf;

    static float GridStep(float half)
    {
        double raw = half / GridLinesAside;
        double mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        double n = raw / mag;
        return (float)((n <= 1 ? 1 : n <= 2 ? 2 : n <= 5 ? 5 : 10) * mag);
    }

    void FitGrid()
    {
        var aabbs = Drawn.Select(v => _root.Transform * (v.Node.Transform * v.Node.GetAabb())).ToList();
        float reach = 0;
        foreach (var a in aabbs)
            reach = Mathf.Max(reach, Mathf.Max(Mathf.Max(Mathf.Abs(a.Position.X), Mathf.Abs(a.End.X)), Mathf.Max(Mathf.Abs(a.Position.Z), Mathf.Abs(a.End.Z))));
        var half = Mathf.Max(LeastGridHalf, reach * GridMargin);
        float step = GridStep(half);
        half = Mathf.Ceil(half / step) * step;
        if (Mathf.IsEqualApprox(half, _gridHalf)) return;
        BuildGrid(half);
    }

    void BuildGrid(float half)
    {
        _gridHalf = half;
        float step = GridStep(half);
        int n = (int)Mathf.Round(half / step);
        var im = new ImmediateMesh();
        var mat = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha };
        im.SurfaceBegin(Mesh.PrimitiveType.Lines, mat);
        var axis = Ui.Hued(new Color(0.8f, 0.64f, 0.3f, 0.7f));
        for (int i = -n; i <= n; i++)
        {
            var c = i == 0 ? axis : new Color(1, 1, 1, i % 5 == 0 ? 0.22f : 0.09f);
            float x = i * step;
            for (int k = 0; k < 2; k++)
            {
                im.SurfaceSetColor(c);
                im.SurfaceAddVertex(k == 0 ? new Vector3(x, 0, -half) : new Vector3(-half, 0, x));
                im.SurfaceSetColor(c);
                im.SurfaceAddVertex(k == 0 ? new Vector3(x, 0, half) : new Vector3(half, 0, x));
            }
        }
        im.SurfaceEnd();
        _grid.Mesh = im;
    }

    partial class Overlay : Control
    {
        public ModelStudio Studio = null!;
        public override void _Draw() => Studio.DrawOverlay(this);
    }

    void MakeOverlay()
    {
        _overlay = new Overlay { Studio = this, MouseFilter = MouseFilterEnum.Ignore };
        _overlay.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_overlay);
    }

    internal void RedrawOverlay() => _overlay?.QueueRedraw();

    void DrawOverlay(Control o)
    {
        _tools.DrawGizmo(o);
        if (!_vc.GetGlobalRect().HasPoint(Pointer.Global(this))) return;
        var at = _tools.Hover;
        if (Tool is "paint" or "erase")
        {
            float r = Mathf.Max(1.5f, P.Brush / 2);
            o.DrawArc(at, r, 0, Mathf.Tau, 40, new Color(0, 0, 0, 0.55f), 2.2f, true);
            o.DrawArc(at, r, 0, Mathf.Tau, 40, new Color(1, 1, 1, 0.85f), 1, true);
        }
        else if (Tool == "decal") Decal.Draw(o, at);
    }

    public static string DocPath(PackageStore store, string packId, string meshSha) => Path.Combine(ArtStore.Dir(store, packId), meshSha + ".model.json");

    public static ModelDoc? LoadDoc(PackageStore store, string packId, string? meshSha)
    {
        if (meshSha == null) return null;
        var p = DocPath(store, packId, meshSha);
        if (!File.Exists(p)) return null;
        try { return JsonSerializer.Deserialize<ModelDoc>(File.ReadAllText(p), Opts); }
        catch (Exception e) when (SaveStore.IsFileTrouble(e)) { return null; }
    }

    public bool Holds(PieceDef d) => _def == d;

    public async Task<bool> Open(PieceDef d)
    {
        int gen = ++_openGen;
        if (!await Settle() || gen != _openGen) return false;
        if (d.Kind != "model")
        {
            if (!(_def == d && _flat && _views.Count > 0 && FlatSig(d) == _openedSig)) OpenFlat(d);
            else ShowPrinting();
            return true;
        }
        if (_def == d && !_flat && _views.Count > 0 && d.Texture == _openedTex && d.Mesh == _openedMesh) return true;
        if (_def != d) { _turned = false; ApplyTurn(); }
        _flat = false;
        _def = d;
        ShowPrinting();
        _bake.Clear();
        _history.Clear();
        foreach (var v in _views.Values) Drop(v);
        _views.Clear();
        _solids.Reset();
        _takeBack = null;
        _selPart = null;
        _openedTex = d.Texture;
        _openedMesh = d.Mesh;
        _dirtyMesh = _dirtyTex = false;
        var doc = LoadDoc(_store, _pack.Id, d.Mesh);
        if (doc == null)
        {
            try { doc = FromExisting(d); }
            catch (Exception e) { GD.PrintErr($"[studio] the model of {d.Name}: {e.Message}"); }
            if (doc == null)
            {
                _studio.Say("This piece's model could not be read.");
                _doc = new ModelDoc();
                return true;
            }
        }
        _doc = doc;
        _spotsScale = doc.Scale;
        var unread = MakeViews(d.Name);
        if (_doc.Baked != null && d.Texture != null && d.Texture != _doc.Baked) TakeBackTexture(d.Texture);
        if (unread.Count > 0) _studio.Say($"Could not read {string.Join(", ", unread)} of this model: left out of the view, kept in its document.");
        _selPart = _doc.Parts.FirstOrDefault(p => _views.ContainsKey(p.Id))?.Id;
        _dirtyMesh = _dirtyTex = false;
        RefreshSelection();
        Frame();
        return true;
    }

    public async Task<bool> StartNew(PieceDef d)
    {
        int gen = ++_openGen;
        if (!await Settle() || gen != _openGen) return false;
        foreach (var v in _views.Values) Drop(v);
        _views.Clear();
        _solids.Reset();
        _takeBack = null;
        _flat = false;
        _def = d;
        _turned = false;
        ApplyTurn();
        ShowPrinting();
        _doc = new ModelDoc();
        _history.Clear();
        _selPart = null;
        _bake.Clear();
        _dirtyMesh = _dirtyTex = false;
        _openedTex = d.Texture;
        _openedMesh = d.Mesh;
        return true;
    }

    ModelDoc? FromExisting(PieceDef d)
    {
        if (d.Mesh == null || !_pack.Assets.TryGetValue(d.Mesh, out var ma)) return null;
        var path = Path.Combine(_store.PackageDir(_pack.Id), ma.File);
        if (!File.Exists(path)) return null;
        var m = ModelImport.Obj(path);
        if (m.Tris == 0) return null;
        if (d.Texture != null && PackEdit.LoadAsset(_store, _pack, d.Texture) is { } tex)
        {
            for (int i = 0; i < m.Tex.Count; i++) m.Tex[i] ??= tex;
        }
        var doc = new ModelDoc();
        var b = m.Bounds();
        doc.Scale = (float)(d.Size.W / Math.Max(1e-4, b.Size.X));
        m.Transform(new Transform3D(Basis.Identity, -b.GetCenter()));
        var part = NewPart(m, d.Name, "mesh", TexSizeFor(m));
        part.Pos = new[] { 0, b.Size.Y / 2, 0 };
        doc.Parts.Add(part);
        return doc;
    }

    internal static int TexSizeFor(MeshData m) => m.Tris > 20000 ? 2048 : m.Tris > 1500 ? 1024 : 512;

    ModelPart NewPart(MeshData m, string name, string kind, int texSize)
    {
        var drawn = kind == "mesh" && ModelImport.HasOwnLayout(m) ? m.UV.ToList() : null;
        var img = ModelImport.Unwrap(m, texSize);
        var meshName = SaveSrcText(ModelImport.ToObj(m), ".obj");
        var texName = ArtStore.AddSrc(_store, _pack.Id, img);
        var own = drawn == null ? null : SaveSrcText(ModelImport.ToObj(m.WithUV(drawn)), ".obj");
        return new ModelPart { Name = name, Kind = kind, Mesh = meshName, Tex = texName, Groups = ModelGroups.Encode(m.Mat, m.Tris), OwnLayout = own };
    }

    internal ModelPart NewPartFor(MeshData m, string name, string kind) => NewPart(m, name, kind, 1024);

    internal string SaveSrcText(string text, string ext)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var name = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()[..24] + ext;
        PackEdit.WriteContent(ArtStore.SrcPath(_store, _pack.Id, name), bytes);
        return name;
    }

    static readonly Shader PartShader = new()
    {
        Code = """
            shader_type spatial;
            render_mode cull_disabled;
            uniform sampler2D base_tex : source_color, filter_linear_mipmap, repeat_disable;
            uniform sampler2D paint_tex : source_color, filter_linear, repeat_disable;
            uniform vec4 tint : source_color = vec4(1.0);
            uniform float selected = 0.0;
            uniform float cut = 0.0;
            void fragment() {
                vec4 b = texture(base_tex, UV);
                vec4 p = texture(paint_tex, UV);
                // A cut-out token: what its picture leaves see-through is not there, as on the table.
                if (cut > 0.5 && max(b.a, p.a) < 0.5) discard;
                ALBEDO = (b.rgb * (1.0 - p.a) + p.rgb) * tint.rgb;
                ROUGHNESS = 0.78;
                // The selected part wears a brass rim, so its own colours are not changed by being picked.
                float rim = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 6.0);
                EMISSION = vec3(0.95, 0.72, 0.3) * rim * 0.7 * selected;
            }
            """,
    };

    List<string> MakeViews(string of)
    {
        var unread = new List<string>();
        foreach (var part in _doc.Parts.Where(p => !p.IsGroup))
        {
            try { MakeView(part); }
            catch (Exception e) when (e is not OutOfMemoryException) { unread.Add(part.Name); GD.PrintErr($"[studio] part {part.Name} of {of}: {e.Message}"); }
        }
        ModelTree.Upgrade(_doc, p => _views.TryGetValue(p.Id, out var v) ? v.Data.Bounds() : null);
        return unread;
    }

    internal void MakeView(ModelPart part)
    {
        if (part.IsGroup) return;
        var data = ModelImport.Obj(ArtStore.SrcPath(_store, _pack.Id, part.Mesh));
        if (part.Groups != null && ModelGroups.Decode(part.Groups, data.Tris) is { } mats) { data.Mat.Clear(); data.Mat.AddRange(mats); }
        var baseImg = ArtStore.LoadSrc(_store, _pack.Id, part.Tex) ?? Image.CreateEmpty(256, 256, false, Image.Format.Rgba8);
        AddView(part, data, baseImg, part.Kind == "block");
    }

    internal ModelPartView AddView(ModelPart part, MeshData data, Image baseImg, bool cut = false)
    {
        int w = baseImg.GetWidth(), h = baseImg.GetHeight();
        var v = new ModelPartView { Part = part, Data = data, Base = baseImg, W = w, H = h, Paint = new byte[w * h * 4] };
        if (part.Paint != null && ArtStore.LoadSrc(_store, _pack.Id, part.Paint) is { } pimg && pimg.GetWidth() == w && pimg.GetHeight() == h)
        {
            var d = pimg.GetData();
            for (int i = 0; i < d.Length; i += 4)
            {
                int a = d[i + 3];
                v.Paint[i] = (byte)(d[i] * a / 255);
                v.Paint[i + 1] = (byte)(d[i + 1] * a / 255);
                v.Paint[i + 2] = (byte)(d[i + 2] * a / 255);
                v.Paint[i + 3] = (byte)a;
            }
        }
        v.PaintImg = Image.CreateFromData(w, h, false, Image.Format.Rgba8, v.Shown());
        v.PaintTex = ImageTexture.CreateFromImage(v.PaintImg);
        var baseCopy = (Image)baseImg.Duplicate();
        baseCopy.GenerateMipmaps();
        v.Mat = new ShaderMaterial { Shader = PartShader };
        v.Mat.SetShaderParameter("base_tex", ImageTexture.CreateFromImage(baseCopy));
        v.Mat.SetShaderParameter("paint_tex", v.PaintTex);
        v.Mat.SetShaderParameter("tint", Hex.To(part.Tint, Colors.White));
        v.Mat.SetShaderParameter("cut", cut ? 1f : 0f);
        v.Xf = IsPart(part.Id) ? ModelTree.WorldXf(_doc, part) : part.Xf();
        v.Node = new MeshInstance3D { Mesh = data.ToMesh(), MaterialOverride = v.Mat, Transform = v.Xf, Visible = part.Visible };
        _root.AddChild(v.Node);
        int n = data.Tris;
        v.Centre = new Vector3[n];
        v.Radius = new float[n];
        for (int t = 0; t < n; t++)
        {
            var c = (data.V[t * 3] + data.V[t * 3 + 1] + data.V[t * 3 + 2]) / 3;
            v.Centre[t] = c;
            v.Radius[t] = Mathf.Max(c.DistanceTo(data.V[t * 3]), Mathf.Max(c.DistanceTo(data.V[t * 3 + 1]), c.DistanceTo(data.V[t * 3 + 2])));
        }
        _views[part.Id] = v;
        _root.Scale = Vector3.One * _doc.Scale;
        return v;
    }

    void TakeBackTexture(string sha)
    {
        if (PackEdit.LoadAsset(_store, _pack, sha) is not { } img) return;
        if (_doc.BakedSize > 0 && img.GetWidth() != _doc.BakedSize) img.Resize(_doc.BakedSize, _doc.BakedSize, Image.Interpolation.Bilinear);
        _takeBack = _doc.Parts.Any(p => p.SolidRegion != null) ? img : null;
        foreach (var v in _views.Values) TakeBack(v, img);
        _dirtyTex = true;
        _studio.Say("The model's picture had been painted in the 2D view: those strokes are now its paint here.");
    }

    internal void TakeBackInto(ModelPartView v)
    {
        if (_takeBack == null || v.Part.Region == null) return;
        TakeBack(v, _takeBack);
        if (!_doc.Parts.Any(p => p.SolidRegion != null && !_views.ContainsKey(ModelTree.SolidKey(p.Id)))) _takeBack = null;
        _dirtyTex = true;
    }

    static void TakeBack(ModelPartView v, Image img)
    {
        {
            if (v.Part.Region is not { Length: 4 } r || r[2] <= 0 || r[3] <= 0) return;
            var cut = img.GetRegion(new Rect2I(r[0], r[1], r[2], r[3]));
            cut.Resize(v.W, v.H, Image.Interpolation.Bilinear);
            var d = cut.GetData();
            for (int i = 0; i < d.Length; i += 4)
            {
                v.Paint[i] = d[i];
                v.Paint[i + 1] = d[i + 1];
                v.Paint[i + 2] = d[i + 2];
                v.Paint[i + 3] = 255;
            }
            v.Repaint();
        }
    }

    public async Task<bool> Settle()
    {
        for (int round = 0; round < 4; round++)
        {
            while (_bakeTask is { IsCompleted: false } running) await running;
            if (_def != null && _solids.Pending) await _solids.Catch();
            if (!_dirtyMesh && !_dirtyTex) return true;
            if (_def == null || _views.Count == 0) { _dirtyMesh = _dirtyTex = false; return true; }
            var t = BakeNow();
            _bakeTask = t;
            if (!await t) return false;
        }
        return !_dirtyMesh && !_dirtyTex;
    }

    public async Task Bake()
    {
        if (_def != null && _views.Count > 0) _dirtyTex = true;
        await Settle();
    }

    public void DiscardUnsaved(PieceDef? only = null)
    {
        if (only != null && only != _def) return;
        _dirtyMesh = _dirtyTex = false;
        _bake.Clear();
    }

    public IEnumerable<string> KeepTexts() => _history.Texts.Append(JsonSerializer.Serialize(_doc, Opts)).ToList();

    async Task<bool> BakeNow()
    {
        if (_flat) return await BakeFlat();
        var def = _def;
        var doc = _doc;
        var views = new Dictionary<string, ModelPartView>(_views);
        if (def == null || views.Count == 0) return true;
        bool meshChanged = _dirtyMesh;
        _dirtyMesh = _dirtyTex = false;
        _bake.Clear();
        try
        {
            _studio.SetSaved("saving…");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var items = views.Values.Where(v => v.Node.Visible && !IsGhost(v)).ToList();
            if (items.Count == 0)
            {
                _baker.KeepPaint(views.Values);
                if (def.Mesh != null) KTable.Core.Json.WriteAtomic(DocPath(_store, _pack.Id, def.Mesh), JsonSerializer.Serialize(doc, Opts));
                _studio.Say("Every part is hidden: the piece on the table stays as it was until one is shown.");
                _studio.SetSaved("saved");
                Problem = null;
                return true;
            }
            var (atlas, size, mesh) = ModelBake.Pack(items);
            _solids.Keep();
            var b = mesh.Bounds();
            mesh.Transform(new Transform3D(Basis.Identity, new Vector3(-b.GetCenter().X, -b.Position.Y, -b.GetCenter().Z)));
            var texSha = PackEdit.AddImage(_store, _pack, atlas);
            string meshSha = def.Mesh ?? "";
            if (meshChanged || def.Mesh == null || LoadDoc(_store, _pack.Id, def.Mesh) == null)
                meshSha = _baker.AddMesh(ModelImport.ToObj(mesh));
            doc.Baked = texSha;
            doc.BakedSize = size;
            _baker.KeepPaint(views.Values);
            KTable.Core.Json.WriteAtomic(DocPath(_store, _pack.Id, meshSha), JsonSerializer.Serialize(doc, Opts));
            Baked(def, doc, b, texSha, meshSha);
            try { await _baker.Thumbnail(this, _vp.FindWorld3D(), _root.Transform, meshSha, views.Values.ToList()); }
            catch (Exception e) { GD.PrintErr($"[studio] thumbnail of {def.Name}: {e.Message}"); }
            _studio.ModelBaked(def);
            _studio.SetSaved(_dirtyMesh || _dirtyTex ? "editing…" : "saved");
            Problem = null;
            return true;
        }
        catch (Exception e)
        {
            if (meshChanged) _dirtyMesh = true;
            _dirtyTex = true;
            if (def == _def && _bake.In < 0) _bake.In = 3;
            Problem = e.Message;
            GD.PrintErr($"[studio] could not save the model of {def.Name}: {e}");
            _studio.Say("Could not save the model: " + e.Message);
            _studio.SetSaved("NOT SAVED");
            return false;
        }
    }

    void Baked(PieceDef def, ModelDoc doc, Aabb b, string texSha, string meshSha)
    {
        var (oldTex, oldMesh) = (def.Texture, def.Mesh);
        def.Mesh = meshSha;
        def.Texture = texSha;
        def.Kind = "model";
        def.Size = new Size2(Math.Round(Math.Max(0.02, b.Size.X * doc.Scale), 4), Math.Round(Math.Max(0.02, b.Size.Z * doc.Scale), 4));
        def.Thickness = Math.Round(Math.Max(0.02, b.Size.Y * doc.Scale), 4);
        if (def == _def) FollowScale(def, doc.Scale);
        foreach (var st in def.States)
        {
            if (st.Texture != null && st.Texture == oldTex) st.Texture = texSha;
            if (st.Mesh != null && st.Mesh == oldMesh) st.Mesh = meshSha;
        }
        if (def == _def) { _openedTex = texSha; _openedMesh = meshSha; }
        _studio.MadeHere.Add(texSha);
        _studio.MadeHere.Add(meshSha);
        _studio.Superseded(oldTex, texSha);
        _studio.Superseded(oldMesh, meshSha);
    }

    void FollowScale(PieceDef def, float scale)
    {
        if (def.Effect?.At is { Count: > 0 } at && _spotsScale > 1e-4f && Math.Abs(scale - _spotsScale) > 1e-5f)
            def.Effect.At = at.Select(v => Math.Round(v * scale / _spotsScale, 4)).ToList();
        _spotsScale = scale;
    }

    internal void Changed(bool mesh)
    {
        if (mesh) _dirtyMesh = true;
        else _dirtyTex = true;
        _solids.Touch();
        _bake.Arm(BakeDelay, BakeMaxDelay);
        _studio.SetSaved("editing…");
    }

    public override void _Process(double delta)
    {
        foreach (var v in _views.Values)
            if (v.PaintDirty && v.Node.HasMeta("upload"))
            {
                v.PaintImg.SetData(v.W, v.H, false, Image.Format.Rgba8, v.Shown());
                v.PaintTex.Update(v.PaintImg);
                v.Node.RemoveMeta("upload");
            }
        _solids.Tick(delta);
        if (_bake.Tick(delta))
        {
            if (Input.IsMouseButtonPressed(MouseButton.Left)) _bake.In = 0.3;
            else { _bake.Clear(); _ = Settle(); }
        }
        if (Camera.Spin && Visible) { Camera.Yaw += (float)delta * 0.5f; Camera.Apply(); }
    }

    const float FlatModelSpan = 0.6f;

    const float NewModelWidth = 2, FileUnitsLeast = 0.1f, FileUnitsMost = 20;

    public static float ScaleFor(IEnumerable<Aabb> set) => NewModelWidth / Widest(set);

    static float Widest(IEnumerable<Aabb> set) => Mathf.Max(1e-4f, set.Select(b => Mathf.Max(b.Size.X, b.Size.Z)).DefaultIfEmpty(0).Max());

    public static (float Scale, bool Fitted, float Widest) ImportScale(IEnumerable<Aabb> set)
    {
        var all = set.ToList();
        float widest = Widest(all);
        return widest is >= FileUnitsLeast and <= FileUnitsMost ? (1, false, widest) : (ScaleFor(all), true, widest);
    }

    public void AddParts(MeshData m, string name, string kind, ShapeSpec? spec = null, float? scale = null)
    {
        Snapshot();
        var pb = m.Bounds();
        m.Transform(new Transform3D(Basis.Identity, -pb.GetCenter()));
        var part = NewPart(m, name, kind, kind == "mesh" ? TexSizeFor(m) : 1024);
        part.Spec = spec;
        float partSpan = Mathf.Max(1e-4f, Mathf.Max(pb.Size.X, Mathf.Max(pb.Size.Y, pb.Size.Z)));
        var into = SelPart is { IsGroup: true } g ? g : null;
        if (_flat && kind == "base") kind = "box";
        if (_flat && kind == "mesh")
        {
            float span = ModelSpan();
            float s = span * FlatModelSpan / Mathf.Max(1e-4f, Mathf.Max(pb.Size.X, pb.Size.Z));
            float floor = Mathf.Clamp((float)(_def?.Thickness ?? 0), 0.02f, 2);
            part.Scl = new[] { s, s, s };
            part.Pos = new[] { 0, floor + pb.Size.Y * s / 2, 0 };
        }
        else if (Drawn.Any())
        {
            float span = ModelSpan();
            float top = Drawn.Select(v => (v.Node.Transform * v.Node.GetAabb()).End.Y).DefaultIfEmpty(0).Max();
            float s = kind switch { "base" => span * 1.15f, "mesh" => span * 0.8f, "flag" => span * 0.5f, _ => span * 0.35f } / partSpan;
            part.Scl = new[] { s, s, s };
            float half = pb.Size.Y * s / 2;
            part.Pos = kind switch { "base" => new[] { 0f, half, 0f }, "mesh" => new[] { span * 0.75f, half, 0f }, _ => new[] { 0f, top + half, 0f } };
            if (kind == "base")
                foreach (var other in ModelTree.Children(_doc, null)) other.Pos[1] += pb.Size.Y * s;
        }
        else
        {
            _doc.Scale = scale ?? ScaleFor(new[] { pb });
            part.Pos = new[] { 0, pb.Size.Y / 2, 0 };
        }
        _doc.Parts.Add(part);
        if (into != null) ModelTree.MoveInto(_doc, part, into);
        MakeView(part);
        _selPart = part.Id;
        RefreshSelection();
        if (_views.Count == 1 || _flat) Frame();
        Changed(mesh: true);
        _studio.RebuildModelPanels();
        _studio.RebuildModelStrip();
    }

    public void AddShape(string kind, string name)
    {
        if (!ShapeMaker.IsSpec(kind)) { AddParts(ModelImport.Primitive(kind), name, kind); return; }
        var spec = ShapeMaker.Default(kind);
        AddParts(ShapeMaker.Make(spec), name, kind, spec);
    }

    internal Aabb? BoundsOf(ModelPart p)
    {
        var items = p.IsGroup ? ModelTree.Descendants(_doc, p).Where(x => !x.IsGroup) : new[] { p };
        Aabb? all = null;
        foreach (var x in items)
        {
            if (!_views.TryGetValue(x.Id, out var v) || !ModelTree.Shown(_doc, x)) continue;
            foreach (var xf in x.Twin ? new[] { ModelTree.WorldXf(_doc, x), ModelTree.TwinXf(_doc, x) } : new[] { ModelTree.WorldXf(_doc, x) })
            {
                var b = xf * v.Data.Bounds();
                all = all is { } a ? a.Merge(b) : b;
            }
        }
        return all;
    }

    internal Aabb? DrawnBounds()
    {
        Aabb? all = null;
        foreach (var v in Drawn)
        {
            var b = v.Node.Transform * v.Node.GetAabb();
            all = all is { } a ? a.Merge(b) : b;
            if (v.TwinNode is { Visible: true } t) all = all.Value.Merge(t.Transform * t.GetAabb());
        }
        return all;
    }

    float ModelSpan() => DrawnBounds() is { } b ? Mathf.Max(b.Size.X, Mathf.Max(b.Size.Y, b.Size.Z)) : 1;

    internal float FootprintWide() => DrawnBounds() is { } b ? Mathf.Max(1e-4f, b.Size.X) : 1;

    internal void SetWidth(double width)
    {
        float span = _views.Count == 0 ? 1 : FootprintWide();
        Snapshot();
        _doc.Scale = (float)(Math.Clamp(width, 0.05, 200) / Math.Max(1e-4, span));
        RefreshSelection();
        Changed(mesh: true);
    }

    static readonly StandardMaterial3D Ghost = new()
    {
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(0.95f, 0.72f, 0.3f, 0.28f),
        CullMode = BaseMaterial3D.CullModeEnum.Disabled, NoDepthTest = true,
    };

    internal void RefreshSelection()
    {
        _solids.Refresh();
        bool several = _flat ? _doc.Parts.Count > 0 : _doc.Parts.Count > 1;
        var sel = SelPart;
        var lit = sel == null ? new HashSet<string>() : (sel.IsGroup ? ModelTree.Descendants(_doc, sel) : new List<ModelPart> { sel }).Select(p => p.Id).ToHashSet();
        foreach (var v in _views.Values)
        {
            v.Mat.SetShaderParameter("tint", Hex.To(v.Part.Tint, Colors.White));
            if (!IsPart(v.Part.Id))
            {
                bool litSolid = ModelTree.IsSolidKey(v.Part.Id) && _solids.Chains.FirstOrDefault(c => c.Key == v.Part.Id) is { } ch && ch.Members.Any(m => m.Id == _selPart);
                v.Mat.SetShaderParameter("selected", several && litSolid ? 1f : 0f);
                continue;
            }
            var p = v.Part;
            bool joined = _solids.IsConsumed(p.Id);
            bool shown = ModelTree.Shown(_doc, p);
            v.Xf = ModelTree.WorldXf(_doc, p);
            v.Node.Transform = v.Xf;
            bool ghost = joined && shown && lit.Contains(p.Id);
            v.Node.Visible = shown && (!joined || ghost);
            v.Node.MaterialOverride = ghost ? Ghost : v.Mat;
            v.Node.CastShadow = ghost ? GeometryInstance3D.ShadowCastingSetting.Off : GeometryInstance3D.ShadowCastingSetting.On;
            v.Node.Layers = ghost ? GhostLayer : 1u;
            v.Mat.SetShaderParameter("selected", several && lit.Contains(p.Id) ? 1f : 0f);
            Twin(v, shown && !joined && p.Twin);
        }
        _root.Scale = Vector3.One * _doc.Scale;
        FitGrid();
    }

    void Twin(ModelPartView v, bool on)
    {
        if (!on)
        {
            if (v.TwinNode != null) { v.TwinNode.QueueFree(); v.TwinNode = null; }
            return;
        }
        if (v.TwinNode == null)
        {
            v.TwinNode = new MeshInstance3D { Mesh = v.Node.Mesh, MaterialOverride = v.Mat };
            _root.AddChild(v.TwinNode);
        }
        v.TwinNode.Transform = ModelTree.TwinXf(_doc, v.Part);
    }

    const uint GhostLayer = 2;

    internal bool IsGhost(ModelPartView v) => v.Node.MaterialOverride == Ghost;

    internal void FrameOn(ModelPart p)
    {
        if (BoundsOf(p) is { } b) Camera.Frame(new List<Aabb> { _root.Transform * b });
    }

    internal void Frame() => Camera.Frame(DrawnBounds() is { } b ? new List<Aabb> { _root.Transform * b } : new List<Aabb>());

    internal void RemovePart(ModelPart p)
    {
        Snapshot();
        var gone = p.IsGroup ? ModelTree.Descendants(_doc, p).Prepend(p).ToList() : new List<ModelPart> { p };
        foreach (var x in gone)
        {
            _doc.Parts.Remove(x);
            if (_views.Remove(x.Id, out var v)) Drop(v);
        }
        _selPart = _doc.Parts.FirstOrDefault()?.Id ?? (_flat ? _views.Values.FirstOrDefault(x => x.Face != null)?.Part.Id : null);
        RefreshSelection();
        Changed(mesh: true);
        _studio.RebuildModelPanels();
        _studio.RebuildModelStrip();
    }

    static void Drop(ModelPartView v)
    {
        v.Node.QueueFree();
        v.TwinNode?.QueueFree();
    }

    internal void BeginStroke() => _history.BeginStroke(Drawn.Where(v => !IsGhost(v)));

    internal void EndStroke()
    {
        if (!_history.EndStroke(_views.Values)) return;
        P.Used(Ink);
        Changed(mesh: false);
    }

    internal void Snapshot() => _history.Snapshot(JsonSerializer.Serialize(_doc, Opts));

    public void Undo() => Step(undo: true);
    public void Redo() => Step(undo: false);

    void Step(bool undo)
    {
        if (_history.Take(undo) is not { } e) return;
        if (e is List<(string part, byte[] before, byte[]? after)> strokes)
        {
            var byKey = _views.Values.GroupBy(v => v.StrokeKey).ToDictionary(g => g.Key, g => g.First());
            foreach (var (part, before, after) in strokes)
                if (byKey.TryGetValue(part, out var v) && v.Paint.Length == before.Length) { Array.Copy(undo ? before : after!, v.Paint, v.Paint.Length); v.Repaint(); }
            _history.Give(undo, strokes);
            Changed(mesh: false);
        }
        else if (e is string json && JsonSerializer.Deserialize<ModelDoc>(json, Opts) is { } d)
        {
            _history.Give(undo, JsonSerializer.Serialize(_doc, Opts));
            var paints = _views.Where(kv => IsPart(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
            foreach (var v in paints.Values) { Drop(v); _views.Remove(v.Part.Id); }
            if (_flat) d.Scale = 1;
            _doc = d;
            foreach (var p in _doc.Parts.Where(x => !x.IsGroup))
            {
                MakeView(p);
                if (paints.TryGetValue(p.Id, out var old) && old.Part.Mesh == p.Mesh && old.Paint.Length == _views[p.Id].Paint.Length) { Array.Copy(old.Paint, _views[p.Id].Paint,
                    old.Paint.Length); _views[p.Id].Repaint(); }
            }
            if (!_views.ContainsKey(_selPart ?? "") && !IsPart(_selPart ?? "")) _selPart = _doc.Parts.FirstOrDefault()?.Id ?? _views.Values.FirstOrDefault(x => x.Face != null)?.Part.Id;
            RefreshSelection();
            Changed(mesh: true);
            _studio.RebuildModelPanels();
            _studio.RebuildModelStrip();
        }
    }

    public void InkChanged(Color c) => Ink = c;

    public bool Key(InputEventKey k)
    {
        bool ctrl = k.CtrlPressed || k.MetaPressed;
        if (Decal.Key(k)) return true;
        switch (k.Keycode)
        {
            case Godot.Key.Home:
                Frame();
                return true;
            case Godot.Key.F when !ctrl && !k.Echo:
                TurnOver();
                return true;
            case Godot.Key.Minus or Godot.Key.KpSubtract when !ctrl:
                P.Brush = Mathf.Max(1, P.Brush / 1.15f);
                _studio.RebuildModelOptions();
                RedrawOverlay();
                return true;
            case Godot.Key.Equal or Godot.Key.KpAdd when !ctrl:
                P.Brush = Mathf.Min(400, P.Brush * 1.15f);
                _studio.RebuildModelOptions();
                RedrawOverlay();
                return true;
            case Godot.Key.Delete when SelPart != null && (_flat || _doc.Parts.Count > 1):
                RemovePart(SelPart);
                return true;
            case Godot.Key.V when ctrl && Tool == "decal":
                if (ImageFiles.FromClipboard() is { } clip) Decal.Set(clip, "pasted");
                else _studio.Say("No picture on the clipboard.");
                return true;
        }
        if (ctrl || k.Echo) return false;
        if (!k.AltPressed && ViewKey(k.Keycode, k.ShiftPressed)) return true;
        var t = k.Keycode switch { Godot.Key.H => "orbit", Godot.Key.V => "move", Godot.Key.B => "paint", Godot.Key.E => "erase", Godot.Key.G => "fill", Godot.Key.I => "pick", Godot.Key.D => "decal", _ => null };
        if (t == null || (t == "move" && !CanMove)) return false;
        SetTool(t);
        return true;
    }

    internal bool ViewKey(Key key, bool opposite)
    {
        switch (key)
        {
            case Godot.Key.Key1 or Godot.Key.Kp1:
                Camera.Face(opposite ? Mathf.Pi : 0, ModelCamera.Level);
                return true;
            case Godot.Key.Key3 or Godot.Key.Kp3:
                Camera.Face(opposite ? -Mathf.Pi / 2 : Mathf.Pi / 2, ModelCamera.Level);
                return true;
            case Godot.Key.Key7 or Godot.Key.Kp7:
                Camera.Face(opposite ? Mathf.Pi : 0, ModelCamera.Above);
                return true;
            case Godot.Key.Key5 or Godot.Key.Kp5:
                Camera.Ortho = P.ModelOrtho = !Camera.Ortho;
                Camera.Apply();
                _studio.RebuildModelStrip();
                return true;
        }
        return false;
    }

    internal bool CanMove => !_flat || _doc.Parts.Count > 0;

    public void SetTool(string t)
    {
        Tool = t;
        _vc.MouseDefaultCursorShape = t is "paint" or "erase" or "fill" or "pick" or "decal" ? CursorShape.Cross : t == "move" ? CursorShape.Move : CursorShape.Arrow;
        _studio.RebuildModelStrip();
        _studio.RebuildModelOptions();
        RedrawOverlay();
    }
}
