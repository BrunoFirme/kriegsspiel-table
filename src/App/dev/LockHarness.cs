#if KTABLE_DEV
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Godot;
using KTable.App;
using KTable.Core;

namespace KTable.Dev;

public partial class LockHarness : Node
{
    readonly Dictionary<string, string> _args = new();
    bool _on;
    string _out = "", _role = "solo", _live = "", _sync = "", _fixtures = "";
    string[] _roles = { "solo" };
    int _resume;
    readonly List<string> _wire = new();
    readonly List<string> _log = new();
    readonly object _gate = new();
    readonly Dictionary<long, long> _alias = new();
    int _taps;
    Vector2 _mouse = new(5, 5);
    MouseButtonMask _held;
    bool _moved;
    IntPtr _theirWindow;
    readonly HashSet<Key> _keysDown = new();

    public override void _EnterTree()
    {
        var a = OS.GetCmdlineUserArgs();
        for (int i = 0; i < a.Length; i++)
            if (a[i].StartsWith("--")) _args[a[i][2..]] = i + 1 < a.Length && !a[i + 1].StartsWith("--") ? a[++i] : "true";
        if (!_args.ContainsKey("lock")) { QueueFree(); return; }
        _on = true;
        AppClipboard.KeepInApp();
        Pointer.Drive(_mouse);
        ProcessMode = ProcessModeEnum.Always;
        _out = _args.GetValueOrDefault("lock-out", Path.Combine(Path.GetTempPath(), "ktable-lock"));
        _role = _args.GetValueOrDefault("lock-role", "solo");
        _roles = _args.GetValueOrDefault("lock-roles", _role).Split(',', StringSplitOptions.RemoveEmptyEntries);
        _live = _args.GetValueOrDefault("lock-live", "");
        _sync = _args.GetValueOrDefault("lock-sync", Path.Combine(_out, "_sync"));
        _fixtures = _args.GetValueOrDefault("lock-fixtures", "");
        _resume = int.TryParse(_args.GetValueOrDefault("lock-resume", "0"), out var r) ? r : 0;
        _theirWindow = long.TryParse(_args.GetValueOrDefault("lock-their-window", "0"), out var w) ? (IntPtr)w : IntPtr.Zero;
        Directory.CreateDirectory(_out);
        KTable.Core.WireTap.Listener = OnTap;
    }

    public override void _Ready()
    {
        if (!_on) return;
        GiveFocusBack();
        Engine.MaxFps = 60;
        ProjectSettings.SetSetting("gui/timers/tooltip_delay_sec", 100000.0);
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        _ = Run();
    }

    void OnTap(string what, long peer, object m)
    {
        string body;
        try
        {
            body = m switch
            {
                KTable.Core.Protocol.Message msg => KTable.Core.Json.Serialize<KTable.Core.Protocol.Message>(msg),
                KTable.Core.Protocol.Command c => KTable.Core.Json.Serialize<KTable.Core.Protocol.Command>(c),
                _ => m.GetType().Name,
            };
        }
        catch (Exception e) { body = "\"<unserializable " + m.GetType().Name + ": " + e.GetType().Name + ">\""; }
        long self = BestEffort.Read(() => GetTree()?.GetMultiplayer() is { } mp && mp.HasMultiplayerPeer() ? mp.GetUniqueId() : 0L, fallback: 0L);
        lock (_gate)
        {
            Known(peer);
            if (self != 0) Known(self);
            _wire.Add($"{{\"dir\":\"{what}\",\"peer\":{peer},\"msg\":{body}}}");
            _taps++;
        }
    }

    void Known(long id)
    {
        if (id == 0 || _alias.ContainsKey(id)) return;
        _alias[id] = id == 1 ? 1 : _alias.Count(kv => kv.Key != 1) + 2;
    }

    string Peers(string s)
    {
        foreach (var (id, al) in _alias)
            if (id != 1 && id != al) s = Regex.Replace(s, $@"(?<![\d.]){id}(?![\d.])", "peer" + al);
        return s;
    }

    sealed record Step(int Line, string Verb, List<string> Args, Dictionary<string, string> Opts, string Text);

    List<Step> Parse(string path)
    {
        var steps = new List<Step>();
        int n = 0;
        var lines = new List<string>();
        foreach (var l in File.ReadAllLines(path))
        {
            var inc = Regex.Match(l.Trim(), @"^(?:([a-z]+):\s+)?include\s+(.+)$");
            if (!inc.Success) { lines.Add(l); continue; }
            var who = inc.Groups[1].Success ? inc.Groups[1].Value + ": " : "";
            foreach (var x in File.ReadAllLines(Path.Combine(Path.GetDirectoryName(path)!, inc.Groups[2].Value.Trim())))
                lines.Add(x.Trim().Length == 0 || x.Trim().StartsWith('#') ? x : who + x);
        }
        foreach (var raw in lines)
        {
            n++;
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith('@')) continue;
            var m = Regex.Match(line, @"^([a-z]+):\s+(.*)$");
            if (m.Success && m.Groups[1].Value is var who && !Verbs.Contains(who))
            {
                if (who != _role) continue;
                line = m.Groups[2].Value;
            }
            var toks = Tokens(line);
            if (toks.Count == 0) continue;
            var opts = new Dictionary<string, string>();
            var args = new List<string>();
            foreach (var t in toks.Skip(1))
            {
                if (!t.StartsWith('"') && Regex.Match(t, @"^([a-z]+)=(.*)$") is { Success: true } o) opts[o.Groups[1].Value] = Unquote(o.Groups[2].Value);
                else args.Add(t);
            }
            steps.Add(new Step(n, toks[0], args, opts, line));
        }
        return steps;
    }

    static readonly HashSet<string> Verbs = new() { "click", "rclick", "dclick", "mclick", "hover", "drag", "rdrag", "mdrag", "wheel", "key", "keydown", "keyup",
        "type", "drop-file", "table-click", "settle", "checkpoint", "wait", "expect", "absent", "menu", "sync", "restart", "quit", "mousedown", "mouseup", "move", "close", "close-window", "note", "probe", "clear", "dismiss" };

    static List<string> Tokens(string line)
    {
        var list = new List<string>();
        var sb = new StringBuilder();
        bool q = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (q)
            {
                if (c == '\\' && i + 1 < line.Length) { sb.Append(c).Append(line[++i]); continue; }
                sb.Append(c);
                if (c == '"') q = false;
                continue;
            }
            if (c == '"')
            {
                q = true;
                sb.Append(c);
                continue;
            }
            if (char.IsWhiteSpace(c))
            {
                if (sb.Length > 0) { list.Add(sb.ToString()); sb.Clear(); } continue;
            }
            if (c == '#' && sb.Length == 0) break;
            sb.Append(c);
        }
        if (sb.Length > 0) list.Add(sb.ToString());
        return list;
    }

    static string Unquote(string s)
    {
        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"') s = s[1..^1];
        return s.Replace("\\n", "\n").Replace("\\t", "\t").Replace("\\\"", "\"").Replace("\\\\", "\\");
    }

    async Task Run()
    {
        var script = _args["lock"];
        List<Step> steps;
        try { steps = Parse(script); }
        catch (Exception e)
        {
            Say("cannot read the script: " + e.Message);
            await Quit();
            return;
        }
        await Frames(2);
        var screen = DisplayServer.GetPrimaryScreen();
        var usable = DisplayServer.ScreenGetUsableRect(screen);
        int lx = int.TryParse(_args.GetValueOrDefault("lock-x", "0"), out var xx) ? xx : 0;
        DisplayServer.WindowSetCurrentScreen(screen);
        DisplayServer.WindowSetPosition(usable.Position + new Vector2I(lx + 8, 32));
        await Frames(2);
        Say($"[{_role}] {Path.GetFileName(script)}: {steps.Count} steps" + (_resume > 0 ? $", resuming at {_resume}" : ""));
        for (int i = _resume; i < steps.Count; i++)
        {
            var s = steps[i];
            try
            {
                if (s.Verb == "restart")
                {
                    File.WriteAllText(Path.Combine(_out, "_resume." + _role), (i + 1).ToString(CultureInfo.InvariantCulture));
                    await Settle(30, 1800, true, s.Line);
                    Flush("_before_restart_" + s.Line);
                    await Quit();
                    return;
                }
                if (s.Verb == "quit")
                {
                    await Settle(30, 1800, true, s.Line);
                    Flush("_before_quit");
                    await Quit();
                    return;
                }
                await Do(s);
                if (_args.ContainsKey("lock-trace"))
                {
                    var t = Path.Combine(_out, "_trace");
                    Directory.CreateDirectory(t);
                    var tn = (_role == "solo" ? "" : _role + ".") + s.Line.ToString("000", CultureInfo.InvariantCulture);
                    GetViewport().GetTexture().GetImage().SavePng(Path.Combine(t, tn + ".png"));
                    File.WriteAllText(Path.Combine(t, tn + ".txt"), s.Text + "\n" + UiDump());
                }
            }
            catch (Exception e) { Say($"line {s.Line}: {s.Verb} failed: {e.GetType().Name}: {e.Message}"); }
        }
        await Settle(30, 1800, true, -1);
        Flush("_end");
        await Quit();
    }

    void Say(string s)
    {
        lock (_gate) _log.Add(Clean(s));
        GD.Print("[lock] " + s);
    }

    async Task Do(Step s)
    {
        var a = s.Args;
        string A(int i) => i < a.Count ? a[i] : throw new ArgumentException("missing argument " + (i + 1));
        int timeout = int.Parse(s.Opts.GetValueOrDefault("timeout", "240"), CultureInfo.InvariantCulture);
        var mods = s.Opts.GetValueOrDefault("mod", "");
        switch (s.Verb)
        {
            case "wait":
                await Frames(int.Parse(A(0), CultureInfo.InvariantCulture));
                return;
            case "note":
                Say(string.Join(" ", a.Select(Unquote)));
                return;
            case "settle":
                await Settle(a.Count > 0 ? int.Parse(a[0], CultureInfo.InvariantCulture) : 30, a.Count > 1 ? int.Parse(a[1], CultureInfo.InvariantCulture) : 1800, !s.Opts.ContainsKey("noimage"), s.Line);
                return;
            case "checkpoint":
                await Checkpoint(A(0));
                return;
            case "sync":
                await Sync(A(0), s.Line);
                return;
            case "close":
                GetTree().Root.PropagateNotification((int)NotificationWMCloseRequest);
                Say($"line {s.Line}: close asked");
                return;
            case "close-window":
            {
                var title = a.Count > 0 ? Unquote(a[0]) : null;
                var w = Windows().Where(x => x is not Popup && (title == null || x.Title == title)).LastOrDefault();
                if (w == null) { Say($"line {s.Line}: close-window {(title ?? "")}: NOT FOUND"); return; }
                w.PropagateNotification((int)NotificationWMCloseRequest);
                if (IsInstanceValid(w)) w.EmitSignal(Window.SignalName.CloseRequested);
                await Frames(3);
                return;
            }
            case "expect":
            case "absent":
            {
                var at = await Find(A(0), s.Verb == "expect" ? timeout : 1);
                bool there = at != null;
                Say($"line {s.Line}: {s.Verb} {A(0)}: {(there == (s.Verb == "expect") ? "ok" : there ? "PRESENT" : "NOT FOUND")}");
                return;
            }
            case "menu":
                if (!await Menu(Unquote(A(0)), timeout)) Say($"line {s.Line}: menu {A(0)}: NOT FOUND");
                return;
            case "key":
                await Key(A(0));
                return;
            case "dismiss":
                foreach (var pm in OpenPopups().Reverse()) pm.Hide();
                await Frames(3);
                return;
            case "clear":
            {
                var field = Visible().Select(v => v.c).LastOrDefault(c => c.HasFocus() && c is LineEdit or TextEdit);
                int n = field switch { LineEdit le => le.Text.Length, TextEdit te => te.Text.Length, _ => 0 };
                if (field == null) { Say($"line {s.Line}: clear: no text field has the keyboard"); return; }
                await Key(field is TextEdit ? "Ctrl+End" : "End");
                for (int i = 0; i < n; i++) await Key("Backspace");
                return;
            }
            case "keydown":
                await KeyEdge(A(0), true);
                return;
            case "keyup":
                await KeyEdge(A(0), false);
                return;
            case "type":
                await Type(Expand(Unquote(A(0)), slashes: false));
                return;
            case "drop-file":
            {
                var files = a.Select(x => Expand(Unquote(x))).ToArray();
                GetWindow().EmitSignal(Window.SignalName.FilesDropped, files);
                await Frames(2);
                return;
            }
        }
        await DoPointed(s, timeout, mods);
    }

    async Task DoPointed(Step s, int timeout, string mods)
    {
        var a = s.Args;
        string A(int i) => i < a.Count ? a[i] : throw new ArgumentException("missing argument " + (i + 1));
        if (s.Verb is "drag" or "rdrag" or "mdrag")
        {
            var from = await Point(A(0), timeout, s);
            var to = await Point(A(1), timeout, s);
            if (from == null || to == null) return;
            var b = s.Verb == "rdrag" ? MouseButton.Right : s.Verb == "mdrag" ? MouseButton.Middle : MouseButton.Left;
            int n = int.Parse(s.Opts.GetValueOrDefault("steps", "10"), CultureInfo.InvariantCulture);
            await WithMods(mods, async () =>
            {
                await Move(from.Value);
                await Button(b, true, from.Value, false);
                for (int i = 1; i <= n; i++) await Move(from.Value.Lerp(to.Value, i / (float)n));
                await Frames(int.Parse(s.Opts.GetValueOrDefault("hold", "1"), CultureInfo.InvariantCulture));
                await Button(b, false, to.Value, false);
            });
            return;
        }
        if (s.Verb == "table-click")
        {
            var p = await Point(A(0).StartsWith("world:") ? A(0) : "world:" + A(0), timeout, s);
            if (p != null) await WithMods(mods, () => Click(MouseButton.Left, p.Value, false));
            return;
        }
        if (s.Verb is "click" && A(0).StartsWith('"') | A(0).StartsWith("~\""))
        {
            var want = Unquote(StripSel(A(0)));
            for (int f = 0; f < timeout; f++)
            {
                if (await TryPopup(want, 1, A(0).StartsWith('~'))) return;
                if (Resolve(A(0)) != null) break;
            }
        }
        var pt = await Point(A(0), timeout, s);
        if (pt == null) return;
        switch (s.Verb)
        {
            case "click":
                await WithMods(mods, () => Click(MouseButton.Left, pt.Value, false));
                break;
            case "rclick":
                await WithMods(mods, () => Click(MouseButton.Right, pt.Value, false));
                break;
            case "mclick":
                await WithMods(mods, () => Click(MouseButton.Middle, pt.Value, false));
                break;
            case "dclick":
                await WithMods(mods, async () => { await Click(MouseButton.Left, pt.Value, false); await Click(MouseButton.Left, pt.Value, true); });
                break;
            case "hover": case "move":
                await Move(pt.Value);
                await Frames(1);
                break;
            case "probe":
            {
                await Move(pt.Value);
                await Frames(1);
                var hc = GetViewport().GuiGetHoveredControl();
                Say($"line {s.Line}: probe {A(0)} at {pt.Value.Round()}: {(hc == null ? "no control" : hc.GetClass() + " " + (TextOf(hc) ?? "") + " tip=" + hc.TooltipText.Split('\n')[0] + " filter=" + hc.MouseFilter + " path=" + hc.GetPath())}; menus lit: {string.Join(",", OpenPopups().Select(m => m.GetFocusedItem() + (m.IsEmbedded() ? " embedded" : " native") + " at " + m.Position + " size " + m.Size))}; root embeds {GetTree().Root.GuiEmbedSubwindows}, window at {DisplayServer.WindowGetPosition()}; pointer {Pointer.In(GetViewport())}");
                break;
            }
            case "mousedown":
                await WithMods(mods, async () => { await Move(pt.Value); await Button(Btn(s), true, pt.Value, false); });
                break;
            case "mouseup":
                await WithMods(mods, async () => { await Move(pt.Value); await Button(Btn(s), false, pt.Value, false); });
                break;
            case "wheel":
            {
                int n = int.Parse(a.Count > 1 ? a[1] : "1", CultureInfo.InvariantCulture);
                await WithMods(mods, async () =>
                {
                    await Move(pt.Value);
                    for (int i = 0; i < Math.Abs(n); i++)
                    {
                        var w = n > 0 ? MouseButton.WheelDown : MouseButton.WheelUp;
                        Parse(new InputEventMouseButton { ButtonIndex = w, Pressed = true, Position = pt.Value, GlobalPosition = pt.Value, Factor = 1, ButtonMask = _held });
                        Parse(new InputEventMouseButton { ButtonIndex = w, Pressed = false, Position = pt.Value, GlobalPosition = pt.Value, Factor = 1, ButtonMask = _held });
                        await Frames(1);
                    }
                });
                break;
            }
            default:
                Say($"line {s.Line}: unknown step '{s.Verb}'");
                break;
        }
    }

    static MouseButton Btn(Step s) => s.Opts.GetValueOrDefault("button", "left") switch { "right" => MouseButton.Right, "middle" => MouseButton.Middle, _ => MouseButton.Left };

    string Expand(string p, bool slashes = true)
    {
        var e = p.Replace("{fixtures}", _fixtures).Replace("{live}", _live).Replace("{packages}", System.Environment.GetEnvironmentVariable("KTABLE_PACKAGES") ?? "");
        return slashes ? e.Replace('/', Path.DirectorySeparatorChar) : e;
    }

    void Parse(InputEvent e)
    {
        if (e is InputEventWithModifiers m)
        {
            m.ShiftPressed = _keysDown.Contains(Godot.Key.Shift);
            m.CtrlPressed = _keysDown.Contains(Godot.Key.Ctrl);
            m.AltPressed = _keysDown.Contains(Godot.Key.Alt);
            m.MetaPressed = _keysDown.Contains(Godot.Key.Meta);
        }
        if (e is InputEventKey) _ourKeys.Add(e.GetInstanceId());
        else e.Device = OurDevice;
        Input.ParseInputEvent(e);
    }

    readonly HashSet<ulong> _ourKeys = new();

    const int OurDevice = 4295;

    void Disturbed(string why)
    {
        BestEffort.Try(() => File.AppendAllText(Path.Combine(_out, "_disturbed"), why + "\n"));
        GD.Print("[lock] disturbed: " + why);
    }

    public override void _Input(InputEvent e)
    {
        if (!_on || e.Device == OurDevice || e is InputEventKey && _ourKeys.Contains(e.GetInstanceId())) return;
        if (e is InputEventMouseButton or InputEventKey && e.IsPressed()) Disturbed("a real " + e.GetClass() + ": " + e.AsText());
        GetViewport().SetInputAsHandled();
    }

    public override void _Notification(int what)
    {
        if (_on && what == NotificationApplicationFocusOut && (_held != 0 || _keysDown.Count > 0))
            Disturbed("the window lost focus while the run held a button or key, which lets go of it");
    }

    public override void _Process(double delta)
    {
        if (!_on) return;
        var root = GetTree().Root;
        if (GetIndex() != root.GetChildCount() - 1) root.MoveChild(this, -1);
        GiveFocusBack();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);

    void GiveFocusBack()
    {
        if (_theirWindow == IntPtr.Zero || OS.GetName() != "Windows") return;
        var ours = (IntPtr)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle);
        if (ours != _theirWindow && GetForegroundWindow() == ours) SetForegroundWindow(_theirWindow);
    }

    async Task Move(Vector2 to)
    {
        Pointer.Drive(to);
        var rel = to - _mouse;
        var vel = rel == Vector2.Zero ? new Vector2(1, 0) : rel * 60;
        Parse(new InputEventMouseMotion { Position = to, GlobalPosition = to, Relative = rel, ScreenRelative = rel, Velocity = vel, ScreenVelocity = vel, ButtonMask = _held });
        _mouse = to;
        await Frames(1);
        if (!_moved) { await Frames(2); _moved = true; }
    }

    static MouseButtonMask MaskOf(MouseButton b) => (MouseButtonMask)(1 << ((int)b - 1));

    async Task Button(MouseButton b, bool down, Vector2 at, bool doubleClick)
    {
        if (down) _held |= MaskOf(b);
        else _held &= ~MaskOf(b);
        Parse(new InputEventMouseButton { ButtonIndex = b, Pressed = down, Position = at, GlobalPosition = at, ButtonMask = _held, DoubleClick = doubleClick && down });
        await Frames(1);
    }

    async Task Click(MouseButton b, Vector2 at, bool doubleClick)
    {
        await Move(at);
        await Button(b, true, at, doubleClick);
        await Button(b, false, at, false);
        await Frames(3);
    }

    async Task WithMods(string mods, Func<Task> act)
    {
        var keys = mods.Split('+', StringSplitOptions.RemoveEmptyEntries).Select(KeyOf).ToList();
        foreach (var k in keys) await KeyEdge(k, true);
        try { await act(); }
        finally { foreach (var k in Enumerable.Reverse(keys)) await KeyEdge(k, false); }
    }

    static Key KeyOf(string name) => name.Trim().ToLowerInvariant() switch
    {
        "ctrl" or "control" => Godot.Key.Ctrl, "shift" => Godot.Key.Shift, "alt" => Godot.Key.Alt, "meta" or "win" => Godot.Key.Meta,
        "esc" => Godot.Key.Escape, "enter" or "return" => Godot.Key.Enter, "del" => Godot.Key.Delete, "space" => Godot.Key.Space,
        "`" => Godot.Key.Quoteleft, "[" => Godot.Key.Bracketleft, "]" => Godot.Key.Bracketright, "=" => Godot.Key.Equal, "-" => Godot.Key.Minus,
        _ => OS.FindKeycodeFromString(name.Trim()) is var k && k != Godot.Key.None ? k : throw new ArgumentException("no key called " + name),
    };

    Task KeyEdge(string name, bool down) => KeyEdge(KeyOf(name), down);

    async Task KeyEdge(Key k, bool down)
    {
        if (down) _keysDown.Add(k);
        else _keysDown.Remove(k);
        Parse(new InputEventKey { Keycode = k, PhysicalKeycode = k, KeyLabel = k, Pressed = down });
        await Frames(1);
    }

    async Task Key(string combo)
    {
        var parts = combo.Length > 1 && combo.EndsWith("++") ? combo[..^2].Split('+').Append("+").ToArray() : combo.Split('+');
        var mods = parts[..^1].Where(p => p.Length > 0).Select(KeyOf).ToList();
        var key = KeyOf(parts[^1]);
        foreach (var m in mods) await KeyEdge(m, true);
        bool plain = !mods.Contains(Godot.Key.Ctrl) && !mods.Contains(Godot.Key.Alt) && !mods.Contains(Godot.Key.Meta);
        long uni = 0;
        if (plain && parts[^1].Length == 1) uni = mods.Contains(Godot.Key.Shift) ? char.ToUpperInvariant(parts[^1][0]) : char.ToLowerInvariant(parts[^1][0]);
        else if (plain && key == Godot.Key.Space) uni = ' ';
        Parse(new InputEventKey { Keycode = key, PhysicalKeycode = key, KeyLabel = key, Unicode = (char)uni, Pressed = true });
        await Frames(1);
        Parse(new InputEventKey { Keycode = key, PhysicalKeycode = key, KeyLabel = key, Unicode = (char)uni, Pressed = false });
        await Frames(1);
        foreach (var m in Enumerable.Reverse(mods)) await KeyEdge(m, false);
    }

    async Task Type(string text)
    {
        foreach (var ch in text)
        {
            if (ch == '\n') { await Key("Enter"); continue; }
            var k = ch switch
            {
                >= 'a' and <= 'z' => Godot.Key.A + (ch - 'a'),
                >= 'A' and <= 'Z' => Godot.Key.A + (ch - 'A'),
                >= '0' and <= '9' => Godot.Key.Key0 + (ch - '0'),
                ' ' => Godot.Key.Space,
                '.' => Godot.Key.Period, ',' => Godot.Key.Comma, '-' => Godot.Key.Minus, '/' => Godot.Key.Slash, ':' => Godot.Key.Colon,
                _ => Godot.Key.None,
            };
            Parse(new InputEventKey { Keycode = k, PhysicalKeycode = k, KeyLabel = k, Unicode = ch, Pressed = true });
            Parse(new InputEventKey { Keycode = k, PhysicalKeycode = k, KeyLabel = k, Unicode = ch, Pressed = false });
            await Frames(1);
        }
        await Frames(1);
    }

    static string StripSel(string sel)
    {
        var m = Regex.Match(sel, "^[~^]?(\".*\")(#\\d+)?([+-]\\d+,[+-]?\\d+)?$");
        return m.Success ? m.Groups[1].Value : sel;
    }

    Control? _lastHit;

    async Task<Vector2?> Point(string sel, int timeout, Step s)
    {
        _lastHit = null;
        var p = await Find(sel, timeout);
        if (p == null) { Say($"line {s.Line}: {s.Verb} {sel}: NOT FOUND"); return null; }
        for (int k = 0; k < 60 && _lastHit != null && IsInstanceValid(_lastHit); k++)
        {
            ScrollContainer? sc = null;
            for (var n = _lastHit.GetParent(); n != null && sc == null; n = n.GetParent()) sc = n as ScrollContainer;
            if (sc == null) break;
            var box = ScreenRect(sc);
            if (box.HasPoint(p.Value)) break;
            await Move(box.GetCenter());
            var w = p.Value.Y > box.End.Y ? MouseButton.WheelDown : MouseButton.WheelUp;
            Parse(new InputEventMouseButton { ButtonIndex = w, Pressed = true, Position = box.GetCenter(), GlobalPosition = box.GetCenter(), Factor = 1, ButtonMask = _held });
            Parse(new InputEventMouseButton { ButtonIndex = w, Pressed = false, Position = box.GetCenter(), GlobalPosition = box.GetCenter(), Factor = 1, ButtonMask = _held });
            await Frames(2);
            p = Resolve(sel);
            if (p == null) { Say($"line {s.Line}: {s.Verb} {sel}: lost while scrolling"); return null; }
        }
        return p;
    }

    async Task<Vector2?> Find(string sel, int timeout)
    {
        for (int f = 0; ; f++)
        {
            var p = Resolve(sel);
            if (p != null || f >= timeout) return p;
            await Frames(1);
        }
    }

    Vector2? Resolve(string sel)
    {
        var inv = CultureInfo.InvariantCulture;
        if (sel.StartsWith('@'))
        {
            var xy = sel[1..].Split(',');
            float X(string v) => v.EndsWith('%') ? float.Parse(v[..^1], inv) / 100f : float.Parse(v, inv);
            var size = GetViewport().GetVisibleRect().Size;
            var x = X(xy[0]);
            var y = X(xy[1]);
            return new Vector2(xy[0].EndsWith('%') ? x * size.X : x, xy[1].EndsWith('%') ? y * size.Y : y);
        }
        if (sel.StartsWith("world:"))
        {
            var v = sel[6..].Split(',').Select(t => float.Parse(t, inv)).ToArray();
            var cam = GetViewport().GetCamera3D();
            if (cam == null) return null;
            var w = v.Length >= 3 ? new Vector3(v[0], v[1], v[2]) : new Vector3(v[0], 0, v[1]);
            if (cam.IsPositionBehind(w)) return null;
            return cam.UnprojectPosition(w);
        }
        var m = Regex.Match(sel, "^([~^]?)\"(.*)\"(?:#(\\d+))?(?:([+-]\\d+),([+-]?\\d+))?$");
        if (!m.Success) throw new ArgumentException("not a selector: " + sel);
        bool contains = m.Groups[1].Value == "~", prefix = m.Groups[1].Value == "^";
        var text = Unquote("\"" + m.Groups[2].Value + "\"");
        int nth = m.Groups[3].Success ? int.Parse(m.Groups[3].Value, inv) : -1;
        var off = m.Groups[4].Success ? new Vector2(float.Parse(m.Groups[4].Value, inv), float.Parse(m.Groups[5].Value, inv)) : Vector2.Zero;
        var hits = Matches(text, contains, prefix);
        if (hits.Count == 0 && !contains && !prefix) hits = Matches(text, true);
        if (hits.Count == 0) return null;
        Rect2 r;
        if (nth >= 1)
        {
            if (nth > hits.Count) return null;
            var hh = hits.OrderBy(h => h.rect.Position.Y).ThenBy(h => h.rect.Position.X).ElementAt(nth - 1);
            r = hh.rect;
            _lastHit = hh.c;
        }
        else
        {
            var level = hits.Max(h => h.depth / 1_000_000);
            var hh = hits.Where(h => h.depth / 1_000_000 == level).OrderBy(h => h.rect.Position.Y).ThenBy(h => h.rect.Position.X).First();
            r = hh.rect;
            _lastHit = hh.c;
        }
        return r.GetCenter() + off;
    }

    List<(Rect2 rect, long depth, Control c)> Matches(string text, bool contains, bool prefix = false)
    {
        var hits = new List<(Rect2, long, Control)>();
        bool Is(string? s) => !string.IsNullOrEmpty(s)
            && (contains ? s.Contains(text, StringComparison.Ordinal) : prefix ? s.TrimStart().StartsWith(text, StringComparison.Ordinal) : s.Trim() == text);
        long order = 0;
        foreach (var (c, depth) in Visible())
        {
            order++;
            var at = ScreenRect(c);
            if (at.Size.X <= 0 || at.Size.Y <= 0) continue;
            long d = depth * 1_000_000 + order;
            if (Is(TextOf(c)) || Is(c.TooltipText) || Is(TipName(c.TooltipText)) || (c is LineEdit le && Is(le.PlaceholderText)) || (c is TextEdit te && Is(te.PlaceholderText))) hits.Add((at, d, c));
            switch (c)
            {
                case TabBar tb:
                    for (int i = 0; i < tb.TabCount; i++) if (Is(tb.GetTabTitle(i)) || Is(tb.GetTabTooltip(i))) hits.Add((new Rect2(at.Position + tb.GetTabRect(i).Position, tb.GetTabRect(i).Size), d,
                        c));
                    break;
                case ItemList il:
                    for (int i = 0; i < il.ItemCount; i++)
                        if (Is(il.GetItemText(i)) || Is(il.GetItemTooltip(i)))
                        {
                            var ir = il.GetItemRect(i);
                            var sb = il.GetVScrollBar();
                            hits.Add((new Rect2(at.Position + ir.Position - new Vector2(0, (float)(sb?.Value ?? 0)), ir.Size), d, c));
                        }
                    break;
                case Tree tr when tr.GetRoot() is { } root:
                    foreach (var item in TreeItems(root))
                        for (int col = 0; col < tr.Columns; col++)
                            if (Is(item.GetText(col)) || Is(item.GetTooltipText(col)))
                            {
                                var ir = tr.GetItemAreaRect(item, col);
                                if (ir.Size.X > 0) hits.Add((new Rect2(at.Position + ir.Position, ir.Size), d, c));
                            }
                    break;
            }
        }
        return hits;
    }

    static string TipName(string tip)
    {
        var first = tip.Split('\n')[0];
        var key = first.IndexOf("   (", StringComparison.Ordinal);
        return key > 0 ? first[..key] : first;
    }

    static IEnumerable<TreeItem> TreeItems(TreeItem root)
    {
        yield return root;
        for (var c = root.GetFirstChild(); c != null; c = c.GetNext())
            foreach (var x in TreeItems(c)) yield return x;
    }

    static string? TextOf(Control c) => c switch
    {
        Button b => b.Text,
        Label l => l.Text,
        LineEdit le => le.Text,
        TextEdit te => te.Text.Length > 200 ? te.Text[..200] : te.Text,
        RichTextLabel rt => Regex.Replace(rt.GetParsedText(), @"(?m)^\d\d:\d\d(?=  )", "<clock>"),
        _ => null,
    };

    static Rect2 ScreenRect(Control c)
    {
        var t = c.GetScreenTransform();
        return new Rect2(t.Origin, c.Size * t.Scale);
    }

    IEnumerable<(Control c, int depth)> Visible()
    {
        var list = new List<(Control, int)>();
        void Walk(Node n, int depth)
        {
            if (n is Window w)
            {
                if (!w.Visible) return;
                if (w != GetTree().Root) depth = depth + 1 + (w.AlwaysOnTop ? 50 : 0) + (w is Popup ? 20 : 0);
            }
            if (n is CanvasItem ci && !ci.Visible) return;
            if (n is CanvasLayer cl && !cl.Visible) return;
            if (n is Control c) list.Add((c, depth * 1000 + (c.GetCanvasLayerNode()?.Layer ?? 0) + 100));
            foreach (var ch in n.GetChildren(true)) Walk(ch, depth);
        }
        Walk(GetTree().Root, 0);
        return list;
    }

    List<Window> Windows()
    {
        var list = new List<Window>();
        void Walk(Node n)
        {
            if (n is Window w && w != GetTree().Root) { if (!w.Visible) return; list.Add(w); }
            foreach (var ch in n.GetChildren(true)) Walk(ch);
        }
        Walk(GetTree().Root);
        return list;
    }

    IEnumerable<PopupMenu> OpenPopups()
    {
        var list = new List<PopupMenu>();
        void Walk(Node n)
        {
            if (n is PopupMenu pm && pm.Visible) list.Add(pm);
            if (n is Window w && !w.Visible) return;
            foreach (var ch in n.GetChildren(true)) Walk(ch);
        }
        Walk(GetTree().Root);
        return list;
    }

    async Task<bool> TryPopup(string text, int timeout, bool contains = false)
    {
        for (int f = 0; f < timeout; f++)
        {
            foreach (var pm in OpenPopups().Reverse())
            {
                int idx = -1;
                for (int i = 0; i < pm.ItemCount && idx < 0; i++) if (!pm.IsItemSeparator(i) && pm.GetItemText(i).Trim() == text) idx = i;
                for (int i = 0; i < pm.ItemCount && idx < 0 && contains; i++) if (!pm.IsItemSeparator(i) && pm.GetItemText(i).Contains(text, StringComparison.Ordinal)) idx = i;
                if (idx < 0) continue;
                float x = pm.Position.X + pm.Size.X * 0.4f, top = pm.Position.Y, h = pm.Size.Y;
                float y = top + (idx + 0.5f) * h / Math.Max(1, pm.ItemCount), step = Math.Max(2f, h / Math.Max(1, pm.ItemCount) / 3f);
                bool on = false;
                float row = h / Math.Max(1, pm.ItemCount);
                var seen = new List<string>();
                for (int k = 0; k < 40 && pm.Visible; k++)
                {
                    await Move(new Vector2(x, y));
                    await Frames(3);
                    int lit = pm.GetFocusedItem();
                    seen.Add($"{y:0}:{lit}");
                    if (lit == idx || pm.GetItemSubmenuNode(idx) is { Visible: true }) { on = true; break; }
                    y += lit < 0 ? 4 : (lit < idx ? Math.Max(3f, row / 2) : -Math.Max(3f, row / 2));
                    if (y > top + h - 2) y = top + 3;
                    if (y < top + 2) y = top + 2;
                }
                if (!on) { Say($"menu row \"{text}\" (row {idx} of {pm.ItemCount}, menu at {pm.Position} {pm.Size}): the pointer never lit it ({string.Join(" ", seen)})"); return true; }
                await Button(MouseButton.Left, true, new Vector2(x, y), false);
                await Button(MouseButton.Left, false, new Vector2(x, y), false);
                await Frames(3);
                return true;
            }
            await Frames(1);
        }
        return false;
    }

    async Task<bool> Menu(string text, int timeout) => await TryPopup(text, Math.Max(1, timeout), true);

    async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    async Task Settle(int quiet, int cap, bool image, int line)
    {
        ulong last = 0;
        int still = 0;
        var since = Time.GetTicksMsec();
        for (int f = 0; f < cap; f += 2)
        {
            await Frames(2);
            var sig = Signature(image, f % 10 == 0);
            if (sig == last) still += 2;
            else
            {
                still = 0;
                last = sig;
                since = Time.GetTicksMsec();
            }
            if (still >= quiet && Time.GetTicksMsec() - since >= 1200) return;
        }
        GD.Print($"[lock] line {line}: settle: still changing after {cap} frames");
    }

    ulong _filesSig;
    ulong Signature(bool image, bool files)
    {
        var h = new HashCode();
        h.Add(UiDump());
        lock (_gate) h.Add(_taps);
        h.Add((long)Performance.GetMonitor(Performance.Monitor.ObjectNodeCount));
        if (files) _filesSig = FilesSignature();
        h.Add(_filesSig);
        if (image)
        {
            var img = GetViewport().GetTexture().GetImage();
            img.Resize(80, 45, Image.Interpolation.Bilinear);
            var data = img.GetData();
            for (int i = 0; i < data.Length; i += 1) data[i] = (byte)(data[i] >> 4);
            h.Add(Convert.ToHexString(SHA1.HashData(data)));
        }
        return (ulong)(uint)h.ToHashCode();
    }

    ulong FilesSignature()
    {
        long sum = 17;
        foreach (var (_, dir) in Roots())
        {
            if (!Directory.Exists(dir)) continue;
            try
            {
                foreach (var f in new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories))
                    sum = unchecked(sum * 31 + f.Length * 7 + f.LastWriteTimeUtc.Ticks);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { sum++; }
        }
        return (ulong)sum;
    }

    async Task Sync(string name, int line)
    {
        Directory.CreateDirectory(_sync);
        File.WriteAllText(Path.Combine(_sync, name + "." + _role), "");
        var start = Time.GetTicksMsec();
        while (!_roles.All(r => File.Exists(Path.Combine(_sync, name + "." + r))))
        {
            if (Time.GetTicksMsec() - start > 90_000) { Say($"line {line}: sync {name}: the other side never came"); return; }
            await Frames(1);
        }
    }

    async Task Quit()
    {
        GetTree().Root.PropagateNotification((int)NotificationWMCloseRequest);
        for (int i = 0; i < 900; i++) await Frames(1);
        GD.Print("[lock] the app did not close on its own; quitting");
        File.WriteAllText(Path.Combine(_out, "_forced_quit"), "the app was still running 900 frames after the close request");
        GetTree().Quit();
    }

    IEnumerable<(string tag, string dir)> Roots()
    {
        if (System.Environment.GetEnvironmentVariable("KTABLE_PACKAGES") is { Length: > 0 } p) yield return ("packages", p);
        if (System.Environment.GetEnvironmentVariable("KTABLE_USER_DIR") is { Length: > 0 } u) yield return ("user", u);
    }

    void Flush(string name)
    {
        lock (_gate) { if (_log.Count == 0) { _wire.Clear(); return; } }
        var dir = Path.Combine(_out, _role == "solo" ? name : _role + "." + name);
        Directory.CreateDirectory(dir);
        lock (_gate)
        {
            File.WriteAllText(Path.Combine(dir, "wire-tail.jsonl"), string.Concat(_wire.Select(w => Clean(Peers(w)) + "\n")));
            File.WriteAllText(Path.Combine(dir, "steps.txt"), string.Concat(_log.Select(l => l + "\n")));
            _wire.Clear();
            _log.Clear();
        }
    }

    async Task Checkpoint(string name)
    {
        var dir = Path.Combine(_out, _role == "solo" ? name : _role + "." + name);
        Directory.CreateDirectory(dir);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        try { GetViewport().GetTexture().GetImage().SavePng(Path.Combine(dir, "shot.png")); } catch (Exception e) { Say("shot: " + e.Message); }
        string state;
        try { state = KTable.Core.WireTap.HostState?.Invoke() is { } g ? KTable.Core.Json.Serialize(g, true) : "null"; }
        catch (Exception e) { state = "\"<state unreadable: " + e.GetType().Name + ">\""; }
        File.WriteAllText(Path.Combine(dir, "state.json"), Clean(Peers(state)) + "\n");
        File.WriteAllText(Path.Combine(dir, "ui.txt"), UiDump());
        File.WriteAllText(Path.Combine(dir, "files.txt"), FilesList());
        lock (_gate)
        {
            File.WriteAllText(Path.Combine(dir, "wire.jsonl"), string.Concat(_wire.Select(w => Clean(Peers(w)) + "\n")));
            File.WriteAllText(Path.Combine(dir, "steps.txt"), string.Concat(_log.Select(l => l + "\n")));
            _wire.Clear();
            _log.Clear();
        }
        GD.Print("[lock] checkpoint " + name);
    }

    static readonly Regex WallTime = new(@"(?<![0-9A-Za-z.])(20\d\d-\d\d-\d\d[T ]\d\d:\d\d(:\d\d(\.\d+)?)?(Z|[+-]\d\d:?\d\d)?|20\d{6}[-_T]?\d{6}(\d{3})?|1[6-9]\d{11}|63\d{16})(?![0-9A-Za-z])", RegexOptions.Compiled);
    static readonly Regex ShownClock = new(@"(?<=ended )\d\d:\d\d|\(\d{1,2} [A-Z][a-z]{2} \d\d:\d\d\)|\b\d+ (min|h|days) ago\b|\bjust now\b", RegexOptions.Compiled);

    static readonly Regex Packed = new("\"packed\":\"([A-Za-z0-9+/=]+)\"", RegexOptions.Compiled);

    static string InnerHash(Match m)
    {
        try
        {
            using var z = new GZipStream(new MemoryStream(Convert.FromBase64String(m.Groups[1].Value)), CompressionMode.Decompress);
            using var plain = new MemoryStream();
            z.CopyTo(plain);
            var text = PastedId.Replace(WallTime.Replace(Encoding.UTF8.GetString(plain.ToArray()), "<time>"), "x-c<id>");
            return "\"packed\":\"<" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..20].ToLowerInvariant() + ">\"";
        }
        catch (Exception e) when (e is FormatException or InvalidDataException) { return m.Value; }
    }

    static readonly Regex Token = new("\"(token|tokenHash)\":\"[^\"]*\"", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex PastedId = new("(?<![0-9A-Za-z])x-c[0-9a-f]{10}(?![0-9A-Za-z])", RegexOptions.Compiled);
    static readonly Regex CaptureTime = new("(?<=_)\\d{6}(?=\\.png$)", RegexOptions.Compiled);
    static readonly Regex BareToken = new("(?<![A-Z0-9])[A-HJ-NP-Z2-9]{16}(?![A-Z0-9])", RegexOptions.Compiled);

    static readonly Regex LongString = new("\"([^\"\\\\]|\\\\.){400,}\"", RegexOptions.Compiled);

    string Clean(string s)
    {
        if (_live.Length > 0)
            foreach (var v in new[] { _live, _live.Replace('\\', '/'), _live.Replace('/', '\\'), _live.Replace("\\", "\\\\") }.Distinct())
                s = s.Replace(v, "<LIVE>", StringComparison.OrdinalIgnoreCase);
        s = WallTime.Replace(s, "<time>");
        s = ShownClock.Replace(s, "<clock>");
        s = Token.Replace(s, "\"$1\":\"<token>\"");
        s = PastedId.Replace(s, "x-c<id>");
        s = LongString.Replace(s, m => $"\"<{m.Length} chars {Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(m.Value)))[..16]}>\"");
        return s;
    }

    string UiDump()
    {
        var lines = new List<(float y, float x, string s)>();
        string Q(string? t) => t == null ? "" : "\"" + t.Replace("\\", "\\\\").Replace("\n", "\\n").Replace("\"", "\\\"") + "\"";
        foreach (var (c, _) in Visible())
        {
            var r = ScreenRect(c);
            if (r.Size.X < 1 || r.Size.Y < 1) continue;
            var sb = new StringBuilder();
            var text = TextOf(c);
            bool show = !string.IsNullOrEmpty(text) || !string.IsNullOrEmpty(c.TooltipText) || c is BaseButton or Godot.Range or LineEdit or TextEdit or ItemList or Tree or TabBar
                || (c is TextureRect tr && tr.Texture != null);
            if (!show) continue;
            sb.Append(c.GetClass());
            if (!string.IsNullOrEmpty(text)) sb.Append(' ').Append(Q(text));
            if (!string.IsNullOrEmpty(c.TooltipText)) sb.Append(" tip=").Append(Q(c.TooltipText));
            switch (c)
            {
                case LineEdit le:
                    if (le.PlaceholderText.Length > 0) sb.Append(" ph=").Append(Q(le.PlaceholderText));
                    if (!le.Editable) sb.Append(" readonly");
                    break;
                case TextEdit te:
                    if (!te.Editable) sb.Append(" readonly");
                    break;
                case BaseButton b:
                    if (b.Disabled) sb.Append(" disabled");
                    if (b.ToggleMode || b is CheckBox or CheckButton) sb.Append(b.ButtonPressed ? " on" : " off");
                    if (b is OptionButton ob) sb.Append(" sel=").Append(ob.Selected);
                    if (b is Button bb && bb.Icon != null) sb.Append(" icon");
                    break;
                case Godot.Range rg:
                    sb.Append(" value=").Append(rg.Value.ToString("0.####", CultureInfo.InvariantCulture));
                    if (rg is ScrollBar) sb.Append(" max=").Append(rg.MaxValue.ToString("0.#", CultureInfo.InvariantCulture));
                    break;
                case ItemList il:
                    sb.Append(" items=[");
                    for (int i = 0; i < il.ItemCount; i++) sb.Append(i > 0 ? "," : "").Append(Q(il.GetItemText(i))).Append(il.IsSelected(i) ? "*" : "");
                    sb.Append(']');
                    break;
                case TabBar tb:
                    sb.Append(" tabs=[");
                    for (int i = 0; i < tb.TabCount; i++) sb.Append(i > 0 ? "," : "").Append(Q(tb.GetTabTitle(i))).Append(i == tb.CurrentTab ? "*" : "");
                    sb.Append(']');
                    break;
                case Tree t when t.GetRoot() is { } root:
                    sb.Append(" rows=[");
                    bool first = true;
                    foreach (var it in TreeItems(root))
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        sb.Append(Q(it.GetText(0)));
                        if (it.IsSelected(0)) sb.Append('*');
                    }
                    sb.Append(']');
                    break;
                case TextureRect tex:
                    sb.Append(" texture=").Append((int)tex.Texture.GetSize().X).Append('x').Append((int)tex.Texture.GetSize().Y);
                    break;
            }
            if (c.HasFocus()) sb.Append(" focus");
            sb.Append($" @{Mathf.RoundToInt(r.Position.X)},{Mathf.RoundToInt(r.Position.Y)},{Mathf.RoundToInt(r.Size.X)}x{Mathf.RoundToInt(r.Size.Y)}");
            lines.Add((Mathf.Round(r.Position.Y), Mathf.Round(r.Position.X), sb.ToString()));
        }
        foreach (var pm in OpenPopups())
        {
            var sb = new StringBuilder("PopupMenu @").Append(pm.Position.X).Append(',').Append(pm.Position.Y).Append(" [");
            for (int i = 0; i < pm.ItemCount; i++)
            {
                if (i > 0) sb.Append(" | ");
                if (pm.IsItemSeparator(i)) { sb.Append("---").Append(pm.GetItemText(i)); continue; }
                sb.Append(pm.GetItemText(i));
                if (pm.IsItemDisabled(i)) sb.Append(" (disabled)");
                if (pm.IsItemCheckable(i) || pm.IsItemRadioCheckable(i)) sb.Append(pm.IsItemChecked(i) ? " (checked)" : " (unchecked)");
                if (pm.GetItemSubmenuNode(i) != null) sb.Append(" >");
                if (pm.GetItemTooltip(i).Length > 0) sb.Append(" tip=").Append(Q(pm.GetItemTooltip(i)));
            }
            lines.Add((pm.Position.Y, pm.Position.X, sb.Append(']').ToString()));
        }
        void Windows(Node n)
        {
            if (n is Window w && w != GetTree().Root)
            {
                if (!w.Visible) return;
                if (w is not PopupMenu) lines.Add((w.Position.Y, w.Position.X, $"Window {Q(w.Title)} @{w.Position.X},{w.Position.Y},{w.Size.X}x{w.Size.Y}"));
            }
            foreach (var ch in n.GetChildren(true)) Windows(ch);
        }
        Windows(GetTree().Root);
        string body;
        lock (_gate) body = Peers(string.Concat(lines.OrderBy(l => l.y).ThenBy(l => l.x).ThenBy(l => l.s, StringComparer.Ordinal).Select(l => l.s + "\n")));
        return Clean(body);
    }

    static readonly string[] SkipParts = { "/logs/", "/app.log", "/app.old.log", "/shader_cache/", "/vulkan/", "/.godot/", "/mono/" };

    string FilesList()
    {
        var sb = new StringBuilder();
        foreach (var (tag, dir) in Roots())
        {
            if (!Directory.Exists(dir)) continue;
            var all = new List<string>();
            try { all.AddRange(Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)); } catch (Exception e) { sb.Append(tag).Append(" <unlistable: ").Append(e.GetType().Name).Append(">\n"); }
            foreach (var rel in all.Select(f => Path.GetRelativePath(dir, f).Replace('\\', '/')).OrderBy(x => x, StringComparer.Ordinal))
            {
                if (SkipParts.Any(p => ("/" + rel).Contains(p, StringComparison.OrdinalIgnoreCase))) continue;
                sb.Append(tag).Append('/').Append(CaptureTime.Replace(WallTime.Replace(rel, "<time>"), "<hhmmss>")).Append(' ').Append(FileHash(Path.Combine(dir, rel))).Append('\n');
            }
        }
        return sb.ToString();
    }

    static byte[] Gunzipped(byte[] bytes)
    {
        using var z = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress);
        using var plain = new MemoryStream();
        z.CopyTo(plain);
        return plain.ToArray();
    }

    static string? StrictUtf8OrNull(byte[] bytes)
    {
        try { return new UTF8Encoding(false, true).GetString(bytes); }
        catch (ArgumentException) { return null; }
    }

    static string FileHash(string path)
    {
        byte[] bytes;
        try { bytes = File.ReadAllBytes(path); } catch (Exception e) { return "<unreadable " + e.GetType().Name + ">"; }
        if (bytes.Length >= 2 && bytes[0] == 0x1F && bytes[1] == 0x8B)
        {
            bytes = BestEffort.Read(() => Gunzipped(bytes), fallback: bytes);
        }
        if (Array.IndexOf(bytes, (byte)0, 0, Math.Min(bytes.Length, 4096)) < 0 && StrictUtf8OrNull(bytes) is { } decoded)
        {
            var text = PastedId.Replace(Token.Replace(Packed.Replace(WallTime.Replace(decoded, "<time>"), InnerHash), "\"$1\":\"<token>\""), "x-c<id>");
            if (Path.GetFileName(path) == "joins.txt") text = BareToken.Replace(text, "<token>");
            bytes = Encoding.UTF8.GetBytes(text);
        }
        return Convert.ToHexString(SHA256.HashData(bytes))[..20].ToLowerInvariant();
    }
}
#else
using Godot;
using KTable.Core;

namespace KTable.Dev;

public partial class LockHarness : Node
{
    public override void _EnterTree() => QueueFree();
}
#endif
