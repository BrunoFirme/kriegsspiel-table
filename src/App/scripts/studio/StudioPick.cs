using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Client.Studio;
using Num = System.Numerics;

namespace KTable.App;

sealed class StudioPick
{
    const float HandleReach = 8, KnobReach = 9, KnobRise = 26, Margin = 5, HitSlack = 4, CopyShift = 12, TurnStep = 15;
    const string ShapeKind = "shape", FillKind = "fill", PaintKind = "paint";

    readonly StudioCanvas _c;
    string? _layer;
    readonly List<int> _at = new();
    readonly List<PaintOp> _held = new();

    public enum Grip { None, Body, Corner, Turn, End }
    Grip _grip;
    int _handle;
    Vector2 _downDoc;
    List<Vector2[]> _from = new();
    float _fromTurn;
    Rect2 _fromInk;
    float _pad;
    (Num.Vector2 A, Num.Vector2 B)? _fromEnds;
    bool _changed;

    public StudioPick(StudioCanvas canvas) { _c = canvas; }

    sealed record Seen(ArtLayer Layer, List<PaintOp> Ops, (Num.Vector2 A, Num.Vector2 B)? Ends, Transform2D Frame, Rect2 Ink, float Pad)
    {
        public Rect2 Box => Ink.Grow(Pad);
        public Transform2D ToDoc => Layer.Xf() * Frame;
    }

    static Num.Vector2 N(Vector2 v) => new(v.X, v.Y);
    static Vector2 G(Num.Vector2 v) => new(v.X, v.Y);
    static Transform2D Frame(float degrees) => new(Mathf.DegToRad(degrees), Vector2.Zero);
    static Transform2D Slide(Vector2 by) => new(0, by);

    public ArtLayer? Layer => _c.Doc.Layer(_layer) is { Visible: true, Locked: false } l ? l : null;

    public List<PaintOp> Ops
    {
        get
        {
            var res = new List<PaintOp>();
            if (Layer is not { } l) return res;
            for (int k = 0; k < _at.Count; k++)
            {
                int i = _at[k];
                if (i < 0 || i >= l.Ops.Count || !ReferenceEquals(l.Ops[i], _held[k]) || !OpPick.Pickable(l.Ops[i])) return new();
                res.Add(l.Ops[i]);
            }
            return res;
        }
    }

    public bool Any => Ops.Count > 0;
    public IReadOnlyList<int> At => Any ? _at : Array.Empty<int>();
    public bool Closed => Ops is var ops && ops.Any(o => o.K == ShapeKind && !OpPick.IsHead(o, ops));
    public bool Filled => Ops is var ops && ops.Any(o => o.K == ShapeKind && !OpPick.IsHead(o, ops) && o.Fc != null);
    public bool Lined => Ops.Any(o => o.W > 0);

    Seen? See()
    {
        var ops = Ops;
        if (ops.Count == 0 || Layer is not { } l) return null;
        var frame = Frame(ops[0].R ?? 0);
        var back = frame.AffineInverse();
        Rect2? ink = null;
        foreach (var op in ops)
            foreach (var p in op.Pts())
            {
                var q = back * p;
                ink = ink is { } r ? r.Expand(q) : new Rect2(q, Vector2.Zero);
            }
        if (ink == null) return null;
        float pad = ops.Max(o => Mathf.Max(o.W, 0)) / 2 + _c.Px(Margin) / StudioCanvas.LayerScale(l);
        return new Seen(l, ops, OpPick.Ends(ops), frame, ink.Value, pad);
    }

    float Slack(ArtLayer l) => _c.Px(HitSlack) / StudioCanvas.LayerScale(l);

    public void Take(ArtLayer l, List<int> at, bool loadLook)
    {
        if (_layer == l.Id && _at.SequenceEqual(at) && Any) return;
        if (_c.Doc.Selected != l.Id) _c.Select(l.Id);
        _layer = l.Id;
        _at.Clear();
        _at.AddRange(at);
        _held.Clear();
        _held.AddRange(at.Select(i => l.Ops[i]));
        if (loadLook) LoadLook();
        _c.PickWasChanged();
        _c.Redraw();
    }

    public void Forget()
    {
        _grip = Grip.None;
        _layer = null;
        _at.Clear();
        _held.Clear();
    }

    public void Rebind()
    {
        if (_layer == null) return;
        if (_c.Doc.Layer(_layer) is { } l && _at.All(i => i >= 0 && i < l.Ops.Count && OpPick.Pickable(l.Ops[i])))
        {
            _held.Clear();
            _held.AddRange(_at.Select(i => l.Ops[i]));
        }
        else Drop();
    }

    public void Drop()
    {
        bool had = _layer != null;
        Forget();
        if (!had) return;
        _c.PickWasChanged();
        _c.Redraw();
    }

    public Vector2[] Handles()
    {
        if (See() is not { } s) return Array.Empty<Vector2>();
        var layer = s.Layer.Xf();
        if (s.Ends is { } e) return new[] { _c.ToScreen(layer * G(e.A)), _c.ToScreen(layer * G(e.B)) };
        var xf = s.ToDoc;
        return StudioCanvas.HandlePts(s.Box).Select(p => _c.ToScreen(xf * p)).ToArray();
    }

    public Vector2[] Outline()
    {
        if (See() is not { Ends: null } s) return Array.Empty<Vector2>();
        var (xf, b) = (s.ToDoc, s.Box);
        return new[] { b.Position, new Vector2(b.End.X, b.Position.Y), b.End, new Vector2(b.Position.X, b.End.Y) }.Select(p => _c.ToScreen(xf * p)).ToArray();
    }

    public (Vector2 Foot, Vector2 Knob)? Knob()
    {
        if (See() is not { Ends: null } s) return null;
        var (xf, b) = (s.ToDoc, s.Box);
        var top = _c.ToScreen(xf * new Vector2(b.GetCenter().X, b.Position.Y));
        var up = (top - _c.ToScreen(xf * b.GetCenter())).Normalized();
        if (up == Vector2.Zero) up = Vector2.Up;
        return (top, top + up * KnobRise);
    }

    public (Grip Grip, int Handle) GripAt(Vector2 screen)
    {
        if (See() is not { } s) return (Grip.None, -1);
        if (Knob() is { } k && (k.Knob - screen).Length() < KnobReach) return (Grip.Turn, -1);
        var h = Handles();
        for (int i = 0; i < h.Length; i++)
            if ((h[i] - screen).Length() < HandleReach) return (s.Ends != null ? Grip.End : Grip.Corner, i);
        return (Grip.None, -1);
    }

    public bool OnInk(Vector2 d)
    {
        if (See() is not { } s) return false;
        var local = N(s.Layer.Xf().AffineInverse() * d);
        float slack = Slack(s.Layer);
        return s.Ops.Any(o => OpPick.Hit(o, local, slack));
    }

    public bool InBox(Vector2 d) => See() is { Ends: null } s ? s.Box.HasPoint(s.ToDoc.AffineInverse() * d) : OnInk(d);

    public (ArtLayer? Layer, List<int>? At) Under(Vector2 d)
    {
        for (int i = _c.Doc.Layers.Count - 1; i >= 0; i--)
        {
            var l = _c.Doc.Layers[i];
            if (!l.Visible || l.Locked) continue;
            var local = l.Xf().AffineInverse() * d;
            float slack = Slack(l);
            for (int k = l.Ops.Count - 1; k >= 0; k--)
            {
                var op = l.Ops[k];
                if (op.E || !Touched(op, local, slack)) continue;
                return (l, OpPick.Pickable(op) ? OpPick.Mates(l.Ops, k) : null);
            }
            if (l.Kind != PaintKind && _c.Covers(l, d)) return (l, null);
        }
        return (null, null);
    }

    static bool Touched(PaintOp op, Vector2 local, float slack) => op.K == FillKind ? ArtDraw.OpBounds(op).HasPoint(local) : OpPick.Hit(op, N(local), slack);

    public bool PressHandle(Vector2 d, Vector2 screen)
    {
        var (grip, handle) = GripAt(screen);
        if (grip == Grip.None) return false;
        Grab(grip, handle, d);
        return true;
    }

    public bool PressOwn(Vector2 d, Vector2 screen)
    {
        if (PressHandle(d, screen)) return true;
        if (!OnInk(d)) return false;
        Grab(Grip.Body, -1, d);
        return true;
    }

    public void GrabBody(Vector2 d) => Grab(Grip.Body, -1, d);

    void Grab(Grip grip, int handle, Vector2 d)
    {
        if (See() is not { } s) return;
        _grip = grip;
        _handle = handle;
        _downDoc = d;
        _changed = false;
        _from = s.Ops.Select(o => o.Pts()).ToList();
        _fromTurn = s.Ops[0].R ?? 0;
        _fromInk = s.Ink;
        _pad = s.Pad;
        _fromEnds = s.Ends;
    }

    public void Drag(Vector2 d, bool shift)
    {
        var ops = Ops;
        if (Layer is not { } l || _grip == Grip.None || ops.Count != _from.Count) return;
        switch (_grip)
        {
            case Grip.Body:
                Place(ops, Slide(l.Xf().AffineInverse().BasisXform(Carried(l, d, shift))));
                break;
            case Grip.Corner:
                Place(ops, Stretched(l, d, shift));
                break;
            case Grip.Turn:
                Place(ops, Swung(l, d, shift, out float turned));
                foreach (var op in ops) op.R = Mathf.Abs(turned) < 0.01f ? null : MathF.Round(turned, 2);
                break;
            case Grip.End:
                PullEnd(l, ops, d, shift);
                break;
        }
        _changed = true;
        _c.Refresh(l);
    }

    Vector2 Carried(ArtLayer l, Vector2 d, bool shift)
    {
        var by = d - _downDoc;
        if (shift) by = Mathf.Abs(by.X) > Mathf.Abs(by.Y) ? new Vector2(by.X, 0) : new Vector2(0, by.Y);
        if (!_c.P.Snap) return by;
        var middle = l.Xf() * Frame(_fromTurn) * _fromInk.GetCenter() + by;
        return by + _c.Snap(middle) - middle;
    }

    Transform2D Stretched(ArtLayer l, Vector2 d, bool free)
    {
        var frame = Frame(_fromTurn);
        var hp = StudioCanvas.HandlePts(_fromInk);
        Vector2 anchor = hp[HandleMath.Opposite(_handle)], grip = hp[_handle];
        var at = (l.Xf() * frame).AffineInverse() * d - new Vector2(Mathf.Sign(grip.X - anchor.X), Mathf.Sign(grip.Y - anchor.Y)) * _pad;
        var (sx, sy) = HandleMath.Scale(_handle, N(anchor), N(grip), N(at), free);
        return frame * Slide(anchor) * new Transform2D(0, new Vector2(sx, sy), 0, Vector2.Zero) * Slide(-anchor) * frame.AffineInverse();
    }

    Transform2D Swung(ArtLayer l, Vector2 d, bool stepped, out float turned)
    {
        var toLocal = l.Xf().AffineInverse();
        var pivot = Frame(_fromTurn) * _fromInk.GetCenter();
        float by = Mathf.RadToDeg((toLocal * d - pivot).Angle() - (toLocal * _downDoc - pivot).Angle());
        if (stepped) by = Mathf.Round((_fromTurn + by) / TurnStep) * TurnStep - _fromTurn;
        turned = Mathf.Wrap(_fromTurn + by, -180, 180);
        return Slide(pivot) * new Transform2D(Mathf.DegToRad(by), Vector2.Zero) * Slide(-pivot);
    }

    void PullEnd(ArtLayer l, List<PaintOp> ops, Vector2 d, bool straight)
    {
        if (_fromEnds is not { } e) return;
        var other = _handle == 0 ? e.B : e.A;
        var to = _c.Snap(d);
        if (straight) to = StudioCanvas.Constrain(l.Xf() * G(other), to);
        var pulled = N(l.Xf().AffineInverse() * to);
        OpPick.SetEnds(ops, _handle == 0 ? pulled : other, _handle == 0 ? other : pulled);
    }

    void Place(List<PaintOp> ops, Transform2D t)
    {
        for (int i = 0; i < ops.Count; i++) ops[i].P = ArtXf.Flat(_from[i].Select(p => t * p));
    }

    public void Release()
    {
        var grip = _grip;
        _grip = Grip.None;
        if (!_changed) return;
        _changed = false;
        _c.Commit(grip switch { Grip.Body => "Move", Grip.Turn => "Turn", _ => "Resize" });
    }

    public void Cancel()
    {
        var ops = Ops;
        if (_grip != Grip.None && _changed && Layer is { } l && ops.Count == _from.Count)
        {
            Place(ops, Transform2D.Identity);
            foreach (var op in ops) op.R = Mathf.Abs(_fromTurn) < 0.01f ? null : _fromTurn;
            _c.Refresh(l);
        }
        _grip = Grip.None;
        _changed = false;
    }

    public bool Nudge(Vector2 by)
    {
        if (See() is not { } s) return false;
        var t = Slide(s.Layer.Xf().AffineInverse().BasisXform(by));
        foreach (var op in s.Ops) op.P = ArtXf.Flat(op.Pts().Select(p => t * p));
        _c.Refresh(s.Layer);
        _c.Commit("Nudge");
        return true;
    }

    public bool Delete()
    {
        if (Layer is not { } l || !Any) return false;
        foreach (int i in _at.OrderByDescending(i => i)) l.Ops.RemoveAt(i);
        Drop();
        _c.Refresh(l);
        _c.Commit("Shape removed");
        return true;
    }

    public bool CanRestack(bool forward) => Layer is { } l && Any && OpPick.CanRestack(l.Ops, _at, forward);

    public bool Restack(bool forward)
    {
        if (Layer is not { } l || !Any || OpPick.Restack(l.Ops, _at, forward) is not { } now) return false;
        _at.Clear();
        _at.AddRange(now);
        _held.Clear();
        _held.AddRange(now.Select(i => l.Ops[i]));
        _c.Refresh(l);
        _c.Commit("Shape order");
        return true;
    }

    public bool Duplicate()
    {
        if (See() is not { } s) return false;
        string? group = s.Ops.Count > 1 || s.Ops[0].G != null ? ArtDoc.NewId() : null;
        var t = Slide(s.Layer.Xf().AffineInverse().BasisXform(new Vector2(_c.Px(CopyShift), _c.Px(CopyShift))));
        var at = new List<int>();
        foreach (var op in s.Ops)
        {
            var copy = op.Clone();
            copy.G = group;
            copy.P = ArtXf.Flat(op.Pts().Select(p => t * p));
            at.Add(s.Layer.Ops.Count);
            s.Layer.Ops.Add(copy);
        }
        Take(s.Layer, at, false);
        _c.Refresh(s.Layer);
        _c.Commit("Shape copied");
        return true;
    }

    public void Restyle(string what)
    {
        if (See() is not { } s) return;
        float w = _c.P.Line / StudioCanvas.LayerScale(s.Layer);
        string ink = Hex.Of(_c.Ink), fill = Hex.Of(_c.Ink2);
        foreach (var op in s.Ops)
        {
            op.C = ink;
            if (op.K != ShapeKind) op.W = w;
            else if (OpPick.IsHead(op, s.Ops)) op.Fc = ink;
            else (op.W, op.Fc) = _c.P.ShapeFill switch { "fill" => (0f, ink), "both" => (w, fill), _ => (w, (string?)null) };
        }
        if (s.Ends is { } e) OpPick.SetEnds(s.Ops, e.A, e.B);
        _c.Refresh(s.Layer);
        _c.Commit(what, sameStep: true);
    }

    void LoadLook()
    {
        if (See() is not { } s) return;
        var body = s.Ops.FirstOrDefault(o => !OpPick.IsHead(o, s.Ops)) ?? s.Ops[0];
        bool filled = body.K == ShapeKind && body.Fc != null;
        if (body.K == ShapeKind) _c.P.ShapeFill = !filled ? "line" : body.W > 0 ? "both" : "fill";
        if (body.W > 0) _c.P.Line = Mathf.Clamp(body.W * StudioCanvas.LayerScale(s.Layer), StudioCanvas.ThinnestLine, StudioCanvas.ThickestLine);
        _c.Ink = Hex.To(filled && body.W <= 0 ? body.Fc : body.C, _c.Ink);
        if (filled && body.W > 0) _c.Ink2 = Hex.To(body.Fc, _c.Ink2);
    }
}
