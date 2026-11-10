using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Protocol;

namespace KTable.App;

public partial class Hud : CanvasLayer
{
    internal HudContext Ctx = null!;
    internal Session S => Ctx?.S!;
    internal Table T => Ctx?.T!;
    internal Net N => Ctx?.N!;
    internal Settings Cfg => Ctx?.Cfg!;

    internal Control Root { get => Ctx.Root; private set => Ctx.Root = value; }
    internal RectOverlay Rect = null!;
    internal TopBar Top = null!;
    internal ToolStrip Strip = null!;
    internal Drawer Drawer = null!;
    internal UmpiresPage Umpires = null!;
    internal MapPage Map = null!;
    internal PiecesPanel Pieces = null!;
    internal DrawPanel Paint = null!;
    internal StampPanel Stamps = null!;
    internal ReliefPanel? Relief;
    internal FormationPanel FormPanel = null!;
    internal RangePanel Ranging = null!;
    internal LayersPanel Boards = null!;
    internal PovPanel Pov = null!;
    internal LogPanel LogPanel = null!;
    internal ConsolePanel? ScriptConsole;
    internal ScriptFileWatch FileWatch = null!;
    internal MenuCards Menus = null!;
    internal DicePanel Dice = null!;
    internal Captures Captures = null!;
    internal Transcript Transcript = null!;
    internal NorthCompass Compass = null!;
    ScriptUi? _scriptUi;

    readonly List<Action> _unhook = new();

    internal void Hook<TD>(Action<TD> on, Action<TD> off, TD handler) where TD : Delegate
    {
        on(handler);
        _unhook.Add(() => off(handler));
    }

    void Unhook()
    {
        foreach (var off in _unhook) off();
        _unhook.Clear();
    }

    public void Bind(Session s, Table t, Net n, Settings cfg)
    {
        Ctx = new HudContext(s, t, n, cfg, this, Log, Say, ShowScriptWindow);
        FileWatch = new ScriptFileWatch(s, ScriptFileWatch.TableFolder(s), s.View.Name);
        AddChild(FileWatch);
        FileWatch.Noted += (text, bad) => { ScriptConsole?.Note(text, bad); if (bad) Say(text); };
        TreeExiting += Unhook;
        var picture = new TablePicture(Ctx);
        n.RequestPicture = picture.Take;
        n.PictureSince = picture.Since;
        _unhook.Add(() => { n.RequestPicture = null; n.PictureSince = null; });
        Build();
        Hook<Action>(h => S.Reset += h, h => S.Reset -= h, () =>
        {
            Pieces.RebuildPacks();
            Umpires.Rebuild();
            Map.Rebuild();
            Pov.Rebuild();
            Boards.Rebuild();
            Paint.Rebuild();
            Top.Update();
            Strip.SyncCalibrateTip();
            _scriptUi?.Rebuild();
        });
        Hook<Action<Command>>(h => S.Applied += h, h => S.Applied -= h, Applied);
        Hook<Action<string>>(h => S.Error += h, h => S.Error -= h, e => Log($"[color=#ff7060]{Fmt.Bb(e)}[/color]"));
        Transcript.RememberPeers();
        Hook<Action>(h => S.PeersChanged += h, h => S.PeersChanged -= h, () =>
        {
            Umpires.Rebuild();
            Top.UpdatePointerInfo();
            Top.SyncRole();
            Transcript.LogPeers();
            Top.UpdateKnocks();
            T.SyncZones();
            Strip.SyncUmpireButtons();
        });
        Hook<Action<Knock>>(h => S.Knocked += h, h => S.Knocked -= h, k => Log($"[color={Transcript.NameColor}]{Fmt.Bb(k.Name)}[/color] is at the door{(S.IsLead ? " · let them in above, or from the people at the top right" : "")}"));
        Hook<Action>(h => S.AccessChanged += h, h => S.AccessChanged -= h, () =>
        {
            Top.UpdateDoor();
            Top.UpdateKnocks();
            Umpires.Rebuild();
            T.SyncZones();
            Top.UpdatePointerInfo();
        });
        Hook<Action>(h => S.TurnsChanged += h, h => S.TurnsChanged -= h, Umpires.Rebuild);
        if (S.IsUmpire) N.RequestTurns();
        Hook<Action<string>>(h => N.StatusChanged += h, h => N.StatusChanged -= h, st => Top.SetStatus(st));
        Hook<Action<string?>>(h => N.SaveProblem += h, h => N.SaveProblem -= h, p =>
        {
            Top.SaveWarn.Visible = p != null;
            Top.SaveWarn.TooltipText = p ?? "";
            if (p != null) Log($"[color=#ff7060]Not saved: {Fmt.Bb(p)}. The game goes on in memory; free the disk or fix the folder before you close the table.[/color]");
            else Log("Saving works again.");
        });
        Hook<Action<string>>(h => N.SaveNote += h, h => N.SaveNote -= h, n => Log($"[color=#a09c94]{Fmt.Bb(n)}[/color]"));
        foreach (var p in Problems.Take()) Log($"[color=#ffd75e]{Fmt.Bb(p)}[/color]");
        Hook<Action<string>>(h => Problems.Told += h, h => Problems.Told -= h, p => Callable.From(() => { if (IsInstanceValid(this)) Log($"[color=#ffd75e]{Fmt.Bb(p)}[/color]"); }).CallDeferred());
        HookTable();
        Top.SetStatus(N.Status);
        Log($"[b]{Fmt.Bb(S.View.Name)}[/b] · {Fmt.Clock(S.View.Clock)}" + (S.IsHost ? "" : $" · you are {Fmt.Role(S.Me.Role)}"));
        Pieces.RebuildPacks();
        Umpires.Rebuild();
        Map.Rebuild();
        Pov.Rebuild();
        Top.Update();
        Top.UpdateKnocks();
        T.SetTool(Tool.Select);
        if (S.IsUmpire && S.View.Sheets.Count == 0)
        {
            Callable.From(() => Drawer.ShowPage("map")).CallDeferred();
            Say("Empty table: Map → Add map sheets…, or New blank ground… to shape your own, then place pieces.");
            Log("No map yet: Map → Add map sheets… (pictures, 3D terrain, heightmaps) or New blank ground…");
        }
    }

    void HookTable()
    {
        var rig = T.Rig;
        Hook<Action>(h => rig.ViewChanged += h, h => rig.ViewChanged -= h, Strip.SyncViewButton);
        Hook<Action>(h => T.SelectionChanged += h, h => T.SelectionChanged -= h, Pov.Rebuild);
        Hook<Action>(h => T.Decals.Changed += h, h => T.Decals.Changed -= h, Stamps.Rebuild);
        Hook<Action>(h => T.ToolChanged += h, h => T.ToolChanged -= h, () =>
        {
            Strip.SyncToolButtons();
            Pov.Rebuild();
            Paint.Rebuild();
            Stamps.Rebuild();
            Relief?.Rebuild();
            if (T.Tool is Tool.Draw or Tool.Shape or Tool.Erase or Tool.Letter) OpenPanel("paint");
            else ClosePanel("paint");
            if (T.Tool == Tool.Decal) OpenPanel("decals");
            else ClosePanel("decals");
            if (T.Tool == Tool.Relief) OpenPanel("relief");
            else ClosePanel("relief");
            if (T.Tool == Tool.Formation) OpenPanel("formation");
            else ClosePanel("formation");
            if (T.Tool == Tool.Range) OpenPanel("range");
            else ClosePanel("range");
            Ranging.Rebuild();
            FormPanel.Rebuild();
            Strip.SyncToggles();
        });
        Hook<Action<string>>(h => T.Hint += h, h => T.Hint -= h, Say);
        Hook<Action>(h => T.Forms.Changed += h, h => T.Forms.Changed -= h, FormPanel.Rebuild);
        Hook<Action>(h => T.Ranges.ReachChanged += h, h => T.Ranges.ReachChanged -= h, Ranging.Sync);
        Hook<Action<string, Command>>(h => T.ReliefShaped += h, h => T.ReliefShaped -= h, Map.Reliefs.Share);
        Hook<Action>(h => T.ReliefPicked += h, h => T.ReliefPicked -= h, () => Relief?.Rebuild());
        Hook<Action<Rect2?>>(h => T.ScreenRect += h, h => T.ScreenRect -= h, r => Rect.SetRect(r));
        Hook<Action<PieceNode, int>>(h => T.ContentsAsked += h, h => T.ContentsAsked -= h, (holder, most) => HolderWindow.Open(Root, S, T, holder, most));
        Hook<Action<double>>(h => T.CalibrationMeasured += h, h => T.CalibrationMeasured -= h, units => CalibrateDialog.Open(Ctx, units));
        Hook<Action>(h => T.SightingChanged += h, h => T.SightingChanged -= h, Pov.Rebuild);
        Hook<Action>(h => T.BoardsChanged += h, h => T.BoardsChanged -= h, () => { Boards.Rebuild(); Paint.Rebuild(); });
        Hook<Action>(h => T.PublishRequested += h, h => T.PublishRequested -= h, () => _ = CaptureSighting());
        Hook<Action>(h => T.CropChanged += h, h => T.CropChanged -= h, () => Map.OnCropChanged());
        Hook<Action<Rect2>>(h => T.CaptureRequested += h, h => T.CaptureRequested -= h, r => _ = Captures.Region(r));
    }

    void Applied(Command c)
    {
        Top.Update();
        if (Touches(c, x => x is SetUi { Piece: null } or SetUiAttributes { Piece: null } or SetUiPictures)) _scriptUi?.Applied(c);
        foreach (var sd in Flatten(c).OfType<ShowDialog>()) _scriptUi?.ShowDialog(sd);
        if (Touches(c, x => x is AddSheet or RemoveSheet or MoveSheet or LockSheet or RenameSheet or CropSheet or SetSheetRelief or SetSheetPicture or SetSheetOpacity)) Map.Rebuild();
        if (Touches(c, x => x is SetLighting)) Relief?.Rebuild();
        foreach (var one in Flatten(c))
        {
            if (one.Actor == 0) one.Actor = c.Actor;
            if (one is RollDice rd) { Transcript.Rolled(rd); Dice.Rolled(rd); }

            if (one is SetCalibration)
            {
                Strip.SyncCalibrateTip();
                Map.Rebuild();
                T.ApplyScale(S.View.Calibration);
            }
            if (one is SetZone or RemoveZone) Umpires.Rebuild();
            if (one is Draw or EraseStroke) Boards.Rebuild();
            if (one is SetDecalPalette or StampDecal or RemoveDecal) Stamps.Rebuild();
            if (one is AdvanceClock or SetClock or RestoreTurn or LoadSave && S.IsUmpire) N.RequestTurns();
            Transcript.Write(one);
        }
    }

    static IEnumerable<Command> Flatten(Command c) => c is Batch b ? b.Items.SelectMany(Flatten) : new[] { c };
    static bool Touches(Command c, Func<Command, bool> f) => Flatten(c).Any(f);

    public void Log(string bb) => LogPanel.Append(bb);

    public void Say(string text) => Top.SayLine(text);

    public override void _Process(double delta) => Top?.Tick();

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F1 }) HelpTags.Open(Ctx.Root, KTable.Client.Docs.KeysPage.File + "#at-the-table");
        if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F2 } && Strip.Btn2d != null) Callable.From(SyncViewButton).CallDeferred();
        if (e is InputEventKey { Pressed: true, Echo: false } k && T?.Typing != true && Cfg.Keys.Is(k, "labelPlates")) { SetLabelPlates(!Cfg.LabelPlates); GetViewport().SetInputAsHandled(); }
    }

    public void SetLabelPlates(bool on)
    {
        Cfg.LabelPlates = on;
        Cfg.Save();
        T.ShowLabelPlates(on);
        Strip.SyncToggles();
    }

    void SyncViewButton() => Strip.SyncViewButton();

    internal void ShowScriptWindow(string? select)
    {
        if (!S.IsUmpire) return;
        ScriptWindow.Open(Root, S, T, FileWatch, select ?? (T.SelectedNodes.Count() == 1 ? T.SelectedNodes.First().Piece.Id : null));
    }

    internal void CopySavedLater(string title, string what, string error) => CallDeferred(nameof(CopySaved), title, what, error);

    void CopySaved(string title, string what, string error)
    {
        if (error.Length > 0) Log($"[color=#ff7060]Could not save the copy '{Fmt.Bb(title)}': {Fmt.Bb(error)}[/color]");
        else Log($"Saved a copy as the scenario '{Fmt.Bb(title)}' ({Fmt.Bb(what)}); it is on the title screen.");
    }

    public Task CaptureSighting() => Captures.Sighting();

    public Task CaptureRect(Rect2 r) => Captures.Region(r);
}

public partial class RectOverlay : Control
{
    Rect2? _r;
    public void SetRect(Rect2? r) { _r = r; QueueRedraw(); }
    public override void _Draw()
    {
        if (_r is not { } r) return;
        DrawRect(r, new Color(Ui.Accent, 0.12f));
        DrawRect(r, new Color(Ui.AccentInk, 0.9f), false, 1.5f);
    }
}
