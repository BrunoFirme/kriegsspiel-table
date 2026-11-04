using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

public partial class Hud
{
    internal const float Gap = 4;
    PanelContainer _tip = null!;
    Label _tipText = null!;
    LoadingStrip _loading = null!;

    void MakeParts()
    {
        Top = new TopBar(Ctx, this);
        Umpires = new UmpiresPage(Ctx);
        Map = new MapPage(Ctx);
        Drawer = new Drawer(Ctx, this, Umpires, Map);
        Strip = new ToolStrip(Ctx, this, Drawer);
        Menus = new MenuCards(Ctx, Strip, () => Pieces);
        Captures = new Captures(Ctx, () => Rect);
        Transcript = new Transcript(Ctx);
    }

    void Build()
    {
        MakeParts();
        BuildRoot();
        Top.Build(Root);
        BuildStrip();
        BuildDrawer();
        BuildToolPanels();
        BuildGripAndCompass();
        BuildOverlays();
        WireTableRequests();
        Callable.From(() => { Relayout(); if (Cfg.LogOpen) OpenPanel("log"); }).CallDeferred();
    }

    void BuildRoot()
    {
        Root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Theme = Ui.Theme() };
        Root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(Root);
        Rect = new RectOverlay { MouseFilter = Control.MouseFilterEnum.Ignore };
        Rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        Root.AddChild(Rect);
    }

    void BuildStrip()
    {
        Strip.Build(Root);
        DrawPanel.RestorePen(T, Cfg);
        Strip.Rebuild();
    }

    void BuildDrawer()
    {
        Drawer.Build(Root);
        Pieces = new PiecesPanel(Ctx, this);
        Drawer.BuildPages();
    }

    void BuildToolPanels()
    {
        Dice = new DicePanel(Ctx, this) { ShowTableDice = () => { OpenPanel("pieces"); Pieces.SelectPack("std.dice"); } };
        Paint = new DrawPanel(Ctx, this);
        if (S.IsUmpire) Relief = new ReliefPanel(Ctx, this);
        FormPanel = new FormationPanel(Ctx, this);
        Boards = new LayersPanel(Ctx, MakePanel("boards", "Layers"), Paint, Strip);
        Pov = new PovPanel(Ctx, this);
        LogPanel = new LogPanel(Ctx, MakePanel("log", "Log"));
        if (S.IsUmpire) ScriptConsole = new ConsolePanel(Ctx, MakePanel("console", "Script console"));
        Paint.Rebuild();
        Boards.Rebuild();
    }

    void BuildGripAndCompass()
    {
        Drawer.BuildGrip(Root);
        Compass = new NorthCompass();
        Compass.Pressed += () => T?.Rig.FaceNorth();
        Compass.Snap = at => SnapAt(Compass, Compass.Size, at);
        Compass.Dropped += at => { Cfg.CompassPos = new[] { at.X, at.Y }; Cfg.Save(); };
        Compass.ResetAsked += () =>
        {
            Cfg.CompassPos = null;
            Cfg.Save();
            Relayout();
        };
        Root.AddChild(Compass);
        _scriptUi = new ScriptUi { Name = "ScriptUi" };
        Root.AddChild(_scriptUi);
        _scriptUi.Bind(S, N);
    }

    void BuildOverlays()
    {
        _tip = new PanelContainer { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _tip.AddThemeStyleboxOverride("panel", Ui.Panel(0.96f));
        _tipText = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
        _tip.AddChild(_tipText);
        Root.AddChild(_tip);
        _loading = new LoadingStrip();
        _loading.Track(S.Assets);
        Root.AddChild(_loading);
        Hook<Action<string?, Vector2>>(h => T.HoverChanged += h, h => T.HoverChanged -= h, (text, at) =>
        {
            _tip.Visible = text != null;
            if (text == null) return;
            if (Cfg.MakerTools && T.HoveredNode is { } hn) text += "\n" + MakerReadout(hn);
            _tipText.Text = text;
            _tip.ResetSize();
            _tip.Position = at + new Vector2(18, 18);
        });
        foreach (var p in new Control[] { Top.Panel, Strip.Panel, Drawer.Panel }) Ui.SwallowWheel(p);
    }

    static string MakerReadout(PieceNode n)
    {
        var p = n.Piece;
        return $"{p.Id}" + (p.Guid != null && p.Guid != p.Id ? $" · guid {p.Guid}" : "") + $" · {p.Def} · x {p.Pos.X:0.##}, z {p.Pos.Z:0.##} · {p.RotDeg:0.#}°"
            + (p.State > 0 ? $" · state {p.State + 1}" : "") + ((p.Script ?? n.Def?.Script) != null ? " · scripted" : "");
    }

    void WireTableRequests()
    {
        Hook<Action<List<Vec2>>>(h => T.ZoneDrawn += h, h => T.ZoneDrawn -= h, poly => ZoneDialog.Open(Ctx, poly));
        Hook<Action<Vector2, PieceNode?>>(h => T.ContextMenu += h, h => T.ContextMenu -= h, Menus.Show);
        Hook<Action<string>>(h => T.PanelRequested += h, h => T.PanelRequested -= h, what =>
        {
            if (!S.IsUmpire) return;
            if (what == "saves") { if (S.IsHost && N.Saves.RunInPlay != null) SavesWindow.Open(Ctx); }
            else if (what == "scripts") ShowScriptWindow(null);
            else if (what == "console") { TogglePanel("console"); if (Panels.TryGetValue("console", out var cp) && cp.Visible) ScriptConsole?.FocusPrompt(); }
        });
        WireFileDrops();
    }

    internal readonly Dictionary<string, FloatPanel> Panels = new();

    internal FloatPanel MakePanel(string key, string title)
    {
        var p = new FloatPanel(title) { Key = key, Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
        p.AddThemeStyleboxOverride("panel", Ui.Panel());
        p.Resizable = key is "log" or "pieces" or "console";
        p.Moved = (k, at) =>
        {
            Cfg.PanelPos[k] = new[] { at.X, at.Y };
            if (Panels.TryGetValue(k, out var it)) Cfg.PanelSize[k] = new[] { it.Size.X, it.Size.Y };
            Cfg.Save();
        };
        p.Snap = SnapPanel;
        p.Ceiling = (Top?.Panel?.Size.Y ?? 28) + Gap;
        p.Closed = () => ClosePanel(key);
        Root.AddChild(p);
        Ui.SwallowWheel(p);
        Panels[key] = p;
        return p;
    }

    internal Vector2 PanelSize(string key, float w, float h) => Ctx.PanelSize(key, w, h);

    internal void OpenPanel(string key)
    {
        if (!Panels.TryGetValue(key, out var p)) return;
        if (key == "paint" && T != null && T.Tool is not (Tool.Draw or Tool.Shape or Tool.Erase)) T.SetTool(Tool.Draw);
        if (key == "relief" && T != null && T.Tool != Tool.Relief) T.SetTool(Tool.Relief);
        if (key == "formation" && T != null && T.Tool != Tool.Formation) T.SetTool(Tool.Formation);
        p.Visible = true;
        p.Fit();
        if (Cfg.PanelPos.TryGetValue(key, out var at) && at.Length == 2) p.Position = new Vector2(at[0], at[1]);
        else
        {
            float x = Gap + Cfg.StripSize + 10 + Gap;
            if (Cfg.LeftOpen) x += Cfg.LeftWidth + Gap;
            p.Position = new Vector2(x, DefaultPanelTop(key));
        }
        Callable.From(() => { if (IsInstanceValid(p)) p.Clamp(); }).CallDeferred();
        SyncStripToggles();
    }

    float DefaultPanelTop(string key)
    {
        float top = (Top?.Panel?.Size.Y ?? 28) + Gap;
        return Strip.Buttons.TryGetValue(key, out var b) && b.Size.Y > 0 ? Math.Max(top, b.GlobalPosition.Y) : top;
    }

    internal void ClosePanel(string key)
    {
        if (!Panels.TryGetValue(key, out var p)) return;
        p.Visible = false;
        if (key == "paint" && T != null && T.Tool is Tool.Draw or Tool.Shape or Tool.Erase) T.SetTool(Tool.Select);
        if (key == "relief" && T != null && T.Tool == Tool.Relief) T.SetTool(Tool.Select);
        if (key == "formation" && T != null && T.Tool == Tool.Formation) T.SetTool(Tool.Select);
        if (key == "pieces" && T != null && T.Tool == Tool.Place) T.SetTool(Tool.Select);
        if (key == "sighting" && T != null && T.Draft != null) T.CancelSighting();
        if (key == "log") { Cfg.LogOpen = false; Cfg.Save(); }
        SyncStripToggles();
    }

    internal void TogglePanel(string key)
    {
        if (Panels.TryGetValue(key, out var p) && p.Visible) ClosePanel(key);
        else
        {
            if (key == "log") { Cfg.LogOpen = true; Cfg.Save(); }
            OpenPanel(key);
        }
    }

    internal void SyncStripToggles() => Strip.SyncToggles();

    internal void PutPanelsBack()
    {
        Cfg.LeftWidth = 300;
        Cfg.CompassPos = null;
        Cfg.PanelPos.Clear();
        Cfg.PanelSize.Clear();
        Cfg.Save();
        foreach (var p in Panels.Values) p.CustomMinimumSize = Vector2.Zero;
        LogPanel.Panel.CustomMinimumSize = new Vector2(420, 200);
        if (Panels.TryGetValue("console", out var cp)) cp.CustomMinimumSize = new Vector2(520, 240);
        Pieces.Panel.CustomMinimumSize = new Vector2(320, 420);
        foreach (var (key, p) in Panels) if (p.Visible) OpenPanel(key);
        Relayout();
    }

    const float Magnet = 14;

    Vector2 SnapPanel(FloatPanel self, Vector2 at) => SnapAt(self, self.Size, at);

    Vector2 SnapAt(Control self, Vector2 size, Vector2 at)
    {
        var vp = GetViewport().GetVisibleRect().Size;
        float top = (Top?.Panel?.Size.Y ?? 28) + Gap;
        float sidebar = Gap + Cfg.StripSize + 10 + Gap;
        var xs = new List<float> { sidebar, Gap, vp.X - Gap - size.X };
        var ys = new List<float> { top, Gap, vp.Y - Gap - size.Y };
        if (Cfg.LeftOpen) xs.Add(sidebar + Cfg.LeftWidth + Gap);
        foreach (var other in Panels.Values.Cast<Control>().Append(Compass))
        {
            if (other == self || other == null || !other.Visible) continue;
            var o = new Rect2(other.Position, other.Size);
            xs.Add(o.Position.X);
            xs.Add(o.End.X + Gap);
            xs.Add(o.Position.X - size.X - Gap);
            xs.Add(o.End.X - size.X);
            ys.Add(o.Position.Y);
            ys.Add(o.End.Y + Gap);
            ys.Add(o.Position.Y - size.Y - Gap);
            ys.Add(o.End.Y - size.Y);
        }
        float Settle(float v, List<float> lines)
        {
            float best = v, gap = Magnet;
            foreach (var l in lines)
                if (Math.Abs(l - v) < gap) { gap = Math.Abs(l - v); best = l; }
            return best;
        }
        return new Vector2(Settle(at.X, xs), Settle(at.Y, ys));
    }

    internal void Relayout()
    {
        if (Top?.Panel == null || Drawer?.Grip == null) return;
        float top = Top.Panel.Size.Y + Gap;
        Drawer.Panel.Visible = Drawer.Grip.Visible = Cfg.LeftOpen;

        float stripW = Cfg.StripSize + 10;
        float stripH = Strip.Panel.GetCombinedMinimumSize().Y;
        Ui.Place(Strip.Panel, Control.LayoutPreset.TopLeft, l: Gap, t: top, r: Gap + stripW, b: top + stripH);
        float left = Gap + stripW + Gap;
        float floor = GetViewport().GetVisibleRect().Size.Y - Gap;
        float logTop = DefaultPanelTop("log");
        float bottom = logTop > top + 200 ? Math.Min(floor, logTop - Gap) : floor;
        Ui.Place(Drawer.Panel, Control.LayoutPreset.TopLeft, l: left, t: top, r: left + Cfg.LeftWidth, b: bottom);
        Ui.Place(Drawer.Grip, Control.LayoutPreset.TopLeft, l: left + Cfg.LeftWidth, t: top, r: left + Cfg.LeftWidth + 6, b: bottom);
        PlaceCompass(top);
        foreach (var p in Panels.Values) { p.Ceiling = top; if (p.Visible) p.Clamp(); }
        SyncStripToggles();
        Top.UpdatePointerInfo();
        Top.SyncTopLine();
    }

    void PlaceCompass(float top)
    {
        Compass.Rig ??= T?.Rig;
        Compass.Visible = Cfg.CompassShown;
        var csz = Compass.CustomMinimumSize;
        if (Cfg.CompassPos is { Length: 2 } cp)
        {
            var vp = GetViewport().GetVisibleRect().Size;
            float cx = Mathf.Clamp(cp[0], 0, Mathf.Max(0, vp.X - csz.X)), cy = Mathf.Clamp(cp[1], top, Mathf.Max(top, vp.Y - csz.Y));
            Ui.Place(Compass, Control.LayoutPreset.TopLeft, l: cx, t: cy, r: cx + csz.X, b: cy + csz.Y);
        }
        else Ui.Place(Compass, Control.LayoutPreset.TopRight, l: -Gap - csz.X, t: top, r: -Gap, b: top + csz.Y);
    }
}

public partial class Grip : Control
{
    public bool Vertical;
    public event Action<float>? Dragged;
    bool _down;

    public override void _Ready()
    {
        MouseDefaultCursorShape = Vertical ? CursorShape.Hsize : CursorShape.Vsize;
        MouseFilter = MouseFilterEnum.Stop;
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left } mb) { _down = mb.Pressed; AcceptEvent(); }
        else if (e is InputEventMouseMotion mm && _down) { Dragged?.Invoke(Vertical ? mm.Relative.X : mm.Relative.Y); AcceptEvent(); }
    }

    public override void _Draw()
    {
        var c = new Color(1, 1, 1, _down ? 0.35f : 0.08f);
        if (Vertical) DrawLine(new Vector2(Size.X / 2, Size.Y * 0.45f), new Vector2(Size.X / 2, Size.Y * 0.55f), c, 2);
        else DrawLine(new Vector2(Size.X * 0.45f, Size.Y / 2), new Vector2(Size.X * 0.55f, Size.Y / 2), c, 2);
    }
}
