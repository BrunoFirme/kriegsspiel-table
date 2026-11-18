using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;
using C = KTable.Core.Model.CrowdCommands;

namespace KTable.App;

sealed class CrowdTool(CrowdLayer layer, TableContext c)
{
    const float PickPx = 12, DragPx = 5;
    public static readonly IReadOnlyList<(int Share, string Name)> Shares = [(1, "All"), (2, "1 in 2"), (3, "1 in 3"), (10, "1 in 10")];

    Vector2? _down, _rightDown;
    Vec2? _rightAt;
    bool _box, _twice;

    public int Share { get; set; } = 1;
    public string? Calling { get; set; }
    public string Pace { get; set; } = CrowdPaces.Walk;
    public event Action? Changed;

    public int Count => Chosen().Count;

    public string Hint() => layer.Picked.Count == 0
        ? "Click a person or drag a box round people · Shift adds · double-click takes their group"
        : $"{Count} people · right-click where they go or what they face · right-drag lays a line's front · Esc lets go";

    public void Press(Vector2 screen, bool shift, bool twice)
    {
        (_down, _box, _twice) = (screen, false, twice);
        if (twice) Group(screen);
    }

    public void Motion(Vector2 screen)
    {
        if (_down is not { } from) return;
        if (!_box && (screen - from).Length() > DragPx) _box = true;
        if (_box) c.NotifyScreenRect(new Rect2(from, screen - from).Abs());
    }

    public void Release(Vector2 screen, bool shift)
    {
        if (_down is not { } from) return;
        _down = null;
        if (_box)
        {
            c.NotifyScreenRect(null);
            var box = new Rect2(from, screen - from).Abs();
            if (!shift) layer.Picked.Clear();
            foreach (var p in layer.Seen.Where(p => !c.Rig.Cam.IsPositionBehind(p.At) && box.HasPoint(c.Rig.Cam.UnprojectPosition(p.At)))) layer.Picked.Add((p.Town, p.Person));
        }
        else if (!_twice)
        {
            var hit = Nearest(screen);
            if (!shift) layer.Picked.Clear();
            if (hit is { } who && !layer.Picked.Remove(who)) layer.Picked.Add(who);
        }
        Told();
    }

    public void LetGo()
    {
        layer.Picked.Clear();
        Told();
    }

    void Told()
    {
        Changed?.Invoke();
        c.Say(Hint());
    }

    (int Town, int Person)? Nearest(Vector2 screen)
    {
        (int, int)? best = null;
        float nearest = PickPx;
        foreach (var p in layer.Seen)
        {
            if (c.Rig.Cam.IsPositionBehind(p.At)) continue;
            float px = c.Rig.Cam.UnprojectPosition(p.At).DistanceTo(screen);
            if (px > nearest) continue;
            (nearest, best) = (px, (p.Town, p.Person));
        }
        return best;
    }

    void Group(Vector2 screen)
    {
        if (Nearest(screen) is not { } who) return;
        layer.Picked.Clear();
        if (layer.OrderOf(who.Town, who.Person) is { } order && c.S.View.Crowds?.Orders?.FirstOrDefault(o => o.Id == order) is { } given)
            foreach (var m in layer.Members(given)) layer.Picked.Add(m);
        else
        {
            var roster = layer.Roster(who.Town);
            var r = roster.Residents[who.Person];
            int lead = r.Leader >= 0 ? r.Leader : who.Person;
            foreach (var p in roster.Residents.Where(p => p.Entry.Id == lead || p.Leader == lead)) layer.Picked.Add((who.Town, p.Entry.Id));
        }
        Told();
    }

    List<(int Town, int Person)> Chosen() => layer.Picked
        .Where(p => Calling == null || layer.Roster(p.Town).Residents[p.Person].Entry.Calling == Calling)
        .OrderBy(p => p.Town).ThenBy(p => p.Person).Where((_, k) => k % Math.Max(1, Share) == 0).ToList();

    public IEnumerable<string> Callings() => layer.Picked.Select(p => layer.Roster(p.Town).Residents[p.Person].Entry.Calling).Distinct().Order();

    public bool Right(bool pressed, Vector2 screen, Vec2? ground)
    {
        if (layer.Picked.Count == 0) return false;
        if (pressed)
        {
            (_rightDown, _rightAt) = (screen, ground);
            return true;
        }
        if (_rightDown is not { } from || _rightAt is not { } at) return true;
        _rightDown = null;
        Menu(screen, at, (screen - from).Length() >= DragPx ? ground : null);
        return true;
    }

    void Menu(Vector2 screen, Vec2 at, Vec2? end)
    {
        var menu = new MenuCard();
        menu.Head($"{Count} people", Calling == null ? "" : Calling);
        menu.Separator();
        var piece = c.S.View.Layer(Layers.Umpire).Where(p => (p.Pos - at).Length < PieceReach()).MinBy(p => (p.Pos - at).Length);
        var person = Nearest(screen) is { } other && !layer.Picked.Contains(other) ? other : ((int, int)?)null;
        if (end is { } to) FrontRows(menu, at, to);
        else if (piece != null) TokenRows(menu, piece);
        else if (person is { } them) GroupRows(menu, them);
        else GroundRows(menu, at);
        menu.Show(c.Scene, screen);
    }

    double PieceReach() => (c.S.View.Calibration.UnitsPerMeter ?? 1) * 3;

    void GroundRows(MenuCard menu, Vec2 at)
    {
        menu.Item(C.NameOf(C.GoTo), () => Give(C.GoTo, [at]));
        menu.Page(C.NameOf(C.Gather), () =>
        {
            menu.Item("Calm", () => Give(C.Gather, [at]));
            menu.Item("Restless", () => Give(C.Gather, [at], act: RigMotion.Guard));
            menu.Item("Angry", () => Give(C.Gather, [at], act: RigMotion.Cheer));
        });
        menu.Page(C.NameOf(C.Form), () =>
        {
            foreach (var (shape, name) in new[] { (CrowdShapes.Line, "Line"), (CrowdShapes.Column, "Column"), (CrowdShapes.Ring, "Ring"), (CrowdShapes.Square, "Square"), (CrowdShapes.Wedge, "Wedge") })
                menu.Item(name, () => Give(C.Form, [at], shape: shape, facing: Facing(at)));
        });
        menu.Item(C.NameOf(C.Halt), () => Give(C.Halt, []));
        menu.Item(C.NameOf(C.Flee), () => Give(C.Flee, [at], pace: CrowdPaces.Run));
        menu.Item(C.NameOf(C.Yield), () => Give(C.Yield, []));
        menu.Separator();
        menu.Item(C.NameOf(C.Home), () => Give(C.Home, []));
        menu.Item(C.NameOf(C.Clear), () => Give(C.Clear, []));
    }

    void TokenRows(MenuCard menu, PieceInstance piece)
    {
        foreach (var command in new[] { C.Follow, C.GoTo, C.Surround, C.Attack, C.FireAt, C.ThrowAt })
            menu.Item(C.NameOf(command), () => Give(command, [piece.Pos], piece: piece.Id, shape: command == C.Surround ? CrowdShapes.Ring : CrowdShapes.Loose));
    }

    void GroupRows(MenuCard menu, (int Town, int Person) them)
    {
        var view = c.S.View;
        string? against = layer.OrderOf(them.Town, them.Person);
        var at = layer.Where(view, them.Town, them.Person, view.Clock.Seconds, (float)(view.Calibration.UnitsPerMeter ?? 1)).At;
        foreach (var command in new[] { C.Attack, C.FireAt, C.ThrowAt, C.Surround, C.Follow })
            menu.Item(C.NameOf(command), () => Give(command, [at], against: against, shape: command == C.Surround ? CrowdShapes.Ring : CrowdShapes.Loose));
        if (against != null) menu.Item(C.NameOf(C.Drive), () => Drive(at, against));
    }

    void FrontRows(MenuCard menu, Vec2 from, Vec2 to)
    {
        var d = to - from;
        double facing = Math.Atan2(-d.Z, d.X), width = d.Length / (c.S.View.Calibration.UnitsPerMeter ?? 1);
        var middle = new Vec2((from.X + to.X) / 2, (from.Z + to.Z) / 2);
        menu.Item("Form a line", () => Give(C.Form, [middle], shape: CrowdShapes.Line, facing: facing, width: width));
        menu.Item("Across the street", () => Give(C.Form, [middle], shape: CrowdShapes.Across, facing: facing, width: width));
        menu.Item(C.NameOf(C.LineTheWay), () => Give(C.LineTheWay, [from, to], shape: CrowdShapes.Sides));
        menu.Item("Form a column", () => Give(C.Form, [middle], shape: CrowdShapes.Column, facing: Math.Atan2(d.X, d.Z)));
    }

    double Facing(Vec2 at)
    {
        var view = c.S.View;
        float upm = (float)(view.Calibration.UnitsPerMeter ?? 1);
        var picked = Chosen();
        if (picked.Count == 0) return 0;
        var spots = picked.Select(p => layer.Where(view, p.Town, p.Person, view.Clock.Seconds, upm).At).ToList();
        var mid = new Vec2(spots.Average(p => p.X), spots.Average(p => p.Z));
        return Math.Atan2(at.X - mid.X, at.Z - mid.Z);
    }

    void Drive(Vec2 at, string against)
    {
        var line = Order(C.Drive, [at], CrowdShapes.Line, Facing(at), 0, null, null, null, CrowdPaces.March);
        if (line == null || c.S.View.Crowds?.Orders?.FirstOrDefault(o => o.Id == against) is not { } them) return;
        var flee = them.Clone();
        (flee.Id, flee.Do, flee.To, flee.Shape, flee.Pace) = ("", C.Flee, [at], CrowdShapes.Loose, CrowdPaces.Run);
        (flee.Start, flee.Until, flee.Act, flee.Name) = (line.Start, null, null, them.Name + " · " + C.NameOf(C.Flee));
        c.S.Do(new Batch { Items = [new SetCrowdOrder { Order = line }, new SetCrowdOrder { Order = flee }] });
    }

    void Give(string command, List<Vec2> to, string? act = null, string? piece = null, string? against = null, string shape = CrowdShapes.Loose, double facing = 0,
        double width = 0, string? pace = null)
    {
        if (Order(command, to, shape, facing, width, act, piece, against, pace) is { } order) c.S.Do(new SetCrowdOrder { Order = order });
    }

    CrowdOrder? Order(string command, List<Vec2> to, string shape, double facing, double width, string? act, string? piece, string? against, string? pace)
    {
        var chosen = Chosen();
        if (chosen.Count == 0) return null;
        var members = chosen.GroupBy(p => p.Town).ToDictionary(g => layer.Roster(g.Key).Thing, g => g.Select(p => p.Person).Order().ToList());
        string who = Calling is { } calling ? CrowdDress.CallingName(layer.Roster(chosen[0].Town).People, calling) : "People";
        return new CrowdOrder
        {
            Name = $"{who} · {C.NameOf(command)}", Members = members, Said = $"{chosen.Count}", Do = command, To = to, Piece = piece, Against = against, Shape = shape,
            Facing = facing, WidthM = width, Pace = pace ?? Pace, Act = act, Start = c.S.View.Clock.Seconds,
        };
    }
}
