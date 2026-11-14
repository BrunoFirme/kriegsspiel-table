using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Godot;
using KTable.Client;
using KTable.Core;
using KTable.Core.Model;

namespace KTable.App;

public static class GroundCard
{
    public const int Width = 1400;
    const float JpegQuality = 0.85f;
    const int NameChars = 32, MostPointsAFrame = 400000;

    public sealed record Spec(string File, Rect2 Uv, Vector2I Size);

    public static string FileFor(SheetDef sheet) =>
        Path.Combine(Caches.GroundCards, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sheet.Package + "\n" + sheet.Id)))[..NameChars].ToLowerInvariant() + ".jpg");

    public static Spec For(SheetDef sheet)
    {
        var crop = sheet.Crop ?? SheetGeometry.Whole;
        double aspect = sheet.Size.W > 0 ? sheet.Size.H / sheet.Size.W : 1;
        return new Spec(FileFor(sheet), new Rect2((float)crop.X, (float)crop.Y, (float)crop.W, (float)crop.H), new Vector2I(Width, Math.Clamp((int)Math.Round(Width * aspect), 1, Width * 4)));
    }

    static readonly CanvasItemMaterial LaidOver = new() { BlendMode = CanvasItemMaterial.BlendModeEnum.PremultAlpha };

    static async Task<Image?> Drawn(Node host, SubViewport vp)
    {
        host.AddChild(vp);
        try
        {
            await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            return GodotObject.IsInstanceValid(vp) ? vp.GetTexture().GetImage() : null;
        }
        finally
        {
            if (GodotObject.IsInstanceValid(vp)) vp.QueueFree();
        }
    }

    public static async Task<ImageTexture?> Built(Node host, Spec card, BuiltPainter built)
    {
        if (built.Layer(card.Uv, card.Size.X, GroundTiles.Style) is not { } layer) return null;
        var vp = new SubViewport { Size = card.Size, Disable3D = true, TransparentBg = true, RenderTargetClearMode = SubViewport.ClearMode.Once };
        var clip = new Control { Size = card.Size, ClipContents = true };
        vp.AddChild(clip);
        host.AddChild(vp);
        try
        {
            await Task.Run(layer.Make);
            for (int from = 0, to = 0; from < layer.Batches; from = to)
            {
                for (int points = 0; to < layer.Batches && points < MostPointsAFrame; to++) points += layer.PointsIn(to);
                var slice = layer.Slice(from, to);
                clip.AddChild(slice);
                vp.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
                await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                if (!GodotObject.IsInstanceValid(vp)) return null;
                slice.QueueFree();
            }
            return ImageTexture.CreateFromImage(vp.GetTexture().GetImage());
        }
        finally
        {
            layer.QueueFree();
            if (GodotObject.IsInstanceValid(vp)) vp.QueueFree();
        }
    }

    public static async Task Draw(Node host, ShaderMaterial bake, Spec card, Texture2D? built = null)
    {
        var vp = new SubViewport { Size = card.Size, Disable3D = true, TransparentBg = false, RenderTargetUpdateMode = SubViewport.UpdateMode.Once };
        var mat = (ShaderMaterial)bake.Duplicate();
        mat.SetShaderParameter("uv_rect", new Vector4(card.Uv.Position.X, card.Uv.Position.Y, card.Uv.Size.X, card.Uv.Size.Y));
        var quad = new ColorRect { Size = card.Size, Material = mat, ClipContents = true };
        if (built != null) quad.AddChild(new Sprite2D { Texture = built, Centered = false, TextureFilter = CanvasItem.TextureFilterEnum.Nearest, Material = LaidOver });
        vp.AddChild(quad);
        if (await Drawn(host, vp) is not { } img) return;
        img.FlipX();
        img.FlipY();
        var jpg = img.SaveJpgToBuffer(JpegQuality);
        await Task.Run(() => Write(card.File, jpg));
    }

    static void Write(string file, byte[] jpg)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            KTable.Core.Json.WriteAtomic(file, jpg);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { GD.PushWarning("the ground's card picture could not be kept: " + e.Message); }
    }
}
