using System;
using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

internal sealed class Inspector
{
    readonly PieceStudio _s;
    string _tabWanted = "";
    bool _inspectedEffect;

    public Inspector(PieceStudio s) => _s = s;

    public TabContainer Tabs { get; private set; } = null!;

    public TabContainer Build()
    {
        Tabs = new TabContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 160) };
        Tabs.TabChanged += _ => { if (Tabs.GetCurrentTabControl()?.Name == "Script") _s.Script.Focus(); };
        return Tabs;
    }

    public void Fresh() => _tabWanted = "";

    public void Rebuild()
    {
        var was = Tabs.GetCurrentTabControl()?.Name.ToString() ?? "";
        var view = _s.View;
        const string firstName = "Layers";
        bool viewChanged = _tabWanted != view;
        _tabWanted = view;
        var sel = _s.Sel;
        string? draft = _s.Script.Draft(sel);
        foreach (var c in Tabs.GetChildren()) { Tabs.RemoveChild(c); c.QueueFree(); }
        _s.Script.Let();
        if (sel == null)
        {
            var none = Page("Piece");
            none.AddChild(Ui.Hint("No piece yet: ＋ New piece makes one."));
            return;
        }
        if (view is "paint" or "model") { _s.LayerPanel.Body = Page(firstName); _s.RebuildLayers(); }
        bool fx = EffectFx.IsEffect(sel);
        if (fx) new EffectTab(_s).Build(Page("Effect"));
        new PieceTab(_s, this).Build(Page("Piece"));
        _s.Script.Build(Page("Script"), draft);
        int index = -1;
        for (int i = 0; i < Tabs.GetTabCount(); i++)
            if (Tabs.GetTabControl(i).Name.ToString() == (viewChanged && view is "paint" or "model" ? firstName : was)) index = i;
        if (index < 0 && was is "Text" or "Markers") index = TabIndex("Piece");
        if (fx && !_inspectedEffect && view == "table") index = 0;
        _inspectedEffect = fx;
        Tabs.CurrentTab = Math.Max(0, index);
    }

    public int TabIndex(string name)
    {
        for (int i = 0; i < Tabs.GetTabCount(); i++) if (Tabs.GetTabControl(i).Name.ToString() == name) return i;
        return -1;
    }

    internal const int BetweenGroups = 14, WithinGroup = 6;

    static void Gap(VBoxContainer page)
    {
        if (page.GetChildCount() > 0) page.AddChild(new Control { CustomMinimumSize = new Vector2(0, BetweenGroups - 4), MouseFilter = Control.MouseFilterEnum.Ignore });
    }

    internal static VBoxContainer Group(VBoxContainer page, string title, Control? end = null)
    {
        Gap(page);
        var head = new HBoxContainer();
        var label = new Label { Text = title.ToUpperInvariant(), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, ClipText = true };
        Ui.HeadStyle(label);
        head.AddChild(label);
        if (end != null) head.AddChild(end);
        page.AddChild(head);
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", WithinGroup);
        page.AddChild(body);
        return body;
    }

    public VBoxContainer Section(VBoxContainer page, string title, bool open = true, string? count = null)
    {
        var prefs = _s.Prefs;
        var key = "fold:" + title;
        bool shut = prefs.Panels.ContainsKey(key) ? prefs.Panels[key][0] > 0.5f : !open;
        string Text(bool closed) => (closed ? "▸  " : "▾  ") + title.ToUpperInvariant() + (count is { Length: > 0 } ? "   " + count : "");
        Gap(page);
        var head = new Button { Text = Text(shut), Flat = true, Alignment = HorizontalAlignment.Left, FocusMode = Control.FocusModeEnum.None };
        head.AddThemeFontSizeOverride("font_size", 11);
        head.AddThemeColorOverride("font_color", Ui.Muted);
        head.AddThemeColorOverride("font_hover_color", Ui.Ink);
        head.AddThemeFontOverride("font", Ui.SansStrong());
        foreach (var state in new[] { "normal", "hover", "pressed", "focus", "hover_pressed" }) head.AddThemeStyleboxOverride(state, new StyleBoxEmpty { ContentMarginTop = 2, ContentMarginBottom = 2 });
        page.AddChild(head);
        var body = new VBoxContainer { Visible = !shut };
        body.AddThemeConstantOverride("separation", WithinGroup);
        page.AddChild(body);
        head.Pressed += () =>
        {
            body.Visible = !body.Visible;
            head.Text = Text(!body.Visible);
            prefs.Panels[key] = new[] { body.Visible ? 0f : 1f };
            prefs.Save();
        };
        return body;
    }

    VBoxContainer Page(string name)
    {
        var scroll = new ScrollContainer { Name = name, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var margin = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        margin.AddThemeConstantOverride("margin_right", 10);
        margin.AddThemeConstantOverride("margin_left", 2);
        margin.AddThemeConstantOverride("margin_top", 8);
        margin.AddThemeConstantOverride("margin_bottom", 10);
        scroll.AddChild(margin);
        var box = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", 4);
        margin.AddChild(box);
        Tabs.AddChild(scroll);
        return box;
    }
}

internal sealed class PieceTab
{
    readonly PieceStudio _s;
    readonly Inspector _i;
    readonly PieceDef _d;

    public PieceTab(PieceStudio s, Inspector i)
    {
        _s = s;
        _i = i;
        _d = s.Sel!;
    }

    void Apply(Action<PieceDef> change, string what, bool list = false, bool inspector = false) => _s.Apply(change, list, inspector, what);

    public void Build(VBoxContainer page)
    {
        var d = _d;
        NameSection(page);
        if (EffectFx.IsEffect(d))
        {
            var p = _i.Section(page, "Size");
            var sg = Ui.Form(76);
            p.AddChild(sg);
            var metres = SizeForm.Rows(sg, _s.Prefs, d.Size, "Height", v => Apply(x => x.Size = v, "the size"));
            Ui.AlignForm(sg);
            p.AddChild(metres);
            page.AddChild(new HSeparator());
            page.AddChild(Ui.Hint($"id {d.Id}"));
            return;
        }
        LooksSection(page);
        CountersSection(page);
        TextLinesSection(page);
        _s.Markers.BuildTab(_i.Section(page, "Markers", open: d.Attachments.Count > 0, count: Count(d.Attachments.Count)));
        page.AddChild(new HSeparator());
        page.AddChild(Ui.Hint($"id {d.Id}" + (PackManifest.IsUserPackId(_s.Pack.Id) ? "" : " · a converted or installed pack: edits stay on this PC until you export it")));
    }

    static string? Count(int n) => n > 0 ? n.ToString() : null;

    void NameSection(VBoxContainer page)
    {
        var d = _d;
        var g = Ui.Form(76);
        page.AddChild(g);
        g.AddChild(new Label { Text = "Name" });
        var name = new LineEdit { Text = d.Name };
        void CommitName() { if (name.Text.Trim().Length > 0 && name.Text.Trim() != d.Name) Apply(x => x.Name = name.Text.Trim(), "the name", list: true); }
        name.TextSubmitted += _ => CommitName();
        name.FocusExited += CommitName;
        g.AddChild(name);
        g.AddChild(new Label { Text = "Tags" });
        var tags = new LineEdit { Text = string.Join(", ", d.Tags), PlaceholderText = "infantry, french" };
        void CommitTags() { var t = tags.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().ToList(); if (!t.SequenceEqual(d.Tags)) Apply(x => x.Tags = t,
            "the tags", list: true); }
        tags.TextSubmitted += _ => CommitTags();
        tags.FocusExited += CommitTags;
        g.AddChild(tags);
        Ui.AlignForm(g);
    }

    void LooksSection(VBoxContainer page)
    {
        var d = _d;
        var p = _i.Section(page, "Looks", count: Count(d.States.Count));
        p.AddChild(Ui.Hint("Switched at the table with [ ] or the number keys. Look 1 is how the piece starts; a copy of it changes with it."));
        for (int i = 0; i < d.States.Count; i++)
        {
            var st = d.States[i];
            var si = i;
            var sr = new HBoxContainer();
            sr.AddChild(new Label { Text = $"{i + 1}", Modulate = Ui.Muted, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
            var sn = new LineEdit { Text = st.Name, PlaceholderText = $"look {i + 1}", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            void CommitSt() { if (sn.Text != st.Name) { Apply(x => x.States[si].Name = sn.Text, "a look's name"); _s.RebuildFaceBar(); } }
            sn.TextSubmitted += _ => CommitSt();
            sn.FocusExited += CommitSt;
            sr.AddChild(sn);
            if (PieceFaces.Follows(d, i)) sr.AddChild(new Label { Text = "as look 1", Modulate = Ui.Muted, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, TooltipText = "Still a copy of look 1: painting look 1 changes it too" });
            sr.AddChild(Ui.Btn("✕", () => _s.RemoveState(si), "Take this look away   (Ctrl+Z brings it back)"));
            p.AddChild(sr);
        }
        p.AddChild(Ui.Btn("Add a look (a copy of look 1)", () => _s.AddState()));
    }

    void CountersSection(VBoxContainer page)
    {
        var d = _d;
        var p = _i.Section(page, "Counters", open: d.Counters.Count > 0, count: Count(d.Counters.Count));
        p.AddChild(Ui.Hint("Numbers on each piece: ammunition, morale, strength. They print on the back until placed with the Place tool (N)."));
        var cg = new GridContainer { Columns = 5 };
        cg.AddThemeConstantOverride("h_separation", 3);
        if (d.Counters.Count > 0) foreach (var hdr in new[] { "Name", "Min", "Max", "Start", "" }) cg.AddChild(new Label { Text = hdr, Modulate = Ui.Muted });
        for (int i = 0; i < d.Counters.Count; i++)
        {
            var c = d.Counters[i];
            var ci = i;
            var cn = new LineEdit { Text = c.Name, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(70, 0) };
            void CommitC() { if (cn.Text.Trim().Length > 0 && cn.Text.Trim() != c.Name) Apply(x => x.Counters[ci].Name = cn.Text.Trim(), "a counter's name"); }
            cn.TextSubmitted += _ => CommitC();
            cn.FocusExited += CommitC;
            cg.AddChild(cn);
            var mn = new SpinBox { MinValue = -999, MaxValue = 999, Value = c.Min, CustomMinimumSize = new Vector2(58, 0) };
            mn.ValueChanged += v => Apply(x => x.Counters[ci].Min = (int)v, "a counter's range");
            cg.AddChild(mn);
            var mx = new SpinBox { MinValue = -999, MaxValue = 999, Value = c.Max, CustomMinimumSize = new Vector2(58, 0) };
            mx.ValueChanged += v => Apply(x => x.Counters[ci].Max = (int)v, "a counter's range");
            cg.AddChild(mx);
            var df = new SpinBox { MinValue = -999, MaxValue = 999, Value = c.Default, CustomMinimumSize = new Vector2(58, 0) };
            df.ValueChanged += v => Apply(x => x.Counters[ci].Default = (int)v, "a counter's start");
            cg.AddChild(df);
            foreach (var sb in new[] { mn, mx, df }) sb.GetLineEdit().AddThemeConstantOverride("minimum_character_width", 2);
            cg.AddChild(Ui.Btn("✕", () => Apply(x => x.Counters.RemoveAt(ci), "a counter taken away", inspector: true), "Take this counter away   (Ctrl+Z brings it back)"));
        }
        p.AddChild(cg);
        p.AddChild(Ui.Btn("Add a counter", () => Apply(x => x.Counters.Add(new CounterDef { Name = x.Counters.Count == 0 ? "Strength" : $"Counter {x.Counters.Count + 1}", Min = 0, Max = 9, Default = 9 }), "a new counter", inspector: true)));
    }

    void TextLinesSection(VBoxContainer page)
    {
        var d = _d;
        var p = _i.Section(page, "Text lines", open: d.TextLines.Count > 0, count: Count(d.TextLines.Count));
        p.AddChild(Ui.Hint("Text on each piece, on every look: unit, commander, formation. It prints in the name band until placed with the Place tool (N)."));
        var tg = new GridContainer { Columns = 3 };
        tg.AddThemeConstantOverride("h_separation", 3);
        for (int i = 0; i < d.TextLines.Count; i++)
        {
            var tl = d.TextLines[i];
            var ti = i;
            var tn = new LineEdit { Text = tl.Name, PlaceholderText = "name", CustomMinimumSize = new Vector2(96, 0) };
            void CommitTn() { if (tn.Text.Trim().Length > 0 && tn.Text.Trim() != tl.Name) Apply(x => x.TextLines[ti].Name = tn.Text.Trim(), "a text line's name"); }
            tn.TextSubmitted += _ => CommitTn();
            tn.FocusExited += CommitTn;
            tg.AddChild(tn);
            var td = new LineEdit { Text = tl.Default, PlaceholderText = "start value", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            void CommitTd() { if (td.Text != tl.Default) Apply(x => x.TextLines[ti].Default = td.Text, "a text line's start"); }
            td.TextSubmitted += _ => CommitTd();
            td.FocusExited += CommitTd;
            tg.AddChild(td);
            tg.AddChild(Ui.Btn("✕", () => Apply(x => x.TextLines.RemoveAt(ti), "a text line taken away", inspector: true), "Take this text line away   (Ctrl+Z brings it back)"));
        }
        p.AddChild(tg);
        p.AddChild(Ui.Btn("Add a text line", () => Apply(x => x.TextLines.Add(new TextLineDef { Name = x.TextLines.Count == 0 ? "Name" : $"Line {x.TextLines.Count + 1}" }), "a new text line", inspector: true)));
    }
}
