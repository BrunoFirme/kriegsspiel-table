using System;
using Godot;

namespace KTable.App;

sealed class SpeechBubbles(TableContext c)
{
    public const string GroupName = "speech-bubbles";
    const double LeastSeconds = 3, SecondsALetter = 0.05, MostSeconds = 10, Fade = 0.6;
    const float Above = 1.5f, Pixel = 0.0008f, PadPixels = 14, PlateAlpha = 0.86f, LiftPixels = 46;
    const int FontSize = 30, WrapAt = 44, MostLines = 4;
    static readonly Color Plate = new(0.08f, 0.08f, 0.07f, PlateAlpha);

    public static string Wrapped(string text)
    {
        var lines = new System.Collections.Generic.List<string>();
        string line = "";
        foreach (string word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > WrapAt)
            {
                lines.Add(line);
                line = "";
            }
            line = line.Length == 0 ? word : line + " " + word;
        }
        if (line.Length > 0) lines.Add(line);
        if (lines.Count > MostLines)
        {
            lines.RemoveRange(MostLines, lines.Count - MostLines);
            lines[^1] += "…";
        }
        return string.Join("\n", lines);
    }

    public void Show(string text, Vector3 at, float tall)
    {
        string words = Wrapped(text);
        if (words.Length == 0) return;
        var root = new Node3D { Position = at + Vector3.Up * (tall * Above) };
        root.AddToGroup(GroupName);
        c.Scene.AddChild(root);
        var label = new Label3D
        {
            Text = words, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true, FixedSize = true, PixelSize = Pixel, FontSize = FontSize, OutlineSize = 0,
            Modulate = Ui.Ink, RenderPriority = 1, Layers = Table.LiveOnlyLayer,
        };
        var size = ThemeDB.FallbackFont.GetMultilineStringSize(words, HorizontalAlignment.Center, -1, FontSize);
        float lift = size.Y / 2 + LiftPixels;
        label.Offset = new Vector2(0, lift);
        var fill = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = Plate, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, NoDepthTest = true,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled, FixedSize = true,
        };
        var plate = new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2((size.X + PadPixels * 2) * Pixel, (size.Y + PadPixels) * Pixel), CenterOffset = new Vector3(0, lift * Pixel, 0) }, MaterialOverride = fill,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Layers = Table.LiveOnlyLayer,
        };
        root.AddChild(plate);
        root.AddChild(label);
        double seconds = Math.Clamp(LeastSeconds + text.Length * SecondsALetter, LeastSeconds, MostSeconds);
        var tween = root.CreateTween().SetParallel();
        tween.TweenProperty(label, "modulate:a", 0f, Fade).SetDelay(seconds - Fade);
        tween.TweenProperty(fill, BaseMaterial3D.PropertyName.AlbedoColor.ToString(), Plate with { A = 0 }, Fade).SetDelay(seconds - Fade);
        tween.Chain().TweenCallback(Callable.From(root.QueueFree));
    }
}
