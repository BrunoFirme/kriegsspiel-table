using System;
using System.Collections.Generic;
using System.Globalization;
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
        var c = Mathf.Cos(a); var s = Mathf.Sin(a);
        return new Vector2(w.X * c + w.Z * s, -w.X * s + w.Z * c);
    }

    public static Vec2 FromLocal(Vector2 local, Vec2 origin, double rotDeg)
    {
        var a = -Yaw(rotDeg);
        var c = Mathf.Cos(a); var s = Mathf.Sin(a);
        var w = new Vector3(local.X * c - local.Y * s, 0, local.X * s + local.Y * c);
        return FromWorld(ToWorld(origin) + w);
    }

    public static Color ParseColor(string? hex, Color fallback)
    {
        if (string.IsNullOrEmpty(hex)) return fallback;
        try { return Color.FromHtml(hex); } catch { return fallback; }
    }
}

public static class Fmt
{
    public static string Bb(string? s) => (s ?? "").Replace("[", "[lb]");

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

    public static string Clock(GameClock c)
    {
        if (DateTime.TryParse(c.GameTime, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
            return $"Turn {c.Turn}  ·  {t:ddd d MMM yyyy  HH:mm}  (+{c.IncrementMinutes}′)";
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

    static ArrayMesh Read(string path)
    {
        var v = new List<Vector3>(); var vt = new List<Vector2>(); var vn = new List<Vector3>();
        var vc = new List<Color>();
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        bool hasN = false;
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
                case "vt": vt.Add(new Vector2(float.Parse(p[1], inv), p.Length > 2 ? float.Parse(p[2], inv) : 0)); break;
                case "vn": vn.Add(new Vector3(float.Parse(p[1], inv), float.Parse(p[2], inv), float.Parse(p[3], inv))); break;
                case "f":
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
                            if (q.n >= 0 && q.n < vn.Count) { st.SetNormal(vn[q.n]); hasN = true; }
                            if (vc.Count == v.Count && q.v >= 0 && q.v < vc.Count) st.SetColor(vc[q.v]);
                            if (q.v >= 0 && q.v < v.Count) st.AddVertex(v[q.v]);
                        }
                    break;
            }
        }
        if (!hasN) st.GenerateNormals();
        return st.Commit();
    }
}
