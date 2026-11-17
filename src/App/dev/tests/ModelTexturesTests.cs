using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace KTable.App;

static class ModelTexturesTests
{
    [NodeTest]
    static async Task A_model_whose_textures_sit_beside_it_finds_them_where_its_own_paths_point(TestRunner r)
    {
        await r.Frames(1);
        var root = Path.Combine(Path.GetTempPath(), "ktable-texfind-" + System.Guid.NewGuid().ToString("N"));
        var dir = Path.Combine(root, "model");
        Directory.CreateDirectory(dir);
        try
        {
            var model = Path.Combine(dir, "warrior.fbx");
            File.WriteAllBytes(model, Encoding.Latin1.GetBytes("\0\0\0..\\Render\\Tex\\skin.png\0\0\0D:\\author\\files\\cloth.tga\0"));
            File.WriteAllBytes(Path.Combine(dir, "skin.png"), [1, 2, 3]);
            File.WriteAllBytes(Path.Combine(dir, "cloth.tga"), [4, 5, 6]);
            string temp;
            using (var staged = ModelTextures.For(model))
            {
                var home = Path.GetDirectoryName(staged.Model)!;
                temp = home;
                Check.That(staged.Model != model, "a model whose textures are not where it points is loaded from a copy");
                Check.That(File.Exists(Path.GetFullPath(Path.Combine(home, "../Render/Tex/skin.png"))), "a relative texture lands where the model's own path points");
                Check.That(File.Exists(Path.Combine(home, "cloth.tga")), "a texture known only by another PC's path lands beside the copy");
            }
            Check.That(!Directory.Exists(temp), "the copy is gone once the model is read");
            Directory.CreateDirectory(Path.Combine(root, "Render", "Tex"));
            File.Move(Path.Combine(dir, "skin.png"), Path.Combine(root, "Render", "Tex", "skin.png"));
            File.WriteAllBytes(model, Encoding.Latin1.GetBytes("\0\0\0..\\Render\\Tex\\skin.png\0"));
            using var direct = ModelTextures.For(model);
            Check.That(direct.Model == model, "a model whose textures are where it points is read in place");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
