namespace KTable.Core.Model;

public static class NoteCard
{
    public const string Tag = "note", TitleLine = "Name", BodyLine = "Note", DefaultName = "Note";
    public const double Width = 7.0, Height = 4.3, Thickness = 0.05;
    public const int PixelsPerUnit = 200;

    public const double TitleTop = 0.035, TitleBottom = 0.14, HeadRule = 0.155, FirstRule = 0.265, RuleStep = 0.0745, BottomMargin = 0.035, SideMargin = 0.03;
    public const double BindingBottom = 0.033, Perforation = 0.041;

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
        const int RuleThickness = 2, HeadThick = 5, HeadThin = 2, HeadGap = 9, StitchThick = 3, StitchDash = 14, StitchGap = 10, HoleStep = 24;
        const double HoleRadius = 3.2;
        var paper = (r: (byte)186, g: (byte)180, b: (byte)156);
        var band = (r: (byte)175, g: (byte)171, b: (byte)143);
        var cloth = (r: (byte)26, g: (byte)25, b: (byte)26);
        var thread = (r: (byte)82, g: (byte)80, b: (byte)71);
        var hole = (r: (byte)152, g: (byte)144, b: (byte)111);
        var print = (r: (byte)39, g: (byte)34, b: (byte)29);
        var rule = (r: (byte)149, g: (byte)141, b: (byte)106);
        int inset = (int)(SideMargin * w);
        double binding = BindingBottom * h, holesAt = Perforation * h, head = HeadRule * h;
        static bool IsRule(int y, double at, int thick) => Math.Abs(y - at) < thick / 2.0 + 0.01;
        bool IsStitch(int x, int y) => IsRule(y, binding / 2, StitchThick) && x >= inset && x < w - inset && (x - inset) % (StitchDash + StitchGap) < StitchDash;
        bool IsHole(int x, int y)
        {
            double nearest = Math.Floor((double)x / HoleStep) * HoleStep + HoleStep / 2.0, dx = x - nearest, dy = y - holesAt;
            return dx * dx + dy * dy < HoleRadius * HoleRadius;
        }
        bool IsBodyRule(int x, int y)
        {
            if (x < inset || x >= w - inset) return false;
            for (int i = 0; i < Lines; i++)
                if (IsRule(y, RuleAt(i) * h, RuleThickness)) return true;
            return false;
        }
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var c = y < binding ? IsStitch(x, y) ? thread : cloth
                    : IsHole(x, y) ? hole
                    : IsRule(y, head, HeadThick) || IsRule(y, head + HeadGap, HeadThin) ? print
                    : y < head ? band
                    : IsBodyRule(x, y) ? rule : paper;
                int o = (y * w + x) * 4;
                px[o] = c.r;
                px[o + 1] = c.g;
                px[o + 2] = c.b;
                px[o + 3] = 255;
            }
        return (px, w, h);
    }
}
