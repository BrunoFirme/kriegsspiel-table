using System;
using System.Linq;
using Godot;

namespace KTable.App;

static class OnScreen
{
    const int Margin = 12;

    public static void Watch(Node n)
    {
        if (n is not Window w || w is Popup || w == w.GetTree()?.Root) return;
        void Soon() => Callable.From(() => Fit(w)).CallDeferred();
        w.VisibilityChanged += () => { if (w.Visible) Soon(); };
        Soon();
    }

    public static void FitAll(Window root)
    {
        foreach (var w in root.FindChildren("*", nameof(Window), true, false).OfType<Window>())
            if (w is not Popup && w.Visible) Fit(w);
    }

    public static void Fit(Window w)
    {
        if (!GodotObject.IsInstanceValid(w) || !w.Visible || !w.IsInsideTree() || !w.IsEmbedded()) return;
        var room = w.GetTree().Root.GetVisibleRect().Size;
        int title = w.Borderless ? 0 : w.GetThemeConstant("title_height");
        var most = new Vector2I(Math.Max(1, (int)room.X - 2 * Margin), Math.Max(1, (int)room.Y - 2 * Margin - title));
        if (w.Size.X > most.X || w.Size.Y > most.Y)
        {
            w.MinSize = new Vector2I(Math.Min(w.MinSize.X, most.X), Math.Min(w.MinSize.Y, most.Y));
            w.Size = new Vector2I(Math.Min(w.Size.X, most.X), Math.Min(w.Size.Y, most.Y));
        }
        int x = Math.Clamp(w.Position.X, Margin, Math.Max(Margin, (int)room.X - Margin - w.Size.X));
        int y = Math.Clamp(w.Position.Y, Margin + title, Math.Max(Margin + title, (int)room.Y - Margin - w.Size.Y));
        if (x != w.Position.X || y != w.Position.Y) w.Position = new Vector2I(x, y);
    }
}
