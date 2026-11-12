using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Client.Panels;
using KTable.Core.Model;
using KTable.Core.Panels;
using KTable.Core.Protocol;

namespace KTable.App;

sealed class PieceUi
{
    readonly PieceNode _piece;
    readonly List<Node> _nodes = new();
    readonly List<Plate> _plates = new();
    readonly Dictionary<string, string> _local = new();

    sealed record Plate(PanelView View, Vector2 Center, float Turn, Vector2 Size, Vector2 Pixels, float Fit, SubViewport Port, bool Under, Func<PanelBox, bool> Shows)
    {
        public float FaceY { get; set; }
    }
    object? _built;
    bool _builtFaceDown;
    const int PlatePixels = 1024;
    const float Thousandth = 0.001f, Upright = 180, Lift = 0.010f;

    public PieceUi(PieceNode piece) => _piece = piece;

    public float ModelTop;

    public void Changed(Command c)
    {
        foreach (var one in PanelChanges.Flat(c))
        {
            if (one is SetUi su && su.Piece == _piece.Piece.Id)
            {
                _local.Clear();
                _said.Clear();
            }
            else if (one is SetUiAttributes sua && sua.Piece == _piece.Piece.Id) PanelChanges.Forget(_local, _piece.Piece.UiLayout, sua.Id);
        }
        var moves = PanelMoves.Of(c, _piece.Piece.UiLayout, _piece.Piece.Id);
        if (moves.Count == 0) { SyncSoon(); return; }
        foreach (var m in moves) if (!m.Showing) _hiding.Add(PanelScene.Key(m.Path));
        Sync(true);
        foreach (var m in moves) Animate(m);
    }

    bool _due;

    void SyncSoon()
    {
        if (_due) return;
        _due = true;
        Callable.From(() =>
        {
            if (!_due) return;
            _due = false;
            if (GodotObject.IsInstanceValid(_piece) && !_piece.IsQueuedForDeletion()) Sync(true);
        }).CallDeferred();
    }

    readonly HashSet<string> _hiding = new();
    readonly HashSet<string> _said = new();

    void Animate(PanelMove m)
    {
        foreach (var p in _plates)
        {
            if (p.View.Scene?.At(m.Path) is not { } box || !p.Shows(box) || !p.View.Nodes.TryGetValue(box, out var node)) continue;
            p.View.Announce(m);
            p.Port.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
            var key = PanelScene.Key(m.Path);
            PanelAnimate.Play(node, m, () =>
            {
                if (GodotObject.IsInstanceValid(p.Port)) p.Port.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
                if (!m.Showing && _hiding.Remove(key)) Sync(true);
            });
            return;
        }
    }

    public void Sync(bool force)
    {
        var piece = _piece.Piece;
        object? shown = piece.UiLayout ?? (object)piece.Ui;
        if (!force && ReferenceEquals(_built, shown) && _builtFaceDown == piece.FaceDown && _nodes.All(n => GodotObject.IsInstanceValid(n) && !n.IsQueuedForDeletion())) return;
        if (!_piece.IsInsideTree())
        {
            Callable.From(() => { if (GodotObject.IsInstanceValid(_piece) && _piece.IsInsideTree()) _piece.SyncUi(true); }).CallDeferred();
            return;
        }
        _due = false;
        _built = shown;
        _builtFaceDown = piece.FaceDown;
        Clear();
        var layout = piece.UiLayout ?? (piece.Ui.Count > 0 ? PanelState.Legacy(piece.Ui) : null);
        if (layout == null || layout.Elements.Count == 0 || _piece.IsDie || _piece.IsEffect) return;
        var s = _piece.Session;
        var (color, host, admin) = PanelSeats.Of(s.Me, s.Peers);
        layout = PanelClasses.Apply(layout, _local);
        var scene = PanelScene.Build(layout, new V2(PanelScene.PieceCanvas, PanelScene.PieceCanvas), new PanelContext(layout.Colors, color, host, admin), PanelPictures.Meter(s), _local, _hiding);
        var top = scene.Root.Children[0];
        var bottom = PanelSides.Bottom(_piece.Def);
        foreach (var root in top.Children.Where(r => r.Active && r.Element != null))
        {
            if (bottom is not { } low)
            {
                DrawPlate(scene, root, false, _ => true);
                continue;
            }
            DrawPlate(scene, root, false, b => !PanelSides.Under(b, low));
            if (PanelSides.AnyUnder(root, low)) DrawPlate(scene, root, true, b => PanelSides.Under(b, low));
        }
    }

    bool FacesUp(Plate p) => p.Under == _piece.Piece.FaceDown;

    static Vector2 Seen(Plate p, Vector2 at) => p.Under ? new Vector2(-at.X, at.Y) : at;

    void Clear()
    {
        foreach (var n in _nodes) if (GodotObject.IsInstanceValid(n)) n.QueueFree();
        _nodes.Clear();
        _plates.Clear();
    }

    public void Forget()
    {
        _built = null;
        _nodes.Clear();
        _plates.Clear();
    }

    Vector2? PixelOn(Func<PanelView?> view, Vector2 screen)
    {
        if (!GodotObject.IsInstanceValid(_piece) || _piece.FacePoint(screen) is not { } face) return null;
        var at = Coords.ToLocal(face, _piece.Piece.Pos, _piece.Piece.RotDeg) / Mathf.Max(1e-6f, _piece.Scale.X);
        foreach (var p in _plates.Where(p => p.View == view()))
            if (OnPlate(p, Seen(p, at)) is { } pixel) return pixel;
        return null;
    }

    Vector3? WorldOf(Func<PanelView?> view, Vector2 pixel)
    {
        if (!GodotObject.IsInstanceValid(_piece) || !_piece.IsInsideTree() || _plates.FirstOrDefault(p => p.View == view()) is not { } p) return null;
        var uv = pixel / (p.Pixels * p.Fit);
        var onPlate = (uv - new Vector2(0.5f, 0.5f)) * p.Size;
        float a = Mathf.DegToRad(p.Turn), c = Mathf.Cos(a), s = Mathf.Sin(a);
        var d = new Vector2(onPlate.X * c + onPlate.Y * s, -onPlate.X * s + onPlate.Y * c);
        var at = p.Center + d;
        return _piece.ToGlobal(new Vector3(at.X, p.FaceY, at.Y));
    }

    static Vector2? OnPlate(Plate p, Vector2 at)
    {
        var d = at - p.Center;
        float a = Mathf.DegToRad(p.Turn), c = Mathf.Cos(a), s = Mathf.Sin(a);
        var onPlate = new Vector2(d.X * c - d.Y * s, d.X * s + d.Y * c);
        var uv = new Vector2(onPlate.X / p.Size.X + 0.5f, onPlate.Y / p.Size.Y + 0.5f);
        if (uv.X < 0 || uv.X > 1 || uv.Y < 0 || uv.Y > 1) return null;
        return uv * p.Pixels * p.Fit;
    }

    public (string Id, Control Control, Vector2 Pixel)? ControlAt(Vector2 local)
    {
        var at = local / Mathf.Max(1e-6f, _piece.Scale.X);
        for (int i = _plates.Count - 1; i >= 0; i--)
        {
            var p = _plates[i];
            if (!FacesUp(p) || OnPlate(p, Seen(p, at)) is not { } pixel) continue;
            if (p.View.Hit(pixel) is { Path: not null } hit && p.View.Nodes.TryGetValue(hit, out var ctl)) return (i + ":" + PanelScene.Key(hit.Path), ctl, pixel);
            if (p.View.Hit(pixel) is { } part && p.View.Nodes.TryGetValue(part, out var pc)) return (i + ":" + part.Name, pc, pixel);
        }
        return null;
    }

    public void Press(string id, Control c, Vector2 inside)
    {
        var colon = id.IndexOf(':');
        if (colon < 0 || !int.TryParse(id[..colon], out var i) || i >= _plates.Count) return;
        var view = _plates[i].View;
        var box = view.Nodes.FirstOrDefault(n => n.Value == c).Key;
        if (box != null) view.Click(box, inside);
    }

    void Send(List<int> path, string ev, string value)
    {
        var s = _piece.Session;
        if (!s.CanTouch(_piece.Layer, _piece.Piece)) return;
        s.Do(new UiEvent { Layer = _piece.Layer, Piece = _piece.Piece.Id, Path = path, Event = ev, Value = value });
    }

    Vector2 PerPanelUnit()
    {
        if (_piece.Def is not { ModuleType: not null } def) return new Vector2(Thousandth, Thousandth);
        var u = PieceUnits.Of(_piece.Session.Store, _piece.Piece, def);
        return new Vector2((float)(u.W * PanelSides.ObjectUnitsPerPanelUnit), (float)(u.H * PanelSides.ObjectUnitsPerPanelUnit));
    }

    void DrawPlate(PanelScene scene, PanelBox root, bool under, Func<PanelBox, bool> shows)
    {
        var k = PerPanelUnit();
        float w = Mathf.Max(1, root.RectSize.X), h = Mathf.Max(1, root.RectSize.Y);
        float pw = w * k.X * Math.Abs(root.Scale.X), ph = h * k.Y * Math.Abs(root.Scale.Y);
        if (pw < Thousandth || ph < Thousandth) return;
        var center = root.PivotAt + root.RectMin + root.RectSize * 0.5f;
        float f = Mathf.Min(1, PlatePixels / Mathf.Max(w, h));
        var vp = new SubViewport
        {
            TransparentBg = true, Disable3D = true, Size = new Vector2I(Mathf.Max(1, Mathf.RoundToInt(w * f)), Mathf.Max(1, Mathf.RoundToInt(h * f))),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Once,
        };
        var canvas = new Control { Size = new Vector2(w, h), Scale = new Vector2(f, f) };
        vp.AddChild(canvas);
        var s = _piece.Session;
        void Fresh() { if (GodotObject.IsInstanceValid(vp)) vp.RenderTargetUpdateMode = SubViewport.UpdateMode.Once; }
        PanelView? made = null;
        var hooks = PanelHost.Hooks(s, Send, (name, ready) => PanelPictures.Texture(s, name, t =>
        {
            ready(t);
            Fresh();
        }), _local, SyncSoon, _said, vp) with
        {
            PlatePixel = screen => PixelOn(() => made, screen), PlateWorld = pixel => WorldOf(() => made, pixel), Shows = shows,
        };
        var view = new PanelView(hooks);
        made = view;
        canvas.AddChild(view.Show(scene, root));
        _piece.AddChild(vp);
        _nodes.Add(vp);
        float turn = -(root.Rotation.Z - Upright);
        _plates.Add(new Plate(view, new Vector2(center.X * k.X, -center.Y * k.Y), turn, new Vector2(pw, ph), new Vector2(w, h), f, vp, under, shows));
        var mat = new StandardMaterial3D
        {
            AlbedoTexture = vp.GetTexture(), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Back, RenderPriority = 2, TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
        };
        var quad = new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(pw, ph), FlipFaces = under }, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        float faceY = under ? -Lift : (_piece.ShowsModel && ModelTop > 0 ? ModelTop : _piece.Thickness) + Lift;
        _plates[^1].FaceY = faceY;
        quad.Position = new Vector3(center.X * k.X, faceY, -center.Y * k.Y);
        quad.RotationDegrees = new Vector3(0, turn, 0);
        _piece.AddChild(quad);
        _nodes.Add(quad);
    }
}
