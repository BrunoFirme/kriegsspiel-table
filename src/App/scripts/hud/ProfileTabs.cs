using System;
using System.Collections.Generic;
using Godot;
using KTable.Client.Prefs;

namespace KTable.App;

sealed class ProfileTabs : HudPart
{
    readonly MarginContainer _strip;
    readonly HBoxContainer _row;
    readonly Action _changed;
    readonly bool _boxes;
    readonly List<Button> _tabs = new();

    const int TabGap = 2, Corner = 4, PadX = 10, PadY = 3, Inset = 6, FontSize = 12, NameWidth = 120;
    const float RestAlpha = 0.82f;
    const long SaveHere = 0, RenameIt = 1, DeleteIt = 2;

    public ProfileTabs(HudContext ctx, FloatPanel panel, Action changed, bool boxes = false) : base(ctx)
    {
        _changed = changed;
        _boxes = boxes;
        _strip = new MarginContainer { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _strip.AddThemeConstantOverride("margin_left", Ui.Px(Inset));
        _row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _row.AddThemeConstantOverride("separation", Ui.Px(TabGap));
        _strip.AddChild(_row);
        panel.Crown(_strip);
    }

    string? Scenario => S.View.ScenarioId is { Length: > 0 } id ? id : null;

    string Word => _boxes ? "Map box profile" : "POV profile";

    CaptureProfile? Now() => _boxes ? T.Box.Profile() : T.CaptureProfile();

    void Apply(CaptureProfile p)
    {
        if (_boxes) T.Box.Take(p);
        else T.TakeCaptureProfile(p);
    }

    IReadOnlyList<CaptureProfile> Listed(string scenario) => CaptureProfiles.Of(Cfg, scenario, _boxes);

    public void Rebuild(bool shown = true)
    {
        foreach (var c in _row.GetChildren()) { _row.RemoveChild(c); c.QueueFree(); }
        _tabs.Clear();
        if (!shown || Scenario is not { } scenario)
        {
            _strip.Visible = false;
            return;
        }
        var list = Listed(scenario);
        for (int i = 0; i < list.Count; i++) _row.AddChild(Tab(list[i], i));
        if (list.Count < CaptureProfiles.Most) _row.AddChild(AddTab());
        _strip.Visible = true;
        _strip.ResetSize();
        Sync();
    }

    public void Sync()
    {
        if (Scenario is not { } scenario || _tabs.Count == 0) return;
        int lit = Now() is { } now ? CaptureProfiles.Lit(Listed(scenario), now) : -1;
        for (int i = 0; i < _tabs.Count; i++) if (GodotObject.IsInstanceValid(_tabs[i])) _tabs[i].SetPressedNoSignal(i == lit);
    }

    Button Tab(CaptureProfile p, int index)
    {
        var b = Styled(new Button { Text = p.Name, ToggleMode = true, TooltipText = $"{Summary(p)}\nRight-click to save over, rename or delete" });
        b.Pressed += () => Take(index);
        b.GuiInput += e =>
        {
            if (e is not InputEventMouseButton { Pressed: true } mb) return;
            if (mb.ButtonIndex == MouseButton.Right) { b.AcceptEvent(); Menu(index, mb.GlobalPosition); }
            else if (mb is { ButtonIndex: MouseButton.Left, DoubleClick: true }) { b.AcceptEvent(); NameTab(index); }
        };
        _tabs.Add(b);
        return b;
    }

    Button AddTab()
    {
        var b = Styled(new Button { Text = "+", TooltipText = _boxes ? "Keep this map box as a profile for this scenario" : "Save these POV settings as a profile for this scenario" });
        b.Pressed += Add;
        return b;
    }

    static Button Styled(Button b)
    {
        b.FocusMode = Control.FocusModeEnum.None;
        b.AddThemeFontSizeOverride("font_size", Ui.Px(FontSize));
        b.AddThemeStyleboxOverride("normal", TabBox(new Color(Ui.Surface, RestAlpha)));
        b.AddThemeStyleboxOverride("hover", TabBox(Ui.Raised));
        b.AddThemeStyleboxOverride("pressed", TabBox(Ui.Overlay));
        b.AddThemeStyleboxOverride("hover_pressed", TabBox(Ui.Overlay));
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        b.AddThemeColorOverride("font_color", Ui.Muted);
        b.AddThemeColorOverride("font_hover_color", Ui.Ink);
        b.AddThemeColorOverride("font_pressed_color", Ui.Ink);
        b.AddThemeColorOverride("font_hover_pressed_color", Ui.Ink);
        return b;
    }

    static StyleBoxFlat TabBox(Color bg) => Ui.Scaled(new StyleBoxFlat
    {
        BgColor = bg, CornerRadiusTopLeft = Corner, CornerRadiusTopRight = Corner,
        ContentMarginLeft = PadX, ContentMarginRight = PadX, ContentMarginTop = PadY, ContentMarginBottom = PadY,
    });

    string Summary(CaptureProfile p)
    {
        if (p.Box is [var x, var z, var wide, var deep, var pixels]) return $"{wide:0.##} × {deep:0.##} u at X {x:0.##}, Y {z:0.##} · {pixels:0} px";
        var cal = S.View.Calibration;
        var reach = cal.UnitsPerMeter is > 0 ? $"{cal.FromTableUnits(p.Radius):#,0} {cal.UnitName}" : $"{p.Radius:0.0} u";
        var pov = p.FirstPerson ? $"first person, {p.Lens:0}°" : $"2D {p.Format}{(p.Viewshed ? " viewshed" : "")}, {reach}";
        var shot = p.SightingGif ? "GIF" : "picture";
        var text = p.HideText ? "text hidden" : "text shown";
        var size = p.SightingGif && p.GifLarge ? " large" : "";
        return $"{pov} · {shot}{size} · {text}";
    }

    void Take(int index)
    {
        if (Scenario is not { } scenario || index >= Listed(scenario).Count) return;
        var p = Listed(scenario)[index];
        Apply(p);
        T.RefreshHint();
        Say($"{Word} {p.Name}");
        _changed();
    }

    void Add()
    {
        if (Scenario is not { } scenario || Now() is not { } now || CaptureProfiles.Add(Cfg, scenario, now, _boxes) is not { } made) return;
        Cfg.Save();
        Rebuild();
        NameTab(Listed(scenario).Count - 1, made.Name);
    }

    void Menu(int index, Vector2 at)
    {
        var pm = new PopupMenu();
        pm.AddItem("Save these settings here", (int)SaveHere);
        pm.AddItem("Rename", (int)RenameIt);
        pm.AddItem("Delete", (int)DeleteIt);
        pm.IdPressed += id =>
        {
            if (id == SaveHere) SaveOver(index);
            else if (id == RenameIt) NameTab(index);
            else if (id == DeleteIt) Delete(index);
        };
        pm.PopupHide += () => pm.QueueFree();
        Ctx.Root.AddChild(pm);
        pm.Popup(new Rect2I((Vector2I)at.Round(), Vector2I.Zero));
    }

    void SaveOver(int index)
    {
        if (Scenario is not { } scenario || Now() is not { } now || !CaptureProfiles.Keep(Cfg, scenario, index, now, _boxes)) return;
        Cfg.Save();
        Say($"{Word} {Listed(scenario)[index].Name} saved");
        Rebuild();
    }

    void Delete(int index)
    {
        if (Scenario is not { } scenario || index >= Listed(scenario).Count) return;
        var name = Listed(scenario)[index].Name;
        Ui.Confirm(HudNode, "Delete profile", "Delete", () =>
        {
            if (!CaptureProfiles.Remove(Cfg, scenario, index, _boxes)) return;
            Cfg.Save();
            Rebuild();
        }, $"Delete the {Word.ToLowerInvariant()} \"{name}\"?");
    }

    void NameTab(int index, string? fresh = null)
    {
        if (Scenario is not { } scenario || index >= _tabs.Count || !GodotObject.IsInstanceValid(_tabs[index])) return;
        var tab = _tabs[index];
        var was = Listed(scenario)[index].Name;
        var edit = new LineEdit
        {
            Text = was, MaxLength = CaptureProfiles.LongestName, CustomMinimumSize = new Vector2(Math.Max(Ui.Px(NameWidth), tab.Size.X), 0),
            SelectAllOnFocus = true, ContextMenuEnabled = false,
        };
        edit.AddThemeFontSizeOverride("font_size", Ui.Px(FontSize));
        edit.AddThemeStyleboxOverride("normal", TabBox(Ui.Overlay));
        edit.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        bool done = false;
        void Finish(bool keep)
        {
            if (done) return;
            done = true;
            if (keep && edit.Text.Trim() != was)
            {
                if (CaptureProfiles.Rename(Cfg, scenario, index, edit.Text, _boxes)) Cfg.Save();
                else if (CaptureProfiles.Tidy(edit.Text).Length > 0) Say($"A {Word.ToLowerInvariant()} is already called {CaptureProfiles.Tidy(edit.Text)}");
            }
            Callable.From(() => Rebuild()).CallDeferred();
        }
        edit.TextSubmitted += _ => Finish(true);
        edit.FocusExited += () => Finish(true);
        edit.GuiInput += e =>
        {
            if (e is InputEventKey { Pressed: true, Keycode: Key.Escape })
            {
                edit.AcceptEvent();
                Finish(false);
            }
        };
        int at = tab.GetIndex();
        _row.RemoveChild(tab);
        tab.QueueFree();
        _row.AddChild(edit);
        _row.MoveChild(edit, at);
        edit.GrabFocus();
        if (fresh != null) Say($"POV profile {fresh} saved · type a name, Enter keeps it");
    }
}
