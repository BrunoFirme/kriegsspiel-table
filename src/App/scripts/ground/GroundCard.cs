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
    const int NameChars = 32;

    public sealed record Spec(string File, Rect2 Uv, Vector2I Size);

    public static string FileFor(SheetDef sheet) =>
        Path.Combine(Caches.GroundCards, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sheet.Package + "\n" + sheet.Id)))[..NameChars].ToLowerInvariant() + ".jpg");

    public static Spec For(SheetDef sheet)
    {
        var crop = sheet.Crop ?? SheetGeometry.Whole;
        double aspect = sheet.Size.W > 0 ? sheet.Size.H / sheet.Size.W : 1;
        return new Spec(FileFor(sheet), new Rect2((float)crop.X, (float)crop.Y, (float)crop.W, (float)crop.H), new Vector2I(Width, Math.Clamp((int)Math.Round(Width * aspect), 1, Width * 4)));
    }

    public static async Task Draw(Node host, ShaderMaterial bake, Spec card)
    {
        var vp = new SubViewport { Size = card.Size, Disable3D = true, TransparentBg = false, RenderTargetUpdateMode = SubViewport.UpdateMode.Once };
        var mat = (ShaderMaterial)bake.Duplicate();
        mat.SetShaderParameter("uv_rect", new Vector4(card.Uv.Position.X, card.Uv.Position.Y, card.Uv.Size.X, card.Uv.Size.Y));
        vp.AddChild(new ColorRect { Size = card.Size, Material = mat });
        host.AddChild(vp);
        try
        {
            await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            if (!GodotObject.IsInstanceValid(vp)) return;
            var img = vp.GetTexture().GetImage();
            img.FlipX();
            img.FlipY();
            var jpg = img.SaveJpgToBuffer(JpegQuality);
            await Task.Run(() => Write(card.File, jpg));
        }
        finally
        {
            if (GodotObject.IsInstanceValid(vp)) vp.QueueFree();
        }
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
