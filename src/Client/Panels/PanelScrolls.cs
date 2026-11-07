namespace KTable.Client.Panels;

public static class PanelScrolls
{
    public const string Suffix = "#scroll";
    const float Slack = 0.01f;

    public static string Key(PanelBox view) => PanelScene.Key(view.Path!) + Suffix;

    public static (V2 Room, V2 View) Measure(PanelBox view)
    {
        var port = view.Part("Viewport")!;
        var content = port.Part("Content")!;
        return (new V2(Math.Max(0, content.RectSize.X - port.RectSize.X), Math.Max(0, content.RectSize.Y - port.RectSize.Y)), port.RectSize);
    }

    public static V2 Clamp(PanelBox view, V2 offset)
    {
        var (room, _) = Measure(view);
        return new V2(Math.Clamp(offset.X, -room.X, 0), Math.Clamp(offset.Y, 0, room.Y));
    }

    public static void Match(PanelBox view, IPanelMeter meter)
    {
        if (view.Part("Viewport")?.Part("Content") is not { } content || content.Children.Count != 1) return;
        var child = content.Children[0];
        content.SetSize(0, child.RectSize.X);
        content.SetSize(1, child.RectSize.Y);
        PanelLayout.Run(content, meter);
    }

    public static void Settle(PanelBox view, IReadOnlyDictionary<string, string>? local)
    {
        if (view.Path == null) return;
        Move(view, local != null && local.TryGetValue(Key(view), out var at) ? PanelValues.Vector2(at) : V2.Zero);
    }

    public static bool Scrolls(PanelBox view, int axis) => axis == 1
        ? view.Extra.GetValueOrDefault("vertical") is not { } v || PanelValues.Bool(v)
        : PanelValues.Bool(view.Extra.GetValueOrDefault("horizontal"));

    public static V2 Move(PanelBox view, V2 to)
    {
        if (view.Part("Viewport")?.Part("Content") is not { } content) return V2.Zero;
        bool vertical = Scrolls(view, 1), horizontal = Scrolls(view, 0);
        var movement = PanelScrollMotion.Movement(view.Extra.GetValueOrDefault("movementtype"));
        var offset = movement == ScrollMovement.Clamped ? Clamp(view, to) : to;
        if (!horizontal) offset = offset with { X = 0 };
        if (!vertical) offset = offset with { Y = 0 };
        content.Position += offset - view.Scrolled;
        view.Scrolled = offset;
        Place(content);
        var (room, port) = Measure(view);
        var size = new V2(content.RectSize.X > 0 ? Math.Clamp(port.X / content.RectSize.X, 0, 1) : 1, content.RectSize.Y > 0 ? Math.Clamp(port.Y / content.RectSize.Y, 0, 1) : 1);
        var normal = new V2(room.X > 0 ? -offset.X / room.X : 0, room.Y > 0 ? 1 - offset.Y / room.Y : 1);
        Bar(view.Part("Scrollbar Vertical"), 1, size.Y, normal.Y, room.Y > Slack, vertical);
        Bar(view.Part("Scrollbar Horizontal"), 0, size.X, normal.X, room.X > Slack, horizontal);
        return offset;
    }

    public static string Text(V2 offset) => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{offset.X} {offset.Y}");

    static void Bar(PanelBox? bar, int axis, float size, float value, bool needed, bool scrolls)
    {
        if (bar == null) return;
        int visibility = PanelValues.Int(bar.Extra.GetValueOrDefault("visibility") ?? (axis == 1 ? "0" : "1"));
        if (visibility > 0 && (!needed || !scrolls)) bar.Active = false;
        if (bar.Part("Handle") is not { } handle) return;
        float start = Math.Clamp(value, 0, 1) * (1 - size);
        handle.AnchorMin = V2.Zero.With(axis, start);
        handle.AnchorMax = V2.One.With(axis, start + size);
        Place(bar);
    }

    static void Place(PanelBox b)
    {
        b.Place();
        foreach (var c in b.Children) Place(c);
    }
}
