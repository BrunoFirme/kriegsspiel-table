using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class WaySignTests
{
    const float Side = 220, CloseM = 40, FarM = 900, InkFrom = 0.08f, BankM = 13, BankWideM = 17, From = 0.15f, To = 0.85f;
    const int LeastInked = 12;
    static readonly Vector2 Corner = new(60, 60);
    static readonly (string Kind, string Class)[] Signs =
    [
        (PartKinds.Line, Grades.Stairs), (PartKinds.Line, Grades.Ramp), (PartKinds.Line, Grades.Holloway), (PartKinds.Line, Grades.Tunnel), (PartKinds.Line, Grades.Viaduct),
        (PartKinds.Area, Grades.Cutting), (PartKinds.Area, Grades.Embankment),
    ];

    static BuiltPainter.Shape Shape(string kind, string cls, float viewM, Vector2[] uv)
    {
        var box = new Rect2(uv[0], Vector2.Zero);
        foreach (var p in uv) box = box.Expand(p);
        return new BuiltPainter.Shape(kind, cls, uv, box, 0, BuiltHand.Rank(kind, cls), 1 / viewM, Manners.Western);
    }

    static Vector2[] Banked(float viewM)
    {
        float edge = BankM / viewM, wide = BankWideM / viewM;
        return [new(From, 0.5f - edge), new(0.5f, 0.5f - wide), new(To, 0.5f - edge), new(To, 0.5f + edge), new(0.5f, 0.5f + wide), new(From, 0.5f + edge)];
    }

    static async Task<Image> Shot(TestRunner r)
    {
        await r.Frames(3);
        return r.GetViewport().GetTexture().GetImage();
    }

    static int Inked(Image shot, Image blank)
    {
        int inked = 0;
        for (int y = (int)Corner.Y; y < Corner.Y + Side; y++)
            for (int x = (int)Corner.X; x < Corner.X + Side; x++)
            {
                Color a = shot.GetPixel(x, y), b = blank.GetPixel(x, y);
                if (Mathf.Abs(a.R - b.R) + Mathf.Abs(a.G - b.G) + Mathf.Abs(a.B - b.B) > InkFrom) inked++;
            }
        return inked;
    }

    [ScreenTest]
    static async Task Stairs_holloways_cuttings_embankments_tunnels_and_viaducts_are_drawn_in_every_map_style_near_and_far(TestRunner r)
    {
        var blank = await Shot(r);
        for (int style = 0; style < MapStyles.ShaderOrder.Count; style++)
            foreach (float viewM in new[] { CloseM, FarM })
                foreach (var (kind, cls) in Signs)
                {
                    if (cls == Grades.Holloway && MapStyles.ShaderOrder[style] == MapStyles.Ancient) continue;
                    var uv = kind == PartKinds.Area ? Banked(viewM) : [new(From, 0.5f), new(To, 0.5f)];
                    var layer = new BuiltLayer([Shape(kind, cls, viewM, uv)], new Rect2(0, 0, 1, 1), Side, Side, 1, style) { Position = Corner };
                    r.AddChild(layer);
                    int inked = Inked(await Shot(r), blank);
                    layer.QueueFree();
                    Check.That(inked >= LeastInked, $"{cls} in the {MapStyles.All.First(s => s.Id == MapStyles.ShaderOrder[style]).Name} style {viewM} m across draws {inked} pixels");
                }
        await r.Frame();
    }
}
