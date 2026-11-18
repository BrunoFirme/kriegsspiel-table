using System;
using System.Linq;
using Godot;

namespace KTable.App;

sealed class SettingsPages
{
    public const string Id = "settings";
    const int KeyLabels = 272, PickWidth = 160, ValueWidth = 92, DistanceStep = 50;
    static readonly Vector2 Size = new(660, 440);

    public sealed record Place(Node Host, Settings Cfg)
    {
        public Hud? Hud { get; init; }
        public Table? Table { get; init; }
        public (Action Run, string Tip)? Tour { get; init; }
        public Action<VBoxContainer>? Storage { get; init; }
    }

    readonly Place _p;
    VBoxContainer _box = null!;

    SettingsPages(Place p) => _p = p;

    Settings Cfg => _p.Cfg;

    public static DialogPages Build(Place p) => new SettingsPages(p).Make();

    DialogPages Make()
    {
        var pages = new DialogPages(Id, Size);
        _box = pages.Add("Table");
        AddTable();
        _box = pages.Add("Keys");
        AddKeys();
        _box = pages.Add("Interface");
        AddInterface();
        _box = pages.Add("Display");
        AddDisplay();
        _box = pages.Add("Graphics");
        AddGraphics();
        _box = pages.Add("Sound");
        AddSound();
        _box = pages.Add("Captures");
        AddCaptures();
        _box = pages.Add("Network");
        AddNetwork();
        if (_p.Storage is { } storage) storage(pages.Add("Storage"));
        if (AgentEndpoint.Current is { } agent) AgentPanel.Simple(pages.Add("AI agent"), agent, Cfg);
        pages.Root.TreeExiting += Cfg.Save;
        return pages;
    }

    void Live(PrefEffect effect)
    {
        if (_p.Host.IsInsideTree()) Preferences.Apply(new[] { effect }, Cfg, _p.Host.GetTree().Root, _p.Hud, _p.Table);
    }

    static HBoxContainer Row(string label, Control control, Control? value = null, bool fill = true)
    {
        var r = new HBoxContainer();
        r.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Group));
        r.AddChild(Ui.RowLabel(label, Ui.DialogLabels));
        control.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        if (fill) control.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        r.AddChild(control);
        if (value != null)
        {
            value.CustomMinimumSize = Ui.Px(ValueWidth, 0);
            value.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            if (value is Label vl) { vl.HorizontalAlignment = HorizontalAlignment.Right; vl.AddThemeColorOverride("font_color", Ui.Muted); }
            r.AddChild(value);
        }
        return r;
    }

    static Control Option(CheckBox box) => Ui.UnderControls(box, Ui.DialogLabels);

    static Control Action(Button b) => Ui.UnderControls(b, Ui.DialogLabels);

    static OptionButton Pick(string tip) => new() { TooltipText = tip, CustomMinimumSize = Ui.Px(PickWidth, 0) };

    OptionButton Choices(string tip, string[] names, int chosen, Action<int> choose, PrefEffect effect)
    {
        var pick = Pick(tip);
        foreach (var n in names) pick.AddItem(n);
        pick.Selected = chosen;
        pick.ItemSelected += i =>
        {
            choose((int)i);
            Cfg.Save();
            Live(effect);
        };
        return pick;
    }

    Control Toggle(string text, bool on, string tip, Action<bool> set, PrefEffect effect)
    {
        var box = new CheckBox { Text = text, ButtonPressed = on, TooltipText = tip };
        box.Toggled += now =>
        {
            set(now);
            Cfg.Save();
            Live(effect);
        };
        return Option(box);
    }

    HBoxContainer DistanceRow(string label, float least, float most, float now, string tip, Func<double, string> word, Action<float> set)
    {
        var slider = new HSlider { MinValue = least, MaxValue = most, Step = DistanceStep, ExpEdit = true, Value = now, TooltipText = tip };
        bool sliding = false;
        slider.DragStarted += () => sliding = true;
        slider.DragEnded += _ =>
        {
            sliding = false;
            Cfg.Save();
        };
        slider.ValueChanged += v =>
        {
            set((float)v);
            if (!sliding) Cfg.Save();
        };
        return Row(label, slider, Ui.ValueBox(slider, word));
    }

    void AddInterface()
    {
        _box.AddChild(Ui.Header("Appearance"));
        var size = AppSettings.SizeSlider(Cfg, () => Live(PrefEffect.Scale));
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
            Live(PrefEffect.Strip);
        };
        _box.AddChild(Row("Sidebar buttons", bar, barL));
        AppSettings.AddTint(_box, Cfg);

        _box.AddChild(Ui.Header("Top bar"));
        var topLine = new CheckBox { Text = "Says what just happened", ButtonPressed = Cfg.TopLine, TooltipText = "Off: the bar stays bare and it is written in the log instead" };
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
            Live(PrefEffect.TopLine);
        };
        topW.ValueChanged += v =>
        {
            Cfg.TopLineWidth = (float)v;
            Live(PrefEffect.TopLine);
        };
        topW.DragEnded += _ => Cfg.Save();
        topRow.Visible = Cfg.TopLine;
        _box.AddChild(Option(topLine));
        _box.AddChild(topRow);

        _box.AddChild(Ui.Header("Files"));
        AppSettings.AddFileBrowser(_box, Cfg);

        var shell = _p.Hud as IHudShell;
        if (_p.Tour == null && shell == null) return;
        _box.AddChild(Ui.Header("Help"));
        if (_p.Tour is { } tour) _box.AddChild(Action(Ui.Btn("Show the tour again", tour.Run, tour.Tip)));
        if (shell != null) _box.AddChild(Action(Ui.Btn("Put the panels back", shell.PutPanelsBack, "The drawer's width, and every tool panel back beside its own button")));
    }

    void AddWalking()
    {
        _box.AddChild(Ui.Header("Walking"));
        var bob = new CheckBox { Text = "Head bob", ButtonPressed = Cfg.HeadBob, TooltipText = "Your view dips gently with each step when you walk a figure" };
        var strength = new HSlider
        {
            MinValue = Settings.HeadBobLeast, MaxValue = Settings.HeadBobMost, Step = 0.05, Value = Cfg.HeadBobStrength,
            TooltipText = "How much your view dips with each step",
        };
        var strengthRow = Row("Bob strength", strength, Ui.ValueBox(strength, v => $"{v * 100:0}%"));
        strength.ValueChanged += v => Cfg.HeadBobStrength = (float)v;
        bob.Toggled += on =>
        {
            Cfg.HeadBob = on;
            strengthRow.Visible = on;
        };
        strengthRow.Visible = Cfg.HeadBob;
        _box.AddChild(Option(bob));
        _box.AddChild(strengthRow);
    }

    void AddTable()
    {
        _box.AddChild(Ui.Header("Pieces"));
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
            Live(PrefEffect.Lettering);
        };
        face.Toggled += on =>
        {
            Cfg.FaceText = on;
            nameRow.Visible = on;
            Live(PrefEffect.Lettering);
        };
        nameRow.Visible = Cfg.FaceText;
        _box.AddChild(Option(face));
        _box.AddChild(nameRow);
        var lift = new HSlider { MinValue = Settings.LiftLeast, MaxValue = Settings.LiftMost, Step = 0.1, Value = Cfg.LiftHeight, TooltipText = "How high a piece rises off the table while you hold it · at 0 it slides on the table" };
        lift.ValueChanged += v => Cfg.LiftHeight = (float)v;
        _box.AddChild(Row("Lift", lift, Ui.ValueBox(lift, v => $"{v:0.0} u")));
        var wheel = new CheckBox { Text = "Wheel turns the piece under the pointer", ButtonPressed = Cfg.WheelTurnsHovered, TooltipText = "No need to select it first. Off: the wheel turns selected pieces and zooms over the rest" };
        wheel.Toggled += on => Cfg.WheelTurnsHovered = on;
        _box.AddChild(Option(wheel));
        var carrier = new CheckBox { Text = "Pointing at a piece lights the piece it rides on", ButtonPressed = Cfg.HoverLightsCarrier, TooltipText = "A piece attached to another shows its carrier's outline with its own" };
        carrier.Toggled += on => Cfg.HoverLightsCarrier = on;
        _box.AddChild(Option(carrier));
        AddWalking();
        _box.AddChild(Ui.Header("New scenarios"));
        AppSettings.AddNewScenarios(_box, Cfg);
    }

    HBoxContainer LetteringReachRow()
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
            if (Math.Abs(Cfg.LetteringReach - (float)reach.Value) < 0.01f) return;
            Cfg.LetteringReach = (float)reach.Value;
            Cfg.Save();
            Live(PrefEffect.Lettering);
        }
        reach.DragStarted += () => dragging = true;
        reach.DragEnded += _ => { dragging = false; ApplyReach(); };
        reach.ValueChanged += v =>
        {
            if (!dragging) ApplyReach();
        };
        return Row("Lettering reach", reach, reachL);
    }

    void AddDisplay()
    {
        _box.AddChild(Ui.Header("Screen"));
        _box.AddChild(Row("Window", AppSettings.WindowPick(Cfg), null, fill: false));
        var scale = Choices("Over 100% draws the table at more pixels than the screen has and shrinks it: cleaner fine lines and less shimmer, costing frame rate in proportion. Under 100% draws fewer and stretches them: softer, and lighter on a weak graphics card.",
            Graphics.RenderScales.Select(n => $"{n * 100:0}%").ToArray(), Graphics.RenderScaleStep(Cfg.RenderScale), i => Cfg.RenderScale = Graphics.RenderScales[i], PrefEffect.Graphics);
        _box.AddChild(Row("Render scale", scale, null, fill: false));

        _box.AddChild(Ui.Header("Frames"));
        _box.AddChild(Toggle("Keep in step with the screen (VSync)", Cfg.VSync, "No tearing. Off draws as fast as the limit below allows.", on => Cfg.VSync = on, PrefEffect.Display));
        var limit = Choices("A lower limit keeps a laptop cooler and quieter", Graphics.FrameLimits.Select(n => n == 0 ? "None" : $"{n} a second").ToArray(),
            Math.Max(0, Array.IndexOf(Graphics.FrameLimits, Cfg.FrameLimit)), i => Cfg.FrameLimit = Graphics.FrameLimits[i], PrefEffect.Display);
        _box.AddChild(Row("Frame limit", limit, null, fill: false));
        _box.AddChild(Toggle("Rest when left alone", Cfg.RestWhenIdle,
            $"At most {Settings.RestingFrames} frames a second while the window is behind others or untouched for two minutes: a laptop stays cooler", on => Cfg.RestWhenIdle = on, PrefEffect.Display));
        _box.AddChild(Toggle("Show the frame rate", Cfg.ShowFrameRate, "Frames a second and milliseconds a frame, at the right of the top bar", on => Cfg.ShowFrameRate = on, PrefEffect.FrameRate));

        if (Graphics.CanChooseRenderer) AddRenderer();
    }

    void AddRenderer()
    {
        _box.AddChild(Ui.Header("Card"));
        var note = Ui.Hint("");
        var noteRow = Ui.UnderControls(note, Ui.DialogLabels);
        void Tell()
        {
            note.Text = Graphics.RendererNote(Cfg.Renderer);
            noteRow.Visible = note.Text.Length > 0;
        }
        var pick = Choices("Try the other one if the graphics driver stops or the table draws wrongly", Settings.Renderers.Select(Graphics.RendererName).ToArray(),
            Math.Max(0, Array.IndexOf(Settings.Renderers, Cfg.Renderer)), i =>
            {
                Cfg.Renderer = Settings.Renderers[i];
                Tell();
            }, PrefEffect.Display);
        _box.AddChild(Row("Draw through", pick, null, fill: false));
        _box.AddChild(noteRow);
        Tell();
    }

    void AddGraphics()
    {
        _box.AddChild(Ui.Header("Quality"));
        var edges = Choices("Multisampling on the 3D table: more is smoother and costs frame rate.", new[] { "Off", "2 samples", "4 samples", "8 samples" },
            Math.Clamp(Cfg.Antialias, 0, 3), i => Cfg.Antialias = i, PrefEffect.Graphics);
        _box.AddChild(Row("Smooth edges", edges, null, fill: false));
        _box.AddChild(Toggle("Smooth fine lines too", Cfg.SmoothFineEdges, "Cut-out pictures and thin drawn lines that multisampling misses. Small lettering and fine map lines soften a little.",
            on => Cfg.SmoothFineEdges = on, PrefEffect.Graphics));
        var shadows = Choices("For scenarios whose light casts shadows. High is sharper and keeps them when zoomed far out.", new[] { "Low", "Medium", "High" },
            Math.Clamp(Cfg.ShadowDetail, 0, Settings.ShadowDetailMost), i => Cfg.ShadowDetail = i, PrefEffect.Graphics);
        _box.AddChild(Row("Shadows", shadows, null, fill: false));
        _box.AddChild(Toggle("Contact shading", Cfg.ContactShade, "A soft shade where pieces meet the table, so 3D models sit down on it", on => Cfg.ContactShade = on, PrefEffect.Graphics));

        _box.AddChild(Ui.Header("Map"));
        var pictures = Choices("Graphics memory the map's pictures may take. When the whole map fits, every sheet is drawn at full sharpness from any distance; when it does not, all sheets step down a size together. Automatic is a quarter of the card's memory.",
            Settings.MapPictureMemoryChoices.Select(mb => mb == 0 ? $"Automatic ({VideoMemory.SuggestedPictureMegabytes()} MB)" : mb >= 1024 ? $"{mb / 1024} GB" : $"{mb} MB").ToArray(),
            Math.Max(0, Array.IndexOf(Settings.MapPictureMemoryChoices, Cfg.MapPictureMemory)), i => Cfg.MapPictureMemory = Settings.MapPictureMemoryChoices[i], PrefEffect.Graphics);
        _box.AddChild(Row("Map pictures", pictures, null, fill: false));
        var far = Choices("How sharp a map's fine lines stay in the distance. Soft lets them fade to grey; Crisper can sparkle as the view moves.", new[] { "Soft", "Crisp", "Crisper" },
            Math.Clamp(Cfg.FarDetail, 0, Settings.FarDetailMost), i => Cfg.FarDetail = i, PrefEffect.Graphics);
        _box.AddChild(Row("Far lines", far, null, fill: false));

        _box.AddChild(Ui.Header("Distance"));
        _box.AddChild(DistanceRow("Raised reach", Settings.RaisedReachLeast, Settings.RaisedReachOff, Cfg.RaisedReachM,
            "How far from the view this PC raises buildings and trees when the host raises the map. Never farther than the host's reach. Right end: as the host",
            v => v >= Settings.RaisedReachOff ? "as the host" : $"{v:0} m", v => Cfg.RaisedReachM = v));
        _box.AddChild(DistanceRow("Building detail", Settings.RaisedDetailLeast, Settings.RaisedDetailMost, Cfg.RaisedDetailM,
            "How far from the view buildings and trees keep their full detail before they turn plain. Farther costs frame rate",
            v => $"{v:0} m", v => Cfg.RaisedDetailM = v));
        _box.AddChild(DistanceRow("Lamp reach", Settings.LampReachLeast, Settings.LampReachOff, Cfg.LampReachM,
            "How far from the view lamps at night light the table. Farther costs frame rate. Right end: no limit",
            v => v >= Settings.LampReachOff ? "no limit" : $"{v:0} m", v => Cfg.LampReachM = v));
        _box.AddChild(LetteringReachRow());
    }

    void AddSound()
    {
        var vol = new HSlider { MinValue = 0, MaxValue = 1, Step = 0.05, Value = Cfg.SoundVolume };
        var volL = Ui.ValueBox(vol, v => $"{v * 100:0}%");
        vol.ValueChanged += v =>
        {
            Cfg.SoundVolume = (float)v;
            Live(PrefEffect.Sound);
        };
        vol.DragEnded += _ => Cfg.Save();
        _box.AddChild(Row("Volume", vol, volL));
        _box.AddChild(Toggle("Silent", Cfg.SoundMuted, $"{Cfg.Keys.Of("mute")} at the table", on => Cfg.SoundMuted = on, PrefEffect.Sound));
        _box.AddChild(Toggle("Heard while the window is behind others", Cfg.SoundInBackground, "", on => Cfg.SoundInBackground = on, PrefEffect.Sound));
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
        pathBox.AddChild(Ui.Btn("Choose…", () => ChooseFolder(path)));
        var pathRow = Row("Folder", pathBox);
        fold.Toggled += on => { Cfg.CaptureToFolder = on; pathRow.Visible = on; };
        pathRow.Visible = Cfg.CaptureToFolder;
        _box.AddChild(Option(clip));
        _box.AddChild(Option(fold));
        _box.AddChild(pathRow);
        var resPick = Pick("Captures are rendered apart from the screen, at this many times its resolution: sharper maps and text in Discord");
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
        _p.Host.AddChild(fd);
        Ui.FreeOnClose(fd);
        fd.PopupCentered(Ui.PxI(720, 480));
    }

    void AddKeys()
    {
        var head = new HBoxContainer();
        var hint = Ui.Hint("Esc, Ctrl+1–9 and the arrow keys are fixed");
        hint.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        head.AddChild(hint);
        var kg = Ui.Form(KeyLabels);
        var reset = Ui.Btn("Reset to defaults", () =>
        {
            Cfg.Keys.Reset();
            Cfg.Save();
            foreach (var c in kg.GetChildren()) if (c is Button b && b.HasMeta("action")) b.Text = Cfg.Keys.Label(b.GetMeta("action").AsString());
        });
        reset.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        head.AddChild(reset);
        _box.AddChild(head);
        Button? arming = null;
        var scripted = _p.Hud?.T.S.View.Hotkeys.Select(h => ("script:" + h.Label, "Script: " + h.Label)) ?? Enumerable.Empty<(string, string)>();
        foreach (var (action, label) in Keymap.Actions.Select(a => (a.action, a.label)).Concat(scripted))
        {
            kg.AddChild(new Label { Text = label });
            var act = action;
            var kb =
                new Button { Text = Cfg.Keys.Label(act), TooltipText = $"Key for {label}\nClick, then press the new key", FocusMode = Control.FocusModeEnum.All, CustomMinimumSize = Ui.Px(120, 0),
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

    void AddNetwork()
    {
        _box.AddChild(Ui.Header("Inviting"));
        AppSettings.AddPort(_box, Cfg);
        if (!UpdatesOffered()) return;
        _box.AddChild(Ui.Header("Updates"));
        var check = new CheckBox { Text = "Check GitHub for a new version at start", ButtonPressed = Cfg.CheckUpdates, FocusMode = Control.FocusModeEnum.None };
        check.Toggled += on => { Cfg.CheckUpdates = on; Cfg.Save(); };
        _box.AddChild(Option(check));
    }

    bool UpdatesOffered() => _p.Host.IsInsideTree() && _p.Host.GetTree().CurrentScene is Main main && UpdateOffer.Applies(main.Platform);
}
