using System;
using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class PieceTiles
{
    const int IconSide = 72, ColumnWidth = 84, FontSize = 11, ThumbMost = 128;

    public static ItemList List()
    {
        var l = new ItemList
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill, IconMode = ItemList.IconModeEnum.Top, MaxColumns = 0,
            FixedIconSize = new Vector2I(IconSide, IconSide), FixedColumnWidth = ColumnWidth, SameColumnWidth = true, MaxTextLines = 2,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, AllowReselect = true,
        };
        l.AddThemeFontSizeOverride("font_size", FontSize);
        return l;
    }

    public static Control Host(ItemList list)
    {
        var host = new Control
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            ClipContents = true, CustomMinimumSize = new Vector2(0, 80),
        };
        host.AddChild(list);
        list.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        return host;
    }

    public static void Picture(Session s, ItemList list, int idx, PieceDef d, PackManifest? p, Func<bool> current)
    {
        if (EffectFx.ListIcon(d) is { } fxIcon) { list.SetItemIcon(idx, fxIcon); return; }
        if (p == null) return;
        void Icon(Texture2D t)
        {
            if (current() && idx < list.ItemCount) list.SetItemIcon(idx, t);
        }
        if (d.Kind == "model") ModelPictures.Show(s.Assets, p, d, Icon, () => Flat(s, d, p, Icon));
        else Flat(s, d, p, Icon);
    }

    static void Flat(Session s, PieceDef d, PackManifest p, Action<Texture2D> icon)
    {
        var img = d.Image ?? d.Texture ?? d.States.Select(st => st.Image ?? st.Texture).FirstOrDefault(h => h != null);
        if (img == null || !p.Assets.TryGetValue(img, out var a)) return;
        if (s.Assets.Texture(p.Id, a.File, icon, mipmaps: false, maxSize: ThumbMost) is { } tex) icon(tex);
    }

    public static Vector2 Centre(ItemList list, int i)
    {
        var r = list.GetItemRect(i, true);
        return list.GetGlobalRect().Position + r.Position + r.Size / 2;
    }
}
