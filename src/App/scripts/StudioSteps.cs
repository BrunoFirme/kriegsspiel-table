using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed class StudioSteps
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    readonly PieceStudio _s;

    public StudioSteps(PieceStudio studio) { _s = studio; }

    public static readonly string[] Verbs =
    {
        "view", "new", "effect", "piece", "face", "save",
        "tool", "kind", "fillmode", "ink", "ink2", "size", "sym", "radial", "grid", "snap",
        "stroke", "drag", "click", "rclick", "dclick", "key", "type", "outline", "zoom", "fit",
        "mtool", "mpart", "mfile", "mturn", "mbake", "mbuild", "mface", "mremove", "blockshape",
        "tab", "addstate", "turnpiece",
    };

    public static (string verb, string arg) Split(string raw) => raw.IndexOf(':') is var i and >= 0 ? (raw[..i].Trim(), raw[(i + 1)..].Trim()) : (raw.Trim(), "");

    public async Task<bool> Run(string verb, string arg) =>
        await StudioStep(verb, arg) || await PaintStep(verb, arg) || await ModelStep(verb, arg) || await PieceStep(verb, arg);

    Task Frames(int n) => _s.Frames(n);
    async Task Wait(double s) => await _s.ToSignal(_s.GetTree().CreateTimer(s), Timer.SignalName.Timeout);

    static float[] Nums(string a) => a.Split(',').Select(x => float.Parse(x, Inv)).ToArray();

    Vector2 Screen(Vector2 doc)
    {
        var local = _s.Canvas.ToScreen(doc);
        var canvasPt = _s.Canvas.GetGlobalTransformWithCanvas() * local;
        var k = (Vector2)_s.GetWindow().Size / _s.GetViewport().GetVisibleRect().Size;
        return canvasPt * k;
    }

    static void Mouse(Vector2 at, MouseButton b, bool down, bool shift = false, bool dbl = false) =>
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = b, Pressed = down, Position = at, GlobalPosition = at, ShiftPressed = shift, DoubleClick = dbl, ButtonMask = down ? (MouseButtonMask)(1 << ((int)b - 1)) : 0 });

    static void Motion(Vector2 from, Vector2 at, bool held) =>
        Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at, Relative = at - from, ButtonMask = held ? MouseButtonMask.Left : 0 });

    async Task DragDoc(Vector2[] pts, MouseButton b = MouseButton.Left)
    {
        var s = pts.Select(Screen).ToArray();
        Motion(s[0], s[0], false); await Frames(1);
        Mouse(s[0], b, true); await Frames(1);
        for (int i = 1; i < s.Length; i++)
        {
            for (int k = 1; k <= 4; k++) { var p = s[i - 1].Lerp(s[i], k / 4f); Motion(s[i - 1].Lerp(s[i], (k - 1) / 4f), p, true); await Frames(1); }
        }
        Mouse(s[^1], b, false); await Frames(2);
    }

    async Task<bool> StudioStep(string verb, string arg)
    {
        switch (verb)
        {
            case "view": _s.SetView(arg); await Frames(3); break;
            case "new":
                _s.NewPieces.NewPiece(arg switch { "block" => 1, "token" => 2, "square" => 3, "round" => 4, "hex" => 5, "shapes" => 9, _ => 1 });
                if (arg == "shapes") await Wait(2.5);
                await Wait(1.2);
                break;
            case "effect":
            {
                var fp = FirmeEffects.Presets.FirstOrDefault(p => p.Name.Equals(arg, StringComparison.OrdinalIgnoreCase));
                if (fp.Fx != null) _s.NewPieces.NewEffect(fp.Name, fp.W, fp.H, fp.Fx);
                await Wait(1.2);
                break;
            }
            case "piece":
            {
                var d = int.TryParse(arg, NumberStyles.Integer, Inv, out var n)
                    ? _s.Pack.Pieces.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ElementAtOrDefault(n)
                    : _s.Pack.Pieces.FirstOrDefault(x => x.Id == arg) ?? _s.Pack.Pieces.FirstOrDefault(x => x.Name.Equals(arg, StringComparison.OrdinalIgnoreCase)) ?? _s.Pack.Pieces.FirstOrDefault(x => x.Name.Contains(arg, StringComparison.OrdinalIgnoreCase));
                if (d != null) _s.Select(d, frame: true);
                await Wait(0.6);
                break;
            }
            case "face": await _s.SwitchFace(arg); await Frames(3); break;
            case "save": await _s.Faces.Save(); await Frames(2); break;
            default: return false;
        }
        return true;
    }

    async Task<bool> PaintStep(string verb, string arg)
    {
        switch (verb)
        {
            case "tool": _s.Canvas.SetTool(arg); break;
            case "kind": _s.Prefs.ShapeKind = arg; _s.RebuildToolPanel(); break;
            case "fillmode": _s.Prefs.ShapeFill = arg; _s.RebuildToolPanel(); break;
            case "ink": _s.Canvas.Ink = Hex.To(arg, Colors.Black); _s.ToolPanel.Colours.SyncInk(); break;
            case "ink2": _s.Canvas.Ink2 = Hex.To(arg, Colors.White); _s.ToolPanel.Colours.SyncInk(); break;
            case "size": { var v = float.Parse(arg, Inv); _s.Prefs.Brush = _s.Prefs.Eraser = _s.Prefs.Line = v; _s.RebuildToolPanel(); break; }
            case "sym": _s.Prefs.Symmetry = arg; _s.Strip.SyncToggles(); break;
            case "radial": _s.Prefs.Radial = int.Parse(arg, Inv); _s.Strip.SyncToggles(); break;
            case "grid": _s.Prefs.Grid = arg != "off"; _s.Strip.SyncToggles(); break;
            case "snap": _s.Prefs.Snap = arg != "off"; _s.Strip.SyncToggles(); break;
            case "stroke":
            {
                var n = Nums(arg);
                var pts = Enumerable.Range(0, n.Length / 2).Select(k => new Vector2(n[k * 2], n[k * 2 + 1])).ToArray();
                await DragDoc(pts);
                break;
            }
            case "drag":
            {
                var n = Nums(arg);
                await DragDoc(new[] { new Vector2(n[0], n[1]), new Vector2(n[2], n[3]) });
                break;
            }
            case "click": case "rclick": case "dclick":
            {
                var n = Nums(arg);
                var s = Screen(new Vector2(n[0], n[1]));
                var b = verb == "rclick" ? MouseButton.Right : MouseButton.Left;
                Motion(s, s, false); await Frames(1);
                Mouse(s, b, true, dbl: verb == "dclick"); await Frames(1);
                Mouse(s, b, false); await Frames(3);
                break;
            }
            case "key": await PressKey(arg); break;
            case "type":
                if (_s.Canvas.Sel is { Kind: "text" } tl) { tl.Text = arg.Replace("\\n", "\n"); _s.Canvas.Refresh(tl); _s.Canvas.Commit("Text"); _s.RebuildToolPanel(); }
                break;
            case "outline":
            {
                _s.Canvas.SetTool("outline");
                await Frames(1);
                var btn = _s.ToolPanel.Body.FindChildren("*", "Button", true, false).OfType<Button>().FirstOrDefault(b => b.Text == arg);
                btn?.EmitSignal(BaseButton.SignalName.Pressed);
                await Wait(0.3);
                break;
            }
            case "zoom": _s.Canvas.ZoomTo(float.Parse(arg, Inv)); break;
            case "fit": _s.Canvas.Fit(); break;
            default: return false;
        }
        return true;
    }

    async Task PressKey(string arg)
    {
        var ev = new InputEventKey { Pressed = true };
        foreach (var part in arg.Split('+'))
            switch (part.ToLowerInvariant())
            {
                case "ctrl": ev.CtrlPressed = true; break;
                case "shift": ev.ShiftPressed = true; break;
                case "alt": ev.AltPressed = true; break;
                default: ev.Keycode = OS.FindKeycodeFromString(part); break;
            }
        Input.ParseInputEvent(ev);
        await Frames(1);
        var up = (InputEventKey)ev.Duplicate(); up.Pressed = false;
        Input.ParseInputEvent(up);
        await Frames(2);
    }

    async Task<bool> ModelStep(string verb, string arg)
    {
        switch (verb)
        {
            case "mtool": _s.Model?.SetTool(arg); await Frames(2); break;
            case "mpart": _s.Model?.AddParts(ModelImport.Primitive(arg), arg, arg); await Frames(3); break;
            case "mfile": _s.NewPieces.AddModelFiles(new[] { arg }); await Wait(1.5); break;
            case "mwidth": _s.Model?.SetWidth(double.Parse(arg, Inv)); await Frames(3); break;
            case "mturn": { var n = Nums(arg); if (_s.Model is { } m) { m.Camera.Yaw = n[0]; m.Camera.Pitch = n[1]; m.Camera.Apply(); } await Frames(2); break; }
            case "mbake": if (_s.Model != null) await _s.Model.Bake(); break;
            case "mbuild": if (_s.Model != null) { _s.Model.AddParts(ModelImport.Primitive(arg), arg, arg); await _s.Model.Bake(); await Wait(1.5); } break;
            case "mface":
            {
                var keys = _s.Model?.ShapeFaces.Keys();
                if (keys != null && int.Parse(arg, Inv) < keys.Count) { _s.PaintIn2D(keys[int.Parse(arg, Inv)].key); await Wait(1.0); }
                break;
            }
            case "mremove": if (_s.Model?.SelPart is { } gone) { _s.Model.RemovePart(gone); await _s.Model.Bake(); await Wait(1); } break;
            case "blockshape":
                if (_s.Sel != null)
                {
                    var outline = PieceFramer.Outlines.FirstOrDefault(o => o.Equals(arg, StringComparison.OrdinalIgnoreCase) || PieceFramer.ShapeOf(o) == arg) ?? arg;
                    await _s.Framer.SetOutline(_s.Sel, Math.Max(0, _s.Model?.FlatState ?? 0), outline);
                    await Wait(1);
                }
                break;
            default: return false;
        }
        return true;
    }

    async Task<bool> PieceStep(string verb, string arg)
    {
        switch (verb)
        {
            case "tab": _s.Inspector.Tabs.CurrentTab = int.TryParse(arg, NumberStyles.Integer, Inv, out var tab) ? tab : Math.Max(0, _s.Inspector.TabIndex(arg)); await Frames(3); break;
            case "addstate": _s.AddState(arg.Length > 0 ? arg : null); break;
            case "turnpiece":
                if (_s.Paper.Instance(_s.Sel) is { } tp) { _s.S.Do(new KTable.Core.Protocol.MovePiece { Layer = Layers.Umpire, Id = tp.Id, Pos = tp.Pos, RotDeg = double.Parse(arg, Inv) }); await Wait(0.5); }
                break;
            default: return false;
        }
        return true;
    }
}
