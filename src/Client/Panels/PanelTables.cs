namespace KTable.Client.Panels;

public sealed class PanelTable
{
    public Offsets Padding;
    public Anchor ChildAlignment = Anchor.UpperLeft;
    public string? RowImage, CellImage;
    public Paint RowColor = Paint.Clear, RowColorAlternate = Paint.Clear, CellColor = Paint.Clear, CellColorAlternate = Paint.Clear;
    public bool AlternateRows, AlternateCells, AddColumns = true, RemoveEmptyColumns = true, GlobalCellPadding = true, AutoHeight;
    public List<float> ColumnWidths = new();
    public Offsets CellPadding;
    public float CellSpacing;
    public PanelTable Clone()
    {
        var t = (PanelTable)MemberwiseClone();
        t.ColumnWidths = ColumnWidths.ToList();
        return t;
    }
}

public sealed class PanelRow
{
    public float PreferredHeight;
    public bool OwnBackground;
}

public sealed class PanelCell
{
    public int ColumnSpan = 1;
    public bool OwnBackground, OwnPadding;
}

public static class PanelTables
{
    public static void Lay(PanelBox table)
    {
        var t = table.Table!;
        var rows = table.Children.Where(c => c.Row != null && c.Active).ToList();
        if (rows.Count == 0) return;
        float width = table.RectSize.X, height = table.RectSize.Y;
        float spare = Math.Max(0, height - rows.Sum(r => r.Row!.PreferredHeight) - t.Padding.Vertical - t.CellSpacing * (rows.Count - 1));
        int open = rows.Count(r => r.Row!.PreferredHeight == 0);
        float share = open > 0 ? spare / open : 0;
        var heights = rows.Select(r => r.Row!.PreferredHeight > 0 ? r.Row.PreferredHeight : share).ToList();
        int columns = rows.Max(r => CellsOf(r).Sum(c => c.Cell!.ColumnSpan));
        if (t.RemoveEmptyColumns && columns < t.ColumnWidths.Count) t.ColumnWidths.RemoveRange(columns, t.ColumnWidths.Count - columns);
        float free = width - t.Padding.Horizontal - t.CellSpacing * (columns - 1);
        foreach (var w in t.ColumnWidths)
        {
            free -= w;
            if (free < 0) { free = 0; break; }
        }
        for (int i = 0; i < columns; i++) if (t.AddColumns && t.ColumnWidths.Count <= i) t.ColumnWidths.Add(0);
        int flexible = t.ColumnWidths.Count(c => c == 0);
        float each = flexible > 0 ? free / flexible : 0;
        var widths = Enumerable.Range(0, columns).Select(i => t.ColumnWidths.Count > i && t.ColumnWidths[i] != 0 ? t.ColumnWidths[i] : each).ToList();
        float y = -t.Padding.Top;
        for (int r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            if (!row.Row!.OwnBackground && row.Image != null)
            {
                row.Image.Sprite = t.RowImage;
                row.Image.Color = !t.AlternateRows || r % 2 == 0 ? t.RowColor : t.RowColorAlternate;
            }
            row.Pivot = new V2(0, 1);
            row.AnchorMin = row.AnchorMax = new V2(0, 1);
            row.Scale = V3.One;
            row.Rotation = V3.Zero;
            row.SizeDelta = new V2(width - t.Padding.Horizontal, heights[r]);
            row.Position = new V2(t.Padding.Left, y);
            row.Depth = 0;
            y -= heights[r] + t.CellSpacing;
            Cells(t, row, widths, width, heights[r]);
        }
        if (t.AutoHeight)
        {
            table.Pivot = table.Pivot with { Y = 1 };
            table.SizeDelta = table.SizeDelta with { Y = -y };
            table.AnchorMin = table.AnchorMin with { Y = 1 };
            table.AnchorMax = table.AnchorMax with { Y = 1 };
            table.Position = table.Position with { Y = 0 };
        }
    }

    static IEnumerable<PanelBox> CellsOf(PanelBox row) => row.Children.Where(c => c.Cell != null && c.Active);

    static void Cells(PanelTable t, PanelBox row, List<float> widths, float width, float height)
    {
        float x = 0;
        int column = 0;
        foreach (var cell in CellsOf(row).ToList())
        {
            int span = cell.Cell!.ColumnSpan;
            float w = 0;
            for (int i = column; i < column + span && i < widths.Count; i++) w += widths[i];
            w += (span - 1) * t.CellSpacing;
            w = Math.Min(w, width - x);
            if (t.GlobalCellPadding && !cell.Cell.OwnPadding && cell.Group != null) cell.Group.Padding = t.CellPadding;
            if (!cell.Cell.OwnBackground && cell.Image != null)
            {
                cell.Image.Sprite = t.CellImage;
                cell.Image.Color = !t.AlternateCells || column % 2 == 0 ? t.CellColor : t.CellColorAlternate;
            }
            cell.Pivot = new V2(0, 1);
            cell.SizeDelta = new V2(w, height);
            cell.Position = new V2(x, 0);
            cell.Depth = 0;
            cell.Scale = V3.One;
            cell.AnchorMin = cell.AnchorMax = new V2(0, 1);
            x += w + t.CellSpacing;
            column += span;
        }
    }
}
