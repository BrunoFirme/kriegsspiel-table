using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed record Drawn(CrowdBatch Body, CrowdBatch Head, float[] BodyColours, float[] HeadColours, float Coat, int Gait);

sealed partial class CrowdLayer
{
    const float SquareM = 1.4f, LiftM = 0.25f, SquarePx = 3, ChildSquare = 0.6f, PointPx = 12, StrideHeights = 1.3f;
    const int PhaseSteps = 64, Gaits = 32, ArmActs = 16;
    const float LitAt = 0.5f, GlowM = 7, GlowReach = 3;
    readonly ShaderMaterial _glowMaterial = new() { Shader = GD.Load<Shader>("res://shaders/crowd_glow.gdshader") };
    readonly CrowdBatch _glows;
    float _dark;
    const double LookEvery = 0.5;

    sealed class Town
    {
        public required CrowdTown Roster;
        public required CrowdRoutes Routes;
        public required bool[] Shown;
        public required List<CrowdLeg>?[] Plans;
        public required List<CrowdSpan>?[] Spans;
        public required int[] Cursor;
        public required Func<int, int, CrowdPath?> PathOf;
        public bool Waiting;
        public required Drawn?[] Drawn;
        public required Drawn?[] LitDrawn;
        public required CrowdLook?[] LitLooks;
        public DayKind Kind;
        public CrowdWhere[]? Stands;
        public string? Idle;
        public List<TrafficTrip>? Trips;
        public OnWay?[]? OnWays;
    }

    readonly TableContext _c;
    readonly ShaderMaterial _figureMaterial = new() { Shader = GD.Load<Shader>("res://shaders/crowd_figure.gdshader") };
    readonly ShaderMaterial _squareMaterial = new() { Shader = GD.Load<Shader>("res://shaders/crowd_square.gdshader") };
    readonly Dictionary<string, CrowdBatch> _batches = new();
    readonly Dictionary<string, CrowdShape> _shapes = new();
    readonly CrowdBatch _squares;
    readonly CrowdNear _near;
    readonly CrowdKinds _kinds;
    readonly List<(float Distance, int Town, int Person, Vector3 At, float Yaw, bool Moving, float Stride)> _close = new();
    readonly float[] _coat = new float[CrowdShape.MostColours];
    readonly HashSet<string> _asked = new();
    readonly Dictionary<string, Nationality> _flats = new();
    readonly List<(int Town, int Person, Vector3 At, CrowdWhere W)> _seen = new();
    List<Town> _towns = new();
    long _day = long.MinValue, _signed;
    double _lookIn;
    bool _due = true;
    (int Town, int Person)? _pointed;

    public readonly Node3D Root = new() { Name = "Crowds" };

    public int People => _towns.Sum(t => t.Shown.Count(s => s));
    public int Out { get; private set; }
    public int Near => _near.Count;
    public bool On { get; set; } = true;

    public CrowdLayer(TableContext c, SheetLayer sheets, PieceLayer pieces)
    {
        _c = c;
        _pieces = pieces;
        Tool = new CrowdTool(this, c);
        c.Crowd = Tool;
        _near = new CrowdNear(Root);
        _kinds = new CrowdKinds(c);
        Root.AddChild((_squares = new CrowdBatch(CrowdMeshes.Square, _squareMaterial, "Squares")).Node);
        Root.AddChild((_marks = new CrowdBatch(CrowdMeshes.Square, _markMaterial, "Picked")).Node);
        Root.AddChild((_glows = new CrowdBatch(CrowdMeshes.Square, _glowMaterial, "Glows")).Node);
        sheets.Built += Due;
    }

    public void Due() => _due = true;

    static bool Changes(Command c) => c switch
    {
        Batch b => b.Items.Any(Changes),
        SetPopulation or SetKeepClear or SetPeoples or SetMadeCrowd or SetCalibration or AddPack or Undo or RestoreTurn or LoadSave => true,
        _ => false,
    };

    object? _hooked;

    static bool Dresses(Command c) => c switch
    {
        Batch b => b.Items.Any(Dresses),
        SetLook { Person: not null } or Undo or RestoreTurn or LoadSave => true,
        _ => false,
    };

    public void Tick(double delta)
    {
        if (!ReferenceEquals(_hooked, _c.S))
        {
            _hooked = _c.S;
            _c.S.Applied += c =>
            {
                _fatesDue = true;
                _dressDue |= Dresses(c);
                if (Changes(c)) Due();
            };
        }
        var view = _c.S.View;
        float upm = (float)(view.Calibration.UnitsPerMeter ?? 0);
        bool live = view.Lighting.Crowds && upm > 0;
        Root.Visible = live && On;
        if (!live && _towns.Count > 0) Forget();
        if (!live || !On) return;
        if ((_lookIn -= delta) <= 0)
        {
            _lookIn = LookEvery;
            long signed = Signature(view);
            if (signed != _signed) (_signed, _due) = (signed, true);
            if (_c.S.IsHost)
            {
                Restamp(view, upm);
                Departed(view);
                Unsmoked(view);
            }
            Pieced(view, LookEvery);
        }
        if (_due) Lay(view);
        Tool.Settle(view);
        Draw(view, upm, (float)delta);
    }

    void Forget()
    {
        _towns = new();
        _near.Clear();
        _signed = 0;
        _due = true;
    }

    static long Signature(GameState v)
    {
        var h = new HashCode();
        h.Add(v.Built.Count);
        h.Add(v.Lighting.MapStyle);
        h.Add(v.Calibration.UnitsPerMeter);
        h.Add(v.Packs.Count);
        foreach (var t in v.Built.Where(b => b.Kind == BuiltKinds.Settlement))
        {
            h.Add(t.Id);
            h.Add(t.Parts.Count);
            foreach (var p in t.Parts) h.Add(p.Population ?? Population.Usual);
        }
        foreach (var s in v.Crowds?.Clear ?? []) h.Add(s.Points.Count + s.Radius);
        h.Add(v.Crowds?.Peoples.Count ?? 0);
        foreach (var m in v.Crowds?.Made ?? []) h.Add(HashCode.Combine(m.Id, m.Count, m.At, m.Seed, m.Mix, m.Nationality, m.Wealth, m.Name) + (m.Settled?.GetHashCode() ?? 0)
            + HashCode.Combine(m.Men, m.Children, m.Elders, m.Callings?.Count ?? 0));
        return h.ToHashCode();
    }

    void Lay(GameState view)
    {
        _due = false;
        _signed = Signature(view);
        var own = Nationalities.Of(view.Packs.Select(p => _c.S.Store.Pack(p)).OfType<PackManifest>());
        var missing = new Dictionary<string, Nationality?>();
        Nationality? People(string id)
        {
            var flat = Resolved(id);
            if (flat != null) _flats[id] = flat;
            return flat;
        }
        Nationality? Resolved(string id)
        {
            Nationality? kept = null;
            view.Crowds?.Peoples.TryGetValue(id, out kept);
            if (!_c.S.IsHost || CrowdDress.Find(id, own) is not { } n) return kept;
            var flat = CrowdDress.Flat(n, own);
            string now = KTable.Core.Json.Serialize(flat);
            if (kept != null && KTable.Core.Json.Serialize(kept) == now) return kept;
            if (_asked.Add(id + now.GetHashCode())) missing[id] = flat;
            return flat;
        }
        var rosters = CrowdRoster.Towns(view, People, view.Clock.Seconds);
        var clear = view.Crowds?.Clear?.Select(s => s.Clone()).ToList() ?? new();
        var ways = Travellers.Default(Manners.Age(view.Lighting.MapStyle));
        float Ground(Vec2 p) => _c.SheetsTop + (float)_c.S.Ground.DrawnAt(p);
        _near.Clear();
        _tracks.Clear();
        _ordersSigned = 0;
        _leaving.Clear();
        _fatesDue = true;
        var kept = Picked.Where(p => p.Town < _towns.Count).Select(p => (_towns[p.Town].Roster.Thing, p.Person)).ToList();
        ClearPicks();
        float upm = (float)(view.Calibration.UnitsPerMeter ?? 1);
        CrowdRoutes Routes(CrowdTown t, SheetDef sheet) =>
            new(t, TravelMap.For(view, sheet.Id, sheet.Relief is { Length: > 0 } sha ? _c.S.Relief.Loaded(sha) ?? _c.S.Relief.Under(sheet) : null), ways, clear, Ground);
        _towns = rosters.Select(t =>
        {
            var thing = view.Built.First(b => b.Id == t.Thing);
            var routes = Routes(t, view.Sheets.First(s => s.Id == thing.Sheet));
            return Laid(t, routes, t.Residents.Select(r => r.Rank < Population.Share(Population.Of(thing.Parts[r.Part])) && !routes.Clears(t.Places[r.Home].At)).ToArray());
        }).ToList();
        foreach (var made in view.Crowds?.Made ?? [])
        {
            if (view.Sheets.FirstOrDefault(s => s.Id == PlaceTree.SheetUnder(view, made.At)) is not { } sheet || People(made.Nationality) is not { } flat) continue;
            if (Settled(view, made, flat, upm) is { } settled)
            {
                _towns.Add(settled);
                continue;
            }
            var t = CrowdMade.Town(made, flat);
            var routes = Routes(t, sheet);
            var town = Laid(t, routes, Enumerable.Repeat(true, t.Residents.Count).ToArray());
            town.Idle = MadeMixes.Of(made.Mix).Act;
            town.Stands = t.Residents.Select((r, i) => CrowdMade.Stand(t, i, r.Entry.Seed, upm) is var w ? w with { Height = routes.Height(w.At) } : default).ToArray();
            _towns.Add(town);
        }
        if (missing.Count > 0) _c.S.Do(new SetPeoples { Peoples = missing });
        foreach (var (thing, person) in kept)
            if (TownOf(thing) is >= 0 and var ti && person < _towns[ti].Shown.Length && _towns[ti].Shown[person]) Picked.Add((ti, person));
        PeopleAsPieces(view);
        _day = long.MinValue;
    }

    Town? Settled(GameState view, MadeCrowd made, Nationality flat, float upm)
    {
        if (made.Settled is not { } id || _towns.FirstOrDefault(t => t.Roster.Thing == id) is not { } home || view.Built.FirstOrDefault(b => b.Id == id) is not { } thing) return null;
        var t = CrowdMade.Settle(made, flat, home.Roster, PlaceZones.FarmingOf(thing, upm));
        var routes = new CrowdRoutes(t, home.Routes.Map, home.Routes.Ways, home.Routes.Clear, home.Routes.Height);
        return Laid(t, routes, Enumerable.Repeat(true, t.Residents.Count).ToArray());
    }

    static Town Laid(CrowdTown t, CrowdRoutes routes, bool[] shown) => new()
    {
        Roster = t, Routes = routes, Shown = shown, Plans = new List<CrowdLeg>?[t.Residents.Count], Spans = new List<CrowdSpan>?[t.Residents.Count],
        Cursor = new int[t.Residents.Count], PathOf = routes.Path, Drawn = new Drawn?[t.Residents.Count],
        LitDrawn = new Drawn?[t.Residents.Count], LitLooks = new CrowdLook?[t.Residents.Count],
    };

    void Draw(GameState view, float upm, float delta)
    {
        long seconds = view.Clock.Seconds;
        long day = (long)Math.Floor(seconds / (double)CrowdDay.Day);
        int second = (int)(seconds - day * CrowdDay.Day);
        if (day != _day) NewDay(view, seconds, day);
        var cam = _c.Rig.Cam;
        var eye = WorldShift.ToTable(cam.GlobalPosition);
        bool flat = cam.Projection == Camera3D.ProjectionType.Orthogonal;
        Shade(view, cam, flat, upm);
        float figures = (float)view.Lighting.FiguresM * upm, near = (float)view.Lighting.NearM * upm;
        Orders(view, upm);
        if (_fatesDue) Fated(view);
        if (_dressDue) Redress(view);
        _squares.Begin();
        _glows.Begin();
        _marks.Begin();
        foreach (var b in _batches.Values) b.Begin();
        _close.Clear();
        _seen.Clear();
        _rects?.Begin();
        _rigsSeen.Clear();
        Rigs = 0;
        for (int ti = 0; ti < _towns.Count; ti++)
        {
            var t = _towns[ti];
            var roster = t.Roster;
            if (t.Waiting && t.Routes.Pending == 0)
            {
                t.Waiting = false;
                Array.Clear(t.Spans);
            }
            for (int i = 0; i < roster.Residents.Count; i++)
            {
                if (!t.Shown[i]) continue;
                var r = roster.Residents[i];
                string? act = null;
                string? fate = _fates.Count > 0 ? FateAt(ti, i, seconds) : null;
                if (fate is CrowdFates.Gone or CrowdFates.Kept || _asPieces.Count > 0 && _asPieces.Contains((ti, i))) continue;
                if (_tracks.Count > 0 && Ordered(ti, i, seconds, upm, out var w, out act, out bool seen))
                {
                    if (!seen) continue;
                    if (float.IsNaN(w.Height)) w = w with { Height = t.Routes.Height(w.At) };
                }
                else if (t.Stands is { } stands) (w, act) = (stands[i], t.Idle);
                else
                {
                    var spans = t.Spans[i] ??= CrowdDay.Spans(roster, r, t.Plans[i] ??= CrowdDay.Plan(roster, r, t.Kind, day), t.PathOf, upm);
                    if (spans.Count == 0) continue;
                    int c = t.Cursor[i];
                    if (c > spans.Count || c > 0 && second < spans[c - 1].End) c = 0;
                    while (c < spans.Count && spans[c].End <= second) c++;
                    t.Cursor[i] = c;
                    if (c >= spans.Count || spans[c].Start > second || !CrowdDay.At(roster, r, spans[c], second, upm, out w)) continue;
                }
                if (fate is CrowdFates.Down or CrowdFates.Dead) (w, act) = (Fallen(view, ti, i, upm), RigMotion.Lie);
                else if (fate == CrowdFates.Held)
                {
                    if (!Dragged(ti, i)) w = Fallen(view, ti, i, upm);
                    if (!w.Moving) act ??= RigMotion.Yield;
                }
                Acting(ti, i, act, seconds, r.Entry.Seed, upm, ref w);
                var at = Coords.ToWorld(w.At, HeightOf(t, r, w));
                _seen.Add((ti, i, at, w));
                if (Picked.Contains((ti, i))) _marks.Add(at + Vector3.Up * LiftM * 2 * upm, 0, MarkM * upm, _brass, 0);
                float yaw = (float)(Math.PI - w.Heading), apart = at.DistanceTo(eye);
                bool lit = Carrying(ti, i);
                var drawn = lit ? t.LitDrawn[i] ??= Dressed(Look(ti, i)) : t.Drawn[i] ??= Dressed(LookOf(ti, i));
                if (lit && apart <= figures * GlowReach) _glows.Add(at + Vector3.Up * 0.05f * upm, 0, GlowM * upm, _coat, 0);
                if (flat || apart > figures)
                {
                    _coat[0] = fate is CrowdFates.Down or CrowdFates.Dead ? Dimmed(drawn.Coat) : drawn.Coat;
                    _squares.Add(at + Vector3.Up * LiftM * upm, 0, SquareM * upm * (r.Entry.Band == CrowdEntry.Child ? ChildSquare : 1), _coat, 0);
                }
                else if (apart <= near) _close.Add((apart, ti, i, at, yaw, w.Moving, (float)w.Stride));
                else if (KindOf(r) is { } kind) Frozen(kind, r, at, yaw, w.Moving, (float)w.Stride, upm, act);
                else Figure(drawn, r, at, yaw, w.Moving, (float)w.Stride, upm, act);
            }
            if (t.Routes.Pending > 0) t.Waiting = true;
            Traffic(ti, second, upm, eye, flat, figures, seconds);
        }
        Close(upm, delta);
        foreach (var id in Tool.Pieces)
            if (PieceNode(id) is { } node) _marks.Add(node.TablePosition() + Vector3.Up * LiftM * 2 * upm, 0, MarkM * upm, _brass, 0);
        Out = _seen.Count;
        _squares.Push();
        _rects?.Push();
        _glows.Push();
        _marks.Push();
        foreach (var b in _batches.Values) b.Push();
        Point(view);
        Follow();
    }

    void NewDay(GameState view, long seconds, long day)
    {
        _day = day;
        PickedRigs.Clear();
        _ordersSigned = 0;
        string? weekday = view.Clock.Cal.WeekDay(seconds);
        foreach (var t in _towns)
        {
            Array.Clear(t.Plans);
            Array.Clear(t.Spans);
            (t.Trips, t.OnWays) = (null, null);
            t.Kind = CrowdDay.KindOf(t.Roster.People, weekday);
        }
    }

    void Shade(GameState view, Camera3D cam, bool flat, float upm)
    {
        float viewH = Math.Max(1, cam.GetViewport().GetVisibleRect().Size.Y);
        _squareMaterial.SetShaderParameter("pixel_size", (flat ? 2 / viewH : 2 * Mathf.Tan(Mathf.DegToRad(cam.Fov) / 2) / viewH) * SquarePx);
        _squareMaterial.SetShaderParameter("adult_size", SquareM * upm);
        _dark = (float)Math.Clamp((Lamps.DuskAt - KTable.Client.GroundLight.OnFlat(view.Lighting)) / (Lamps.DuskAt - Lamps.DarkAt), 0, 1);
        _glowMaterial.SetShaderParameter("glow", _dark);
        _rigMaterial.SetShaderParameter("lit", _dark);
        _rectMaterial.SetShaderParameter("pixel_size", (flat ? 2 / viewH : 2 * Mathf.Tan(Mathf.DegToRad(cam.Fov) / 2) / viewH) * RectPx);
    }

    static float HeightOf(Town t, CrowdResident r, CrowdWhere w) => float.IsNaN(w.Height) ? t.Routes.PlaceHeights[w.Place >= 0 ? w.Place : r.Home] : w.Height;

    void Close(float upm, float delta)
    {
        _near.Begin();
        _close.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        for (int k = 0; k < _close.Count; k++)
        {
            var (_, ti, i, at, yaw, moving, stride) = _close[k];
            var t = _towns[ti];
            var r = t.Roster.Residents[i];
            var kind = KindOf(r);
            if (k < CrowdNear.Most) _near.Show((ti, i), k, Look(ti, i), at, yaw, upm, moving, stride, delta, _acts.GetValueOrDefault((ti, i)), kind);
            else if (kind != null) Frozen(kind, r, at, yaw, moving, stride, upm, _acts.GetValueOrDefault((ti, i)));
            else Figure(Carrying(ti, i) ? t.LitDrawn[i]! : t.Drawn[i]!, r, at, yaw, moving, stride, upm, _acts.GetValueOrDefault((ti, i)));
        }
        _near.End();
    }

    static void Figure(Drawn drawn, CrowdResident r, Vector3 at, float yaw, bool moving, float stride, float upm, string? act = null) =>
        Coded(drawn, r, at, yaw, moving, stride, upm, ActCode(act));

    static void Coded(Drawn drawn, CrowdResident r, Vector3 at, float yaw, bool moving, float stride, float upm, int code)
    {
        float tall = (float)r.Entry.Look!.HeightM * upm, phase = Mathf.PosMod(stride / Mathf.Max(tall * StrideHeights, 1e-6f), 1);
        int steps = Mathf.Clamp((int)(phase * PhaseSteps), 0, PhaseSteps - 1);
        float packed = ((code * Gaits + drawn.Gait) * 2 + (moving ? 1 : 0)) * PhaseSteps + steps;
        drawn.Body.Add(at, yaw, tall, drawn.BodyColours, packed);
        drawn.Head.Add(at, yaw, tall, drawn.HeadColours, packed);
    }

    bool Carrying(int ti, int i) => _dark >= LitAt && Look(ti, i) is var lit && !ReferenceEquals(lit, LookOf(ti, i));

    CrowdLook Look(int ti, int i)
    {
        var look = LookOf(ti, i);
        if (_dark < LitAt) return look;
        var t = _towns[ti];
        return t.LitLooks[i] ??= CrowdNight.Carrying(look, t.Roster.People, t.Roster.Residents[i].Entry.Seed);
    }

    public CrowdLook LookOf(int ti, int i)
    {
        var town = _towns[ti].Roster;
        var entry = town.Residents[i].Entry;
        return _dressed.GetValueOrDefault(CrowdPiece.Key(town.Thing, i)) ?? CrowdDress.Look(entry, PeopleOf(town, entry), town.Coat);
    }

    Nationality PeopleOf(CrowdTown town, CrowdEntry entry) =>
        entry.Nationality != town.People.Id && _flats.TryGetValue(entry.Nationality, out var own) ? own : town.People;

    CrowdKinds.Kind? KindOf(CrowdResident r) => r.Entry.Kind is { } kind ? _kinds.Of(kind) : null;

    void Frozen(CrowdKinds.Kind kind, CrowdResident r, Vector3 at, float yaw, bool moving, float stride, float upm, string? act)
    {
        float tall = (float)r.Entry.Look!.HeightM * upm, phase = Mathf.PosMod(stride / Mathf.Max(tall * StrideHeights, 1e-6f), 1);
        int pose = CrowdKinds.Pose(moving, phase, act);
        string key = $"k:{r.Entry.Kind}:{pose}";
        if (!_batches.TryGetValue(key, out var batch))
        {
            _batches[key] = batch = new CrowdBatch(kind.Frozen[pose], kind.Material, "Kinds");
            Root.AddChild(batch.Node);
        }
        batch.Add(at, yaw, tall, _coat, 0);
    }

    Drawn Dressed(CrowdLook look)
    {
        var body = Batch("b:" + CrowdFigure.BodyKey(look), () => CrowdFigure.Body(look), out var bodyShape);
        var head = Batch("h:" + CrowdFigure.HeadKey(look), () => CrowdFigure.Headwear(look), out var headShape);
        return new Drawn(body, head, CrowdMeshes.Palette(CrowdFigure.Colours(bodyShape, look)), CrowdMeshes.Palette(CrowdFigure.Colours(headShape, look)),
            CrowdMeshes.Packed(look.Colours.GetValueOrDefault(CrowdSlots.Outer) ?? "#808080"), CrowdFigure.Gait(look));
    }

    CrowdBatch Batch(string key, Func<CrowdShape> make, out CrowdShape shape)
    {
        if (!_shapes.TryGetValue(key, out var made)) _shapes[key] = made = make();
        shape = made;
        if (_batches.TryGetValue(key, out var batch)) return batch;
        _batches[key] = batch = new CrowdBatch(CrowdMeshes.Of(made), _figureMaterial, "Figures");
        Root.AddChild(batch.Node);
        return batch;
    }

    void Point(GameState view)
    {
        if (!_c.S.IsUmpire || _c.Scene.GetViewport() is not { } port || Input.IsAnythingPressed()) return;
        var mouse = port.GetMousePosition();
        var cam = _c.Rig.Cam;
        (int Town, int Person, float Px, CrowdWhere W)? best = null;
        foreach (var (ti, i, at, w) in _seen)
        {
            if (_c.Rig.Behind(at)) continue;
            float px = _c.Rig.Screen(at).DistanceTo(mouse);
            if (px <= PointPx && (best == null || px < best.Value.Px)) best = (ti, i, px, w);
        }
        if (best is not { } b)
        {
            _pointed = null;
            PointRig(mouse);
            return;
        }
        if (_pointed == (b.Town, b.Person)) return;
        _pointed = (b.Town, b.Person);
        var town = _towns[b.Town].Roster;
        var resident = town.Residents[b.Person];
        var e = resident.Entry;
        int age = (int)((view.Clock.Seconds - e.Born) / (365.25 * CrowdDay.Day));
        string doing = _towns[b.Town].Stands != null ? town.Places[0].Class : b.W.Place < 0 ? "on the way to " + Named(town, b.W.To, resident.Home) : "at the " + Named(town, b.W.Place, -1);
        _c.Say($"{e.Name} · {CrowdDress.CallingName(town.People, e.Calling)}, {age} · {doing}");
    }

    static string Named(CrowdTown town, int place, int home) =>
        place < 0 ? "street" : place == home ? "home" : town.Places[place].Use switch
        {
            CrowdUse.Market => "market",
            CrowdUse.Well => "well",
            CrowdUse.Fields => "fields",
            _ => town.Places[place].Class,
        };
}
