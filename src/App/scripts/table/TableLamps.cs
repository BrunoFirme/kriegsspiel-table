using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Client;
using KTable.Core.Model;

namespace KTable.App;

sealed class TableLamps
{
    const double SettleSeconds = 0.25;
    const float Seen = 0.01f, RangeShare = 1.6f, Energy = 1.25f, Attenuation = 1.4f, FadeShare = 0.2f;
    const int Seeds = 997;

    static readonly Color[] Tints = [new(1f, 0.69f, 0.3f), new(1f, 0.66f, 0.32f), new(1f, 0.55f, 0.2f), new(1f, 0.47f, 0.15f), new(1f, 0.7f, 0.4f), new(0.9f, 0.93f, 1f)];
    static readonly float[] Strengths = [1f, 0.8f, 0.85f, 1.1f, 0.45f, 1.3f];
    static readonly float[] Flickers = [0, 0.12f, 0.3f, 0.35f, 0.03f, 0];

    readonly record struct Flame(OmniLight3D Light, float Energy, float Flicker, float Seed);

    readonly TableContext _c;
    readonly SheetLayer _sheets;
    readonly List<Flame> _flames = new();
    bool _due = true;
    double _wait;
    float _glow, _upm, _reachShown = -1;

    public readonly Node3D Root = new() { Name = "Lamps", Visible = false };

    public static int Laid { get; private set; }

    public TableLamps(TableContext c, SheetLayer sheets) => (_c, _sheets) = (c, sheets);

    public void Due() => _due = true;

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
                    var on = BuiltGeometry.ToWorld(sheet, lamp.At.Stored);
                    var at = Coords.ToWorld(on, _c.SheetsTop + (float)(s.Ground.DrawnAt(on) + lamp.HighM * upm));
                    float energy = Energy * Strengths[lamp.Kind] * (float)lamp.Strength;
                    var light = new OmniLight3D
                    {
                        Position = at, OmniRange = (float)(lamp.ReachM * upm) * RangeShare, OmniAttenuation = Attenuation, LightColor = Tints[lamp.Kind],
                        LightEnergy = energy * _glow, LightSpecular = 0, ShadowEnabled = false,
                    };
                    Root.AddChild(light);
                    _flames.Add(new Flame(light, energy, Flickers[lamp.Kind], (float)(System.Math.Abs(at.X * 131 + at.Z * 71) % Seeds)));
                }
            }
        }
        Laid = _flames.Count;
        _reachShown = -1;
    }
}
