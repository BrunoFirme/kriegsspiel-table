using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Client.Hand;
using KTable.Core.Model;

namespace KTable.App;

sealed class FormationTool
{
    readonly TableContext _c;
    readonly ToolOverlay _overlay;
    readonly Selection _selection;
    readonly Carry _hand;

    public FormationTool(TableContext c, ToolOverlay overlay, Selection selection, Carry hand)
    {
        _c = c; _overlay = overlay; _selection = selection; _hand = hand;
        _c.SelectionChanged += () => { if (_c.Tool == Tool.Formation) { _c.Say(Hint()); Changed?.Invoke(); } };
    }

    public event Action? Changed;

    Settings Cfg => _c.Settings;

    public FormationKind Kind => Formations.Find(Cfg.Formation) ?? Formations.All[0];

    public double Gap => Math.Clamp(Cfg.FormationGap, 0, Formations.MostGap);

    int _depth;

    public int Depth(int n) => _depth > 0 ? _depth : Formations.DefaultDepth(Kind, n);

    const float RideAbove = 0.005f;
    const double SameSpot = 1e-3, SameTurn = 0.5, LeastDrag = 0.4, GapFine = 0.05, GapCoarse = 0.25, GapFineBelow = 0.5;

    sealed record Formed(HashSet<string> Ids, string Kind, double Facing, int? Front, Dictionary<PieceNode, (Vec2 Pos, double Rot)> Spots, Dictionary<PieceNode, int> Order);
    Formed? _formed;

    public List<PieceNode> Units()
    {
        var all = _selection.Movable.Where(n => !n.IsDie).ToList();
        bool Rides(PieceNode n) => all.Any(o => o != n && (o.Piece.Attached.Contains(n.Piece.Id)
            || n.Piece.Y > RideAbove && n.RestY > o.RestY + RideAbove && o.ContainsAt(n.Piece.Pos, o.Piece.Pos, o.Piece.RotDeg)));
        return all.Where(n => !Rides(n)).ToList();
    }

    public int Count => Units().Count;

    public string Hint() => Count < 2
        ? "Select the pieces to form up: drag a box round them, Shift adds"
        : "Pick a formation, or drag across the table where its front should stand · Q/E turn · Ctrl + wheel spacing · Shift + wheel ranks";

    Pitch PitchOf(List<PieceNode> units)
    {
        double g = 1 + Gap;
        return new(units.Max(n => n.Footprint.X) * g, units.Max(n => n.Footprint.Y) * g);
    }

    Formed? Mine(List<PieceNode> units)
    {
        if (_formed is not { } f || !f.Ids.SetEquals(units.Select(n => n.Piece.Id))) return null;
        if (_hand.Marching) return f;
        return units.All(n => f.Spots.TryGetValue(n, out var s) && (n.Piece.Pos - s.Pos).Length < SameSpot
            && Math.Abs(Mathf.Wrap((float)(n.Piece.RotDeg - s.Rot), -180, 180)) < SameTurn) ? f : null;
    }

    Vec2 Where(PieceNode n) => _hand.Marching && _formed?.Spots.TryGetValue(n, out var s) == true ? s.Pos : n.Piece.Pos;

    double RotOf(PieceNode n) => _hand.Marching && _formed?.Spots.TryGetValue(n, out var s) == true ? s.Rot : n.Piece.RotDeg;

    Vec2 Centre(List<PieceNode> units)
    {
        var at = units.Select(Where).ToList();
        return new Vec2(at.Average(p => p.X), at.Average(p => p.Z));
    }

    bool Enough(List<PieceNode> units)
    {
        if (units.Count >= 2) return true;
        _c.Say("Select two or more pieces to form up");
        return false;
    }

    public void Form(FormationKind kind)
    {
        if (Cfg.Formation != kind.Id) { Cfg.Formation = kind.Id; Cfg.Save(); _depth = 0; }
        var units = Units();
        if (!Enough(units)) { Changed?.Invoke(); return; }
        var mine = Mine(units);
        double facing = mine?.Facing ?? Formations.Facing(units.Select(RotOf)) ?? units[0].Piece.RotDeg;
        int? front = mine != null && mine.Kind == kind.Id ? mine.Front : null;
        Lay(units, kind, Centre(units), facing, front, frontAt: false, mine);
        _c.Say(Describe(kind, units.Count, front));
    }

    string Describe(FormationKind kind, int n, int? front)
    {
        if (!kind.TakesFront) return kind.Name;
        int wide = front ?? (kind.Depth == FormationDepth.Abreast ? Depth(n) : (n + Depth(n) - 1) / Depth(n));
        wide = Math.Clamp(wide, 1, n);
        if (kind.Id == "block" && front == null) return kind.Name;
        return $"{kind.Name} · {wide} wide, {(n + wide - 1) / wide} deep";
    }

    public void Turn(double deg)
    {
        var units = Units();
        if (!Enough(units)) return;
        var mine = Mine(units);
        var centre = Centre(units);
        double rad = deg * Math.PI / 180;
        var targets = units.Select(n => (n, centre + Formation.Turned(Where(n) - centre, rad), Angles.Norm360(RotOf(n) + deg))).ToList();
        var facing = mine != null ? Angles.Norm360(mine.Facing + deg) : Formations.Facing(targets.Select(t => t.Item3)) ?? targets[0].Item3;
        _formed = new Formed(units.Select(n => n.Piece.Id).ToHashSet(), mine?.Kind ?? "", facing, mine?.Front, targets.ToDictionary(t => t.n, t => (t.Item2, t.Item3)), mine?.Order ?? new());
        _hand.March(targets);
    }

    public void Space(int notches)
    {
        double step = Gap < GapFineBelow || Gap == GapFineBelow && notches < 0 ? GapFine : GapCoarse;
        SetGap(Gap + notches * step);
    }

    public void SetGap(double gap)
    {
        Cfg.FormationGap = Math.Round(Math.Clamp(gap, 0, Formations.MostGap), 2);
        Cfg.Save();
        Relay(keepFront: true);
        _c.Say($"Spacing {Gap * 100:0}% of a piece");
        Changed?.Invoke();
    }

    public void Deepen(int by)
    {
        if (Kind.Depth == FormationDepth.None) return;
        var units = Units();
        SetDepth(Depth(Math.Max(1, units.Count)) + by);
    }

    public void SetDepth(int depth)
    {
        _depth = Math.Clamp(depth, 1, Formations.MostRanks);
        Relay(keepFront: false);
        var units = Units();
        if (units.Count >= 2) _c.Say(Describe(Kind, units.Count, null));
        Changed?.Invoke();
    }

    void Relay(bool keepFront)
    {
        var units = Units();
        if (units.Count < 2 || Mine(units) is not { } mine || Formations.Find(mine.Kind) is not { } kind) return;
        Lay(units, kind, Centre(units), mine.Facing, keepFront ? mine.Front : null, frontAt: false, mine);
    }

    List<(PieceNode Node, Vec2 Pos, double Rot)> Plan(List<PieceNode> units, FormationKind kind, Vec2 at, double facing, int? front, bool frontAt, Formed? before, out Dictionary<PieceNode, int> order)
    {
        var places = Formations.Place(Formations.Slots(kind, units.Count, PitchOf(units), _depth, front), at, facing, frontAt);
        if (before is { } b && b.Kind == kind.Id && b.Order.Count == units.Count && units.All(b.Order.ContainsKey)) order = b.Order;
        else
        {
            var m = Formations.Match(units.Select(Where).ToList(), places.Select(p => p.Pos).ToList());
            order = units.Select((n, i) => (n, m[i])).ToDictionary(x => x.n, x => x.Item2);
        }
        var o = order;
        return units.Select(n => (n, places[o[n]].Pos, places[o[n]].Rot)).ToList();
    }

    void Lay(List<PieceNode> units, FormationKind kind, Vec2 at, double facing, int? front, bool frontAt, Formed? before)
    {
        var targets = Plan(units, kind, at, facing, front, frontAt, before, out var order);
        _formed = new Formed(units.Select(n => n.Piece.Id).ToHashSet(), kind.Id, facing, front, targets.ToDictionary(t => t.Node, t => (t.Pos, t.Rot)), order);
        _hand.March(targets);
        Changed?.Invoke();
    }

    public bool Dragging { get; private set; }
    Vec2 _from, _to;
    List<(Vec2 Pos, double Rot, Vector2 Size)>? _ghost;

    public bool Press(Vec2 g, bool adding, bool onPiece)
    {
        if (adding || onPiece || Units().Count < 2) return false;
        _hand.Land();
        Dragging = true;
        _from = _to = g;
        _ghost = null;
        return true;
    }

    (FormationKind kind, double facing, int? front)? DragShape(List<PieceNode> units)
    {
        double len = (_to - _from).Length;
        if (units.Count < 2 || len < PitchOf(units).Across * LeastDrag) return null;
        var kind = Kind;
        int? front = kind.TakesFront ? Math.Clamp((int)Math.Round(len / PitchOf(units).Across), 1, units.Count) : null;
        return (kind, Formations.FacingAlong(_from, _to), front);
    }

    Vec2 Mid => new((_from.X + _to.X) / 2, (_from.Z + _to.Z) / 2);

    public void Drag(Vec2 g)
    {
        _to = g;
        var units = Units();
        if (DragShape(units) is not var (kind, facing, front)) { _ghost = null; return; }
        _ghost = Plan(units, kind, Mid, facing, front, frontAt: true, null, out _).Select(t => (t.Pos, t.Rot, t.Node.Footprint)).ToList();
        _c.Say(Describe(kind, units.Count, front));
    }

    public void Release()
    {
        if (!Dragging) return;
        Dragging = false;
        _ghost = null;
        var units = Units();
        if (DragShape(units) is not var (kind, facing, front)) return;
        Lay(units, kind, Mid, facing, front, frontAt: true, null);
    }

    public void Cancel()
    {
        Dragging = false;
        _ghost = null;
    }

    static readonly StandardMaterial3D GhostInk = Ink(new Color(0.96f, 0.94f, 0.86f, 0.9f));
    static readonly StandardMaterial3D FrontInk = Ink(Ui.AccentInk);

    static StandardMaterial3D Ink(Color c) => new()
    {
        AlbedoColor = c, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, NoDepthTest = true,
        Transparency = c.A < 1 ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled,
    };

    public void Draw()
    {
        if (!Dragging) return;
        var im = _overlay.Mesh;
        _overlay.SegPx(im, FrontInk, _from, _to, 0.3f, Table.ToolPx);
        if (_ghost == null) return;
        foreach (var (pos, rot, size) in _ghost)
        {
            var (right, back) = Formations.Axes(rot);
            double w = size.X / 2, d = size.Y / 2;
            Vec2 C(double x, double z) => new(pos.X + right.X * x + back.X * z, pos.Z + right.Z * x + back.Z * z);
            _overlay.RibbonPx(im, GhostInk, new List<Vec2> { C(-w, -d), C(w, -d), C(w, d), C(-w, d), C(-w, -d) }, 0.25f, 2f);
        }
    }

    public bool Key(InputEventKey k, Keymap km)
    {
        foreach (var kind in Formations.All)
            if (km.Is(k, "form:" + kind.Id)) { Form(kind); return true; }
        if (Units().Count < 2) return false;
        if (km.Is(k, "turnLeft")) { Turn(k.ShiftPressed ? -1 : -15); return true; }
        if (km.Is(k, "turnRight")) { Turn(k.ShiftPressed ? 1 : 15); return true; }
        return false;
    }
}
