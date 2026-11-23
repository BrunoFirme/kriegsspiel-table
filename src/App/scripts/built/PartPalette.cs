using System;
using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed partial class PartSwatch : Control
{
    const float Darker = 0.12f;
    readonly Color _ground;

    public PartSwatch(Catalogue.Entry entry, int style, int manner, float side)
    {
        CustomMinimumSize = Ui.Px(side, side);
        ClipContents = true;
        MouseFilter = MouseFilterEnum.Ignore;
        _ground = Color.FromHtml(GroundKinds.Color(GroundKinds.Of("grass") ?? 1, MapStyles.ShaderOrder[Math.Clamp(style, 0, MapStyles.ShaderOrder.Count - 1)])).Darkened(Darker);
        var site = new BuiltSite { Width = 1, Height = 1, UnitsPerMetre = 1 / entry.ViewM, Manner = manner };
        var shapes = BuiltThings.Made(site, entry.Id, new Spot(0.5, 0.5)).Select(part =>
        {
            var uv = BuiltGeometry.Points(part.At).Select(p => new Vector2(p.X, p.Y) / BuiltGeometry.PerUnit).ToArray();
            var box = new Rect2(uv[0], Vector2.Zero);
            foreach (var p in uv) box = box.Expand(p);
            return new BuiltPainter.Shape(part.Kind, part.Class, uv, box, part.Turn, BuiltHand.Rank(part.Kind, part.Class), (float)(1 / entry.ViewM), manner);
        }).OrderBy(s => s.Rank).ToList();
        AddChild(new BuiltLayer(shapes, new Rect2(0, 0, 1, 1), side, side, 1, style));
    }

    public override void _Draw() => DrawRect(new Rect2(Vector2.Zero, Size), _ground);
}

static class PartPalette
{
    const int TileHigh = 70, Swatch = 44, Inset = 3, ToARow = 3, MostLinksToARow = 5;

    static Button Tile(Catalogue.Entry entry, string name, bool chosen, int style, int manner)
    {
        var b = new Button
        {
            ToggleMode = true, ButtonPressed = chosen, FocusMode = Control.FocusModeEnum.None, TooltipText = name, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = Ui.Px(0, TileHigh), ClipText = true,
        };
        Ui.MarkTool(b);
        var stack = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        stack.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect, Control.LayoutPresetMode.Minsize, Inset);
        stack.AddThemeConstantOverride("separation", Ui.Gap.None);
        var centre = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        centre.AddChild(new PartSwatch(entry, style, manner, Swatch));
        stack.AddChild(centre);
        stack.AddChild(new Label { Text = name, HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore, ClipText = true });
        b.AddChild(stack);
        return b;
    }

    public static VBoxContainer Of(bool blocksOnly, string open, string chosen, int style, int manner, Func<string, string?> named, Action<string> turn, Action<string> pick)
    {
        var sections = Catalogue.Every(blocksOnly).ToList();
        var shown = sections.FirstOrDefault(s => s.Id == open) ?? sections.FirstOrDefault(s => s.Entries.Any(e => e.Id == chosen)) ?? sections[0];
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Tight));
        int rows = (sections.Count + MostLinksToARow - 1) / MostLinksToARow, linksToARow = (sections.Count + rows - 1) / rows;
        for (int i = 0; i < sections.Count; i += linksToARow)
            box.AddChild(Ui.Links(sections.Skip(i).Take(linksToARow).Select(s => (s.Name, s.Id == shown.Id, s.Tip, (Action)(() => turn(s.Id)))).ToList()));
        foreach (var era in shown.Entries.GroupBy(e => e.Era))
        {
            var entries = era.ToList();
            if (Catalogue.EraName(era.Key) is { } name) box.AddChild(EraMark(name));
            for (int i = 0; i < entries.Count; i += ToARow)
            {
                var row = new HBoxContainer();
                row.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Tight));
                for (int k = i; k < i + ToARow; k++)
                {
                    if (k >= entries.Count)
                    {
                        row.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
                        continue;
                    }
                    var entry = entries[k];
                    var tile = Tile(entry, named(entry.Id) ?? entry.Name, entry.Id == chosen, style, manner);
                    tile.Pressed += () => pick(entry.Id);
                    row.AddChild(tile);
                }
                box.AddChild(row);
            }
        }
        return box;
    }

    internal static Label EraMark(string name)
    {
        var mark = new Label { Text = name, Modulate = Ui.Muted };
        mark.AddThemeFontSizeOverride("font_size", Ui.Px(EraFont));
        return mark;
    }

    const int EraFont = 11;
}
