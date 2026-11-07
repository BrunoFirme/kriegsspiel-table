using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Client.Panels;
using KTable.Core.Panels;

namespace KTable.App;

sealed partial class PanelView
{
    void Section(PanelBox header)
    {
        if (header.Parent is not { Tag: PanelAccordions.Section } section || section.Path == null || PanelAccordions.OwnerOf(section) is not { } accordion || _scene == null) return;
        bool open = PanelAccordions.Open(section, _local);
        if (!open)
        {
            foreach (var other in PanelAccordions.Sections(accordion, _scene.Elements).Where(o => o != section && PanelAccordions.Open(o, _local))) Turn(accordion, other, false);
            Turn(accordion, section, true);
        }
        else if (PanelValues.Bool(accordion.Extra.GetValueOrDefault("collapsible"))) Turn(accordion, section, false);
    }

    PanelBox? _placeholder;
    bool _wasOpen;
    int _slot = -1;
    const float SeeThrough = 0.5f;

    static bool Reorders(PanelBox b) => b.Tag == PanelAccordions.Section && b.Path != null && PanelAccordions.OwnerOf(b) is { } a
        && PanelValues.Bool(a.Extra.GetValueOrDefault("allowsectionreordering"));

    static float HeaderHeight(PanelBox section) => Math.Max(0, section.Children.FirstOrDefault(c => c.Tag == PanelAccordions.Header)?.Layout?.PreferredHeight ?? 0);

    void SectionBegin(DragState d)
    {
        var section = d.Box;
        var accordion = PanelAccordions.OwnerOf(section)!;
        var key = PanelAccordions.Key(section);
        _wasOpen = PanelAccordions.Open(section, _local);
        _local[key] = false.ToString();
        _local[PanelAccordions.Opening + key] = PanelAccordions.Text(0);
        PanelClasses.Ops(_local, section, "-" + PanelOps.ExpandedClass, "+" + PanelOps.CollapsedClass);
        if (section.Children.FirstOrDefault(c => c.Tag == PanelAccordions.Content)?.Layout is { } content) content.PreferredHeight = 0;
        float header = HeaderHeight(section);
        section.Layout ??= new LayoutProps();
        section.Layout.PreferredHeight = header;
        section.Layout.Ignore = true;
        section.SetSize(1, header);
        if (accordion.Extra.GetValueOrDefault("makedraggedsectiontransparent") is not { } t || PanelValues.Bool(t)) d.Node.Modulate = Colors.White with { A = SeeThrough };
        _placeholder = accordion.Add(PanelAccordions.MakePlaceholder(0));
        _slot = -1;
        _placed = accordion.Children.Count(c => c.Tag == PanelAccordions.Section) - 1;
        _gapMoving = false;
        Relayout(accordion, d);
    }

    void SectionMove(DragState d, Vector2 at)
    {
        var accordion = PanelAccordions.OwnerOf(d.Box)!;
        if (_placeholder == null || _gapMoving) return;
        var sections = accordion.Children.Where(c => c.Tag == PanelAccordions.Section).ToList();
        int slot = PanelAccordions.Slot(sections, d.Box, Local(accordion, at).Y);
        if (slot == _slot) return;
        _slot = slot;
        MoveGap(accordion, d, slot);
    }

    int _placed;
    bool _gapMoving;
    const float GapRate = 10;

    async void MoveGap(PanelBox accordion, DragState d, int slot)
    {
        var gap = _placeholder;
        if (gap == null) return;
        _gapMoving = true;
        if (await GapTo(accordion, d, gap, 0))
        {
            var others = accordion.Children.Where(c => c.Tag == PanelAccordions.Section && c != d.Box).ToList();
            accordion.Children.Remove(gap);
            int index = slot < others.Count ? accordion.Children.IndexOf(others[slot]) : accordion.Children.IndexOf(others.LastOrDefault() ?? d.Box) + 1;
            accordion.Children.Insert(Math.Clamp(index, 0, accordion.Children.Count), gap);
            _placed = slot;
            await GapTo(accordion, d, gap, HeaderHeight(d.Box));
        }
        if (_placeholder == gap) _gapMoving = false;
    }

    async System.Threading.Tasks.Task<bool> GapTo(PanelBox accordion, DragState d, PanelBox gap, float height)
    {
        if (!await GapFrame(gap)) return false;
        float from = gap.Layout!.PreferredHeight, index = 0;
        while (index < 1)
        {
            gap.Layout.PreferredHeight = Mathf.Lerp(from, height, index);
            Relayout(accordion, d);
            index += GapRate * (float)_top!.GetProcessDeltaTime();
            if (!await GapFrame(gap)) return false;
        }
        gap.Layout.PreferredHeight = height;
        Relayout(accordion, d);
        return true;
    }

    async System.Threading.Tasks.Task<bool> GapFrame(PanelBox gap)
    {
        if (_top == null || !GodotObject.IsInstanceValid(_top) || !_top.IsInsideTree()) return false;
        await _top.ToSignal(_top.GetTree(), SceneTree.SignalName.ProcessFrame);
        return _placeholder == gap && GodotObject.IsInstanceValid(_top);
    }

    void Relayout(PanelBox accordion, DragState d)
    {
        if (_scene == null || !GodotObject.IsInstanceValid(d.Node)) return;
        var keep = d.Node.Position;
        PanelLayout.Run(accordion, _scene.Meter);
        Refit(accordion);
        d.Node.Position = keep;
    }

    void SectionEnd(DragState d)
    {
        var section = d.Box;
        var accordion = PanelAccordions.OwnerOf(section)!;
        section.Layout!.Ignore = false;
        if (_placeholder != null) accordion.Children.Remove(_placeholder);
        _placeholder = null;
        var sections = accordion.Children.Where(c => c.Tag == PanelAccordions.Section).ToList();
        int slot = _placed;
        _gapMoving = false;
        var order = PanelAccordions.Dropped(sections, section, slot);
        _local[PanelAccordions.Order + PanelScene.Key(accordion.Path!)] = string.Join(" ", order.Where(s => s.Path != null).Select(s => PanelScene.Key(s.Path!)));
        if (_wasOpen) Turn(accordion, section, true);
        bool expandAfter = accordion.Extra.GetValueOrDefault("expandsectionafterreorder") is not { } e || PanelValues.Bool(e);
        if (expandAfter && !PanelAccordions.Open(section, _local))
        {
            foreach (var other in order.Where(o => o != section && PanelAccordions.Open(o, _local))) Turn(accordion, other, false);
            Turn(accordion, section, true);
        }
        Redraw();
    }

    void Turn(PanelBox accordion, PanelBox section, bool open)
    {
        var key = PanelAccordions.Key(section);
        float from = PanelAccordions.Shown(section, _local);
        _local[key] = open.ToString();
        if (open) PanelClasses.Ops(_local, section, "-" + PanelOps.CollapsedClass, "+" + PanelOps.ExpandedClass);
        else PanelClasses.Ops(_local, section, "-" + PanelOps.ExpandedClass, "+" + PanelOps.CollapsedClass);
        float duration = accordion.Extra.TryGetValue("accordionanimationduration", out var d) ? PanelValues.Float(d) : PanelAccordions.Duration;
        PanelTweens.Run(_local, PanelAccordions.Opening + key, from, open ? 1 : 0, duration, _hooks.Redraw);
    }
}

static class PanelTweens
{
    sealed record Tween(Dictionary<string, string> Local, string Key, float From, float To, float Rate, Action Redraw)
    {
        public float Index { get; set; }
    }

    static readonly List<Tween> Running = new();
    static ulong _last;
    static bool _hooked;
    const float PerSecond = 1e-6f;

    public static void Run(Dictionary<string, string> local, string key, float from, float to, float duration, Action redraw)
    {
        Running.RemoveAll(t => ReferenceEquals(t.Local, local) && t.Key == key);
        if (duration <= 0)
        {
            local[key] = PanelAccordions.Text(to);
            redraw();
            return;
        }
        local[key] = PanelAccordions.Text(from);
        Running.Add(new Tween(local, key, from, to, 1 / duration, redraw));
        redraw();
        if (_hooked || Engine.GetMainLoop() is not SceneTree tree) return;
        _hooked = true;
        _last = Time.GetTicksUsec();
        tree.ProcessFrame += Frame;
    }

    static void Frame()
    {
        var now = Time.GetTicksUsec();
        float dt = (now - _last) * PerSecond;
        _last = now;
        foreach (var t in Running.ToList())
        {
            t.Index += t.Rate * dt;
            bool done = t.Index >= 1;
            t.Local[t.Key] = PanelAccordions.Text(done ? t.To : Mathf.Lerp(t.From, t.To, t.Index));
            if (done) Running.Remove(t);
            t.Redraw();
        }
        if (Running.Count > 0 || Engine.GetMainLoop() is not SceneTree tree) return;
        _hooked = false;
        tree.ProcessFrame -= Frame;
    }
}
