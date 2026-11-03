using KTable.Core.Model;

namespace KTable.Client.Studio;

public static class PiecePrinting
{
    public static FaceBox Band(bool boxy) => boxy ? new FaceBox { X = 0.07, Y = 0.02, W = 0.86, H = 0.2 } : new FaceBox { X = 0.22, Y = 0.3, W = 0.56, H = 0.3 };

    public static bool CardTitle(PieceDef d) => !d.TextLines.Any(t => t.Place?.Side == PrintPlace.Back);

    public static List<int> CardCounters(PieceDef d) =>
        Enumerable.Range(0, d.Counters.Count).Where(i => d.Counters[i].Place == null).ToList();

    public static (FaceBox? title, List<FaceBox> rows) Card(bool boxy, bool title, int rows)
    {
        double cw = boxy ? 0.88 : 0.62, ch = boxy ? 0.8 : 0.62, top = boxy ? 0.04 : 0.19, left = (1 - cw) / 2;
        double titleH = !title ? 0 : rows > 0 ? ch * 0.24 : ch * 0.4;
        double rowH = rows > 0 ? (ch - titleH) / rows : 0;
        var list = new List<FaceBox>();
        for (int i = 0; i < rows; i++) list.Add(new FaceBox { X = left, Y = top + titleH + rowH * i, W = cw, H = rowH });
        return (title ? new FaceBox { X = left, Y = top, W = cw, H = titleH } : null, list);
    }

    public static List<FaceBox> CardRows(PieceDef d, bool boxy) => Card(boxy, CardTitle(d), CardCounters(d).Count).rows;

    public static void PinDefaults(PieceDef d, FaceBox band, bool boxy)
    {
        var lines = d.TextLines.Where(t => t.Place == null).ToList();
        for (int i = 0; i < lines.Count; i++)
            lines[i].Place = Rounded(new PrintPlace { Side = PrintPlace.Front, X = band.X, Y = band.Y + band.H * i / lines.Count, W = band.W, H = band.H / lines.Count });
        if (d.Script != null) return;
        var counters = CardCounters(d);
        var rows = CardRows(d, boxy);
        for (int i = 0; i < counters.Count; i++) d.Counters[counters[i]].Place = Rounded(PrintPlace.On(PrintPlace.Back, rows[i]));
    }

    static PrintPlace Rounded(PrintPlace p) => new() { Side = p.Side, X = Math.Round(p.X, 4), Y = Math.Round(p.Y, 4), W = Math.Round(p.W, 4), H = Math.Round(p.H, 4) };

    public static void SwapSides(PieceDef d, FaceBox band, bool boxy)
    {
        PinDefaults(d, band, boxy);
        (d.Image, d.Back) = (d.Back, d.Image);
        foreach (var s in d.States) (s.Image, s.Back) = (s.Back, s.Image);
        foreach (var c in d.Counters) c.Place = c.Place?.Turned();
        foreach (var t in d.TextLines) t.Place = t.Place?.Turned();
    }

    public static PrintPlace Fresh(string side, int index) =>
        new() { Side = side, X = 0.1, Y = 0.1 + index % 6 * 0.13, W = 0.8, H = 0.15 };
}
