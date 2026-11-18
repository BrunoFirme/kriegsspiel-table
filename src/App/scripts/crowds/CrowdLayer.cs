using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed class CrowdLayer
{
    const float SquareM = 1.4f, LiftM = 0.25f, SquarePx = 3, ChildSquare = 0.6f, PointPx = 12, StrideHeights = 1.3f;
    const double LookEvery = 0.5;

    sealed class Town
    {
        public required CrowdTown Roster;
        public required CrowdRoutes Routes;
        public required bool[] Shown;
        public required List<CrowdLeg>?[] Plans;
        public required (CrowdBatch Body, CrowdBatch Head, float[] BodyColours, float[] HeadColours, float Coat)?[] Drawn;
        public DayKind Kind;
    }

    readonly TableContext _c;
    readonly ShaderMaterial _figureMaterial = new() { Shader = GD.Load<Shader>("res://shaders/crowd_figure.gdshader") };
    readonly ShaderMaterial _squareMaterial = new() { Shader = GD.Load<Shader>("res://shaders/crowd_square.gdshader") };
    readonly Dictionary<string, CrowdBatch> _batches = new();
    readonly Dictionary<string, CrowdShape> _shapes = new();
    readonly CrowdBatch _squares;
    readonly CrowdNear _near;
    readonly List<(float Distance, int Town, int Person, Vector3 At, float Yaw, bool Moving, float Stride)> _close = new();
    readonly float[] _coat = new float[CrowdShape.MostColours];
    readonly HashSet<string> _asked = new();
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

    public CrowdLayer(TableContext c, SheetLayer sheets)
    {
        _c = c;
        _near = new CrowdNear(Root);
        Root.AddChild((_squares = new CrowdBatch(CrowdMeshes.Square, _squareMaterial, "Squares")).Node);
        sheets.Built += Due;
    }

    public void Due() => _due = true;

    public void Tick(double delta)
    {
        var view = _c.S.View;
        float upm = (float)(view.Calibration.UnitsPerMeter ?? 0);
        bool live = view.Lighting.Crowds && On && upm > 0;
        Root.Visible = live;
        if (!live)
        {
            if (_towns.Count > 0) Forget();
            return;
        }
        if ((_lookIn -= delta) <= 0)
        {
            _lookIn = LookEvery;
            long signed = Signature(view);
            if (signed != _signed) (_signed, _due) = (signed, true);
        }
        if (_due) Lay(view);
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
        return h.ToHashCode();
    }

    void Lay(GameState view)
    {
        _due = false;
        var own = Nationalities.Of(view.Packs.Select(p => _c.S.Store.Pack(p)).OfType<PackManifest>());
        var missing = new Dictionary<string, Nationality?>();
        Nationality? People(string id)
        {
            if (view.Crowds?.Peoples.TryGetValue(id, out var kept) == true) return kept;
            if (!_c.S.IsHost || CrowdDress.Find(id, own) is not { } n) return null;
            var flat = CrowdDress.Flat(n, own);
            if (_asked.Add(id)) missing[id] = flat;
            return flat;
        }
        var rosters = CrowdRoster.Towns(view, People, view.Clock.Seconds);
        if (missing.Count > 0) _c.S.Do(new SetPeoples { Peoples = missing });
        var clear = view.Crowds?.Clear?.Select(s => s.Clone()).ToList() ?? new();
        var foot = Travellers.Default(Manners.Age(view.Lighting.MapStyle))[Travellers.Foot];
        float Ground(Vec2 p) => _c.SheetsTop + (float)_c.S.Ground.DrawnAt(p);
        _near.Clear();
        _towns = rosters.Select(t =>
        {
            var thing = view.Built.First(b => b.Id == t.Thing);
            var sheet = view.Sheets.First(s => s.Id == thing.Sheet);
            var map = TravelMap.For(view, sheet.Id, sheet.Relief is { Length: > 0 } sha ? _c.S.Relief.Loaded(sha) ?? _c.S.Relief.Under(sheet) : null);
            var routes = new CrowdRoutes(t, map, foot, clear, Ground);
            var shown = t.Residents.Select(r => r.Rank < Population.Share(Population.Of(thing.Parts[r.Part])) && !routes.Clears(t.Places[r.Home].At)).ToArray();
            return new Town
            {
                Roster = t, Routes = routes, Shown = shown, Plans = new List<CrowdLeg>?[t.Residents.Count],
                Drawn = new (CrowdBatch, CrowdBatch, float[], float[], float)?[t.Residents.Count],
            };
        }).ToList();
        _day = long.MinValue;
    }

    void Draw(GameState view, float upm, float delta)
    {
        long seconds = view.Clock.Seconds;
        long day = (long)Math.Floor(seconds / (double)CrowdDay.Day);
        int second = (int)(seconds - day * CrowdDay.Day);
        if (day != _day)
        {
            _day = day;
            string? weekday = view.Clock.Cal.WeekDay(seconds);
            foreach (var t in _towns)
            {
                Array.Clear(t.Plans);
                t.Kind = CrowdDay.KindOf(t.Roster.People, weekday);
            }
        }
        var cam = _c.Rig.Cam;
        var eye = cam.GlobalPosition;
        bool flat = cam.Projection == Camera3D.ProjectionType.Orthogonal;
        float viewH = Math.Max(1, cam.GetViewport().GetVisibleRect().Size.Y);
        _squareMaterial.SetShaderParameter("pixel_size", (flat ? 2 / viewH : 2 * Mathf.Tan(Mathf.DegToRad(cam.Fov) / 2) / viewH) * SquarePx);
        float figures = (float)view.Lighting.FiguresM * upm, near = (float)view.Lighting.NearM * upm;
        _squares.Begin();
        foreach (var b in _batches.Values) b.Begin();
        _close.Clear();
        int outside = 0;
        for (int ti = 0; ti < _towns.Count; ti++)
        {
            var t = _towns[ti];
            var roster = t.Roster;
            for (int i = 0; i < roster.Residents.Count; i++)
            {
                if (!t.Shown[i]) continue;
                var r = roster.Residents[i];
                var plan = t.Plans[i] ??= CrowdDay.Plan(roster, r, t.Kind, day);
                if (!CrowdDay.Where(roster, r, plan, t.Routes.Path, second, upm, out var w)) continue;
                outside++;
                var at = Coords.ToWorld(w.At, HeightOf(t, r, w));
                float yaw = (float)(Math.PI - w.Heading), apart = at.DistanceTo(eye);
                var drawn = t.Drawn[i] ??= Dressed(roster, r);
                if (flat || apart > figures)
                {
                    _coat[0] = drawn.Coat;
                    _squares.Add(at + Vector3.Up * LiftM * upm, 0, SquareM * upm * (r.Entry.Band == CrowdEntry.Child ? ChildSquare : 1), _coat, 0);
                }
                else if (apart <= near) _close.Add((apart, ti, i, at, yaw, w.Moving, (float)w.Stride));
                else Figure(drawn, r, at, yaw, w.Moving, (float)w.Stride, upm);
            }
        }
        Close(upm, delta);
        Out = outside;
        _squares.Push();
        foreach (var b in _batches.Values) b.Push();
        Point(view, upm, second);
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
            if (k < CrowdNear.Most) _near.Show((ti, i), r.Entry.Look!, at, yaw, upm, moving, stride, delta);
            else Figure(t.Drawn[i]!.Value, r, at, yaw, moving, stride, upm);
        }
        _near.End();
    }

    static void Figure((CrowdBatch Body, CrowdBatch Head, float[] BodyColours, float[] HeadColours, float Coat) drawn, CrowdResident r, Vector3 at, float yaw, bool moving, float stride, float upm)
    {
        float tall = (float)r.Entry.Look!.HeightM * upm, phase = Mathf.PosMod(stride / Mathf.Max(tall * StrideHeights, 1e-6f), 1) + (moving ? 16 : 0);
        drawn.Body.Add(at, yaw, tall, drawn.BodyColours, phase);
        drawn.Head.Add(at, yaw, tall, drawn.HeadColours, phase);
    }

    (CrowdBatch, CrowdBatch, float[], float[], float) Dressed(CrowdTown town, CrowdResident r)
    {
        var look = CrowdDress.Look(r.Entry, town.People, town.Coat);
        var body = Batch("b:" + CrowdFigure.BodyKey(look), () => CrowdFigure.Body(look), out var bodyShape);
        var head = Batch("h:" + CrowdFigure.HeadKey(look), () => CrowdFigure.Headwear(look), out var headShape);
        return (body, head, CrowdMeshes.Palette(CrowdFigure.Colours(bodyShape, look)), CrowdMeshes.Palette(CrowdFigure.Colours(headShape, look)),
            CrowdMeshes.Packed(look.Colours.GetValueOrDefault(CrowdSlots.Outer) ?? "#808080"));
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

    void Point(GameState view, float upm, int second)
    {
        if (!_c.S.IsUmpire || _c.Scene.GetViewport() is not { } port || Input.IsAnythingPressed()) return;
        var mouse = port.GetMousePosition();
        var cam = _c.Rig.Cam;
        (int Town, int Person, float Px, CrowdWhere W)? best = null;
        for (int ti = 0; ti < _towns.Count; ti++)
        {
            var t = _towns[ti];
            for (int i = 0; i < t.Roster.Residents.Count; i++)
            {
                var r = t.Roster.Residents[i];
                if (!t.Shown[i] || t.Plans[i] is not { } plan || !CrowdDay.Where(t.Roster, r, plan, t.Routes.Path, second, upm, out var w)) continue;
                var at = Coords.ToWorld(w.At, HeightOf(t, r, w));
                if (cam.IsPositionBehind(at)) continue;
                float px = cam.UnprojectPosition(at).DistanceTo(mouse);
                if (px <= PointPx && (best == null || px < best.Value.Px)) best = (ti, i, px, w);
            }
        }
        if (best is not { } b)
        {
            _pointed = null;
            return;
        }
        if (_pointed == (b.Town, b.Person)) return;
        _pointed = (b.Town, b.Person);
        var town = _towns[b.Town].Roster;
        var resident = town.Residents[b.Person];
        var e = resident.Entry;
        int age = (int)((view.Clock.Seconds - e.Born) / (365.25 * CrowdDay.Day));
        string doing = b.W.Place < 0 ? "on the way to " + Named(town, b.W.To, resident.Home) : "at the " + Named(town, b.W.Place, -1);
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
