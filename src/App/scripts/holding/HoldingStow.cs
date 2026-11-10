using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed partial class Carry
{
    const double StowWaits = 0.3, StowFills = 0.6, StowSwallow = 0.18, RefusedAfter = 1.0;
    const float StowPull = 0.45f, StowShrink = 0.45f, StowSink = 0.7f, SwallowShrink = 0.85f, AboveHolder = 0.05f;

    PieceNode? _stowInto;
    double _stowSince;
    StowRing? _ring;

    public PieceNode? StowTarget => _stowInto is { } n && GodotObject.IsInstanceValid(n) ? n : null;

    double StowFill => _stowInto == null ? 0 : Math.Clamp((Now - _stowSince - StowWaits) / StowFills, 0, 1);

    bool StowArmed => StowTarget != null && StowFill >= 1;

    void WatchStow(Vec2 cursor)
    {
        var target = HolderUnder(cursor);
        if (target != _stowInto)
        {
            LeaveStow();
            if (target == null) return;
            _stowInto = target;
            _stowSince = Now;
            target.SetHovered(true);
        }
        if (_stowInto == null) return;
        _ring ??= new StowRing(_c.Scene);
        _ring.Show(_stowInto, (float)StowFill, StowArmed);
    }

    PieceNode? HolderUnder(Vec2 cursor)
    {
        if (_held.Count == 0 || Marching) return null;
        var layer = _held[0].Layer;
        if (_held.Any(h => h.Layer != layer || h.Node.IsEffect)) return null;
        var s = _c.S;
        PieceNode? best = null;
        foreach (var n in _pieces.Grid.Near(cursor))
        {
            if (_heldNodes.Contains(n) || n.Layer != layer || n.IsDie || n.IsEffect || !n.Contains(cursor)) continue;
            if (!n.Piece.HoldsPieces(n.Def) || !s.CanTouch(n.Layer, n.Piece)) continue;
            if (best == null || n.TopY > best.TopY) best = n;
        }
        return best;
    }

    void LeaveStow()
    {
        if (_stowInto is { } was && GodotObject.IsInstanceValid(was)) was.SetHovered(false);
        _stowInto = null;
        _ring?.Hide();
    }

    (Vec2 pos, double scale, float y) Pulled(Vec2 pos, double scale, float y)
    {
        if (StowTarget is not { } into) return (pos, scale, y);
        float k = (float)StowFill;
        if (k <= 0) return (pos, scale, y);
        float ease = k * k * (3 - 2 * k);
        var centre = into.Piece.Pos;
        var pulled = new Vec2(pos.X + (centre.X - pos.X) * StowPull * ease, pos.Z + (centre.Z - pos.Z) * StowPull * ease);
        float floor = into.TopY + AboveHolder;
        return (pulled, scale * (1 - StowShrink * ease), Mathf.Lerp(y, Mathf.Max(floor, y - LiftHeight * StowSink), ease));
    }

    bool DropIntoHolder()
    {
        if (!StowArmed || StowTarget is not { } into) { LeaveStow(); return false; }
        StowHeld(into);
        return true;
    }

    public bool PutInto(string layer, string holder)
    {
        if (!Holding || _pieces.NodeOf(layer, holder) is not { } into || _heldNodes.Contains(into) || !_c.S.CanTouch(layer, into.Piece)) return false;
        if (_held.Any(h => h.Layer != layer)) return false;
        Rehome();
        EndMarch();
        StowHeld(into);
        return true;
    }

    void StowHeld(PieceNode into)
    {
        var held = _held.Where(h => GodotObject.IsInstanceValid(h.Node)).ToList();
        var riding = held.SelectMany(h => h.Node.Piece.Attached).ToHashSet();
        var cmds = held.Where(h => !riding.Contains(h.Id))
            .Select(h => (Command)new Stow { Layer = into.Layer, Holder = into.Piece.Id, Id = h.Id }).ToList();
        var nodes = held.Select(h => h.Node).ToList();
        var name = into.Piece.Label is { Length: > 0 } lb ? lb : into.Def?.Name ?? "the holder";
        _held.Clear();
        _heldNodes.Clear();
        _floor = -1;
        StopShake();
        LeaveStow();
        Swallow(nodes, into, () =>
        {
            _c.DoAll(cmds);
            _c.Say(nodes.Count == 1 ? $"Put into {name}" : $"{nodes.Count} pieces put into {name}");
        });
    }

    void Swallow(List<PieceNode> nodes, PieceNode into, Action then)
    {
        _c.Sound.Gathered(nodes);
        var mouth = new Vector3(into.Position.X, into.TopY, into.Position.Z);
        var from = nodes.ToDictionary(n => n, n => (pos: n.Position, scale: n.Scale));
        var tw = _c.Scene.CreateTween();
        tw.TweenMethod(Callable.From<float>(k =>
        {
            float e = k * k;
            foreach (var (n, f) in from)
            {
                if (!GodotObject.IsInstanceValid(n)) continue;
                n.Position = f.pos.Lerp(mouth, e);
                n.Scale = f.scale * (1 - SwallowShrink * e);
                _c.Moving(n);
            }
        }), 0f, 1f, StowSwallow);
        tw.Finished += () =>
        {
            foreach (var n in nodes) if (GodotObject.IsInstanceValid(n)) n.Held = false;
            then();
            _c.Scene.GetTree().CreateTimer(RefusedAfter).Timeout += () =>
            {
                foreach (var n in nodes) if (GodotObject.IsInstanceValid(n) && !n.IsQueuedForDeletion()) n.Refresh(n.Piece);
            };
        };
    }

    sealed class StowRing
    {
        const int Steps = 72;
        const float Width = 0.08f, WidthPerRadius = 0.12f, Gap = 0.18f, Above = 0.03f;
        static readonly Color Track = new(0, 0, 0, 0.22f);

        readonly MeshInstance3D _mesh;
        readonly ImmediateMesh _im = new();

        public StowRing(Node3D scene)
        {
            var mat = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                NoDepthTest = true, CullMode = BaseMaterial3D.CullModeEnum.Disabled, RenderPriority = 10,
            };
            _mesh = new MeshInstance3D { Mesh = _im, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false, Layers = Table.LiveOnlyLayer };
            scene.AddChild(_mesh);
        }

        public void Show(PieceNode into, float fill, bool armed)
        {
            float pulse = armed ? 1 + 0.06f * Mathf.Sin((float)(Time.GetTicksMsec() / 1000.0) * 12) : 1;
            float r = (Mathf.Max(into.Footprint.X, into.Footprint.Y) * 0.5f * (float)into.Piece.Scale + Gap) * pulse;
            _mesh.Position = new Vector3(into.Position.X, into.TopY + Above, into.Position.Z);
            _mesh.Visible = true;
            float w = Mathf.Max(Width, r * WidthPerRadius);
            _im.ClearSurfaces();
            Arc(0, 1, r, w, Track);
            if (fill > 0) Arc(0, fill, r, armed ? w * 1.5f : w, Ui.Accent);
        }

        void Arc(float from, float to, float r, float width, Color c)
        {
            int n = Math.Max(2, (int)(Steps * (to - from)) + 1);
            _im.SurfaceBegin(Mesh.PrimitiveType.TriangleStrip);
            for (int i = 0; i < n; i++)
            {
                float a = Mathf.Tau * (from + (to - from) * i / (n - 1)) - Mathf.Pi / 2;
                var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                _im.SurfaceSetColor(c);
                _im.SurfaceAddVertex(d * (r - width / 2));
                _im.SurfaceSetColor(c);
                _im.SurfaceAddVertex(d * (r + width / 2));
            }
            _im.SurfaceEnd();
        }

        public void Hide() => _mesh.Visible = false;
    }
}
