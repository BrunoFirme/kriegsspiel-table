using System;
using System.Linq;
using Godot;
using KTable.Core;
using KTable.Core.Protocol;

namespace KTable.App;

sealed class TopBar : HudPart
{
    public PanelContainer Panel = null!;
    public MenuButton? MenuButton;
    public Label Role = null!, Title = null!;
    public Button ClockBtn = null!, NextTurnBtn = null!, BackNow = null!, PrevTurn = null!, NextRev = null!;
    public Button? ScenarioBtn;
    public Label? Said;
    public Control SaidGap = null!;
    ulong _saidAt;
    public Label SaveWarn = null!;
    public VBoxContainer? Knocks;
    public PeopleList? People;
    ulong _turnEndedAt;

    readonly IHudShell Shell;
    public TopBar(HudContext ctx, IHudShell shell) : base(ctx) { Shell = shell; }

    public void Build(Control root)
    {
        Panel = new PanelContainer();
        var topStyle = Ui.TopBar();
        topStyle.ContentMarginLeft = Hud.Gap + 3;
        Panel.AddThemeStyleboxOverride("panel", topStyle);
        Panel.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        root.AddChild(Panel);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Gap.Group);
        Panel.AddChild(row);
        row.AddChild(MenuButton = new MainMenu(Ctx, Shell).Build());
        SaidGap = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        Shell.Hook<Action>(h => T.MenuRequested += h, h => T.MenuRequested -= h, () => { if (GodotObject.IsInstanceValid(MenuButton)) MenuButton!.ShowPopup(); });
        row.AddChild(new VSeparator());
        Role = new Label { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, MaxLinesVisible = 1 };
        Role.AddThemeFontSizeOverride("font_size", 12);
        row.AddChild(Role);
        Title =
            new Label { Modulate = Ui.Muted, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                CustomMinimumSize = new Vector2(236, 0), SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin, MouseFilter = Control.MouseFilterEnum.Ignore, MaxLinesVisible = 1 };
        if (S.IsUmpire) row.AddChild(BuildScenarioButton());
        else row.AddChild(Title);
        row.AddChild(new VSeparator());
        BuildClockAndTurns(row);
        BuildRightEnd(row);
        Panel.Resized += Shell.Relayout;
        Knocks = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        Knocks.AddThemeConstantOverride("separation", Ui.Gap.Row);
        Knocks.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        Knocks.OffsetLeft = 360;
        Knocks.OffsetRight = -12;
        Knocks.OffsetTop = 40;
        Knocks.OffsetBottom = 40;
        root.AddChild(Knocks);
    }

    Button BuildScenarioButton()
    {
        ScenarioBtn =
            new Button { FocusMode = Control.FocusModeEnum.None, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, MouseDefaultCursorShape = Control.CursorShape.PointingHand,
                TooltipText = "Scenario properties: clock, lighting, scale, wind, distances, script" };
        ScenarioBtn.AddThemeStyleboxOverride("normal", Ui.Pill(0.07f));
        ScenarioBtn.AddThemeStyleboxOverride("hover", Ui.Pill(0.14f));
        ScenarioBtn.AddThemeStyleboxOverride("pressed", Ui.Pill(0.2f));
        ScenarioBtn.AddThemeStyleboxOverride("disabled", Ui.Pill(0.04f));
        var inner = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        inner.AddThemeConstantOverride("separation", Ui.Gap.Row);
        inner.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        inner.OffsetLeft = 8;
        inner.OffsetRight = -10;
        var mark = new Control { CustomMinimumSize = new Vector2(18, 18), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, MouseFilter = Control.MouseFilterEnum.Ignore };
        mark.AddChild(new ToolIcon { Kind = "sliders", Color = Ui.Muted });
        inner.AddChild(mark);
        inner.AddChild(Title);
        ScenarioBtn.AddChild(inner);
        ScenarioBtn.Pressed += () => ScenarioDialog.Open(Ctx);
        return ScenarioBtn;
    }

    void BuildClockAndTurns(HBoxContainer row)
    {
        ClockBtn = new Button { Flat = true, FocusMode = Control.FocusModeEnum.None, TooltipText = S.IsUmpire ? "Set the turn, the time and how long a turn lasts" : "Game clock" };
        ClockBtn.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        ClockBtn.Pressed += () => { if (S.IsUmpire) ClockDialog.Open(Ctx); };
        row.AddChild(ClockBtn);
        NextTurnBtn = Ui.Primary("Next turn ▸", NextTurn, "Advance the game clock one turn; the table as this turn ended is kept in the turn history");
        row.AddChild(NextTurnBtn);
        PrevTurn = Btn("◂ Turn", () => { if (S.ReviewInfo is { IsTurn: true } ri && ri.Index > 0) N.RequestTurn(ri.Index - 1); }, "The turn before this one");
        NextRev = Btn("Turn ▸", () => { if (S.ReviewInfo is { IsTurn: true } ri) { if (ri.Index + 1 < S.Turns.Count) N.RequestTurn(ri.Index + 1); else S.Review(null, null); } }, "The next turn (after the last one: back to now)");
        BackNow = Btn("Back to now", () => S.Review(null, null), "Return to the live table");
        PrevTurn.Visible = NextRev.Visible = BackNow.Visible = false;
        row.AddChild(PrevTurn);
        row.AddChild(NextRev);
        row.AddChild(BackNow);
        if (S.IsUmpire)
        {
            var history = new Button { Text = "History", Flat = true, FocusMode = Control.FocusModeEnum.None, TooltipText = "The turns so far: look at how any of them ended" };
            history.AddThemeFontSizeOverride("font_size", 12);
            history.AddThemeColorOverride("font_color", Ui.Muted);
            history.Pressed += () => ShowHistory(history);
            row.AddChild(history);
        }
    }

    void ShowHistory(Control at)
    {
        var pm = new PopupMenu();
        if (S.Turns.Count == 0) { pm.AddItem("No turn has ended yet", 0); pm.SetItemDisabled(0, true); }
        foreach (var ti in S.Turns.AsEnumerable().Reverse())
        {
            pm.AddRadioCheckItem($"Turn {ti.Turn} · {UmpiresPage.ShortClockText(ti.GameTime)} · {ti.Pieces} pcs · ended {UmpiresPage.LocalTime(ti.EndedAt)}", 100 + ti.Index);
            pm.SetItemChecked(pm.ItemCount - 1, S.Reviewing && S.ReviewInfo?.Index == ti.Index);
        }
        if (S.Reviewing)
        {
            pm.AddSeparator();
            pm.AddItem("Back to now", 1);
            if (S.IsHost && N.Saves.RunInPlay != null && S.ReviewInfo is { IsTurn: true } rv) pm.AddItem($"Load the end of turn {rv.Turn}…", 2);
        }
        if (S.IsHost && N.Saves.RunInPlay != null)
        {
            pm.AddSeparator();
            pm.AddItem($"Saves…   ({Cfg.Keys.Label("saves")})", 3);
        }
        pm.IdPressed += id =>
        {
            if (id == 1) S.Review(null, null);
            else if (id == 2 && S.ReviewInfo is { IsTurn: true } rv) SavesWindow.LoadTurn(Ctx, rv);
            else if (id == 3) SavesWindow.Open(Ctx);
            else if (id >= 100)
            {
                int idx = (int)id - 100;
                if (S.Reviewing && S.ReviewInfo?.Index == idx) S.Review(null, null);
                else N.RequestTurn(idx);
            }
        };
        HudNode.AddChild(pm);
        pm.PopupHide += () => pm.QueueFree();
        var r = at.GetGlobalRect();
        pm.Popup(new Rect2I((int)r.Position.X, (int)(r.Position.Y + r.Size.Y + 2), 0, 0));
    }

    void BuildRightEnd(HBoxContainer row)
    {
        Said = new Label
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, MaxLinesVisible = 1,
            HorizontalAlignment = HorizontalAlignment.Left, Modulate = Ui.Muted, MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        Said.AddThemeFontSizeOverride("font_size", 12);
        row.AddChild(Said);
        row.AddChild(SaidGap);
        SaveWarn = new Label { Text = "NOT SAVED", Visible = false, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, MouseFilter = Control.MouseFilterEnum.Stop, MaxLinesVisible = 1 };
        SaveWarn.AddThemeFontSizeOverride("font_size", 12);
        SaveWarn.AddThemeColorOverride("font_color", Ui.Danger);
        row.AddChild(SaveWarn);
        People = new PeopleList(Ctx);
        if (AgentEndpoint.Current is { } agent) row.AddChild(AgentPill.Make(agent, () => { DialogPages.LastPage["settings"] = "AI agent"; Shell.ShowSettings(); }));
        row.AddChild(People.Pill());
    }

    public void SetStatus(string text) => People?.SetStatus(text);

    public void Update()
    {
        ClockBtn.Text = Fmt.Clock(S.View.Clock);
        NextTurnBtn.Visible = S.IsUmpire && !S.Reviewing;
        BackNow.Visible = S.Reviewing;
        PrevTurn.Visible = NextRev.Visible = S.ReviewInfo is { IsTurn: true };
        PrevTurn.Disabled = !(S.ReviewInfo?.Index > 0);
        Title.Text = Fmt.OneLine(S.ReviewInfo is { SaveName: { } saveName } ? $"{saveName.ToUpperInvariant()}  ·  read-only"
            : S.Reviewing ? $"TURN {S.ReviewInfo?.Turn} AS IT ENDED  ·  read-only" : S.View.Name);
        Title.CustomMinimumSize = new Vector2(Mathf.Min(236, Title.GetThemeFont("font").GetStringSize(Title.Text, HorizontalAlignment.Left, -1, Title.GetThemeFontSize("font_size")).X + 6), 0);
        Title.Modulate = S.Reviewing ? Ui.AccentInk : Colors.White;
        if (ScenarioBtn != null)
        {
            ScenarioBtn.CustomMinimumSize = new Vector2(Title.CustomMinimumSize.X + 42, 26);
            ScenarioBtn.Disabled = S.Reviewing;
        }
        UpdatePointerInfo();
        UpdateDoor();
        SyncRole();
    }

    public void SyncRole()
    {
        Role.Text = S.Peers.Count > 1 ? $"{Fmt.RoleCaps(S.Me.Role)}  {S.Me.Name}" : S.Me.Name;
        Role.AddThemeColorOverride("font_color", S.IsUmpire ? Ui.Muted : new Color(0.7f, 0.8f, 0.95f));
    }

    public void UpdateDoor() { }

    public void UpdatePointerInfo() => People?.Sync();

    public void UpdateKnocks()
    {
        if (Knocks == null) return;
        foreach (var c in Knocks.GetChildren()) c.QueueFree();
        Knocks.OffsetTop = Panel.Size.Y + 8;
        Knocks.OffsetBottom = Knocks.OffsetTop;
        if (!S.IsLead) return;
        foreach (var w in S.Waiting)
        {
            var card = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd };
            card.AddThemeStyleboxOverride("panel", Ui.TopBar());
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", Ui.Gap.Row);
            row.AddChild(new Label { Text = $"{w.Name} wants to join", SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
            foreach (var b in PeopleList.AdmitButtons(S, N, w)) row.AddChild(b);
            card.AddChild(row);
            Knocks.AddChild(card);
        }
    }

    public void SyncTopLine()
    {
        if (MenuButton == null || Said == null) return;
        MenuButton.CustomMinimumSize = new Vector2(Cfg.StripSize + 4, 0);
        Said.Visible = Cfg.TopLine;
        if (!Cfg.TopLine) Said.Text = "";
        float p = Mathf.Clamp(Cfg.TopLineWidth, 0.25f, 1f);
        Said.SizeFlagsStretchRatio = p;
        SaidGap.SizeFlagsStretchRatio = Mathf.Max(0.0001f, 1f - p);
    }

    public void SayLine(string text)
    {
        if (!Cfg.TopLine)
        {
            if (text.Length > 0) Log($"[color=#8a8f98]{Fmt.Bb(text)}[/color]");
            return;
        }
        Said!.Text = Fmt.OneLine(text);
        _saidAt = Time.GetTicksMsec();
    }

    public void Tick()
    {
        if (Said is { Text.Length: > 0 } && Time.GetTicksMsec() - _saidAt > 12000) Said.Text = "";
    }

    void NextTurn()
    {
        var now = Time.GetTicksMsec();
        if (_turnEndedAt != 0 && now - _turnEndedAt < 1000) return;
        _turnEndedAt = now;
        S.Do(new AdvanceClock());
    }
}
