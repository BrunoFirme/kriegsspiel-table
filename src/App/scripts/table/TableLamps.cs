using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Client;
using KTable.Core.Model;

namespace KTable.App;

sealed class TableLamps
{
    const double SettleSeconds = 0.25;
    const float Seen = 0.01f, RangeShare = 3f, Energy = 3.5f, Decay = 1.2f, FadeShare = 0.2f;
    const float NearShare = 0.4f;
    const int Seeds = 997;

    static readonly Color[] Tints = [new(1f, 0.69f, 0.3f), new(1f, 0.66f, 0.32f), new(1f, 0.55f, 0.2f), new(1f, 0.47f, 0.15f), new(1f, 0.7f, 0.4f), new(0.9f, 0.93f, 1f)];
    static readonly float[] Strengths = [0.4f, 1.25f, 1.15f, 1.35f, 0.35f, 0.55f];
    static readonly float[] Flickers = [0, 0.12f, 0.3f, 0.35f, 0.03f, 0];

    readonly record struct Flame(OmniLight3D Light, float Energy, float Flicker, float Seed, Vec2 On, float Lift);

    readonly TableContext _c;
    readonly SheetLayer _sheets;
    readonly List<Flame> _flames = new();
    bool _due = true;
    double _wait;
    float _glow, _upm, _reachShown = -1;
    int _laidOn;

    public readonly Node3D Root = new() { Name = "Lamps", Visible = false };

    public static int Laid { get; private set; }

    public TableLamps(TableContext c, SheetLayer sheets) => (_c, _sheets) = (c, sheets);

    public void Due() => _due = true;

    public void Reground(Rect2? where)
    {
        if (where == null && Layout() != _laidOn) { Due(); return; }
        var ground = _c.S.Ground;
        foreach (var f in _flames)
        {
            var at = f.Light.Position;
            if (where is not { } box || box.HasPoint(new Vector2(at.X, at.Z))) f.Light.Position = at with { Y = _c.SheetsTop + (float)ground.DrawnAt(f.On) + f.Lift };
        }
    }

    int Layout()
    {
        var hash = new System.HashCode();
        var hidden = _sheets.Hidden;
        foreach (var sheet in _c.S.View.Sheets)
        {
            hash.Add(sheet.Id);
            hash.Add(hidden.Contains(sheet.Id));
            hash.Add(BuiltGeometry.ToWorld(sheet, new SheetPoint(0, 0)));
            hash.Add(BuiltGeometry.ToWorld(sheet, new SheetPoint(BuiltGeometry.PerUnit, BuiltGeometry.PerUnit)));
        }
        return hash.ToHashCode();
    }

    public float Glow => _glow;

    public IEnumerable<OmniLight3D> Lights => _flames.Select(f => f.Light);

    public void Light(Lighting l)
    {
        _glow = (float)Lamps.Glow(l, GroundLight.OnFlat(l));
        Root.Visible = _glow > Seen;
        foreach (var f in _flames) f.Light.LightEnergy = f.Energy * _glow;
    }

    public void Tick(double delta)
    {
        if (_glow <= Seen) return;
        _wait -= delta;
        if (_due && _wait <= 0)
        {
            _due = false;
            _wait = SettleSeconds;
            Lay();
        }
        if (_c.Settings.LampReachM != _reachShown) Reach();
        Flicker();
    }

    void Reach()
    {
        _reachShown = _c.Settings.LampReachM;
        bool all = _reachShown >= Settings.LampReachOff;
        float begin = _reachShown * _upm;
        foreach (var f in _flames)
        {
            f.Light.DistanceFadeEnabled = !all;
            f.Light.DistanceFadeBegin = begin;
            f.Light.DistanceFadeLength = begin * FadeShare;
        }
    }

    void Flicker()
    {
        float time = Time.GetTicksMsec() / 1000f;
        foreach (var f in _flames)
            if (f.Flicker > 0)
                f.Light.LightEnergy = f.Energy * _glow * (1 + f.Flicker * (0.6f * Mathf.Sin(time * 9 + f.Seed) + 0.4f * Mathf.Sin(time * 23.7f + f.Seed * 3.1f)));
    }

    void Lay()
    {
        foreach (var f in _flames) f.Light.QueueFree();
        _flames.Clear();
        var s = _c.S;
        var view = s.View;
        if (view.Calibration.UnitsPerMeter is { } upm && upm > 0)
        {
            _upm = (float)upm;
            int epoch = Manners.Age(view.Lighting.MapStyle);
            var sheets = view.Sheets.ToDictionary(sh => sh.Id);
            var hidden = _sheets.Hidden;
            foreach (var thing in view.Built)
            {
                if (!sheets.TryGetValue(thing.Sheet, out var sheet) || hidden.Contains(sheet.Id)) continue;
                foreach (var lamp in Lamps.Of(thing, epoch, upm))
                {
                    if (lamp.Dark) continue;
                    var on = BuiltGeometry.ToWorld(sheet, lamp.At.Stored);
                    float lift = (float)(lamp.HighM * upm);
                    var at = Coords.ToWorld(on, _c.SheetsTop + (float)s.Ground.DrawnAt(on) + lift);
                    float near = (float)(System.Math.Max(lamp.HighM, lamp.ReachM * NearShare) * upm);
                    float energy = Energy * Strengths[lamp.Kind] * (float)lamp.Strength * Mathf.Pow(near, Decay);
                    var light = new OmniLight3D
                    {
                        Position = at, OmniRange = (float)(lamp.ReachM * upm) * RangeShare, OmniAttenuation = Decay, LightColor = Tints[lamp.Kind],
                        LightEnergy = energy * _glow, LightSpecular = 0, ShadowEnabled = false,
                    };
                    Root.AddChild(light);
                    _flames.Add(new Flame(light, energy, Flickers[lamp.Kind], (float)(System.Math.Abs(at.X * 131 + at.Z * 71) % Seeds), on, lift));
                }
            }
        }
        Laid = _flames.Count;
        _laidOn = Layout();
        _reachShown = -1;
    }
}
