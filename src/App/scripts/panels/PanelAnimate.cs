using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using KTable.Client.Panels;
using KTable.Core.Panels;
using KTable.Core.Protocol;

namespace KTable.App;

static class PanelAnimate
{
    const float Overshoot = 1.25f, GrowFirst = 0.7f, ShrinkFirst = 0.3f;

    public static void Play(Control c, PanelMove m, Action? done)
    {
        var tween = c.CreateTween();
        var home = c.Position;
        var opacity = c.Modulate.A;
        if (m.Showing && m.Delay > 0)
        {
            c.Modulate = c.Modulate with { A = 0 };
            tween.TweenInterval(m.Delay);
            tween.TweenCallback(Callable.From(() => c.Modulate = c.Modulate with { A = opacity }));
        }
        else if (!m.Showing && m.Delay > 0) tween.TweenInterval(m.Delay);
        Steps(tween, c, m, home);
        tween.TweenCallback(Callable.From(() => done?.Invoke()));
    }

    static void Steps(Tween tween, Control c, PanelMove m, Vector2 home)
    {
        float d = Math.Max(0.0001f, m.Duration);
        switch (m.Animation)
        {
            case "FadeIn" or "FadeOut":
                if (m.Showing) c.Modulate = c.Modulate with { A = 0 };
                tween.TweenProperty(c, "modulate:a", m.Showing ? 1f : 0f, d).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
                break;
            case "Grow":
                c.Scale = Vector2.Zero;
                tween.TweenProperty(c, "scale", Vector2.One * Overshoot, d * GrowFirst);
                tween.TweenProperty(c, "scale", Vector2.One, d * (1 - GrowFirst)).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
                break;
            case "Shrink":
                tween.TweenProperty(c, "scale", Vector2.One * Overshoot, d * ShrinkFirst);
                tween.TweenProperty(c, "scale", Vector2.Zero, d * (1 - ShrinkFirst)).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
                break;
            case "Grow_Horizontal" or "Grow_Vertical" or "Shrink_Horizontal" or "Shrink_Vertical":
                bool across = m.Animation.EndsWith("Horizontal");
                var flat = across ? new Vector2(0, 1) : new Vector2(1, 0);
                if (m.Showing) c.Scale = flat;
                tween.TweenProperty(c, "scale", m.Showing ? Vector2.One : flat, d).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
                break;
            default:
                if (m.Animation.StartsWith("Slide")) Slide(tween, c, m, home, d);
                break;
        }
    }

    static void Slide(Tween tween, Control c, PanelMove m, Vector2 home, float d)
    {
        var parent = c.GetParentControl();
        if (parent == null) return;
        var dir = m.Animation[(m.Animation.IndexOf('_') + 1)..];
        var away = dir switch
        {
            "Top" => home with { Y = -c.Size.Y },
            "Bottom" => home with { Y = parent.Size.Y },
            "Left" => home with { X = -c.Size.X },
            _ => home with { X = parent.Size.X },
        };
        if (m.Showing) c.Position = away;
        tween.TweenProperty(c, "position", m.Showing ? home : away, d);
    }
}
