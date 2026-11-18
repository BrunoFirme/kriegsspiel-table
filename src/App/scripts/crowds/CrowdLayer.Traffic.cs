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
    const double ParkReach = 0.8;
    const float SitDrop = 0.12f, RideHip = 0.55f, AnimalWideM = 0.6f, AnimalLongM = 2;
    static readonly string[] Paints = ["#4a3a2c", "#2f3b4a", "#5a2a24", "#2f4030", "#3a3a3a"];
    const string Canvas = "#d9cfb6", WoodMark = "#6a4a30";
    static readonly int SitCode = ActCode(RigMotion.Sit), CarryCode = ActCode(RigMotion.Carry);
    readonly ShaderMaterial _rigMaterial = new() { Shader = GD.Load<Shader>("res://shaders/crowd_rig.gdshader") };
    readonly ShaderMaterial _rectMaterial = new() { Shader = GD.Load<Shader>("res://shaders/crowd_rect.gdshader") };
    CrowdBatch? _rects;

    public int Rigs { get; private set; }

    readonly List<(Vector3 At, TrafficRig Rig, CrowdTown Town, int To, bool Parked)> _rigsSeen = new();
    TrafficRig? _rigPointed;

    void PointRig(Vector2 mouse)
    {
        (float Px, TrafficRig Rig, CrowdTown Town, int To, bool Parked)? best = null;
        foreach (var (at, rig, town, to, parked) in _rigsSeen)
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
        public required float[][] Marks;
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

    void Traffic(Town t, int second, float upm, Vector3 eye, bool flat, float figures, long now)
    {
        if (t.Roster.Made || t.Stands != null || t.Roster.People.Traffic is not { Count: > 0 } rigs) return;
        t.Trips ??= CrowdTraffic.Plan(t.Roster, t.Kind, _day, t.Shown.Count(s => s));
        t.OnWays ??= new OnWay?[t.Trips.Count];
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
            if (made.Path == null || second >= made.End) continue;
            Rigs++;
            Place(t, trip, rig, made, second, upm, eye, flat, figures);
        }
    }

    OnWay Rig(Town t, TrafficTrip trip, TrafficRig rig, CrowdPath? path, float upm, int k, long now)
    {
        int n = rig.Parts.Count;
        var made = new OnWay { Path = path, Colours = new float[n][], Marks = new float[n][], Batches = new CrowdBatch?[n], Who = new CrowdResident?[n], Dressed = new Drawn?[n] };
        if (path == null) return made;
        made.Path = path = Parked(t, trip, path, upm);
        double front = PersonHalfM, back = PersonHalfM;
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
            string mark = p.Is == RigParts.Animal ? coat : p.Livery != null || p.What is "coach" or "chair" or "car" or "lorry" or "tram" ? livery : p.Style == "tilt" ? Canvas : WoodMark;
            made.Marks[j] = CrowdMeshes.Palette([mark]);
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
        made.End = trip.Until > 0 ? trip.Until : trip.Depart + (int)Math.Ceiling((made.LengthM + front + back) / Math.Max(rig.SpeedM, 0.1));
        return made;
    }

    static CrowdPath Parked(Town t, TrafficTrip trip, CrowdPath path, float upm)
    {
        var places = t.Roster.Places;
        Vec2? Spot(int place)
        {
            var p = places[place];
            if (!p.Outdoor || p.ReachM <= 0) return null;
            double turn = (uint)trip.Seed % 3600 / 3600.0 * Math.Tau, reach = p.ReachM * ParkReach * Math.Sqrt((uint)trip.Seed % 997 / 997.0) * upm;
            return p.At + new Vec2(Math.Sin(turn) * reach, Math.Cos(turn) * reach);
        }
        var points = path.Points.ToList();
        if (Spot(trip.From) is { } start) points.Insert(0, start);
        if (Spot(trip.To) is { } end) points.Add(end);
        return points.Count == path.Points.Count ? path : new CrowdPath(points, points.Select(t.Routes.Height).ToArray());
    }

    void Place(Town t, TrafficTrip trip, TrafficRig rig, OnWay made, int second, float upm, Vector3 eye, bool flat, float figures)
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
            float yaw = (float)(Math.PI - heading);
            var at = Coords.ToWorld(point, path.HeightAt(sp * upm)) + new Vector3(Mathf.Cos(yaw), 0, -Mathf.Sin(yaw)) * (float)(p.X * upm);
            bool far = flat || at.DistanceTo(eye) > figures;
            var colours = made.Colours[j];
            if (j == 0) _rigsSeen.Add((at, rig, t.Roster, trip.To, parked));
            if (p.Is == RigParts.Vehicle)
            {
                if (far) Rects().Add(at + Vector3.Up * LiftM * upm, yaw, (float)(p.Width * 2 * upm), (float)(p.Length * 2 * upm), made.Marks[j], 0);
                else made.Batches[j]!.Add(at + Vector3.Up * (float)(p.Y * upm), yaw, upm, colours, (int)(sp / WheelStepM) % WheelSteps);
                continue;
            }
            if (p.Is == RigParts.Animal)
            {
                if (far) Rects().Add(at + Vector3.Up * LiftM * upm, yaw, AnimalWideM * upm, AnimalLongM * upm, made.Marks[j], 0);
                else
                {
                    double cycle = sp * RigShapes.StrideOf(p.What) / Math.Tau;
                    int steps = Mathf.Clamp((int)((cycle - Math.Floor(cycle)) * PhaseSteps), 0, PhaseSteps - 1);
                    made.Batches[j]!.Add(at, yaw, upm, colours, ((p.Trot ? 2 : 0) + (parked ? 0 : 1)) * PhaseSteps + steps);
                }
                continue;
            }
            var who = made.Who[j]!;
            var drawn = made.Dressed[j] ??= Dressed(CrowdDress.Look(who.Entry, PeopleOf(t.Roster, who.Entry), t.Roster.Coat));
            float tall = (float)who.Entry.Look!.HeightM;
            if (far)
            {
                _coat[0] = drawn.Coat;
                _squares.Add(at + Vector3.Up * LiftM * upm, 0, SquareM * upm, _coat, 0);
                continue;
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
                    Coded(drawn, who, at + Vector3.Up * (float)(p.Y * upm), yaw, !parked, (float)(sp * upm), upm, p.Is == RigParts.Bear ? CarryCode : 0);
                    break;
            }
        }
    }
}
