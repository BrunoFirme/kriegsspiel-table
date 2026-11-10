using System.IO;
using System.Text;
using System.Threading.Tasks;
using Godot;
using KTable.Client.Studio;

namespace KTable.App;

static class ModelPictureTests
{
    static string Folder()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ktable-model-pictures", System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    static string Cube(string dir)
    {
        var obj = new StringBuilder();
        for (int i = 0; i < 8; i++) obj.Append($"v {((i & 1) == 0 ? -0.5 : 0.5)} {((i & 2) == 0 ? -0.5 : 0.5)} {((i & 4) == 0 ? -0.5 : 0.5)}\n");
        obj.Append("vt 0.5 0.5\n");
        foreach (var f in new[] { "1 2 4 3", "5 7 8 6", "1 5 6 2", "3 4 8 7", "1 3 7 5", "2 6 8 4" })
        {
            var c = f.Split(' ');
            obj.Append($"f {c[0]}/1 {c[1]}/1 {c[2]}/1 {c[3]}/1\n");
        }
        var path = Path.Combine(dir, "cube.obj");
        File.WriteAllText(path, obj.ToString().Replace(',', '.'));
        return path;
    }

    static string Plain(string dir, string name, Color c, int side)
    {
        var img = Image.CreateEmpty(side, side, false, Image.Format.Rgba8);
        img.Fill(c);
        var path = Path.Combine(dir, name);
        img.SavePng(path);
        return path;
    }

    static Color Middle(Image img) => img.GetPixel(img.GetWidth() / 2, img.GetHeight() / 2);

    [NodeTest]
    static async Task A_model_is_drawn_in_its_paint_once_and_then_read_from_the_cache(TestRunner r)
    {
        var dir = Folder();
        var mesh = Cube(dir);
        var cached = Path.Combine(dir, "cache", "model.png");
        var drawn = ModelPictures.Draw(mesh, Plain(dir, "paint.png", new Color(0.8f, 0.1f, 0.1f), 4), null, cached);
        Check.That(drawn != null, "the model is drawn");
        Check.Equal(ModelThumb.Out, drawn!.GetWidth(), "the picture is the thumbnail size");
        var mid = Middle(drawn);
        Check.That(mid.A > 0.99f && mid.R > 2 * mid.G, $"the middle shows the model in its red paint: {mid}");
        Check.That(drawn.GetPixel(0, 0).A < 0.01f, "the corner is clear");
        Check.That(File.Exists(cached), "the picture is kept in the cache");
        File.Delete(mesh);
        Check.That(ModelPictures.Draw(mesh, null, null, cached) is { } again && Middle(again).R > 2 * Middle(again).G, "a second call reads the kept picture");
        await r.Frames(1);
    }

    [NodeTest]
    static async Task The_studio_s_own_picture_of_a_model_wins_and_a_broken_model_gives_none(TestRunner r)
    {
        var dir = Folder();
        var baked = Plain(dir, "baked.png", new Color(0.1f, 0.2f, 0.9f), ModelThumb.Out);
        var own = ModelPictures.Draw(Cube(dir), null, baked, null);
        Check.That(own != null && Middle(own).B > 2 * Middle(own).R, "the studio's picture is used as it is");
        var broken = Path.Combine(dir, "broken.obj");
        File.WriteAllText(broken, "v 1 2\nf 1 x 3\n");
        var cached = Path.Combine(dir, "cache", "broken.png");
        Check.That(ModelPictures.Draw(broken, null, null, cached) == null, "a model that cannot be read gives no picture");
        Check.That(!File.Exists(cached), "and nothing is kept for it");
        await r.Frames(1);
    }
}
