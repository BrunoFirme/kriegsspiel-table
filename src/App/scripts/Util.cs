using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

public static class Problems
{
    public static event Action<string>? Told
    {
        add => KTable.Client.Problems.Told += value;
        remove => KTable.Client.Problems.Told -= value;
    }

    public static void Tell(string text) => KTable.Client.Problems.Tell(text);

    public static void TellOnce(string key, string text) => KTable.Client.Problems.TellOnce(key, text);

    public static List<string> Take() => KTable.Client.Problems.Take();
}

public static class Coords
{
    public static Vector3 ToWorld(Vec2 p, float y = 0) => new((float)p.X, y, (float)-p.Z);
    public static Vec2 FromWorld(Vector3 v) => new(v.X, -v.Z);
    public static float Yaw(double rotDeg) => Mathf.DegToRad((float)(180 - rotDeg));

    public static Vector2 ToLocal(Vec2 point, Vec2 origin, double rotDeg)
    {
        var w = ToWorld(point) - ToWorld(origin);
        var a = -Yaw(rotDeg);
        var c = Mathf.Cos(a);
        var s = Mathf.Sin(a);
        return new Vector2(w.X * c + w.Z * s, -w.X * s + w.Z * c);
    }

    public static Vec2 FromLocal(Vector2 local, Vec2 origin, double rotDeg)
    {
        var a = -Yaw(rotDeg);
        var c = Mathf.Cos(a);
        var s = Mathf.Sin(a);
        var w = new Vector3(local.X * c - local.Y * s, 0, local.X * s + local.Y * c);
        return FromWorld(ToWorld(origin) + w);
    }

    public static Color ParseColor(string? hex, Color fallback)
    {
        if (string.IsNullOrEmpty(hex)) return fallback;
        return Color.HtmlIsValid(hex) ? Color.FromHtml(hex) : fallback;
    }
}

public static class Fmt
{
    public static string Bb(string? s) => (s ?? "").Replace("[", "[lb]");

    static readonly char[] Breaks = { '\n', '\r' };
    const string LineJoin = "  ·  ";

    public static string OneLine(string? s)
    {
        if (s == null) return "";
        if (s.IndexOfAny(Breaks) < 0) return s;
        var parts = s.Split(Breaks, StringSplitOptions.RemoveEmptyEntries);
        var one = new System.Text.StringBuilder();
        foreach (var part in parts)
        {
            var t = part.Trim();
            if (t.Length == 0) continue;
            if (one.Length > 0) one.Append(LineJoin);
            one.Append(t);
        }
        return one.ToString();
    }

    public static string BbColor(string? c, string fallback) =>
        c != null && System.Text.RegularExpressions.Regex.IsMatch(c, "^(#[0-9a-fA-F]{3,8}|[a-zA-Z]{1,20})$") ? c : fallback;

    public static string Role(KTable.Core.Model.Role r) => r switch
    {
        KTable.Core.Model.Role.HostUmpire => "the host umpire",
        KTable.Core.Model.Role.LeadUmpire => "a lead umpire",
        KTable.Core.Model.Role.Umpire => "an umpire",
        _ => "an observer",
    };
    public static string RoleCaps(KTable.Core.Model.Role r) => r switch
    {
        KTable.Core.Model.Role.HostUmpire => "HOST",
        KTable.Core.Model.Role.LeadUmpire => "LEAD UMPIRE",
        KTable.Core.Model.Role.Umpire => "UMPIRE",
        _ => "OBSERVER",
    };

    public static string Distance(double units, Calibration cal) => cal.Describe(units);

    public static string Distance(double units, GameState view) => view.Calibration.Describe(units, view.Grid);

    public static string Footprint(double across, double deep, GameState view)
    {
        const string TableUnits = " u";
        var units = string.Format(CultureInfo.InvariantCulture, "{0:0.##} × {1:0.##} u", across, deep);
        var measured = view.Calibration.DescribeSize(across, deep, view.Grid);
        return measured.EndsWith(TableUnits, StringComparison.Ordinal) ? units : units + " · " + measured;
    }

    public static string Clock(GameClock c)
    {
        if (DateTime.TryParse(c.GameTime, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
            return $"Turn {c.Turn}  ·  {t:ddd d MMM yyyy  HH:mm}  (+{GameClock.StepText(c.IncrementMinutes)})";
        return $"Turn {c.Turn}  ·  {c.GameTime}";
    }
}

public static class KeyEvents
{
    public static string Describe(InputEventKey k)
    {
        var code = k.Keycode;
        if (code is Key.Ctrl or Key.Shift or Key.Alt or Key.Meta or Key.None) return "";
        var name = OS.GetKeycodeString(code);
        return (k.CtrlPressed ? "Ctrl+" : "") + (k.AltPressed ? "Alt+" : "") + (k.ShiftPressed && !ShiftIsPart(action: null, code) ? "Shift+" : "") + name;
    }

    static bool ShiftIsPart(string? action, Key code) => false;

    public static bool Is(this Keymap keys, InputEventKey k, string action) => keys.Matches(Describe(k), action);
}

public static class ObjLoader
{
    public static ArrayMesh? Load(string path)
    {
        if (!System.IO.File.Exists(path)) return null;
        if (!KTable.Core.ContentLimits.ModelAllowed(path)) { GD.PrintErr($"[models] not read, larger than a model file may be: {path}"); return null; }
        try { return Read(path); }
        catch (Exception e) when (e is FormatException or IndexOutOfRangeException or OverflowException or System.IO.IOException or UnauthorizedAccessException)
        {
            GD.PrintErr($"[models] could not read {path}: {e.Message}");
            return null;
        }
    }

    const string TintedSurface = "tinted", OwnSurface = "own";

    public static (bool Tinted, SurfaceDraw Draw) Surface(ArrayMesh mesh, int surface)
    {
        var name = mesh.SurfaceGetName(surface);
        int gap = name.IndexOf(' ');
        return gap < 0 ? (surface == 0, SurfaceDraw.Plain) : (name[..gap] == TintedSurface, SurfaceDraw.Named(name[(gap + 1)..]));
    }

    sealed record Part(bool Tinted, SurfaceDraw Draw, SurfaceTool Tool);

    static ArrayMesh Read(string path)
    {
        var v = new List<Vector3>();
        var vt = new List<Vector2>();
        var vn = new List<Vector3>();
        var vc = new List<Color>();
        var parts = new List<Part>();
        var draw = SurfaceDraw.Plain;
        SurfaceTool Tool(bool tinted)
        {
            if (parts.Find(x => x.Tinted == tinted && x.Draw == draw) is { } had) return had.Tool;
            var made = new SurfaceTool();
            made.Begin(Mesh.PrimitiveType.Triangles);
            parts.Add(new Part(tinted, draw, made));
            return made;
        }
        var lit = new HashSet<SurfaceTool>();
        bool firstGroup = true, faced = false;
        var inv = CultureInfo.InvariantCulture;
        foreach (var raw in System.IO.File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length < 2 || line[0] == '#') continue;
            var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            switch (p[0])
            {
                case "v":
                    v.Add(new Vector3(float.Parse(p[1], inv), float.Parse(p[2], inv), float.Parse(p[3], inv)));
                    if (p.Length >= 7) vc.Add(new Color(float.Parse(p[4], inv), float.Parse(p[5], inv), float.Parse(p[6], inv)));
                    break;
                case "vt":
                    vt.Add(new Vector2(float.Parse(p[1], inv), p.Length > 2 ? float.Parse(p[2], inv) : 0));
                    break;
                case "vn":
                    vn.Add(new Vector3(float.Parse(p[1], inv), float.Parse(p[2], inv), float.Parse(p[3], inv)));
                    break;
                case "usemtl" or "o" or "g":
                    if (faced) firstGroup = false;
                    break;
                case "f":
                    faced = true;
                    var st = Tool(firstGroup);
                    var idx = new List<(int v, int t, int n)>();
                    for (int i = 1; i < p.Length; i++)
                    {
                        var s = p[i].Split('/');
                        int Fix(string x, int count) => string.IsNullOrEmpty(x) ? -1 : (int.Parse(x) is var k && k < 0 ? count + k : k - 1);
                        idx.Add((Fix(s[0], v.Count), s.Length > 1 ? Fix(s[1], vt.Count) : -1, s.Length > 2 ? Fix(s[2], vn.Count) : -1));
                    }
                    for (int i = 1; i + 1 < idx.Count; i++)
                        foreach (var q in new[] { idx[0], idx[i + 1], idx[i] })
                        {
                            if (q.t >= 0 && q.t < vt.Count) st.SetUV(new Vector2(vt[q.t].X, 1 - vt[q.t].Y));
                            if (q.n >= 0 && q.n < vn.Count) { st.SetNormal(vn[q.n]); lit.Add(st); }
                            if (vc.Count == v.Count && q.v >= 0 && q.v < vc.Count) st.SetColor(vc[q.v]);
                            if (q.v >= 0 && q.v < v.Count) st.AddVertex(v[q.v]);
                        }
                    break;
                default:
                    if (SurfaceDraw.Says(p)) draw = draw.After(p);
                    break;
            }
        }
        if (parts.Count == 0) Tool(true);
        ArrayMesh? mesh = null;
        foreach (var part in parts.OrderBy(x => !x.Tinted))
        {
            if (!lit.Contains(part.Tool)) part.Tool.GenerateNormals();
            mesh = mesh == null ? part.Tool.Commit() : part.Tool.Commit(mesh);
            mesh.SurfaceSetName(mesh.GetSurfaceCount() - 1, $"{(part.Tinted ? TintedSurface : OwnSurface)} {part.Draw.Name}");
        }
        return mesh!;
    }
}
