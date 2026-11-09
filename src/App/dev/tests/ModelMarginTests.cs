using System.Threading.Tasks;
using Godot;

namespace KTable.App;

static class ModelMarginTests
{
    const int W = 12, H = 4;
    const byte None = 0, Full = 255;

    [NodeTest]
    static async Task Paint_runs_on_into_the_margin_round_its_patch_so_no_base_colour_shows_at_the_edge(TestRunner r)
    {
        var uv = new[] { new Vector2(0, 0), new Vector2(0.25f, 0), new Vector2(0.25f, 1), new Vector2(0, 0), new Vector2(0.25f, 1), new Vector2(0, 1) };
        var paint = new byte[W * H * 4];
        var scale = new Vector2(W, H);
        var clip = new Rect2I(0, 0, W - 1, H - 1);
        for (int t = 0; t < 2; t++)
            ModelImport.Raster(uv[t * 3] * scale, uv[t * 3 + 1] * scale, uv[t * 3 + 2] * scale, W, clip, (x, y, _, _, _) =>
            {
                int o = (y * W + x) * 4;
                paint[o] = Full;
                paint[o + 3] = Full;
            });
        var edge = (1 * W + 6) * 4;
        Check.That(paint[edge + 3] == None, $"the texel beyond the patch starts bare: alpha {paint[edge + 3]}");
        var filled = ModelBake.PaintIntoMargins(paint, uv, 2, W, H);
        Check.That(filled[edge] == Full && filled[edge + 3] == Full, $"the margin takes the patch's paint: {filled[edge]},{filled[edge + 3]}");
        var far = (1 * W + 11) * 4;
        Check.That(filled[far + 3] == None, $"paint stops a few texels out: alpha {filled[far + 3]}");
        await r.Frames(1);
    }
}
