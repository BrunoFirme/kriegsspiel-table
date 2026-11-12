using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed class RulerTool
{
    readonly TableContext _c;
    readonly ToolOverlay _overlay;
    public RulerTool(TableContext c, ToolOverlay overlay) { _c = c; _overlay = overlay; }

    public Vec2? A, B;
    const double MinRise = 0.01;

    public void Press(Vec2? g) { A = g; B = g; }

    const double StraightStep = 45;

    public void Drag(Vec2 to, bool straight) => B = straight && A is { } from ? _c.S.View.Calibration.Straightened(from, to, StraightStep) : to;

    public string Hint() =>
        $"Drag to measure · Shift keeps the line straight · read in {_c.S.View.Calibration.MeasureName.ToLowerInvariant()}: Scenario properties → Map → Distances";
    public void Clear() => A = B = null;

    readonly List<Button> _pins = new();
    const float PinSize = 14, PinGap = 3, PinResting = 0.45f;
    static readonly Color LineInk = new(1f, 0.42f, 0.28f);
    const string LiveKey = "ruler", PinnedKey = "ruler-pinned-", BearingSuffix = "-bearing";

    static readonly List<double[]> NonePinned = new();

    List<double[]> Kept => _c.Settings.RulerPins.GetValueOrDefault(_c.S.View.ScenarioId) ?? NonePinned;

    public IReadOnlyList<(Vec2 A, Vec2 B)> Pinned => Kept.Select(p => (new Vec2(p[0], p[1]), new Vec2(p[2], p[3]))).ToList();

    public static bool IsLabel(string key) => key.StartsWith(LiveKey, StringComparison.Ordinal);

    Button PinButton(int i)
    {
        while (_pins.Count <= i)
        {
            var pin = new Button { Flat = true, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(PinSize, PinSize), Size = new Vector2(PinSize, PinSize) };
            foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed" }) pin.AddThemeStyleboxOverride(state, new StyleBoxEmpty());
            pin.AddChild(new ToolIcon { Kind = "pin" });
            pin.Pressed += () => PinPressed(_pins.IndexOf(pin));
            _c.Labels.AddChild(pin);
            _pins.Add(pin);
        }
        return _pins[i];
    }

    void PlacePin(int i, Rect2? label, bool pinned)
    {
        if (label is not { } at)
        {
            if (i < _pins.Count) _pins[i].Visible = false;
            return;
        }
        var pin = PinButton(i);
        pin.Visible = true;
        pin.Position = new Vector2(at.End.X + PinGap, at.Position.Y + (at.Size.Y - PinSize) / 2).Round();
        if (pin.GetChild(0) is ToolIcon icon)
        {
            var ink = pinned || pin.IsHovered() ? LineInk : new Color(LineInk, PinResting);
            if (icon.Color != ink) { icon.Color = ink; icon.QueueRedraw(); }
        }
        pin.TooltipText = pinned ? "Pinned · click to take it away" : "Pin this line to the table";
    }

    void PinPressed(int i)
    {
        if (i < 0) return;
        var scenario = _c.S.View.ScenarioId;
        var kept = Kept;
        if (i < kept.Count)
        {
            kept.RemoveAt(i);
            if (kept.Count == 0) _c.Settings.RulerPins.Remove(scenario);
        }
        else if (A is { } a && B is { } b && Measured != null)
        {
            if (kept == NonePinned) _c.Settings.RulerPins[scenario] = kept = new();
            kept.Add(new[] { a.X, a.Z, b.X, b.Z });
            Clear();
        }
        else return;
        _c.Settings.Save();
    }

    public double? Measured => A != null && B != null && (B.Value - A.Value).Length > 0.01 ? (B.Value - A.Value).Length : null;

    public static double NiceStep(double span)
    {
        if (span <= 0 || double.IsNaN(span)) return 1;
        double raw = span / 6;
        double mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        double n = raw / mag;
        return (n <= 1 ? 1 : n <= 2 ? 2 : n <= 5 ? 5 : 10) * mag;
    }

    public void Draw()
    {
        var pinned = Pinned;
        for (int i = 0; i < pinned.Count; i++) Draw(pinned[i].A, pinned[i].B, false, PinnedKey + i);
        bool cal = _c.Tool == Tool.Calibrate;
        if (A is { } ra && B is { } rb) Draw(ra, rb, cal, LiveKey);
        for (int i = 0; i < pinned.Count; i++) PlacePin(i, _c.Labels.RectOf(PinnedKey + i), true);
        PlacePin(pinned.Count, Measured != null && !cal ? _c.Labels.RectOf(LiveKey) : null, false);
        for (int i = pinned.Count + 1; i < _pins.Count; i++) _pins[i].Visible = false;
    }

    void Draw(Vec2 a, Vec2 b, bool cal, string key)
    {
        var overlay = _overlay;
        var colour = cal ? new Color(0.3f, 0.9f, 1f) : LineInk;
        var mat = new StandardMaterial3D { AlbedoColor = colour, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, NoDepthTest = true };
        double len = (b - a).Length;
        var dir = len > 1e-9 ? new Vec2((b.X - a.X) / len, (b.Z - a.Z) / len) : new Vec2(1, 0);
        var perp = new Vec2(-dir.Z, dir.X);
        const float y = 0.3f;
        void Seg(Vec2 p, Vec2 q) => overlay.SegPx(overlay.Mesh, mat, p, q, y);
        Vec2 At(Vec2 p, Vec2 along, double k) => new(p.X + along.X * k, p.Z + along.Z * k);
        Seg(a, b);
        float cap = overlay.Px(7);
        Seg(At(a, perp, cap), At(a, perp, -cap));
        Seg(At(b, perp, cap), At(b, perp, -cap));
        var c = _c.S.View.Calibration;
        double shown = cal ? len : c.FromTableUnits(len);
        double step = NiceStep(shown);
        double stepUnits = cal ? step : c.ToTableUnits(step);
        if (stepUnits > 1e-6 && len / stepUnits < 400)
            for (int i = 1; i * stepUnits < len; i++)
            {
                float t = overlay.Px(i % 5 == 0 ? 9 : 4.5f);
                var p = At(a, dir, i * stepUnits);
                Seg(At(p, perp, t), At(p, perp, -t));
            }
        var mid = new Vec2((a.X + b.X) / 2, (a.Z + b.Z) / 2);
        var ground = _c.S.Ground;
        _c.Labels.Set(key, Coords.ToWorld(mid, y + (float)ground.At(mid)), cal ? $"{len:0.00} u" : Fmt.Distance(len, _c.S.View),
            cal ? new Color(0.6f, 0.95f, 1f) : new Color(1f, 0.95f, 0.85f), 15, new Vector2(0, -18), centered: true, plate: true);
        if (!cal && len > 1e-6)
        {
            double bearing = c.CompassBearing(Mathf.RadToDeg((float)Math.Atan2(b.X - a.X, b.Z - a.Z)));
            double rise = ground.At(b) - ground.At(a);
            var heading = string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:000}°", bearing);
            if (Math.Abs(rise) >= MinRise) heading += $" · {(rise > 0 ? "up" : "down")} {Fmt.Distance(Math.Abs(rise), _c.S.View)}";
            _c.Labels.Set(key + BearingSuffix, Coords.ToWorld(b, y + (float)ground.At(b)), heading,
                new Color(1f, 0.82f, 0.6f), 13, new Vector2(14, -14), centered: false, plate: true);
        }
    }
}
