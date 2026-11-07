using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core;

namespace KTable.App;

sealed class ToolStrip : HudPart
{
    public PanelContainer Panel = null!;
    VBoxContainer _strip = null!;
    public readonly Dictionary<Tool, Button> ToolButtons = new();
    public readonly Dictionary<string, Button> Buttons = new();
    public Button? Btn2d;

    readonly IHudShell Shell;
    readonly Drawer _drawer;
    public ToolStrip(HudContext ctx, IHudShell shell, Drawer drawer) : base(ctx) { Shell = shell; _drawer = drawer; }

    public void Build(Control root)
    {
        Panel = new PanelContainer();
        var stripStyle = Ui.Panel();
        stripStyle.ContentMarginLeft = stripStyle.ContentMarginRight = 3;
        stripStyle.ContentMarginTop = stripStyle.ContentMarginBottom = 4;
        Panel.AddThemeStyleboxOverride("panel", stripStyle);
        root.AddChild(Panel);
        _strip = new VBoxContainer();
        _strip.AddThemeConstantOverride("separation", Ui.Gap.Hair);
        Panel.AddChild(_strip);
    }

    static readonly (string Key, int Group)[] StripCatalog =
    {
        ("map", 0), ("calibrate", 0), ("pieces", 0), ("dice", 0),
        ("layers", 6), ("zones", 6),
        ("select", 1), ("gizmo", 1), ("formation", 1),
        ("ruler", 2), ("range", 2),
        ("boards", 3), ("paint", 3), ("decals", 3), ("relief", 3),
        ("sighting", 4), ("capture", 4),
        ("2d", 5), ("text", 5), ("frame", 5), ("compass", 5), ("log", 5),
    };

    static string HelpAnchor(string key) => key switch
    {
        "map" => "map-sheets", "calibrate" => "calibrate", "pieces" => "pieces", "dice" => "dice", "layers" => "umpires-and-zones",
        "select" => "selecting-and-moving", "gizmo" => "turn-and-resize", "formation" => "formations", "ruler" => "ruler", "range" => "ranging",
        "boards" => "layers", "paint" => "draw", "decals" => "decals", "relief" => "relief", "sighting" => "pov-capture", "capture" => "capture", "log" => "log", "zones" => "umpires-and-zones",
        _ => "views",
    };

    bool OtherUmpires => S.IsUmpire && S.Peers.Any(p => Visibility.IsUmpire(p.Role) && p.PeerId != S.Me.PeerId);

    bool StripAllowed(string key) => key is "layers" or "zones" ? OtherUmpires
        : S.IsUmpire || key is "select" or "formation" or "ruler" or "range" or "capture" or "dice" or "2d" or "text" or "frame" or "compass" or "log";

    bool _othersShown;
    Button? _text;

    public void SyncUmpireButtons()
    {
        if (OtherUmpires == _othersShown) return;
        if (!OtherUmpires && Cfg.LeftOpen && Cfg.LeftPage == "layers") _drawer.ShowPage(null);
        Rebuild();
    }

    public void Rebuild()
    {
        _othersShown = OtherUmpires;
        foreach (var c in _strip.GetChildren()) { _strip.RemoveChild(c); c.QueueFree(); }
        ToolButtons.Clear();
        Buttons.Clear();
        int last = -1;
        foreach (var (key, group) in StripCatalog)
        {
            if (!StripAllowed(key)) continue;
            var b = MakeButton(key);
            if (b == null) continue;
            HelpTags.Tag(b, "umpire-guide.md#" + HelpAnchor(key));
            if (last >= 0 && group != last) _strip.AddChild(new HSeparator());
            last = group;
            b.CustomMinimumSize = new Vector2(Cfg.StripSize, Cfg.StripSize);
            _strip.AddChild(b);
        }
        Callable.From(Shell.SyncStripToggles).CallDeferred();
    }

    string CalibrateDetail() => $"Drag along the map's own scale bar to teach the table what {S.View.Calibration.ScaleBar.Words} is";

    public void SyncCalibrateTip()
    {
        if (ToolButtons.TryGetValue(Tool.Calibrate, out var b)) b.TooltipText = $"Calibrate\n{CalibrateDetail()}";
    }

    public void SyncToolButtons()
    {
        foreach (var (tool, b) in ToolButtons) b.ButtonPressed = T.Tool == tool;
    }

    public void SyncViewButton() { if (Btn2d != null && T != null) Btn2d.SetPressedNoSignal(!T.Rig.TopDown); }

    public void SyncToggles()
    {
        foreach (var (key, b) in Buttons)
            if (Shell.Panels.TryGetValue(key, out var p)) b.SetPressedNoSignal(p.Visible);
        _text?.SetPressedNoSignal(Cfg.LabelPlates);
        SyncPenLock();
    }

    public void SyncPenLock()
    {
        if (T == null || !Buttons.TryGetValue("paint", out var b)) return;
        bool locked = T.DrawingLocked;
        foreach (var c in b.GetChildren()) if (c is ToolIcon ic) ic.Locked = locked;
        b.TooltipText = locked
            ? "Draw\nLayer is hidden · show it to unlock"
            : "Draw\nThe pen, the shapes and the rubber";
    }

    Button? MakeButton(string key) => ToolButton(key) ?? OtherButton(key);

    static Button Icon(string kind, string tip, string detail = "")
    {
        var b = new Button { FocusMode = Control.FocusModeEnum.None, TooltipText = detail.Length > 0 ? $"{tip}\n{detail}" : tip };
        b.AddChild(new ToolIcon { Kind = kind });
        return Ui.Bare(b);
    }

    Button ToolB(Tool t, string kind, string tip, string detail)
    {
        var b = Icon(kind, tip, detail);
        b.ToggleMode = true;
        b.ButtonPressed = T != null && T.Tool == t;
        Ui.MarkTool(b);
        b.Pressed += () => T!.SetTool(T.Tool == t && t != Tool.Select ? Tool.Select : t);
        ToolButtons[t] = b;
        return b;
    }

    Button Page(string page, string kind, string tip, string detail)
    {
        var b = Icon(kind, tip, detail);
        b.ToggleMode = true;
        b.ButtonPressed = Cfg.LeftOpen && Cfg.LeftPage == page;
        b.Pressed += () => _drawer.ShowPage(Cfg.LeftOpen && Cfg.LeftPage == page ? null : page);
        _drawer.SetButton(page, b);
        return b;
    }

    Button PanelToggle(string key, string kind, string tip, string detail, bool on = false)
    {
        var b = Icon(kind, tip, detail);
        b.ToggleMode = true;
        b.ButtonPressed = on;
        b.Pressed += () => Shell.TogglePanel(key);
        Buttons[key] = b;
        return b;
    }

    Button? ToolButton(string key)
    {
        switch (key)
        {
            case "select": return ToolB(Tool.Select, "select", "Select   (Esc)", "Drag a piece to pick it up; drag the empty table to box-select");
            case "gizmo": return ToolB(Tool.Transform, "gizmo", $"Turn / resize   ({Cfg.Keys.Label("gizmo")})", "A ring around the selection: drag the round knob to turn, the square knob to resize");
            case "ruler": return ToolB(Tool.Ruler, "ruler", $"Ruler   ({Cfg.Keys.Label("ruler")})", "Drag to measure, in whatever the scenario's Distances are set to");
            case "calibrate": return ToolB(Tool.Calibrate, "calibrate", "Calibrate", CalibrateDetail());
            case "range":
            {
                var b = ToolB(Tool.Range, "range", "Ranging", "Click a piece to see how far it reaches: type the standing reach in its panel or set it with the wheel, and a piece carrying a \"range\" counter uses its own");
                Buttons["range"] = b;
                return b;
            }
            case "sighting":
            {
                var b = ToolB(Tool.Sighting, "sighting", $"POV capture   ({Cfg.Keys.Label("sighting")})", "The picture a piece would send home: from above, trace the edge of what it can see, or look out from its own eyes. The shot goes to the clipboard and the capture folder. The wheel sets its reach; Alt + wheel zooms in to trace");
                Buttons["sighting"] = b;
                return b;
            }
            case "capture": return ToolB(Tool.Capture, "capture", $"Capture   ({Cfg.Keys.Label("capture")})", $"For AARs and the like: drag a region, it goes to the clipboard and the capture folder ({Cfg.Keys.Label("captureAgain")} repeats)");
        }
        return null;
    }

    Button? OtherButton(string key)
    {
        switch (key)
        {
            case "paint": return PanelToggle("paint", "draw", "Draw", "The pen, the shapes and the rubber");
            case "decals": return PanelToggle("decals", "decal", "Decals", "Stamp pictures on the table or a piece: blood where a fight is, arrows, marks · click a stamp to lift it off");
            case "formation": return PanelToggle("formation", "formation", $"Formations   ({Cfg.Keys.Label("formations")})", "Form the selected pieces up: line, column, square, wedge and more · right-drag across the table to set the front");
            case "relief": return PanelToggle("relief", "relief", "Relief", "Raise, lower, smooth and flatten the ground, and paint grass, fields, woods, marsh, water and roads on it");
            case "boards": return PanelToggle("boards", "stack", "Layers", "The sheets of tracing paper the drawings are on: draw on one, hide it for a screenshot");
            case "dice": return PanelToggle("dice", "dice", "Dice", "Pick the dice and roll; every umpire sees the result");
            case "2d":
            {
                var b = Icon("text:3D", $"3D table   ({Cfg.Keys.Label("map2d")})", "On: the table in three dimensions. Off: the map read flat from above.");
                b.ToggleMode = true;
                b.ButtonPressed = T != null && !T.Rig.TopDown;
                b.Pressed += () => { T!.Rig.ToggleTopDown(); SyncViewButton(); };
                Btn2d = b;
                return b;
            }
            case "text":
            {
                var b = Icon("text:Aa", $"Plates behind labels   ({Cfg.Keys.Label("labelPlates")})", "A backing behind the text a piece writes beside itself, to read it on a busy map");
                b.ToggleMode = true;
                b.ButtonPressed = Cfg.LabelPlates;
                b.Toggled += on => Shell.SetLabelPlates(on);
                _text = b;
                return b;
            }
            case "compass":
            {
                var b = Icon("compass", "Compass", "Shows where north is; click it to turn the view north-up, drag it to move it, right-click it to put it back");
                b.ToggleMode = true;
                b.ButtonPressed = Cfg.CompassShown;
                b.Toggled += on =>
                {
                    Cfg.CompassShown = on;
                    Cfg.Save();
                    Shell.Relayout();
                };
                Buttons["compass"] = b;
                return b;
            }
            case "zones":
            {
                var b = Icon("zones", "Umpire zones", "Show or hide the parts of the umpire board given to each umpire");
                b.ToggleMode = true;
                b.ButtonPressed = Cfg.ShowZones;
                b.Toggled += on =>
                {
                    Cfg.ShowZones = on;
                    Cfg.Save();
                    T.ShowZones = on;
                    T.SyncZones();
                };
                return b;
            }
            case "frame":
            {
                var b = Icon("frame", $"Frame the map   ({Cfg.Keys.Label("frame")})", "Show the whole map");
                b.Pressed += () => T.FrameAll();
                return b;
            }
            case "pieces": return PanelToggle("pieces", "pieces", "Pieces", "What you can place on the table · drag one onto the ground");
            case "map": return Page("map", "map", "Map", "Map sheets: add, move, lock");
            case "layers": return Page("layers", "people", S.IsUmpire ? "Umpires" : "Table", S.IsUmpire ? "The umpire zones" : "What you see");
            case "log": return PanelToggle("log", "log", "Log", "The session transcript: who joined, rolls, turns, sheets", Cfg.LogOpen);
        }
        return null;
    }
}
