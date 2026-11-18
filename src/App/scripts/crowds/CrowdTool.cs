using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;
using C = KTable.Core.Model.CrowdCommands;

namespace KTable.App;

sealed partial class CrowdTool(CrowdLayer layer, TableContext c)
{
    const float PickPx = 12, DragPx = 5, RigPickPx = 24;
    public static readonly IReadOnlyList<(int Share, string Name)> Shares = [(1, "All"), (2, "1 in 2"), (3, "1 in 3"), (10, "1 in 10")];
    public static readonly int[] Sizes = [10, 20, 50, 100, 200, 500, 1000, 2000];

    Vector2? _down, _rightDown;
    Vec2? _rightAt;
    bool _box, _twice;

    (string Id, Action Then)? _after;
    string? _made;
    readonly HashSet<string> _pieces = new();

    public Action<string, Vector3>? Take { get; set; }
    public Action<string, Vector3, float, string>? Assign { get; set; }
    public Action<string, CrowdLook, string>? Dress { get; set; }
    public int Share { get; set; } = 1;
    public string? Calling { get; set; }
    public string Pace { get; set; } = CrowdPaces.Walk;
    public string? Adding { get; private set; }
    public int AddCount { get; private set; } = 50;
    public string? AddPeople { get; set; }
    public event Action? Changed;

    public int Count => Chosen().Count;

    public IReadOnlyCollection<string> Pieces => _pieces;

    string PiecesSaid => _pieces.Count > 0 ? $" · {_pieces.Count} pieces" : "";

    public string Hint() => Adding is { } mix ? $"Click where {AddCount} {MadeMixes.Of(mix).Name.ToLowerInvariant()} appear · = − or Ctrl+wheel size · Esc lets go"
        : layer.Picked.Count == 0
        ? "Click a person or drag a box round people · Shift adds · double-click takes their group"
        : $"{Count} people{PiecesSaid} · right-click where they go or what they face · right-drag lays a line's front · Esc lets go";

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
        if (Adding is { } mix)
        {
            c.NotifyScreenRect(null);
            if (c.Ground(screen) is { } at) Make(mix, at);
            return;
        }
        if (_box)
        {
            c.NotifyScreenRect(null);
            var box = new Rect2(from, screen - from).Abs();
            if (!shift) Unpick();
            foreach (var p in layer.Seen.Where(p => !c.Rig.Behind(p.At) && box.HasPoint(c.Rig.Screen(p.At)))) layer.Picked.Add((p.Town, p.Person));
            foreach (var r in layer.RigsSeen.Where(r => !c.Rig.Behind(r.At) && box.HasPoint(c.Rig.Screen(r.At)))) layer.PickedRigs.Add((r.Town, r.Rig));
            var view = c.S.View;
            foreach (var n in layer.PieceNodes.Where(n => layer.PieceJoins(view, n) && n.TablePosition() is var at && !c.Rig.Behind(at) && box.HasPoint(c.Rig.Screen(at))))
                _pieces.Add(n.Piece.Id);
        }
        else if (!_twice)
        {
            var hit = Nearest(screen);
            if (!shift) Unpick();
            var rig = hit == null ? RigNear(screen) : null;
            if (hit is { } who && !layer.Picked.Remove(who)) layer.Picked.Add(who);
            else if (rig is { } one && !layer.PickedRigs.Remove(one)) layer.PickedRigs.Add(one);
            else if (hit == null && rig == null && c.Ground(screen) is { } ground) Building(ground);
        }
        Told();
    }

    void Building(Vec2 at)
    {
        var view = c.S.View;
        foreach (var thing in view.Built.Where(b => b.Kind == BuiltKinds.Settlement))
        {
            int ti = layer.TownOf(thing.Id);
            if (ti < 0 || view.Sheets.FirstOrDefault(s => s.Id == thing.Sheet) is not { } sheet) continue;
            var spot = Spot.Of(BuiltGeometry.ToSheet(sheet, at));
            int part = thing.Parts.FindIndex(p => p.Kind == PartKinds.Block && BuiltThings.Reach(p, spot) <= 0);
            if (part < 0) continue;
            var roster = layer.Roster(ti);
            var places = Enumerable.Range(0, roster.Places.Count).Where(k => roster.Places[k].Part == part).ToHashSet();
            foreach (var p in layer.HereNow(ti, places)) layer.Picked.Add((ti, p));
            return;
        }
    }

    public MadeRecipe Recipe { get; private set; } = MadeMixes.Of(MadeMixes.Townsfolk).Fill();

    public void Add(string mix)
    {
        Recipe = MadeMixes.Of(mix).Fill();
        Adding = mix;
        layer.ClearPicks();
        Told();
    }

    public bool Resize(int by)
    {
        if (Adding == null) return false;
        int k = Array.IndexOf(Sizes, AddCount);
        AddCount = Sizes[Math.Clamp((k < 0 ? 2 : k) + by, 0, Sizes.Length - 1)];
        Told();
        return true;
    }

    void Make(string mix, Vec2 at)
    {
        var view = c.S.View;
        if (CrowdMade.Refusal(view, at) is { } refusal)
        {
            c.Say(char.ToUpperInvariant(refusal[0]) + refusal[1..]);
            return;
        }
        if ((view.Crowds?.Made?.Sum(m => m.Count) ?? 0) + AddCount > view.Lighting.MadeMost)
        {
            c.Say($"At most {view.Lighting.MadeMost} made people on this table");
            return;
        }
        _made = "m-" + Guid.NewGuid().ToString("N")[..10];
        var sheet = view.Sheets.FirstOrDefault(s => s.Id == PlaceTree.SheetUnder(view, at));
        var appear = Recipe.Edge && sheet != null ? CrowdMade.EdgeOf(sheet, at) : at;
        var crowd = new MadeCrowd
        {
            Id = _made, Name = MadeMixes.Of(mix).Name, Count = AddCount, Mix = mix, Nationality = AddPeople ?? CrowdMade.NationalityAt(view, at), At = appear,
            Seed = Random.Shared.Next(), Start = view.Clock.Seconds,
        };
        Recipe.Into(crowd);
        var items = new List<Command> { new SetMadeCrowd { Crowd = crowd } };
        if (appear != at) items.Add(new SetCrowdOrder { Order = CrowdMade.WalkIn(crowd, at) });
        c.S.Do(items.Count == 1 ? items[0] : new Batch { Items = items });
        Adding = null;
        Told();
    }

    public void PickWithin(Vec2 at, double reach)
    {
        layer.ClearPicks();
        foreach (var who in layer.Within(at, reach)) layer.Picked.Add(who);
        Told();
    }

    public void PickOrder(CrowdOrder order)
    {
        layer.ClearPicks();
        foreach (var m in layer.Members(order)) layer.Picked.Add(m);
        Told();
    }

    public void MenuAt(Vector2 screen, Vec2 at) => Menu(screen, at, null);

    public void LetGo()
    {
        Adding = null;
        layer.Unfollow();
        Unpick();
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
            if (c.Rig.Behind(p.At)) continue;
            float px = c.Rig.Screen(p.At).DistanceTo(screen);
            if (px > nearest) continue;
            (nearest, best) = (px, (p.Town, p.Person));
        }
        return best;
    }

    void Group(Vector2 screen)
    {
        if (Nearest(screen) is not { } who) return;
        layer.ClearPicks();
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
        if (layer.Picked.Count == 0 && layer.PickedRigs.Count == 0) return false;
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
        menu.Head($"{Count} people{PiecesSaid}", Calling == null ? "" : Calling);
        menu.Separator();
        var piece = c.S.View.Layer(Layers.Umpire).Where(p => (p.Pos - at).Length < PieceReach()).MinBy(p => (p.Pos - at).Length);
        var person = Nearest(screen) is { } other && !layer.Picked.Contains(other) ? other : ((int, int)?)null;
        if (layer.Picked.Count == 1) WearRows(menu, layer.Picked.First());
        if (end is { } to) FrontRows(menu, at, to);
        else if (piece != null) TokenRows(menu, piece);
        else if (person is { } them) GroupRows(menu, them);
        else GroundRows(menu, at);
        StateRows(menu, at);
        menu.Show(c.Scene, screen);
    }

    public void Settle(GameState view)
    {
        if (_made is { } made && layer.TownOf(made) is >= 0 and var ti)
        {
            _made = null;
            layer.ClearPicks();
            for (int i = 0; i < layer.Roster(ti).Residents.Count; i++) layer.Picked.Add((ti, i));
            Told();
        }
        if (_after is not { } after || view.Find(Layers.Umpire, after.Id) == null) return;
        _after = null;
        after.Then();
    }

    void WearRows(MenuCard menu, (int Town, int Person) who)
    {
        var roster = layer.Roster(who.Town);
        if (layer.Person(who.Town, who.Person) is not { } person) return;
        var at = Coords.ToWorld(person.At, layer.HeightAt(who.Town, person.At));
        float yaw = (float)(Math.PI - person.Heading);
        void Wear(Action<string> then)
        {
            string id = "c-" + Guid.NewGuid().ToString("N")[..10];
            c.S.Do(new SpawnPiece
            {
                Layer = Layers.Umpire, Id = id, Def = CrowdPiece.DefId, Pos = person.At, RotDeg = AvatarBody.RotOf(yaw), Y = at.Y,
                Look = person.Look.Clone(), Crowd = CrowdPiece.Key(roster.Thing, who.Person),
            });
            _after = (id, () => then(id));
        }
        menu.Item("Take control", () => Wear(id => Take?.Invoke(id, at)));
        menu.Item("Follow", () => layer.Follow(who.Town, who.Person));
        menu.Item("Dress…", () => Dress?.Invoke(CrowdPiece.Key(roster.Thing, who.Person), layer.LookOf(who.Town, who.Person), roster.Residents[who.Person].Entry.Name));
        foreach (var name in c.S.Peers.Where(p => p.Role == Role.Player).Select(p => p.Name).Order())
            menu.Item($"Assign to {name}", () => Wear(id => Assign?.Invoke(name, at, yaw, id)));
        menu.Separator();
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
        menu.Item(C.NameOf(C.Leave), () => Give(C.Leave, []));
        menu.Item("Remove", Remove);
        SettleRows(menu, at);
    }

    void Remove()
    {
        var view = c.S.View;
        var whole = new List<string>();
        var fates = new Dictionary<string, CrowdFate?>();
        foreach (var g in Chosen().GroupBy(p => p.Town))
        {
            var roster = layer.Roster(g.Key);
            if (roster.Made && g.Count() == roster.Residents.Count) whole.Add(roster.Thing);
            else foreach (var (_, p) in g) fates[CrowdPiece.Key(roster.Thing, p)] = new CrowdFate { State = CrowdFates.Gone, At = view.Clock.Seconds };
        }
        var items = CrowdLayer.Unmade(view, whole);
        if (fates.Count > 0) items.Add(new SetCrowdFates { Fates = fates.Take(SetCrowdFates.Most).ToDictionary(f => f.Key, f => f.Value) });
        if (items.Count > 0) c.S.Do(new Batch { Items = items });
        LetGo();
    }

    void TokenRows(MenuCard menu, PieceInstance piece)
    {
        foreach (var command in new[] { C.Follow, C.GoTo, C.Surround, C.Attack, C.FireAt, C.ThrowAt })
            menu.Item(C.NameOf(command), () =>
            {
                var (shape, width) = Formed(command);
                Give(command, [piece.Pos], piece: piece.Id, shape: shape, width: width);
            });
    }

    (string Shape, double Width) Formed(string command) => command == C.Surround ? (CrowdShapes.Ring, 0) : layer.Drilled(command, Chosen()) ?? (CrowdShapes.Loose, 0);

    void GroupRows(MenuCard menu, (int Town, int Person) them)
    {
        var view = c.S.View;
        string? against = layer.OrderOf(them.Town, them.Person);
        var at = layer.Where(view, them.Town, them.Person, view.Clock.Seconds, (float)(view.Calibration.UnitsPerMeter ?? 1)).At;
        foreach (var command in new[] { C.Attack, C.FireAt, C.ThrowAt, C.Surround, C.Follow })
            menu.Item(C.NameOf(command), () =>
            {
                var (shape, width) = Formed(command);
                Give(command, [at], against: against, shape: shape, width: width);
            });
        if (against != null) menu.Item(C.NameOf(C.Drive), () => Drive(at, against));
        OutcomeRows(menu, them, against);
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
        line.Pieces = Carried();
        Send([new SetCrowdOrder { Order = line }, new SetCrowdOrder { Order = flee }], line);
    }

    (int Town, int Rig)? RigNear(Vector2 screen)
    {
        (float Px, int Town, int Rig)? best = null;
        foreach (var (at, ti, k) in layer.RigsSeen)
        {
            if (c.Rig.Behind(at)) continue;
            float px = c.Rig.Screen(at).DistanceTo(screen);
            if (px <= RigPickPx && (best == null || px < best.Value.Px)) best = (px, ti, k);
        }
        return best is { } b ? (b.Town, b.Rig) : null;
    }

    void Unpick()
    {
        layer.ClearPicks();
        _pieces.Clear();
    }

    List<string>? Carried()
    {
        var view = c.S.View;
        var ids = _pieces.Where(id => layer.PieceNode(id) is { } n && layer.PieceJoins(view, n)).Order(StringComparer.Ordinal).ToList();
        return ids.Count > 0 ? ids : null;
    }

    void Send(List<Command> items, CrowdOrder moving)
    {
        items.AddRange(layer.PieceSteps(c.S.View, moving));
        if (layer.Volley(c.S.View, moving) is { } smoke) items.Add(smoke);
        c.S.Do(items.Count == 1 ? items[0] : new Batch { Items = items });
    }

    void Give(string command, List<Vec2> to, string? act = null, string? piece = null, string? against = null, string shape = CrowdShapes.Loose, double facing = 0,
        double width = 0, string? pace = null)
    {
        if (Order(command, to, shape, facing, width, act, piece, against, pace) is not { } order) return;
        order.Pieces = Carried();
        Send([new SetCrowdOrder { Order = order }], order);
    }

    CrowdOrder? Order(string command, List<Vec2> to, string shape, double facing, double width, string? act, string? piece, string? against, string? pace)
    {
        var chosen = Chosen();
        if (chosen.Count == 0 && layer.PickedRigs.Count == 0) return null;
        var members = layer.MembersOf(chosen);
        layer.AddRigs(members);
        string who = chosen.Count == 0 ? "Traffic" : Calling is { } calling ? CrowdDress.CallingName(layer.Roster(chosen[0].Town).People, calling) : "People";
        return new CrowdOrder
        {
            Name = $"{who} · {C.NameOf(command)}", Members = members, Said = $"{chosen.Count + layer.PickedRigs.Count}", Do = command, To = to, Piece = piece, Against = against, Shape = shape,
            Facing = facing, WidthM = width, Pace = pace ?? Pace, Act = act, Start = c.S.View.Clock.Seconds,
        };
    }
}
