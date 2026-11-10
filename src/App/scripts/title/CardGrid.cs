using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace KTable.App;

sealed partial class CardGrid : Container
{
    public float Least { get; init; } = TitleStyle.CardWidth;
    public float Gap { get; init; } = Ui.Gap.Block;

    public CardGrid() => SizeFlagsHorizontal = SizeFlags.ExpandFill;

    public override Vector2 _GetMinimumSize() => new(Least, Lay(place: false));

    public override void _Notification(int what)
    {
        if (what == NotificationSortChildren) Lay(place: true);
        else if (what == NotificationResized) UpdateMinimumSize();
    }

    List<Control> Cards() => GetChildren().OfType<Control>().Where(c => c.Visible && !c.TopLevel).ToList();

    public int Columns => Math.Max(1, (int)((Math.Max(Size.X, Least) + Gap) / (Least + Gap)));

    float Lay(bool place)
    {
        var cards = Cards();
        if (cards.Count == 0) return 0;
        int cols = Columns;
        float width = (Math.Max(Size.X, Least) - Gap * (cols - 1)) / cols, y = 0;
        for (int first = 0; first < cards.Count; first += cols)
        {
            var row = cards.Skip(first).Take(cols).ToList();
            float tall = row.Max(c => c.GetCombinedMinimumSize().Y);
            if (place)
                for (int i = 0; i < row.Count; i++) FitChildInRect(row[i], new Rect2(i * (width + Gap), y, width, tall));
            y += tall + Gap;
        }
        return y - Gap;
    }
}
