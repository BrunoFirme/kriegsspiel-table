using Godot;

namespace KTable.App;

public static class HelpTags
{
    const string Meta = "help";

    public static void Tag(Control c, string target)
    {
        c.SetMeta(Meta, target);
        if (!c.TooltipText.Contains("F1")) c.TooltipText += (c.TooltipText.Length > 0 ? "\n" : "") + "F1: help";
    }

    public static string? Under(Viewport vp)
    {
        for (var n = vp.GuiGetHoveredControl() as Node; n != null; n = n.GetParent())
            if (n is Control c && c.HasMeta(Meta)) return c.GetMeta(Meta).AsString();
        return null;
    }

    public static void Open(Node parent, string fallback) => GuideReader.Spec(parent, Under(parent.GetViewport()) ?? fallback);
}
