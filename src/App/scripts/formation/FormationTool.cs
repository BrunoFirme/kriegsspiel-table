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
        _c = c;
        _overlay = overlay;
        _selection = selection;
        _hand = hand;
        _c.SelectionChanged += () => { if (_c.Tool == Tool.Formation) { _c.Say(Hint()); Changed?.Invoke(); } };
    }

    public event Action? Changed;

    Settings Cfg => _c.Settings;

    public FormationKind Kind => Formations.Named(Cfg.Formation)?.Kind ?? Formations.All[0];

    public double Gap => Math.Clamp(Cfg.FormationGap, 0, Formations.MostGap);

    public double Distance => Math.Clamp(Cfg.FormationDistance ?? Cfg.FormationGap, 0, Formations.MostGap);

    int KeptDepth => Cfg.FormationRanks.GetValueOrDefault(Kind.Id);

    public bool KeepsSpacing => Cfg.StretchKeepsSpacing;

    public void SetKeepsSpacing(bool on)
    {
        Cfg.StretchKeepsSpacing = on;
        Spaced(on ? "The line stretches until the pieces stand the interval apart, and no further" : "The pieces spread to the line's ends, however far");
    }

    public int Depth(int n) => KeptDepth > 0 ? KeptDepth : Formations.DefaultDepth(Kind, n);

    const float RideAbove = 0.005f;
    const double SameSpot = 1e-3, SameTurn = 0.5, LeastDrag = 0.4, DrawEvery = 0.1, GapFine = 0.05, GapCoarse = 0.25, GapFineBelow = 0.5;

    sealed record Formed(HashSet<string> Ids, string Kind, double Facing, int? Front, IReadOnlyList<Vec2>? Path, Dictionary<PieceNode, (Vec2 Pos, double Rot)> Spots,
        Dictionary<PieceNode, int> Order);
    Formed? _formed;

    public List<PieceNode> Units()
    {
        var readyOut = _selection.ReadyOut();
        var all = _selection.Movable.Where(n => !n.IsDie && !readyOut.ContainsKey(n.Piece.Id)).ToList();
        bool Rides(PieceNode n) => all.Any(o => o != n && (o.Piece.Attached.Contains(n.Piece.Id)
            || n.Piece.Y > RideAbove && n.RestY > o.RestY + RideAbove && o.ContainsAt(n.Piece.Pos, o.Piece.Pos, o.Piece.RotDeg)));
        return all.Where(n => !Rides(n)).ToList();
    }

    public int Count => Units().Count;

    public string Hint()
    {
        var units = Units();
        if (units.Count < 2) return "Select the pieces to form up: click, Shift+click or drag a box · double-click a piece for all of its kind in view";
        return (Kind.AlongLine ? "Right-drag the line to string them along, back over it to shorten it" : Kind.Marches ? "Right-drag the line of march, the head where it ends" : "Right-drag where the front should stand")
            + (FormedHere(units) ? Adjusting : "");
    }

    string Adjusting => $" · {Keys("turnLeft", "turnRight")} wheel it · {Keys("smaller", "larger")} interval" + (Kind.HasDistance ? ", with Shift distance" : "")
        + (Kind.Depth == FormationDepth.None ? "" : $" · {Keys("statePrev", "stateNext")} {(Kind.Depth == FormationDepth.Abreast ? "abreast" : "ranks")}");

    bool FormedHere(List<PieceNode> units) => _formed is { } f && f.Ids.SetEquals(units.Select(n => n.Piece.Id));

    string Keys(string less, string more) => Cfg.Keys.Label(less) + " " + Cfg.Keys.Label(more);

    Pitch PitchOf(List<PieceNode> units) => Formations.PitchOf(units.Max(n => n.Footprint.X), units.Max(n => n.Footprint.Y), Gap, Distance);

    double Touching(List<PieceNode> units) => Formations.PitchOf(units.Max(n => n.Footprint.X), units.Max(n => n.Footprint.Y), 0, Distance).Across;

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
        if (Cfg.Formation != kind.Id)
        {
            Cfg.Formation = kind.Id;
            Cfg.Save();
        }
        var units = Units();
        if (!Enough(units)) { Changed?.Invoke(); return; }
        var mine = Mine(units);
        double facing = mine?.Facing ?? Formations.Facing(units.Select(RotOf)) ?? units[0].Piece.RotDeg;
        bool same = mine != null && mine.Kind == kind.Id;
        int? front = same ? mine!.Front : null;
        var path = kind.AlongLine ? (same ? mine!.Path : null) ?? Straight(units, facing) : null;
        Lay(units, kind, Centre(units), facing, front, path, frontAt: false, mine);
        _c.Say((path != null ? Strung(kind, units, path) : Describe(kind, units.Count, front)) + Adjusting);
    }

    List<Vec2> Straight(List<PieceNode> units, double facing)
    {
        var (right, _) = Formations.Axes(facing);
        var along = units.Select(n => { var p = Where(n); return p.X * right.X + p.Z * right.Z; }).ToList();
        double half = Math.Max(along.Max() - along.Min(), (units.Count - 1) * PitchOf(units).Across) / 2;
        var c = Centre(units);
        return new() { new(c.X - right.X * half, c.Z - right.Z * half), new(c.X + right.X * half, c.Z + right.Z * half) };
    }

    string Strung(FormationKind kind, List<PieceNode> units, IReadOnlyList<Vec2> path) => kind.Name + TooShort(units, path);

    string TooShort(List<PieceNode> units, IReadOnlyList<Vec2>? path) =>
        path != null && Formations.PastTheEnd(path, units.Count, PitchOf(units).Across, KeepsSpacing, Touching(units)) is var past and > 0
            ? $" · the line is too short for them: the last {past} run on past its end" : "";

    string Describe(FormationKind kind, int n, int? front)
    {
        if (!kind.TakesFront) return kind.Name;
        int wide = front ?? (kind.Depth == FormationDepth.Abreast ? Depth(n) : (n + Depth(n) - 1) / Depth(n));
        wide = Math.Clamp(wide, 1, n);
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
        var spots = targets.ToDictionary(t => t.n, t => (t.Item2, t.Item3));
        var path = mine?.Path?.Select(p => centre + Formation.Turned(p - centre, rad)).ToList();
        _formed = new Formed(units.Select(n => n.Piece.Id).ToHashSet(), mine?.Kind ?? "", facing, mine?.Front, path, spots, mine?.Order ?? new());
        _hand.March(targets);
    }

    public void Space(int notches, bool distance = false)
    {
        double now = distance ? Distance : Gap;
        double next = now + notches * (now < GapFineBelow || now == GapFineBelow && notches < 0 ? GapFine : GapCoarse);
        if (distance) SetDistance(next);
        else SetGap(next);
    }

    static double Kept(double gap) => Math.Round(Math.Clamp(gap, 0, Formations.MostGap), 2);

    public void SetGap(double gap)
    {
        Cfg.FormationDistance ??= Cfg.FormationGap;
        Cfg.FormationGap = Kept(gap);
        Spaced($"Interval {Gap * 100:0}% of a piece's width");
    }

    public void SetDistance(double distance)
    {
        Cfg.FormationDistance = Kept(distance);
        Spaced($"Distance {Distance * 100:0}% of a piece's width");
    }

    void Spaced(string what)
    {
        Cfg.Save();
        _c.Say(what + Relay(keepFront: true));
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
        Cfg.FormationRanks[Kind.Id] = Math.Clamp(depth, 1, Formations.MostRanks);
        Cfg.Save();
        Relay(keepFront: false);
        var units = Units();
        if (units.Count >= 2) _c.Say(Describe(Kind, units.Count, null));
        Changed?.Invoke();
    }

    string Relay(bool keepFront)
    {
        var units = Units();
        if (units.Count < 2 || _formed is not { } formed || !formed.Ids.SetEquals(units.Select(n => n.Piece.Id)) || Formations.Find(formed.Kind) is not { } kind) return "";
        var mine = Mine(units);
        double facing = mine?.Facing ?? Formations.Facing(units.Select(RotOf)) ?? units[0].Piece.RotDeg;
        bool turnedOnly = units.All(n => formed.Spots.TryGetValue(n, out var s) && (n.Piece.Pos - s.Pos).Length < SameSpot);
        var path = mine?.Path ?? (kind.AlongLine ? (turnedOnly ? formed.Path : null) ?? Straight(units, facing) : null);
        Lay(units, kind, Centre(units), facing, keepFront ? mine?.Front : null, path, frontAt: false, formed);
        return TooShort(units, path);
    }

    List<(PieceNode Node, Vec2 Pos, double Rot)> Plan(List<PieceNode> units, FormationKind kind, Vec2 at, double facing, int? front, IReadOnlyList<Vec2>? path,
        bool frontAt, Formed? before, out Dictionary<PieceNode, int> order)
    {
        var places = kind.AlongLine && path != null
            ? Formations.Along(path, units.Count, PitchOf(units).Across, KeepsSpacing, Touching(units))
            : Formations.Place(Formations.Slots(kind, units.Count, PitchOf(units), KeptDepth, front), at, facing, frontAt);
        if (before is { } b && b.Kind == kind.Id && b.Order.Count == units.Count && units.All(b.Order.ContainsKey)) order = b.Order;
        else
        {
            var m = Formations.Match(units.Select(n => n.MiddleAt(Where(n), RotOf(n))).ToList(), places.Select(p => p.Pos).ToList());
            order = units.Select((n, i) => (n, m[i])).ToDictionary(x => x.n, x => x.Item2);
        }
        var o = order;
        return units.Select(n => (n, places[o[n]].Pos - n.CentreOffset(places[o[n]].Rot), places[o[n]].Rot)).ToList();
    }

    void Lay(List<PieceNode> units, FormationKind kind, Vec2 at, double facing, int? front, IReadOnlyList<Vec2>? path, bool frontAt, Formed? before)
    {
        var targets = Plan(units, kind, at, facing, front, path, frontAt, before, out var order);
        _formed = new Formed(units.Select(n => n.Piece.Id).ToHashSet(), kind.Id, facing, front, kind.AlongLine ? path : null,
            targets.ToDictionary(t => t.Node, t => (t.Pos, t.Rot)), order);
        _hand.March(targets);
        Changed?.Invoke();
    }

    public bool Dragging { get; private set; }
    Vec2 _from, _to;
    readonly List<Vec2> _drawn = new();
    List<(Vec2 Pos, double Rot, Vector2 Size)>? _ghost;

    public bool Press(Vec2 g)
    {
        if (Units().Count < 2) return false;
        _hand.Land();
        Dragging = true;
        _from = _to = g;
        _drawn.Clear();
        _drawn.Add(g);
        _across = PitchOf(Units()).Across;
        _ghost = null;
        return true;
    }

    double _across = 1;

    List<Vec2> Drawn()
    {
        var path = _drawn.ToList();
        Formations.Extend(path, _to, _across, Formations.Least);
        return path;
    }

    (FormationKind kind, Vec2 at, double facing, int? front, IReadOnlyList<Vec2>? path)? DragShape(List<PieceNode> units)
    {
        if (units.Count < 2) return null;
        var kind = Kind;
        double across = PitchOf(units).Across;
        if (kind.AlongLine)
        {
            var path = Drawn();
            return Formations.Length(path) < across * LeastDrag ? null : (kind, _from, Formations.FacingOf(path), null, path);
        }
        double len = (_to - _from).Length;
        if (len < across * LeastDrag) return null;
        if (kind.Marches) return (kind, _to, Formations.FacingDown(_from, _to), null, null);
        int? front = kind.TakesFront ? Math.Clamp((int)Math.Round(len / across), 1, units.Count) : null;
        return (kind, new Vec2((_from.X + _to.X) / 2, (_from.Z + _to.Z) / 2), Formations.FacingAlong(_from, _to), front, null);
    }

    public void Drag(Vec2 g)
    {
        _to = g;
        var units = Units();
        if (units.Count >= 2) _across = PitchOf(units).Across;
        Formations.Extend(_drawn, g, _across, _across * DrawEvery);
        if (DragShape(units) is not var (kind, at, facing, front, path)) { _ghost = null; return; }
        _ghost = Plan(units, kind, at, facing, front, path, frontAt: true, null, out _).Select(t => (t.Node.MiddleAt(t.Pos, t.Rot), t.Rot, t.Node.Footprint)).ToList();
        _c.Say(path != null ? Strung(kind, units, path) : Describe(kind, units.Count, front));
    }

    public void Release()
    {
        if (!Dragging) return;
        Dragging = false;
        _ghost = null;
        var units = Units();
        if (DragShape(units) is not var (kind, at, facing, front, path)) return;
        Lay(units, kind, at, facing, front, path, frontAt: true, null);
        _c.Say((path != null ? Strung(kind, units, path) : Describe(kind, units.Count, front)) + Adjusting);
    }

    public void Cancel()
    {
        Dragging = false;
        _ghost = null;
    }

    static readonly StandardMaterial3D GhostInk = Ink(new Color(0.96f, 0.94f, 0.86f, 0.9f));
    static StandardMaterial3D? _frontInk;
    static StandardMaterial3D FrontInk => _frontInk ??= Ink(Ui.AccentInk);

    static StandardMaterial3D Ink(Color c) => new()
    {
        AlbedoColor = c, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, NoDepthTest = true,
        Transparency = c.A < 1 ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled,
    };

    public void Draw()
    {
        if (!Dragging) return;
        var im = _overlay.Mesh;
        if (Kind.AlongLine) _overlay.RibbonPx(im, FrontInk, Drawn(), 0.3f, Table.ToolPx);
        else _overlay.SegPx(im, FrontInk, _from, _to, 0.3f, Table.ToolPx);
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
            if (km.Is(k, Keymap.FormationPrefix + kind.Id)) { Form(kind); return true; }
        if (!FormedHere(Units())) return false;
        if (km.Is(k, "turnLeft")) { Turn(k.ShiftPressed ? -1 : -15); return true; }
        if (km.Is(k, "turnRight")) { Turn(k.ShiftPressed ? 1 : 15); return true; }
        bool distance = k.ShiftPressed && Kind.HasDistance;
        if (km.Is(k, "larger")) { Space(1, distance); return true; }
        if (km.Is(k, "smaller")) { Space(-1, distance); return true; }
        if (Kind.Depth == FormationDepth.None) return false;
        if (km.Is(k, "stateNext")) { Deepen(1); return true; }
        if (km.Is(k, "statePrev")) { Deepen(-1); return true; }
        return false;
    }
}
