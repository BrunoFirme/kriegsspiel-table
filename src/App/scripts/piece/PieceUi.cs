using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed class PieceUi
{
    readonly PieceNode _piece;
    readonly List<Node> _nodes = new();
    readonly List<Plate> _plates = new();

    sealed record Plate(UiBuilder Builder, Vector2 Center, float Turn, Vector2 Size, Vector2 Pixels, float Fit);
    List<UiNode>? _built;
    bool _builtFaceDown;
    const int PlatePixels = 1024;

    public PieceUi(PieceNode piece) => _piece = piece;

    public float ModelTop;

    public void Sync(bool force)
    {
        var piece = _piece.Piece;
        if (!force && ReferenceEquals(_built, piece.Ui) && _builtFaceDown == piece.FaceDown && _nodes.All(n => GodotObject.IsInstanceValid(n) && !n.IsQueuedForDeletion())) return;
        if (!_piece.IsInsideTree())
        {
            Callable.From(() => { if (GodotObject.IsInstanceValid(_piece) && _piece.IsInsideTree()) _piece.SyncUi(true); }).CallDeferred();
            return;
        }
        _built = piece.Ui;
        _builtFaceDown = piece.FaceDown;
        Clear();
        if (piece.Ui.Count == 0 || piece.FaceDown || _piece.IsDie || _piece.IsEffect) return;
        foreach (var root in UiStyles.Styled(piece.Ui))
            if (!UiBuilder.Attr(root, "active", "true").Equals("false", StringComparison.OrdinalIgnoreCase)) DrawPlate(root);
    }

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

    public (string Id, Control Control, Vector2 Pixel)? ControlAt(Vector2 local)
    {
        var at = local / Mathf.Max(1e-6f, _piece.Scale.X);
        for (int i = _plates.Count - 1; i >= 0; i--)
        {
            var p = _plates[i];
            var d = at - p.Center;
            float a = Mathf.DegToRad(p.Turn), c = Mathf.Cos(a), s = Mathf.Sin(a);
            var onPlate = new Vector2(d.X * c - d.Y * s, d.X * s + d.Y * c);
            var uv = new Vector2(onPlate.X / p.Size.X + 0.5f, onPlate.Y / p.Size.Y + 0.5f);
            if (uv.X < 0 || uv.X > 1 || uv.Y < 0 || uv.Y > 1) continue;
            var pixel = uv * p.Pixels;
            (string Id, Control Control, Vector2 Pixel)? best = null;
            float least = float.MaxValue;
            foreach (var (id, ctl) in p.Builder.ById)
            {
                if (!GodotObject.IsInstanceValid(ctl) || !ctl.IsVisibleInTree() || !Answers(ctl)) continue;
                var rect = ctl.GetGlobalRect();
                if (!rect.HasPoint(pixel * p.Fit) || rect.Area >= least) continue;
                least = rect.Area;
                best = (id, ctl, pixel * p.Fit - rect.Position);
            }
            if (best != null) return best;
        }
        return null;
    }

    static bool Answers(Control c) => c is BaseButton { Disabled: false } or LineEdit { Editable: true } or TextEdit { Editable: true } or Slider { Editable: true };

    public void Press(string id, Control c, Vector2 inside)
    {
        switch (c)
        {
            case OptionButton ob:
                var menu = new PopupMenu();
                for (int i = 0; i < ob.ItemCount; i++) menu.AddItem(ob.GetItemText(i), i);
                menu.IdPressed += pick =>
                {
                    ob.Select((int)pick);
                    ob.EmitSignal(OptionButton.SignalName.ItemSelected, pick);
                    menu.QueueFree();
                };
                menu.PopupHide += () => { if (GodotObject.IsInstanceValid(menu)) menu.QueueFree(); };
                _piece.GetTree().Root.AddChild(menu);
                menu.Popup(new Rect2I((Vector2I)_piece.GetViewport().GetMousePosition(), Vector2I.Zero));
                break;
            case BaseButton { ToggleMode: true } tb:
                tb.ButtonPressed = !tb.ButtonPressed;
                break;
            case BaseButton b:
                b.EmitSignal(BaseButton.SignalName.Pressed);
                break;
            case Slider sl:
                sl.Value = sl.MinValue + (sl.MaxValue - sl.MinValue) * Mathf.Clamp(inside.X / Mathf.Max(1, sl.Size.X), 0, 1);
                sl.EmitSignal(Slider.SignalName.DragEnded, true);
                break;
            case LineEdit or TextEdit:
                AskText(id, c is LineEdit le ? le.Text : ((TextEdit)c).Text, c is TextEdit);
                break;
        }
    }

    void AskText(string id, string text, bool lines)
    {
        var dlg = new ConfirmationDialog { Title = _piece.Piece.Label ?? "Text", OkButtonText = "OK" };
        Control field = lines
            ? new TextEdit { Text = text, CustomMinimumSize = new Vector2(420, 160) }
            : new LineEdit { Text = text, CustomMinimumSize = new Vector2(320, 0), SelectAllOnFocus = true };
        dlg.AddChild(field);
        dlg.Confirmed += () => Send(id, "end", field is LineEdit l ? l.Text : ((TextEdit)field).Text);
        dlg.VisibilityChanged += () => { if (!dlg.Visible) dlg.QueueFree(); };
        _piece.GetTree().Root.AddChild(dlg);
        dlg.PopupCentered();
        field.GrabFocus();
    }

    void Send(string id, string ev, string value)
    {
        var s = _piece.Session;
        if (!s.CanTouch(_piece.Layer, _piece.Piece)) return;
        s.Do(new KTable.Core.Protocol.UiEvent { Layer = _piece.Layer, Piece = _piece.Piece.Id, Id = id, Event = ev, Value = value });
    }

    void DrawPlate(UiNode root)
    {
        float w = Mathf.Max(1, UiBuilder.Num(root, "width", 100)), h = Mathf.Max(1, UiBuilder.Num(root, "height", 100));
        var scale = UiBuilder.Vec3(root, "scale", 1);
        var pos = UiBuilder.Vec3(root, "position", 0);
        var rot = UiBuilder.Vec3(root, "rotation", 0);
        const float thousandth = 0.001f;
        float pw = w * thousandth * scale.X, ph = h * thousandth * scale.Y;
        if (pw < 0.001f || ph < 0.001f) return;

        float f = Mathf.Min(1, PlatePixels / Mathf.Max(w, h));
        var vp = new SubViewport
        {
            TransparentBg = true, Disable3D = true, Size = new Vector2I(Mathf.Max(1, Mathf.RoundToInt(w * f)), Mathf.Max(1, Mathf.RoundToInt(h * f))),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Once,
        };
        var canvas = new Control { Size = new Vector2(w, h), Scale = new Vector2(f, f) };
        vp.AddChild(canvas);
        var s = _piece.Session;
        var builder =
            new UiBuilder(Send,
                (id, ready) => UiBuilder.AssetTexture(s, id, t => { ready(t); if (GodotObject.IsInstanceValid(vp)) vp.RenderTargetUpdateMode = SubViewport.UpdateMode.Once; }));
        var c = builder.Build(root, new Vector2(w, h));
        if (c == null) { vp.QueueFree(); return; }
        c.Position = Vector2.Zero;
        c.Size = new Vector2(w, h);
        canvas.AddChild(c);
        _piece.AddChild(vp);
        _nodes.Add(vp);
        _plates.Add(new Plate(builder, new Vector2(pos.X * thousandth, -pos.Y * thousandth), -(rot.Z - 180), new Vector2(pw, ph), new Vector2(w, h), f));

        var mat = new StandardMaterial3D
        {
            AlbedoTexture = vp.GetTexture(), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Back, RenderPriority = 2, TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
        };
        var quad = new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(pw, ph) }, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        float faceY = (_piece.Def?.Kind == "model" && ModelTop > 0 ? ModelTop : _piece.Thickness) + 0.010f;
        quad.Position = new Vector3(pos.X * thousandth, faceY, -pos.Y * thousandth);
        quad.RotationDegrees = new Vector3(0, -(rot.Z - 180), 0);
        _piece.AddChild(quad);
        _nodes.Add(quad);
    }
}
