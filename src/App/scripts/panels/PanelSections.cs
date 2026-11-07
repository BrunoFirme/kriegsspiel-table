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
