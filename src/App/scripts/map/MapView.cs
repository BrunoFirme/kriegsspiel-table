using System;
using System.Globalization;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class MapView
{
    public const int DefaultPixels = 1024, MinPixels = 256, MaxPixels = 4096;
    public const double MinSize = 0.5, MaxSize = 50000;
    const int TargetLines = 8, LabelSize = 15, LabelPad = 3, CornerX = 64, CornerY = 26;
    const float CameraHeight = 4000;
    public static readonly Color Line = new(0.06f, 0.06f, 0.09f, 0.6f);
    static readonly Color Tag = new(0.98f, 0.96f, 0.9f, 0.9f), Ink = new(0.06f, 0.06f, 0.09f);

    public static double Step(double size)
    {
        var raw = size / TargetLines;
        var mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        foreach (var m in new[] { 1.0, 2.0, 5.0, 10.0 })
            if (m * mag >= raw) return m * mag;
        return 10 * mag;
    }

    public static string Num(double v) => Math.Round(v, 6).ToString("0.###", CultureInfo.InvariantCulture);

    public static Vector2 Pixel(Vec2 at, Vec2 centre, double size, int pixels)
    {
        var k = pixels / size;
        return new Vector2((float)((at.X - (centre.X - size / 2)) * k), (float)((centre.Z + size / 2 - at.Z) * k));
    }

    public static string NorthWords(double north) => north == 0 ? "North is up" : FormattableString.Invariant($"North is {north:0}° clockwise from up");

    public static async Task<byte[]?> Render(Node host, Node3D table, Vec2 centre, double size, double step, int pixels, double north)
    {
        var vp = new SubViewport
        {
            Size = new Vector2I(pixels, pixels), World3D = table.GetViewport().FindWorld3D(),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always, TransparentBg = false, Msaa3D = Viewport.Msaa.Msaa4X,
        };
        var cam = new Camera3D
        {
            Projection = Camera3D.ProjectionType.Orthogonal, Size = (float)size, Near = 1, Far = CameraHeight * 2,
            KeepAspect = Camera3D.KeepAspectEnum.Height, CullMask = Table.WorldLayer | Table.TextLayer,
        };
        vp.AddChild(cam);
        var grid = new Control { Size = new Vector2(pixels, pixels), MouseFilter = Control.MouseFilterEnum.Ignore };
        grid.Draw += () => DrawGrid(grid, centre, size, step, pixels, north);
        vp.AddChild(grid);
        host.AddChild(vp);
        try
        {
            var c = Coords.ToWorld(centre);
            cam.GlobalPosition = c + Vector3.Up * CameraHeight;
            cam.LookAt(c, Vector3.Forward);
            await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            if (!GodotObject.IsInstanceValid(vp)) return null;
            var img = vp.GetTexture().GetImage();
            return img == null || img.IsEmpty() ? null : img.SavePngToBuffer();
        }
        finally
        {
            if (GodotObject.IsInstanceValid(vp)) vp.QueueFree();
        }
    }

    static void DrawGrid(Control g, Vec2 centre, double size, double step, int pixels, double north)
    {
        var font = ThemeDB.FallbackFont;
        double left = centre.X - size / 2, bottom = centre.Z - size / 2;
        for (var x = Math.Ceiling(left / step) * step; x <= left + size; x += step)
        {
            var px = Pixel(new Vec2(x, centre.Z), centre, size, pixels).X;
            g.DrawLine(new Vector2(px, 0), new Vector2(px, pixels), Line, 1);
            if (px > CornerX) Label(g, font, "x " + Num(x), new Vector2(px + LabelPad, LabelPad));
        }
        for (var z = Math.Ceiling(bottom / step) * step; z <= bottom + size; z += step)
        {
            var py = Pixel(new Vec2(centre.X, z), centre, size, pixels).Y;
            g.DrawLine(new Vector2(0, py), new Vector2(pixels, py), Line, 1);
            if (py > CornerY) Label(g, font, "z " + Num(z), new Vector2(LabelPad, py + LabelPad));
        }
        var northWords = NorthWords(north).ToLowerInvariant();
        Label(g, font, northWords, new Vector2(pixels - font.GetStringSize(northWords, HorizontalAlignment.Left, -1, LabelSize).X - 4 * LabelPad, pixels - LabelSize - 3 * LabelPad));
    }

    static void Label(Control g, Font font, string text, Vector2 at)
    {
        var w = font.GetStringSize(text, HorizontalAlignment.Left, -1, LabelSize).X;
        g.DrawRect(new Rect2(at, new Vector2(w + 2 * LabelPad, LabelSize + 2 * LabelPad)), Tag);
        g.DrawString(font, at + new Vector2(LabelPad, LabelSize), text, HorizontalAlignment.Left, -1, LabelSize, Ink);
    }
}
