namespace KTable.Core.Model;

public static class NoteCard
{
    public const string Tag = "note", TitleLine = "Name", BodyLine = "Note", DefaultName = "Note";
    public const double Width = 7.0, Height = 4.3, Thickness = 0.05;
    public const int PixelsPerUnit = 200;

    public const double TitleTop = 0.035, TitleBottom = 0.14, RedRule = 0.155, FirstRule = 0.265, RuleStep = 0.0745, BottomMargin = 0.035, SideMargin = 0.03;

    public static int Lines => (int)Math.Floor((1 - BottomMargin - FirstRule) / RuleStep) + 1;

    public static double RuleAt(int line) => FirstRule + line * RuleStep;

    public static bool Is(PieceDef? def) => def?.Tags.Contains(Tag) == true;

    public static List<TextLineDef> TextLines(string title = "", string body = "") => new()
    {
        new TextLineDef { Name = TitleLine, Default = title },
        new TextLineDef { Name = BodyLine, Default = body },
    };

    public static (byte[] Rgba, int W, int H) Paint()
    {
        int w = (int)(Width * PixelsPerUnit), h = (int)(Height * PixelsPerUnit);
        var px = new byte[w * h * 4];
        const int Corner = 18, Edge = 4, RuleThickness = 2, RedThickness = 3;
        var paper = (r: (byte)251, g: (byte)250, b: (byte)244);
        var edge = (r: (byte)176, g: (byte)176, b: (byte)168);
        var blue = (r: (byte)120, g: (byte)150, b: (byte)215);
        var red = (r: (byte)215, g: (byte)90, b: (byte)90);
        int inset = (int)(SideMargin * w * 0.3);
        bool IsRule(int y, double at, int thick) => Math.Abs(y - at * h) < thick / 2.0 + 0.01;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double dx = Math.Max(0, Math.Max(Corner - x, x - (w - 1 - Corner))), dy = Math.Max(0, Math.Max(Corner - y, y - (h - 1 - Corner)));
                double fromEdge = Corner - Math.Sqrt(dx * dx + dy * dy);
                if (dx > 0 && dy > 0 && fromEdge < 0) continue;
                int border = (int)Math.Min(Math.Min(x, w - 1 - x), Math.Min(y, h - 1 - y));
                var c = paper;
                if (border < Edge || (dx > 0 && dy > 0 && fromEdge < Edge)) c = edge;
                else if (x >= inset && x < w - inset)
                {
                    if (IsRule(y, RedRule, RedThickness)) c = red;
                    else
                        for (int i = 0; i < Lines; i++)
                            if (IsRule(y, RuleAt(i), RuleThickness)) { c = blue; break; }
                }
                int o = (y * w + x) * 4;
                px[o] = c.r;
                px[o + 1] = c.g;
                px[o + 2] = c.b;
                px[o + 3] = 255;
            }
        return (px, w, h);
    }
}
