using System;
using System.Linq;
using Godot;

namespace KTable.App;

sealed class SettingsDialog : HudPart
{
    VBoxContainer _box = null!;

    readonly IHudShell Shell;
    readonly TopBar _top;
    readonly ToolStrip _strip;
    SettingsDialog(HudContext ctx, IHudShell shell, TopBar top, ToolStrip strip) : base(ctx) { Shell = shell; _top = top; _strip = strip; }

    public static void Open(HudContext ctx, IHudShell shell, TopBar top, ToolStrip strip) => new SettingsDialog(ctx, shell, top, strip).Show();

    void Show()
    {
        var dlg = new AcceptDialog { Title = "Settings", OkButtonText = "Done" };
        var pages = new DialogPages("settings", new Vector2(660, 440));
        _box = pages.Add("Accessibility"); AddAccessibility();
        _box = pages.Add("Interface"); AddInterface();
        _box = pages.Add("Table"); AddTable();
        _box = pages.Add("Graphics"); AddGraphics();
        _box = pages.Add("Sound"); AddSound();
        if (S.IsUmpire) { _box = pages.Add("Umpires"); AddUmpires(); }
        _box = pages.Add("Captures"); AddCaptures();
        _box = pages.Add("Keys"); AddKeys();
        if (AgentEndpoint.Current is { } agent) { _box = pages.Add("AI agent"); AgentPanel.Simple(_box, agent, Cfg); }
        pages.ShowFirst();
        dlg.AddChild(pages.Root);
        dlg.Confirmed += Cfg.Save;
        dlg.CloseRequested += Cfg.Save;
        Ui.Open(HudNode, dlg, new Vector2I(700, 520));
        Ui.FreeOnClose(dlg);
    }

    static HBoxContainer Row(string label, Control control, Control? value = null, bool fill = true)
    {
        var r = new HBoxContainer();
        r.AddThemeConstantOverride("separation", 10);
        r.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(150, 0), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, Modulate = new Color(1, 1, 1, 0.74f) });
        control.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        if (fill) control.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        r.AddChild(control);
        if (value != null)
        {
            value.CustomMinimumSize = new Vector2(64, 0);
            value.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            if (value is Label vl) { vl.HorizontalAlignment = HorizontalAlignment.Right; vl.AddThemeColorOverride("font_color", Ui.Muted); }
            r.AddChild(value);
        }
        return r;
    }

    static Control Option(CheckBox box)
    {
        var m = new MarginContainer();
        m.AddThemeConstantOverride("margin_left", 154);
        m.AddChild(box);
        return m;
    }

    static Control Action(Button b)
    {
        b.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        var m = new MarginContainer();
        m.AddThemeConstantOverride("margin_left", 160);
        m.AddChild(b);
        return m;
    }

    void AddAccessibility()
    {
        var size = AppSettings.SizeSlider(Cfg, () => Callable.From(Shell.Relayout).CallDeferred());
        _box.AddChild(Row("Interface size", size.Size, size.Shown));
        _box.AddChild(Option(size.Match));
        var cappedAt = new MarginContainer();
        cappedAt.AddThemeConstantOverride("margin_left", 160);
        cappedAt.AddChild(size.Capped);
        cappedAt.Visible = size.Capped.Visible;
        size.Capped.VisibilityChanged += () => cappedAt.Visible = size.Capped.Visible;
        _box.AddChild(cappedAt);
        var bar = new HSlider { MinValue = Settings.StripSizeMin, MaxValue = Settings.StripSizeMax, Step = 2, Value = Cfg.StripSize };
        var barL = new Label { Text = $"{Cfg.StripSize:0} px" };
        bar.ValueChanged += v =>
        {
            barL.Text = $"{v:0} px";
            Cfg.StripSize = (float)v; Cfg.Save();
            _strip.Rebuild(); Callable.From(Shell.Relayout).CallDeferred();
        };
        _box.AddChild(Row("Sidebar buttons", bar, barL));
    }

    void AddInterface()
    {
        _box.AddChild(Header("Top bar"));
        var topLine = new CheckBox { Text = "Say what just happened", ButtonPressed = Cfg.TopLine, TooltipText = "Off: the bar stays bare and it is written in the log instead" };
        var topW = new HSlider { MinValue = Settings.TopLineWidthMin, MaxValue = 1, Step = 0.05, Value = Cfg.TopLineWidth, TooltipText = "How much of the room between the turn buttons and the right-hand end that line may take" };
        var topWL = new Label { Text = $"{Cfg.TopLineWidth * 100:0}%" };
        var topRow = Row("Line length", topW, topWL);
        topLine.Toggled += on => { Cfg.TopLine = on; Cfg.Save(); topRow.Visible = on; _top.SyncTopLine(); };
        topW.ValueChanged += v => { topWL.Text = $"{v * 100:0}%"; Cfg.TopLineWidth = (float)v; _top.SyncTopLine(); };
        topW.DragEnded += _ => Cfg.Save();
        topRow.Visible = Cfg.TopLine;
        _box.AddChild(Option(topLine));
        _box.AddChild(topRow);

        _box.AddChild(Header("Panels"));
        _box.AddChild(Action(Btn("Put the panels back", Shell.PutPanelsBack, "The drawer's width, and every tool panel back beside its own button")));

    }

    void AddSound()
    {
        _box.AddChild(Header("Table"));
        var vol = new HSlider { MinValue = 0, MaxValue = 1, Step = 0.05, Value = Cfg.SoundVolume };
        var volL = new Label { Text = $"{Cfg.SoundVolume * 100:0}%" };
        vol.ValueChanged += v => { volL.Text = $"{v * 100:0}%"; Cfg.SoundVolume = (float)v; Sfx.Current?.Apply(); };
        vol.DragEnded += _ => Cfg.Save();
        _box.AddChild(Row("Volume", vol, volL));
        var mute = new CheckBox { Text = "Silent", ButtonPressed = Cfg.SoundMuted, TooltipText = $"{Cfg.Keys.Of("mute")} at the table" };
        mute.Toggled += on => { Cfg.SoundMuted = on; Cfg.Save(); Sfx.Current?.Apply(); };
        _box.AddChild(Option(mute));
        var behind = new CheckBox { Text = "Heard while the window is behind others", ButtonPressed = Cfg.SoundInBackground };
        behind.Toggled += on => { Cfg.SoundInBackground = on; Cfg.Save(); Sfx.Current?.Apply(); };
        _box.AddChild(Option(behind));
    }

    void AddTable()
    {
        _box.AddChild(Header("Surface"));
        var shade = new HSlider { MinValue = 0, MaxValue = 1, Step = 0.05, Value = Cfg.TableShade, TooltipText = "Squared paper … grey … black" };
        var shadeL = new Label { Text = ShadeWord(Cfg.TableShade) };
        shade.ValueChanged += v => { Cfg.TableShade = (float)v; shadeL.Text = ShadeWord((float)v); T.SetStandIn(true, (float)v); };
        _box.AddChild(Row("Table under the map", shade, shadeL));

        _box.AddChild(Header("Pieces"));
        var face = new CheckBox { Text = "Names on pieces, counters on their backs", ButtonPressed = Cfg.FaceText, TooltipText = "Off: on hover only" };
        var nameS = new HSlider { MinValue = Settings.FaceTextScaleMin, MaxValue = Settings.FaceTextScaleMax, Step = 0.05, Value = Cfg.FaceTextScale, TooltipText = "How big the names print on pieces, and the lettering an imported piece draws itself (bigger reads in captures; it may spill past the name band)" };
        var nameL = new Label { Text = $"{Cfg.FaceTextScale:0.00}×" };
        var nameRow = Row("Name size", nameS, nameL);
        nameS.ValueChanged += v => { Cfg.FaceTextScale = (float)v; nameL.Text = $"{v:0.00}×"; T.ApplyLettering(); };
        face.Toggled += on => { Cfg.FaceText = on; nameRow.Visible = on; T.ApplyLettering(); };
        nameRow.Visible = Cfg.FaceText;
        _box.AddChild(Option(face));
        _box.AddChild(nameRow);
    }

    void AddGraphics()
    {
        _box.AddChild(Header("Quality"));
        var aaPick = new OptionButton { TooltipText = "Multisampling on the 3D table. More is smoother and costs more; it takes effect at once.", CustomMinimumSize = new Vector2(160, 0) };
        foreach (var n in new[] { "Off", "2 samples", "4 samples", "8 samples" }) aaPick.AddItem(n);
        aaPick.Selected = Math.Clamp(Cfg.Antialias, 0, 3);
        aaPick.ItemSelected += i => { Cfg.Antialias = (int)i; Cfg.Save(); T.ApplyGraphics(); };
        _box.AddChild(Row("Smooth edges", aaPick, null, fill: false));
        var fine = new CheckBox { Text = "Smooth fine lines too", ButtonPressed = Cfg.SmoothFineEdges, TooltipText = "Cut-out pictures and thin drawn lines that multisampling misses. Small lettering softens a little." };
        fine.Toggled += on => { Cfg.SmoothFineEdges = on; Cfg.Save(); T.ApplyGraphics(); };
        _box.AddChild(Option(fine));
        var shadowPick = new OptionButton { TooltipText = "For scenarios whose light casts shadows. High is sharper and keeps them when zoomed far out.", CustomMinimumSize = new Vector2(160, 0) };
        foreach (var n in new[] { "Low", "Medium", "High" }) shadowPick.AddItem(n);
        shadowPick.Selected = Math.Clamp(Cfg.ShadowDetail, 0, Settings.ShadowDetailMost);
        shadowPick.ItemSelected += i => { Cfg.ShadowDetail = (int)i; Cfg.Save(); T.ApplyGraphics(); };
        _box.AddChild(Row("Shadows", shadowPick, null, fill: false));
        var contact = new CheckBox { Text = "Contact shading", ButtonPressed = Cfg.ContactShade, TooltipText = "A soft shade where pieces meet the table, so 3D models sit down on it" };
        contact.Toggled += on => { Cfg.ContactShade = on; Cfg.Save(); T.ApplyGraphics(); };
        _box.AddChild(Option(contact));

        _box.AddChild(Header("Frames"));
        var vsync = new CheckBox { Text = "Keep in step with the screen (VSync)", ButtonPressed = Cfg.VSync, TooltipText = "No tearing. Off draws as fast as the limit below allows." };
        vsync.Toggled += on => { Cfg.VSync = on; Cfg.Save(); Graphics.ApplyDisplay(Cfg); };
        _box.AddChild(Option(vsync));
        var limitPick = new OptionButton { TooltipText = "A lower limit keeps a laptop cooler and quieter", CustomMinimumSize = new Vector2(160, 0) };
        foreach (var n in Graphics.FrameLimits) limitPick.AddItem(n == 0 ? "None" : $"{n} a second");
        limitPick.Selected = Math.Max(0, Array.IndexOf(Graphics.FrameLimits, Cfg.FrameLimit));
        limitPick.ItemSelected += i => { Cfg.FrameLimit = Graphics.FrameLimits[i]; Cfg.Save(); Graphics.ApplyDisplay(Cfg); };
        _box.AddChild(Row("Frame limit", limitPick, null, fill: false));

        _box.AddChild(Header("Speed on big tables"));
        var reach = new HSlider { MinValue = Settings.ReachMin, MaxValue = Settings.ReachOff, Step = 1, ExpEdit = true, Value = Cfg.LetteringReach, TooltipText = "How far a piece keeps its names, counters and script buttons, in its own sizes. Lower is faster on a huge table. Right end: no limit" };
        var reachL = new Label();
        static string ReachWord(double v) => v >= Settings.ReachOff ? "no limit" : $"{v:0}×";
        reachL.Text = ReachWord(reach.Value);
        bool dragging = false;
        void ApplyReach()
        {
            if (Math.Abs(S.Art.Look.LetteringReach - (float)reach.Value) < 0.01f) return;
            Cfg.LetteringReach = (float)reach.Value; Cfg.Save();
            T.ApplyLettering();
        }
        reach.DragStarted += () => dragging = true;
        reach.DragEnded += _ => { dragging = false; ApplyReach(); };
        reach.ValueChanged += v => { reachL.Text = ReachWord(v); if (!dragging) ApplyReach(); };
        _box.AddChild(Row("Lettering reach", reach, reachL));
    }

    void AddUmpires()
    {
        _box.AddChild(Header("Pointers"));
        var forced = S.View.PointersAlwaysShown;
        var share = new CheckBox { Text = "Share my pointer", ButtonPressed = Cfg.ShareCursor || forced, Disabled = forced, TooltipText = forced ? PeopleList.PointerShownByHost : "" };
        share.Toggled += on => { Cfg.ShareCursor = on; _top.UpdatePointerInfo(); };
        var show = new CheckBox { Text = "Show the others' pointers", ButtonPressed = Cfg.ShowCursors };
        show.Toggled += on => { Cfg.ShowCursors = on; T.ShowCursors = on; };
        _box.AddChild(Option(share)); _box.AddChild(Option(show));
    }

    void AddCaptures()
    {
        _box.AddChild(Header("Where they go"));
        var clip = new CheckBox { Text = "Copy to the clipboard", ButtonPressed = Cfg.CaptureToClipboard };
        clip.Toggled += on => Cfg.CaptureToClipboard = on;
        var fold = new CheckBox { Text = "Save to a folder", ButtonPressed = Cfg.CaptureToFolder };
        var path = new LineEdit { Text = Cfg.CaptureFolder };
        path.TextChanged += v => Cfg.CaptureFolder = v;
        var pathBox = new HBoxContainer();
        pathBox.AddChild(path);
        path.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        pathBox.AddChild(Btn("Choose…", () => ChooseFolder(path)));
        var pathRow = Row("Folder", pathBox);
        fold.Toggled += on => { Cfg.CaptureToFolder = on; pathRow.Visible = on; };
        pathRow.Visible = Cfg.CaptureToFolder;
        _box.AddChild(Option(clip)); _box.AddChild(Option(fold)); _box.AddChild(pathRow);
        _box.AddChild(Header("Picture"));
        var resPick = new OptionButton { TooltipText = "Captures are rendered apart from the screen, at this many times its resolution: sharper maps and text in Discord", CustomMinimumSize = new Vector2(160, 0) };
        for (int kk = 1; kk <= Settings.CaptureScaleMost; kk++) resPick.AddItem($"{kk}× the screen");
        resPick.Selected = Math.Clamp(Cfg.CaptureScale, 1, Settings.CaptureScaleMost) - 1;
        resPick.ItemSelected += i => Cfg.CaptureScale = (int)i + 1;
        _box.AddChild(Row("Resolution", resPick, null, fill: false));
    }

    void ChooseFolder(LineEdit path)
    {
        var fd = new FileDialog { FileMode = FileDialog.FileModeEnum.OpenDir, Access = FileDialog.AccessEnum.Filesystem, Title = "Capture folder", UseNativeDialog = true };
        if (System.IO.Directory.Exists(path.Text)) fd.CurrentDir = path.Text;
        fd.DirSelected += d => { path.Text = d; Cfg.CaptureFolder = d; Cfg.Save(); };
        HudNode.AddChild(fd);
        Ui.FreeOnClose(fd);
        fd.PopupCentered(new Vector2I(720, 480));
    }

    void AddKeys()
    {
        var head = new HBoxContainer();
        var hint = Hint("Click a key, then press the new one. Esc, Ctrl+1–9 and the arrow keys are fixed.");
        hint.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        head.AddChild(hint);
        var kg = Ui.Form(250);
        var reset = Btn("Reset to defaults", () => { Cfg.Keys.Reset(); Cfg.Save(); foreach (var c in kg.GetChildren()) if (c is Button b && b.HasMeta("action")) b.Text = Cfg.Keys.Label(b.GetMeta("action").AsString()); });
        reset.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        head.AddChild(reset);
        _box.AddChild(head);
        Button? arming = null;
        var rows = Keymap.Actions.Select(a => (a.action, a.label)).Concat(S.View.Hotkeys.Select(h => ("script:" + h.Label, "Script: " + h.Label)));
        foreach (var (action, label) in rows)
        {
            kg.AddChild(new Label { Text = label });
            var act = action;
            var kb = new Button { Text = Cfg.Keys.Label(act), TooltipText = $"Key for {label}\nClick, then press the new key", FocusMode = Control.FocusModeEnum.All, CustomMinimumSize = new Vector2(120, 0), SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
            kb.Pressed += () => { if (arming != null && arming != kb) arming.Text = Cfg.Keys.Label(ActionOf(arming)); arming = kb; kb.Text = "press a key…"; kb.GrabFocus(); };
            kb.SetMeta("action", act);
            kb.GuiInput += e =>
            {
                if (arming != kb || e is not InputEventKey { Pressed: true } key) return;
                if (key.Keycode is Key.Escape) { kb.Text = Cfg.Keys.Label(act); arming = null; kb.AcceptEvent(); return; }
                var desc = KeyEvents.Describe(key);
                if (desc.Length == 0 || key.Keycode is Key.Enter or Key.KpEnter or Key.Space or Key.Tab) { kb.AcceptEvent(); return; }
                Cfg.Keys.Set(act, desc); Cfg.Save();
                kb.Text = Cfg.Keys.Label(act); arming = null; kb.AcceptEvent();
            };
            kg.AddChild(kb);
        }
        Ui.AlignForm(kg);
        _box.AddChild(kg);
    }

    static string ActionOf(Button b) => b.HasMeta("action") ? b.GetMeta("action").AsString() : "";

    static string ShadeWord(float v) => v < 0.1f ? "paper" : v < 0.45f ? "dim paper" : v < 0.6f ? "grey" : v < 0.95f ? "dark" : "black";
}
