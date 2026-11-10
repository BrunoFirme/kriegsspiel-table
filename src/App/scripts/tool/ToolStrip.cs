using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core;

namespace KTable.App;

sealed record StripItem(string Key, int Group, string Name, string Hotkey = "", bool Fixed = false);

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
        OffersMenu(Panel);
    }

    public static readonly StripItem[] Catalog =
    {
        new("map", 0, "Map", Fixed: true), new("calibrate", 0, "Calibrate"), new("pieces", 0, "Pieces", Fixed: true), new("dice", 0, "Dice"),
        new("layers", 6, "Umpires", Fixed: true), new("zones", 6, "Umpire zones"),
        new("select", 1, "Select", "Esc", Fixed: true), new("gizmo", 1, "Turn / resize", "gizmo"), new("formation", 1, "Formations", "formations"),
        new("ruler", 2, "Ruler", "ruler"), new("range", 2, "Ranging"),
        new("boards", 3, "Layers"), new("paint", 3, "Draw"), new("decals", 3, "Decals"), new("relief", 3, "Relief"),
        new("sighting", 4, "POV capture", "sighting"), new("capture", 4, "Capture", "capture"),
        new("2d", 5, "3D table", "map2d"), new("text", 5, "Plates behind labels", "labelPlates"), new("frame", 5, "Frame the map", "frame"), new("compass", 5, "Compass"),
        new("log", 5, "Log", Fixed: true),
    };

    string Title(StripItem item)
    {
        var key = Cfg.Keys.Label(item.Hotkey) is { Length: > 0 } bound ? bound : item.Hotkey;
        return key.Length > 0 ? $"{item.Name}   ({key})" : item.Name;
    }

    public bool Shown(StripItem item) => item.Fixed || !Cfg.StripHidden.Contains(item.Key);

    public void Show(StripItem item, bool on)
    {
        Cfg.StripHidden.Remove(item.Key);
        if (!on) Cfg.StripHidden.Add(item.Key);
        Cfg.Save();
        Rebuild();
        Callable.From(Shell.Relayout).CallDeferred();
    }

    public void FillMenu(PopupMenu menu)
    {
        menu.Clear();
        menu.AddSeparator("Sidebar buttons");
        int last = -1;
        for (int n = 0; n < Catalog.Length; n++)
        {
            var item = Catalog[n];
            if (!StripAllowed(item.Key)) continue;
            if (last >= 0 && item.Group != last) menu.AddSeparator();
            last = item.Group;
            menu.AddCheckItem(Title(item), n);
            menu.SetItemChecked(menu.ItemCount - 1, Shown(item));
            menu.SetItemDisabled(menu.ItemCount - 1, item.Fixed);
        }
    }

    public PopupMenu NewMenu()
    {
        var menu = new PopupMenu { HideOnCheckableItemSelection = false };
        FillMenu(menu);
        menu.IdPressed += id =>
        {
            var item = Catalog[id];
            Show(item, !Shown(item));
            menu.SetItemChecked(menu.GetItemIndex((int)id), Shown(item));
        };
        return menu;
    }

    void OffersMenu(Control c) => c.GuiInput += e =>
    {
        if (e is not InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } click) return;
        c.AcceptEvent();
        var menu = NewMenu();
        menu.PopupHide += () => menu.QueueFree();
        Ctx.Root.AddChild(menu);
        menu.Popup(new Rect2I((Vector2I)click.GlobalPosition.Round(), Vector2I.Zero));
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
        Btn2d = _text = null;
        _drawer.ForgetButtons();
        int last = -1;
        foreach (var item in Catalog)
        {
            if (!StripAllowed(item.Key) || !Shown(item)) continue;
            var b = ToolButton(item.Key, Title(item)) ?? OtherButton(item.Key, Title(item));
            if (b == null) continue;
            HelpTags.Tag(b, "umpire-guide.md#" + HelpAnchor(item.Key));
            OffersMenu(b);
            if (last >= 0 && item.Group != last) _strip.AddChild(new HSeparator { MouseFilter = Control.MouseFilterEnum.Pass });
            last = item.Group;
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

    Button? ToolButton(string key, string title)
    {
        switch (key)
        {
            case "select": return ToolB(Tool.Select, "select", title, "Drag a piece to pick it up; drag the empty table to box-select");
            case "gizmo": return ToolB(Tool.Transform, "gizmo", title, "A ring around the selection: drag the round knob to turn, the square knob to resize");
            case "ruler": return ToolB(Tool.Ruler, "ruler", title, "Drag to measure, in whatever the scenario's Distances are set to");
            case "calibrate": return ToolB(Tool.Calibrate, "calibrate", title, CalibrateDetail());
            case "range":
            {
                var b = ToolB(Tool.Range, "range", title, "Click a piece to see how far it reaches: type the standing reach in its panel or set it with the wheel, and a piece carrying a \"range\" counter uses its own");
                Buttons["range"] = b;
                return b;
            }
            case "sighting":
            {
                var b = ToolB(Tool.Sighting, "sighting", title, "The picture a piece would send home: from above, trace the edge of what it can see, or look out from its own eyes. The shot goes to the clipboard and the capture folder. Ctrl + wheel or = − set its reach; the wheel zooms in to trace");
                Buttons["sighting"] = b;
                return b;
            }
            case "capture": return ToolB(Tool.Capture, "capture", title, $"For AARs and the like: drag a region, it goes to the clipboard and the capture folder ({Cfg.Keys.Label("captureAgain")} repeats)");
        }
        return null;
    }

    Button? OtherButton(string key, string title)
    {
        switch (key)
        {
            case "paint": return PanelToggle("paint", "draw", title, "The pen, the shapes and the rubber");
            case "decals": return PanelToggle("decals", "decal", title, "Stamp pictures on the table or a piece: blood where a fight is, arrows, marks · click a stamp to lift it off");
            case "formation": return PanelToggle("formation", "formation", title, "Form the selected pieces up: line, column, square, wedge and more · right-drag across the table to set the front");
            case "relief": return PanelToggle("relief", "relief", title, "Raise, lower, smooth and flatten the ground, and paint grass, fields, woods, marsh, water and roads on it");
            case "boards": return PanelToggle("boards", "stack", title, "The sheets of tracing paper the drawings are on: draw on one, hide it for a screenshot");
            case "dice": return PanelToggle("dice", "dice", title, "Pick the dice and roll; every umpire sees the result");
            case "2d":
            {
                var b = Icon("text:3D", title, "On: the table in three dimensions. Off: the map read flat from above.");
                b.ToggleMode = true;
                b.ButtonPressed = T != null && !T.Rig.TopDown;
                b.Pressed += () => { T!.Rig.ToggleTopDown(); SyncViewButton(); };
                Btn2d = b;
                return b;
            }
            case "text":
            {
                var b = Icon("text:Aa", title, "A backing behind the text a piece writes beside itself, to read it on a busy map");
                b.ToggleMode = true;
                b.ButtonPressed = Cfg.LabelPlates;
                b.Toggled += on => Shell.SetLabelPlates(on);
                _text = b;
                return b;
            }
            case "compass":
            {
                var b = Icon("compass", title, "Shows where north is; click it to turn the view north-up, drag it to move it, right-click it to put it back");
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
                var b = Icon("zones", title, "Show or hide the parts of the umpire board given to each umpire");
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
                var b = Icon("frame", title, "Show the whole map");
                b.Pressed += () => T.FrameAll();
                return b;
            }
            case "pieces": return PanelToggle("pieces", "pieces", title, "What you can place on the table · drag one onto the ground");
            case "map": return Page("map", "map", title, "Map sheets: add, move, lock");
            case "layers": return Page("layers", "people", title, "The umpire zones");
            case "log": return PanelToggle("log", "log", title, "The session transcript: who joined, rolls, turns, sheets", Cfg.LogOpen);
        }
        return null;
    }
}
