using System;
using System.Linq;
using Godot;

namespace KTable.App;

sealed class SettingsDialog : HudPart
{
    const int KeyLabels = 272;
    VBoxContainer _box = null!;

    readonly IHudShell Shell;
    readonly TopBar _top;
    readonly ToolStrip _strip;
    SettingsDialog(HudContext ctx, IHudShell shell, TopBar top, ToolStrip strip) : base(ctx)
    {
        Shell = shell;
        _top = top;
        _strip = strip;
    }

    public static void Open(HudContext ctx, IHudShell shell, TopBar top, ToolStrip strip) => new SettingsDialog(ctx, shell, top, strip).Show();

    void Show()
    {
        var dlg = new AcceptDialog { Title = "Settings", OkButtonText = "Done" };
        var pages = new DialogPages("settings", new Vector2(660, 440));
        _box = pages.Add("Interface");
        AddInterface();
        _box = pages.Add("Table");
        AddTable();
        _box = pages.Add("Graphics");
        AddGraphics();
        _box = pages.Add("Sound");
        AddSound();
        _box = pages.Add("Captures");
        AddCaptures();
        _box = pages.Add("Keys");
        AddKeys();
        if (AgentEndpoint.Current is { } agent)
        {
            _box = pages.Add("AI agent");
            AgentPanel.Simple(_box, agent, Cfg);
        }
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
        r.AddThemeConstantOverride("separation", Ui.Gap.Group);
        r.AddChild(Ui.RowLabel(label, Ui.DialogLabels));
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

    static Control Option(CheckBox box) => Ui.UnderControls(box, Ui.DialogLabels);

    static Control Action(Button b) => Ui.UnderControls(b, Ui.DialogLabels);

    void AddInterface()
    {
        _box.AddChild(Header("Size"));
        _box.AddChild(Action(Ui.Btn("Show the tour again", () => { Tour.Restart(Cfg); if (Ctx.Node is Hud hud) Tour.AtTable(hud, Cfg); },
            "The table's part of the first-run tour, ending in Help; the title screen shows the rest")));
        var size = AppSettings.SizeSlider(Cfg, () => Callable.From(Shell.Relayout).CallDeferred());
        _box.AddChild(Row("Interface size", size.Size, size.Shown));
        var cappedAt = Ui.UnderControls(size.Capped, Ui.DialogLabels);
        cappedAt.Visible = size.Capped.Visible;
        size.Capped.VisibilityChanged += () => cappedAt.Visible = size.Capped.Visible;
        _box.AddChild(cappedAt);
        var bar = new HSlider { MinValue = Settings.StripSizeMin, MaxValue = Settings.StripSizeMax, Step = 2, Value = Cfg.StripSize };
        var barL = Ui.ValueBox(bar, v => $"{v:0} px");
        bar.ValueChanged += v =>
        {
            Cfg.StripSize = (float)v;
            Cfg.Save();
            _strip.Rebuild();
            Callable.From(Shell.Relayout).CallDeferred();
        };
        _box.AddChild(Row("Sidebar buttons", bar, barL));
        _box.AddChild(Row("Window", AppSettings.WindowPick(Cfg), null, fill: false));
        AppSettings.AddTint(_box, Cfg);
        AppSettings.AddFileBrowser(_box, Cfg);

        _box.AddChild(Header("Layout"));
        var topLine = new CheckBox { Text = "Top bar says what just happened", ButtonPressed = Cfg.TopLine, TooltipText = "Off: the bar stays bare and it is written in the log instead" };
        var topW = new HSlider
        {
            MinValue = Settings.TopLineWidthMin, MaxValue = 1, Step = 0.05, Value = Cfg.TopLineWidth,
            TooltipText = "How much of the room between the turn buttons and the right-hand end that line may take",
        };
        var topWL = Ui.ValueBox(topW, v => $"{v * 100:0}%");
        var topRow = Row("Line length", topW, topWL);
        topLine.Toggled += on =>
        {
            Cfg.TopLine = on;
            Cfg.Save();
            topRow.Visible = on;
            _top.SyncTopLine();
        };
        topW.ValueChanged += v =>
        {
            Cfg.TopLineWidth = (float)v;
            _top.SyncTopLine();
        };
        topW.DragEnded += _ => Cfg.Save();
        topRow.Visible = Cfg.TopLine;
        _box.AddChild(Option(topLine));
        _box.AddChild(topRow);
        _box.AddChild(Action(Btn("Put the panels back", Shell.PutPanelsBack, "The drawer's width, and every tool panel back beside its own button")));
    }

    void AddTable()
    {
        bool pointers = S.IsUmpire;
        _box.AddChild(Header("Pieces"));
        var face = new CheckBox { Text = "Text on pieces", ButtonPressed = Cfg.FaceText, TooltipText = "The counters and text placed on pieces. Off: names show on hover only" };
        var nameS = new HSlider
        {
            MinValue = Settings.FaceTextScaleMin, MaxValue = Settings.FaceTextScaleMax, Step = 0.05, Value = Cfg.FaceTextScale,
            TooltipText = "How big text prints on pieces and in an imported piece's own lettering; bigger may spill past its box",
        };
        var nameL = Ui.ValueBox(nameS, v => $"{v:0.00}×");
        var nameRow = Row("Text size", nameS, nameL);
        nameS.ValueChanged += v =>
        {
            Cfg.FaceTextScale = (float)v;
            T.ApplyLettering();
        };
        face.Toggled += on =>
        {
            Cfg.FaceText = on;
            nameRow.Visible = on;
            T.ApplyLettering();
        };
        nameRow.Visible = Cfg.FaceText;
        _box.AddChild(Option(face));
        _box.AddChild(nameRow);
        _box.AddChild(ReachRow());
        var lift = new HSlider { MinValue = Settings.LiftLeast, MaxValue = Settings.LiftMost, Step = 0.1, Value = Cfg.LiftHeight, TooltipText = "How high a piece rises off the table while you hold it" };
        lift.ValueChanged += v => Cfg.LiftHeight = (float)v;
        _box.AddChild(Row("Lift", lift, Ui.ValueBox(lift, v => $"{v:0.0} u")));
        var wheel = new CheckBox { Text = "Wheel turns the piece under the pointer", ButtonPressed = Cfg.WheelTurnsHovered, TooltipText = "No need to select it first. Off: the wheel turns selected pieces and zooms over the rest" };
        wheel.Toggled += on => Cfg.WheelTurnsHovered = on;
        _box.AddChild(Option(wheel));
        var carrier = new CheckBox { Text = "Pointing at a piece lights the piece it rides on", ButtonPressed = Cfg.HoverLightsCarrier, TooltipText = "A piece attached to another shows its carrier's outline with its own" };
        carrier.Toggled += on => Cfg.HoverLightsCarrier = on;
        _box.AddChild(Option(carrier));
        _box.AddChild(Header("New scenarios"));
        AppSettings.AddNewScenarios(_box, Cfg);
        if (!pointers) return;

        _box.AddChild(Header("Pointers"));
        var forced = S.View.PointersAlwaysShown;
        var share = new CheckBox { Text = "Share my pointer", ButtonPressed = Cfg.ShareCursor || forced, Disabled = forced, TooltipText = forced ? PeopleList.PointerShownByHost : "" };
        share.Toggled += on => { Cfg.ShareCursor = on; _top.UpdatePointerInfo(); };
        var show = new CheckBox { Text = "Show the others' pointers", ButtonPressed = Cfg.ShowCursors };
        show.Toggled += on => { Cfg.ShowCursors = on; T.ShowCursors = on; };
        _box.AddChild(Option(share));
        _box.AddChild(Option(show));
    }

    HBoxContainer ReachRow()
    {
        var reach = new HSlider
        {
            MinValue = Settings.ReachMin, MaxValue = Settings.ReachOff, Step = 1, ExpEdit = true, Value = Cfg.LetteringReach,
            TooltipText = "How far a piece keeps its names, counters and script buttons, in its own sizes. Lower is faster on a huge table. Right end: no limit",
        };
        var reachL = Ui.ValueBox(reach, ReachWord);
        static string ReachWord(double v) => v >= Settings.ReachOff ? "no limit" : $"{v:0}×";
        bool dragging = false;
        void ApplyReach()
        {
            if (Math.Abs(S.Art.Look.LetteringReach - (float)reach.Value) < 0.01f) return;
            Cfg.LetteringReach = (float)reach.Value;
            Cfg.Save();
            T.ApplyLettering();
        }
        reach.DragStarted += () => dragging = true;
        reach.DragEnded += _ => { dragging = false; ApplyReach(); };
        reach.ValueChanged += v =>
        {
            if (!dragging) ApplyReach();
        };
        return Row("Lettering reach", reach, reachL);
    }

    void AddGraphics()
    {
        _box.AddChild(Header("Quality"));
        var aaPick = new OptionButton { TooltipText = "Multisampling on the 3D table: more is smoother and costs frame rate.", CustomMinimumSize = new Vector2(160, 0) };
        foreach (var n in new[] { "Off", "2 samples", "4 samples", "8 samples" }) aaPick.AddItem(n);
        aaPick.Selected = Math.Clamp(Cfg.Antialias, 0, 3);
        aaPick.ItemSelected += i =>
        {
            Cfg.Antialias = (int)i;
            Cfg.Save();
            T.ApplyGraphics();
        };
        _box.AddChild(Row("Smooth edges", aaPick, null, fill: false));
        var fine = new CheckBox { Text = "Smooth fine lines too", ButtonPressed = Cfg.SmoothFineEdges, TooltipText = "Cut-out pictures and thin drawn lines that multisampling misses. Small lettering and fine map lines soften a little." };
        fine.Toggled += on =>
        {
            Cfg.SmoothFineEdges = on;
            Cfg.Save();
            T.ApplyGraphics();
        };
        _box.AddChild(Option(fine));
        var farPick = new OptionButton { TooltipText = "How sharp a map's fine lines stay in the distance. Soft lets them fade to grey; Crisper can sparkle as the view moves.", CustomMinimumSize = new Vector2(160, 0) };
        foreach (var n in new[] { "Soft", "Crisp", "Crisper" }) farPick.AddItem(n);
        farPick.Selected = Math.Clamp(Cfg.FarDetail, 0, Settings.FarDetailMost);
        farPick.ItemSelected += i =>
        {
            Cfg.FarDetail = (int)i;
            Cfg.Save();
            T.ApplyGraphics();
        };
        _box.AddChild(Row("Far detail", farPick, null, fill: false));
        var scalePick = new OptionButton { TooltipText = "Draws the table at more pixels than the screen has and shrinks it: cleaner fine lines and less shimmer, costing frame rate in proportion.", CustomMinimumSize = new Vector2(160, 0) };
        foreach (var n in Graphics.RenderScales) scalePick.AddItem($"{n * 100:0}%");
        scalePick.Selected = Math.Max(0, Array.FindIndex(Graphics.RenderScales, n => Mathf.IsEqualApprox(n, Cfg.RenderScale)));
        scalePick.ItemSelected += i =>
        {
            Cfg.RenderScale = Graphics.RenderScales[i];
            Cfg.Save();
            T.ApplyGraphics();
        };
        _box.AddChild(Row("Render scale", scalePick, null, fill: false));
        var shadowPick = new OptionButton { TooltipText = "For scenarios whose light casts shadows. High is sharper and keeps them when zoomed far out.", CustomMinimumSize = new Vector2(160, 0) };
        foreach (var n in new[] { "Low", "Medium", "High" }) shadowPick.AddItem(n);
        shadowPick.Selected = Math.Clamp(Cfg.ShadowDetail, 0, Settings.ShadowDetailMost);
        shadowPick.ItemSelected += i =>
        {
            Cfg.ShadowDetail = (int)i;
            Cfg.Save();
            T.ApplyGraphics();
        };
        _box.AddChild(Row("Shadows", shadowPick, null, fill: false));
        var contact = new CheckBox { Text = "Contact shading", ButtonPressed = Cfg.ContactShade, TooltipText = "A soft shade where pieces meet the table, so 3D models sit down on it" };
        contact.Toggled += on =>
        {
            Cfg.ContactShade = on;
            Cfg.Save();
            T.ApplyGraphics();
        };
        _box.AddChild(Option(contact));
        var picPick = new OptionButton { TooltipText = "Graphics memory the map's pictures may take. When the whole map fits, every sheet is drawn at full sharpness from any distance; when it does not, all sheets step down a size together. Automatic is a quarter of the card's memory.", CustomMinimumSize = new Vector2(160, 0) };
        foreach (var mb in Settings.MapPictureMemoryChoices) picPick.AddItem(mb == 0 ? $"Automatic ({VideoMemory.SuggestedPictureMegabytes()} MB)" : mb >= 1024 ? $"{mb / 1024} GB" : $"{mb} MB");
        picPick.Selected = Math.Max(0, Array.IndexOf(Settings.MapPictureMemoryChoices, Cfg.MapPictureMemory));
        picPick.ItemSelected += i =>
        {
            Cfg.MapPictureMemory = Settings.MapPictureMemoryChoices[i];
            Cfg.Save();
            T.ApplyGraphics();
        };
        _box.AddChild(Row("Map pictures", picPick, null, fill: false));

        AddFrames();
        if (Graphics.CanChooseRenderer) AddRenderer();
    }

    void AddFrames()
    {
        _box.AddChild(Header("Frames"));
        var vsync = new CheckBox { Text = "Keep in step with the screen (VSync)", ButtonPressed = Cfg.VSync, TooltipText = "No tearing. Off draws as fast as the limit below allows." };
        vsync.Toggled += on =>
        {
            Cfg.VSync = on;
            Cfg.Save();
            Graphics.ApplyDisplay(Cfg);
        };
        _box.AddChild(Option(vsync));
        var limitPick = new OptionButton { TooltipText = "A lower limit keeps a laptop cooler and quieter", CustomMinimumSize = new Vector2(160, 0) };
        foreach (var n in Graphics.FrameLimits) limitPick.AddItem(n == 0 ? "None" : $"{n} a second");
        limitPick.Selected = Math.Max(0, Array.IndexOf(Graphics.FrameLimits, Cfg.FrameLimit));
        limitPick.ItemSelected += i =>
        {
            Cfg.FrameLimit = Graphics.FrameLimits[i];
            Cfg.Save();
            Graphics.ApplyDisplay(Cfg);
        };
        _box.AddChild(Row("Frame limit", limitPick, null, fill: false));
        var rest = new CheckBox { Text = "Rest when left alone", ButtonPressed = Cfg.RestWhenIdle,
            TooltipText = $"At most {Settings.RestingFrames} frames a second while the window is behind others or untouched for two minutes: a laptop stays cooler" };
        rest.Toggled += on =>
        {
            Cfg.RestWhenIdle = on;
            Cfg.Save();
            Graphics.ApplyDisplay(Cfg);
        };
        _box.AddChild(Option(rest));
        var rate = new CheckBox { Text = "Show the frame rate", ButtonPressed = Cfg.ShowFrameRate, TooltipText = "Frames a second and milliseconds a frame, at the right of the top bar" };
        rate.Toggled += on =>
        {
            Cfg.ShowFrameRate = on;
            Cfg.Save();
            _top.SyncFrameRate();
        };
        _box.AddChild(Option(rate));
    }

    void AddRenderer()
    {
        _box.AddChild(Header("Card"));
        var pick = new OptionButton { TooltipText = "Try the other one if the graphics driver stops or the table draws wrongly", CustomMinimumSize = new Vector2(160, 0) };
        foreach (var r in Settings.Renderers) pick.AddItem(Graphics.RendererName(r));
        pick.Selected = Math.Max(0, Array.IndexOf(Settings.Renderers, Cfg.Renderer));
        var note = Hint("");
        var noteRow = Ui.UnderControls(note, Ui.DialogLabels);
        void Tell()
        {
            note.Text = Graphics.RendererNote(Cfg.Renderer);
            noteRow.Visible = note.Text.Length > 0;
        }
        pick.ItemSelected += i =>
        {
            Cfg.Renderer = Settings.Renderers[i];
            Cfg.Save();
            Graphics.ApplyDisplay(Cfg);
            Tell();
        };
        _box.AddChild(Row("Draw through", pick, null, fill: false));
        _box.AddChild(noteRow);
        Tell();
    }

    void AddSound()
    {
        var vol = new HSlider { MinValue = 0, MaxValue = 1, Step = 0.05, Value = Cfg.SoundVolume };
        var volL = Ui.ValueBox(vol, v => $"{v * 100:0}%");
        vol.ValueChanged += v =>
        {
            Cfg.SoundVolume = (float)v;
            Sfx.Current?.Apply();
        };
        vol.DragEnded += _ => Cfg.Save();
        _box.AddChild(Row("Volume", vol, volL));
        var mute = new CheckBox { Text = "Silent", ButtonPressed = Cfg.SoundMuted, TooltipText = $"{Cfg.Keys.Of("mute")} at the table" };
        mute.Toggled += on =>
        {
            Cfg.SoundMuted = on;
            Cfg.Save();
            Sfx.Current?.Apply();
        };
        _box.AddChild(Option(mute));
        var behind = new CheckBox { Text = "Heard while the window is behind others", ButtonPressed = Cfg.SoundInBackground };
        behind.Toggled += on =>
        {
            Cfg.SoundInBackground = on;
            Cfg.Save();
            Sfx.Current?.Apply();
        };
        _box.AddChild(Option(behind));
    }

    void AddCaptures()
    {
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
        _box.AddChild(Option(clip));
        _box.AddChild(Option(fold));
        _box.AddChild(pathRow);
        var resPick = new OptionButton
        {
            TooltipText = "Captures are rendered apart from the screen, at this many times its resolution: sharper maps and text in Discord",
            CustomMinimumSize = new Vector2(160, 0),
        };
        for (int kk = 1; kk <= Settings.CaptureScaleMost; kk++) resPick.AddItem($"{kk}× the screen");
        resPick.Selected = Math.Clamp(Cfg.CaptureScale, 1, Settings.CaptureScaleMost) - 1;
        resPick.ItemSelected += i => Cfg.CaptureScale = (int)i + 1;
        _box.AddChild(Row("Resolution", resPick, null, fill: false));
    }

    void ChooseFolder(LineEdit path)
    {
        var fd = new FileDialog { FileMode = FileDialog.FileModeEnum.OpenDir, Access = FileDialog.AccessEnum.Filesystem, Title = "Capture folder", UseNativeDialog = true };
        if (System.IO.Directory.Exists(path.Text)) fd.CurrentDir = path.Text;
        fd.DirSelected += d =>
        {
            path.Text = d;
            Cfg.CaptureFolder = d;
            Cfg.Save();
        };
        HudNode.AddChild(fd);
        Ui.FreeOnClose(fd);
        fd.PopupCentered(new Vector2I(720, 480));
    }

    void AddKeys()
    {
        var head = new HBoxContainer();
        var hint = Hint("Esc, Ctrl+1–9 and the arrow keys are fixed");
        hint.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        head.AddChild(hint);
        var kg = Ui.Form(KeyLabels);
        var reset = Btn("Reset to defaults", () =>
        {
            Cfg.Keys.Reset();
            Cfg.Save();
            foreach (var c in kg.GetChildren()) if (c is Button b && b.HasMeta("action")) b.Text = Cfg.Keys.Label(b.GetMeta("action").AsString());
        });
        reset.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        head.AddChild(reset);
        _box.AddChild(head);
        Button? arming = null;
        var rows = Keymap.Actions.Select(a => (a.action, a.label)).Concat(S.View.Hotkeys.Select(h => ("script:" + h.Label, "Script: " + h.Label)));
        foreach (var (action, label) in rows)
        {
            kg.AddChild(new Label { Text = label });
            var act = action;
            var kb =
                new Button { Text = Cfg.Keys.Label(act), TooltipText = $"Key for {label}\nClick, then press the new key", FocusMode = Control.FocusModeEnum.All, CustomMinimumSize = new Vector2(120, 0),
                    SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
            kb.Pressed += () =>
            {
                if (arming != null && arming != kb) arming.Text = Cfg.Keys.Label(ActionOf(arming));
                arming = kb;
                kb.Text = "press a key…";
                kb.GrabFocus();
            };
            kb.SetMeta("action", act);
            kb.GuiInput += e =>
            {
                if (arming != kb || e is not InputEventKey { Pressed: true } key) return;
                if (key.Keycode is Key.Escape)
                {
                    kb.Text = Cfg.Keys.Label(act);
                    arming = null;
                    kb.AcceptEvent();
                    return;
                }
                var desc = KeyEvents.Describe(key);
                if (desc.Length == 0 || key.Keycode is Key.Enter or Key.KpEnter or Key.Space or Key.Tab) { kb.AcceptEvent(); return; }
                Cfg.Keys.Set(act, desc);
                Cfg.Save();
                kb.Text = Cfg.Keys.Label(act);
                arming = null;
                kb.AcceptEvent();
            };
            kg.AddChild(kb);
        }
        Ui.AlignForm(kg);
        _box.AddChild(kg);
    }

    static string ActionOf(Button b) => b.HasMeta("action") ? b.GetMeta("action").AsString() : "";

}
