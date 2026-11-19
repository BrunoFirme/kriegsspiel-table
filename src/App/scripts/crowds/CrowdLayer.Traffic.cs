using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed partial class CrowdLayer
{
    const double WheelStepM = 0.02, PersonHalfM = 0.3, AnimalHalfM = 1;
    const int WheelSteps = 4096, RideCode = 7 * ArmActs;
    const double BayRunM = 6;
    const float SitDrop = 0.12f, RideHip = 0.55f, RectPx = 4.5f;
    const double MarkLeastM = 1.2;
    static readonly string[] Paints = ["#4a3a2c", "#2f3b4a", "#5a2a24", "#2f4030", "#3a3a3a"];
    const string Canvas = "#d9cfb6", WoodMark = "#6a4a30";
    static readonly int SitCode = ActCode(RigMotion.Sit), CarryCode = ActCode(RigMotion.Carry);
    readonly ShaderMaterial _rigMaterial = new() { Shader = GD.Load<Shader>("res://shaders/crowd_rig.gdshader") };
    readonly ShaderMaterial _rectMaterial = new() { Shader = GD.Load<Shader>("res://shaders/crowd_rect.gdshader") };
    CrowdBatch? _rects;

    public int Rigs { get; private set; }

    readonly List<(Vector3 At, TrafficRig Rig, CrowdTown Town, int To, bool Parked, int Ti, int Member)> _rigsSeen = new();
    TrafficRig? _rigPointed;

    void PointRig(Vector2 mouse)
    {
        (float Px, TrafficRig Rig, CrowdTown Town, int To, bool Parked)? best = null;
        foreach (var (at, rig, town, to, parked, _, _) in _rigsSeen)
        {
            if (_c.Rig.Behind(at)) continue;
            float px = _c.Rig.Screen(at).DistanceTo(mouse);
            if (px <= PointPx * 2 && (best == null || px < best.Value.Px)) best = (px, rig, town, to, parked);
        }
        if (best is not { } b || ReferenceEquals(_rigPointed, b.Rig))
        {
            if (best == null) _rigPointed = null;
            return;
        }
        _rigPointed = b.Rig;
        _c.Say($"{b.Rig.Name} · {(b.Parked ? "at the " : "on the way to ")}{Named(b.Town, b.To, -1)}");
    }

    sealed class OnWay
    {
        public CrowdPath? Path;
        public double LengthM, FrontM;
        public int End;
        public required float[][] Colours;
        public float[] Mark = [];
        public double MarkWideM, MarkLongM, MarkShiftM;
        public required CrowdBatch?[] Batches;
        public required CrowdResident?[] Who;
        public required Drawn?[] Dressed;
    }

    CrowdBatch Rects()
    {
        if (_rects != null) return _rects;
        Root.AddChild((_rects = new CrowdBatch(CrowdMeshes.Square, _rectMaterial, "Rigs")).Node);
        return _rects;
    }

    CrowdBatch RigBatch(string key, Func<CrowdShape> make)
    {
        if (_batches.TryGetValue(key, out var batch)) return batch;
        _batches[key] = batch = new CrowdBatch(CrowdMeshes.Of(make()), _rigMaterial, "Rigs");
        Root.AddChild(batch.Node);
        return batch;
    }

    static Traveller TravellerOf(CrowdRoutes routes, TrafficRig rig)
    {
        string who = rig.Parts.Any(p => p.Is == RigParts.Vehicle && p.What != "chair") ? Travellers.Cart
            : rig.Parts.Any(p => p.Is is RigParts.Animal or RigParts.Ride) ? Travellers.Rider : Travellers.Foot;
        return routes.Ways.GetValueOrDefault(who) ?? routes.Foot;
    }

    void Traffic(int ti, int second, float upm, Vector3 eye, bool flat, float figures, long now)
    {
        var t = _towns[ti];
        if (t.Roster.Made || t.Stands != null || t.Roster.People.Traffic is not { Count: > 0 } rigs) return;
        t.Trips ??= CrowdTraffic.Plan(t.Roster, t.Kind, _day, t.Shown.Count(s => s));
        t.OnWays ??= new OnWay?[t.Trips.Count];
        if (_rigTracks.Count > 0) HeldFromPastDays(ti, now, upm, eye, flat, figures);
        for (int k = 0; k < t.Trips.Count && t.Trips[k].Depart <= second; k++)
        {
            var trip = t.Trips[k];
            var rig = rigs[trip.Rig];
            var made = t.OnWays[k];
            if (made == null)
            {
                if (!t.Routes.Way(trip.From, trip.To, TravellerOf(t.Routes, rig), out var path)) continue;
                made = t.OnWays[k] = Rig(t, trip, rig, path, upm, k, now);
            }
            if (made.Path == null) continue;
            int member = RigMember(_day, k);
            if (_rigTracks.TryGetValue((ti, member), out var held) && now >= held.Run.Begins)
            {
                Rigs++;
                Held(t, ti, member, trip.To, rig, made, held, now, upm, eye, flat, figures);
                continue;
            }
            if (second >= made.End) continue;
            Rigs++;
            Place(t, ti, member, trip, rig, made, second, upm, eye, flat, figures);
        }
    }

    OnWay Rig(Town t, TrafficTrip trip, TrafficRig rig, CrowdPath? path, float upm, int k, long now)
    {
        int n = rig.Parts.Count;
        var made = new OnWay { Path = path, Colours = new float[n][], Batches = new CrowdBatch?[n], Who = new CrowdResident?[n], Dressed = new Drawn?[n] };
        if (path == null) return made;
        made.Path = path = Parked(t, trip, path, upm);
        double front = PersonHalfM, back = PersonHalfM;
        string? mark = null;
        string paint = Paints[(int)((uint)trip.Seed % (uint)Paints.Length)];
        for (int j = 0; j < n; j++)
        {
            var p = rig.Parts[j];
            double half = p.Is == RigParts.Vehicle ? Math.Max(p.Length, 0.5) : p.Is == RigParts.Animal ? AnimalHalfM : PersonHalfM;
            front = Math.Max(front, p.Z - p.Lag + half);
            back = Math.Max(back, p.Lag - p.Z + half);
            string livery = p.Livery ?? rig.Parts.FirstOrDefault(q => q.Livery != null)?.Livery ?? paint;
            string coat = p.Is == RigParts.Animal ? RigShapes.CoatOf(p.What, trip.Seed + j * 7919) : "#6b3f26";
            string load = p.Load switch { "hay" => "#c4aa62", "jars" => "#b0683f", _ => "#8a7350" };
            made.Colours[j] = CrowdMeshes.Palette([livery, "#2b2622", Canvas, load, coat, "#2a2420", "#1c1d20"]);
            if (p.Is == RigParts.Vehicle) mark ??= p.Livery != null || p.What is "coach" or "chair" or "car" or "lorry" or "tram" ? livery : p.Style == "tilt" ? Canvas : WoodMark;
            else if (p.Is == RigParts.Animal && rig.Parts.All(q => q.Is != RigParts.Vehicle)) mark ??= coat;
            if (p.Is == RigParts.Vehicle)
                made.Batches[j] = RigBatch($"r:v:{p.What}:{p.Style}:{p.Load}:{p.Livery != null}", () => RigShapes.Vehicle(p.What, p.Style, p.Load, p.Livery != null));
            else if (p.Is == RigParts.Animal) made.Batches[j] = RigBatch($"r:a:{p.What}:{p.Tack}", () => RigShapes.Animal(p.What, p.Tack));
            else
            {
                var entry = CrowdDress.Person(t.Roster.People, CrowdRoster.TrafficIds + k * 16 + j, unchecked(trip.Seed * 31 + j), now, CrowdEntry.Man, CrowdEntry.Adult, p.What);
                made.Who[j] = new CrowdResident { Entry = entry, Home = trip.From, Part = 0, Rank = 0 };
            }
        }
        made.LengthM = path.Length / Math.Max(upm, 1e-6);
        made.FrontM = front;
        (made.Mark, made.MarkWideM, made.MarkLongM, made.MarkShiftM) = (CrowdMeshes.Palette([mark ?? WoodMark]), Math.Max(rig.Footprint().Width, MarkLeastM), front + back, (front - back) / 2);
        made.End = trip.Until > 0 ? trip.Until : trip.Depart + (int)Math.Ceiling((made.LengthM + front + back) / Math.Max(rig.SpeedM, 0.1));
        return made;
    }

    static CrowdPath Parked(Town t, TrafficTrip trip, CrowdPath path, float upm)
    {
        var places = t.Roster.Places;
        double run = BayRunM * upm;
        var points = path.Points.ToList();
        if (CrowdSpots.Bay(places[trip.From], trip.Seed, upm) is { } start)
            points.InsertRange(0, [start.At, new Vec2(start.At.X + start.Along.X * run, start.At.Z + start.Along.Z * run)]);
        if (CrowdSpots.Bay(places[trip.To], trip.Seed, upm) is { } end)
            points.AddRange([new Vec2(end.At.X - end.Along.X * run, end.At.Z - end.Along.Z * run), end.At]);
        return points.Count == path.Points.Count ? path : new CrowdPath(points, points.Select(t.Routes.Height).ToArray());
    }

    void Place(Town t, int ti, int member, TrafficTrip trip, TrafficRig rig, OnWay made, int second, float upm, Vector3 eye, bool flat, float figures)
    {
        var path = made.Path!;
        double gone = (second - trip.Depart) * rig.SpeedM;
        bool parked = trip.Until > 0 && gone >= made.LengthM;
        if (parked && !t.Roster.Places[trip.To].Outdoor) return;
        double s = (parked ? made.LengthM : gone) - made.FrontM;
        for (int j = 0; j < rig.Parts.Count; j++)
        {
            var p = rig.Parts[j];
            double sp = s + p.Z - p.Lag;
            if (sp < 0 || sp > made.LengthM) continue;
            var (point, heading) = path.At(sp * upm);
            Part(t, ti, member, rig, made, j, point, heading, path.HeightAt(sp * upm), sp, !parked, trip.To, upm, eye, flat, figures);
        }
    }

    void Part(Town t, int ti, int member, TrafficRig rig, OnWay made, int j, Vec2 point, double heading, float height, double sp, bool moving, int to, float upm,
        Vector3 eye, bool flat, float figures)
    {
        var p = rig.Parts[j];
        float yaw = (float)(Math.PI - heading);
        var at = Coords.ToWorld(point, height) + new Vector3(Mathf.Cos(yaw), 0, -Mathf.Sin(yaw)) * (float)(p.X * upm);
        bool far = flat || at.DistanceTo(eye) > figures;
        var colours = made.Colours[j];
        if (j == 0)
        {
            if (far) Rects().Add(at + new Vector3(Mathf.Sin(yaw), 0, Mathf.Cos(yaw)) * (float)(made.MarkShiftM * upm) + Vector3.Up * LiftM * upm, yaw,
                (float)(made.MarkWideM * upm), (float)(made.MarkLongM * upm), made.Mark, 0);
            _rigsSeen.Add((at, rig, t.Roster, to, !moving, ti, member));
            if (PickedRigs.Contains((ti, member))) _marks.Add(at + Vector3.Up * LiftM * 2 * upm, 0, MarkM * upm, _brass, 0);
        }
        if (p.Is == RigParts.Vehicle)
        {
            if (!far) made.Batches[j]!.Add(at + Vector3.Up * (float)(p.Y * upm), yaw, upm, colours, (int)(Math.Max(sp, 0) / WheelStepM) % WheelSteps);
            return;
        }
        if (p.Is == RigParts.Animal)
        {
            if (!far)
            {
                double cycle = sp * RigShapes.StrideOf(p.What) / Math.Tau;
                int steps = Mathf.Clamp((int)((cycle - Math.Floor(cycle)) * PhaseSteps), 0, PhaseSteps - 1);
                made.Batches[j]!.Add(at, yaw, upm, colours, ((p.Trot ? 2 : 0) + (moving ? 1 : 0)) * PhaseSteps + steps);
            }
            return;
        }
        var who = made.Who[j]!;
        var drawn = made.Dressed[j] ??= Dressed(CrowdDress.Look(who.Entry, PeopleOf(t.Roster, who.Entry), t.Roster.Coat));
        float tall = (float)who.Entry.Look!.HeightM;
        if (far)
        {
            _coat[0] = drawn.Coat;
            _squares.Add(at + Vector3.Up * LiftM * upm, 0, SquareM * upm, _coat, 0);
            return;
        }
        switch (p.Is)
        {
            case RigParts.Sit:
                Coded(drawn, who, at + Vector3.Up * (float)((p.Y - SitDrop * tall) * upm), yaw, false, 0, upm, SitCode);
                break;
            case RigParts.Ride:
                Coded(drawn, who, at + Vector3.Up * (float)((RigShapes.BackOf(p.Style ?? "horse") - RideHip * tall) * upm), yaw, false, 0, upm, RideCode);
                break;
            default:
                Coded(drawn, who, at + Vector3.Up * (float)(p.Y * upm), yaw, moving, (float)(sp * upm), upm, p.Is == RigParts.Bear ? CarryCode : 0);
                break;
        }
    }
}
