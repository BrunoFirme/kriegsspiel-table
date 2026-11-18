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
        Tabs = new TabContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, CustomMinimumSize = Ui.Px(0, 160) };
        Tabs.TabChanged += _ =>
        {
            if (Tabs.GetCurrentTabControl()?.Name == "Script") _s.Script.Focus();
            _s.Prints.SyncLive();
        };
        return Tabs;
    }

    string? _hold;

    public void Fresh() => _tabWanted = "";

    public void Hold(string tab) => _hold = tab;

    public bool OnTab(string tab) => Tabs.GetCurrentTabControl()?.Name.ToString() == tab;

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
            none.AddChild(Ui.Hint("No piece yet"));
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
        if (_hold != null && TabIndex(_hold) is var held and >= 0) index = held;
        _hold = null;
        Tabs.CurrentTab = Math.Max(0, index);
        _s.Prints.SyncLive();
    }

    public int TabIndex(string name)
    {
        for (int i = 0; i < Tabs.GetTabCount(); i++) if (Tabs.GetTabControl(i).Name.ToString() == name) return i;
        return -1;
    }

    internal const int BetweenGroups = 14, WithinGroup = 6;

    static void Gap(VBoxContainer page)
    {
        if (page.GetChildCount() > 0) page.AddChild(new Control { CustomMinimumSize = Ui.Px(0, BetweenGroups - 4), MouseFilter = Control.MouseFilterEnum.Ignore });
    }

    internal static VBoxContainer Group(VBoxContainer page, string title, Control? end = null)
    {
        Gap(page);
        var head = new HBoxContainer();
        var label = Ui.HeadLabel(title);
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        label.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        label.ClipText = true;
        head.AddChild(label);
        if (end != null) head.AddChild(end);
        page.AddChild(head);
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", Ui.Px(WithinGroup));
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
        head.AddThemeFontSizeOverride("font_size", Ui.Px(11));
        head.AddThemeColorOverride("font_color", Ui.Muted);
        head.AddThemeColorOverride("font_hover_color", Ui.Ink);
        head.AddThemeFontOverride("font", Ui.SansStrong());
        foreach (var state in new[] { "normal", "hover", "pressed", "focus", "hover_pressed" }) head.AddThemeStyleboxOverride(state, Ui.Scaled(new StyleBoxEmpty { ContentMarginTop = 2, ContentMarginBottom = 2 }));
        page.AddChild(head);
        var body = new VBoxContainer { Visible = !shut };
        body.AddThemeConstantOverride("separation", Ui.Px(WithinGroup));
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
        margin.AddThemeConstantOverride("margin_right", Ui.Px(10));
        margin.AddThemeConstantOverride("margin_left", Ui.Px(2));
        margin.AddThemeConstantOverride("margin_top", Ui.Px(8));
        margin.AddThemeConstantOverride("margin_bottom", Ui.Px(10));
        scroll.AddChild(margin);
        var box = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Tight));
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
            var sg = Ui.Form(Ui.PanelLabels);
            p.AddChild(sg);
            var metres = SizeForm.Rows(sg, _s.Prefs, d.Size, v => Apply(x => x.Size = v, "the size"));
            Ui.AlignForm(sg);
            p.AddChild(metres);
            page.AddChild(new HSeparator());
            page.AddChild(Ui.Hint($"id {d.Id}"));
            return;
        }
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
        var g = Ui.Form(Ui.PanelLabels);
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
        if (d.Rig != null) WalkRow(g, d);
        g.AddChild(new Label { Text = "Holds pieces" });
        var holds = Ui.Segments(new (string, bool, string, Action)[]
        {
            ("Off", !d.Holds, "Pieces held over it stay on top", () => Holds(false)),
            ("On", d.Holds, "Pieces held over it a moment go inside, as into a bag; each piece at the table can still be switched", () => Holds(true)),
        });
        holds.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        g.AddChild(holds);
        Ui.AlignForm(g);
    }

    void Holds(bool on)
    {
        if (_d.Holds != on) Apply(x => x.Holds = on, "whether it holds pieces", inspector: true);
        else _s.RebuildInspector();
    }

    string? Printed(int n) => n > 0 && _d.States.Count > 1 && _d.Kind != "model" ? $"{n} · state {_s.State + 1}" : Count(n);

    void WalkRow(Control form, PieceDef d)
    {
        form.AddChild(new Label { Text = "Walk" });
        var walk = new OptionButton { FitToLongestItem = false, TooltipText = "Its own animations (Natural when the model has none), or one of the built-in walks" };
        walk.AddItem("Its own");
        foreach (var (_, name) in RigMotion.Gaits) walk.AddItem(name);
        walk.Selected = d.Gait == null ? 0 : Math.Max(0, RigMotion.Gaits.ToList().FindIndex(g => g.Id == d.Gait)) + 1;
        walk.ItemSelected += i => Apply(x => x.Gait = i == 0 ? null : RigMotion.Gaits[(int)i - 1].Id, "the walk");
        form.AddChild(walk);
    }

    void CountersSection(VBoxContainer page)
    {
        var d = _d;
        var p = _i.Section(page, "Counters", open: d.Counters.Count > 0, count: Printed(d.Counters.Count));
        for (int i = 0; i < d.Counters.Count; i++) _s.Prints.Row(p, d, $"c{i}");
        p.AddChild(Ui.Btn("Add a counter", _s.Prints.AddCounter));
    }

    string? BlockInk() => _s.Canvas.Doc.Layers.FirstOrDefault(l => l.Block != null)?.Block?.LabelColor;

    void TextLinesSection(VBoxContainer page)
    {
        var d = _d;
        var p = _i.Section(page, "Text lines", open: d.TextLines.Count > 0, count: Printed(d.TextLines.Count));
        for (int i = 0; i < d.TextLines.Count; i++) _s.Prints.Row(p, d, $"t{i}");
        p.AddChild(Ui.Btn("Add a text line", () => _s.Prints.AddTextLine(BlockInk())));
    }
}
