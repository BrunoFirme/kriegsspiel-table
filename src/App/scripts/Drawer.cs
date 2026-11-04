using System.Collections.Generic;
using Godot;

namespace KTable.App;

sealed class Drawer : HudPart
{
    public PanelContainer Panel = null!;
    Control _pages = null!;
    public Grip? Grip;
    public readonly Dictionary<string, (Button? btn, Control? page)> Pages = new();

    readonly IHudShell Shell;
    readonly UmpiresPage _umpires;
    readonly MapPage _map;
    public Drawer(HudContext ctx, IHudShell shell, UmpiresPage umpires, MapPage map) : base(ctx)
    {
        Shell = shell;
        _umpires = umpires;
        _map = map;
    }

    public void Build(Control root)
    {
        Panel = new PanelContainer();
        Panel.AddThemeStyleboxOverride("panel", Ui.Panel());
        root.AddChild(Panel);
        _pages = new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        Panel.AddChild(_pages);
    }

    public void BuildPages()
    {
        AddPage("layers", _umpires.Build());
        if (S.IsUmpire) AddPage("map", _map.Build());
        if (!Pages.ContainsKey(Cfg.LeftPage)) Cfg.LeftPage = Pages.ContainsKey("map") ? "map" : "layers";
        ShowPage(Cfg.LeftOpen ? Cfg.LeftPage : null);
    }

    public void BuildGrip(Control root)
    {
        Grip = new Grip { Vertical = true };
        Grip.Dragged += d => { Cfg.LeftWidth = Mathf.Clamp(Cfg.LeftWidth + d, 200, 700); Shell.Relayout(); };
        root.AddChild(Grip);
    }

    public void SetButton(string key, Button b) => Pages[key] = (b, Pages.TryGetValue(key, out var e) ? e.page : null);

    void AddPage(string key, Control page)
    {
        page.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        page.Visible = false;
        _pages.AddChild(page);
        Pages[key] = (Pages.TryGetValue(key, out var e) ? e.btn : null, page);
    }

    public void ShowPage(string? key)
    {
        if (key == "pieces") { Shell.TogglePanel("pieces"); return; }
        if (!S.IsUmpire && key == "map") key = "layers";
        if (key != null) Cfg.LeftPage = key;
        Cfg.LeftOpen = key != null;
        if (key != "map" && T != null)
        {
            if (T.Tool == Tool.Crop) T.CancelCrop();
            if (T.SelectedSheet != null) { T.SelectSheet(null); if (S.IsUmpire) _map.Rebuild(); }
        }
        if (key != "pieces" && T != null && T.Tool == Tool.Place) T.SetTool(Tool.Select);
        foreach (var (k, (btn, page)) in Pages)
        {
            btn?.SetPressedNoSignal(k == key);
            if (page != null) page.Visible = k == key;
        }
        Cfg.Save();
        Shell.Relayout();
    }
}
