using System.Threading.Tasks;
using Godot;
using KTable.Client.Studio;

namespace KTable.App;

static class CanvasTests
{
    static void Drag(StudioCanvas c, Vector2 fromDoc, Vector2 toDoc)
    {
        Vector2 a = c.ToScreen(fromDoc), b = c.ToScreen(toDoc);
        c._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = a });
        c._GuiInput(new InputEventMouseMotion { Position = (a + b) / 2, Relative = (b - a) / 2, ButtonMask = MouseButtonMask.Left });
        c._GuiInput(new InputEventMouseMotion { Position = b, Relative = (b - a) / 2, ButtonMask = MouseButtonMask.Left });
        c._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = b });
    }

    static Image Picture()
    {
        var img = Image.CreateEmpty(40, 20, false, Image.Format.Rgba8);
        img.Fill(Colors.Red);
        return img;
    }

    [NodeTest]
    static async Task A_picture_s_handles_crop_it_and_scale_it_as_dragged(TestRunner r)
    {
        var c = new StudioCanvas { Size = new Vector2(800, 600) };
        r.AddChild(c);
        await r.Frame();
        c.Open(new ArtDoc { W = 200, H = 200 });
        c.Fit();
        var cropped = c.AddImage(Picture(), "cropped", new Vector2(60, 60));
        var scaled = c.AddImage(Picture(), "scaled", new Vector2(140, 140));

        c.SetTool("crop");
        c.Select(cropped.Id);
        await r.Frame();
        Drag(c, new Vector2(40, 60), new Vector2(50, 60));
        Check.Equal("10 0 30 20", string.Join(" ", c.Doc.Layer(cropped.Id)!.Crop ?? new float[0]), "the crop after the left edge was dragged in");

        c.SetTool("move");
        c.Select(scaled.Id);
        await r.Frame();
        Drag(c, new Vector2(160, 150), new Vector2(180, 160));
        var s = c.Doc.Layer(scaled.Id)!;
        Check.That(Mathf.Abs(s.Sx - 1.5f) < 0.01f && Mathf.Abs(s.Sy - 1.5f) < 0.01f, $"scaled by half again both ways (got {s.Sx}, {s.Sy})");
        Check.That(Mathf.Abs(s.X - 150) < 0.5f && Mathf.Abs(s.Y - 145) < 0.5f, $"the top-left corner stayed put (centre now {s.X}, {s.Y})");
        await r.Frames(2);

        var band = new StudioCanvas.PrintBox("band", "Name", new KTable.Core.Model.FaceBox { X = 0.1, Y = 0.1, W = 0.2, H = 0.1 });
        var counter = new StudioCanvas.PrintBox("c0", "Strength", new KTable.Core.Model.FaceBox { X = 0.6, Y = 0.6, W = 0.2, H = 0.1 });
        c.Prints = new() { band, counter };
        c.Chosen = "band";
        string? moved = null, chosen = null;
        c.PrintMoved += (key, _) => moved = key;
        c.PrintChosen += key => chosen = key;
        c.SetTool("place");
        Drag(c, new Vector2(60, 30), new Vector2(900, 30));
        Check.That(band.Box.X == 0 && System.Math.Abs(band.Box.W - 1) < 1e-9, $"the box spans the face (got x {band.Box.X}, w {band.Box.W})");
        Check.That(moved == "band" && chosen == null, $"the chosen band moved (chosen {chosen}, moved {moved})");
        moved = null;
        var mid = new Vector2(0.7f * c.Doc.W, 0.65f * c.Doc.H);
        Drag(c, mid, mid);
        Check.That(chosen == "c0" && moved == null, $"a click chooses the counter's box without moving it (chosen {chosen}, moved {moved})");
        await r.Frames(2);
        c.QueueFree();
    }
}
