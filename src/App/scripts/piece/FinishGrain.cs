using System;
using System.Collections.Generic;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class FinishGrain
{
    const string Folder = "res://textures/raised/";
    const float Mean = 0.84f, Usual = 1.9f;
    const int BroadCells = 16;

    static readonly Dictionary<string, (string File, float Across, float Contrast, bool FineOnly)> Kinds = new()
    {
        [Finishes.Planks] = ("weathered_planks", 1.1f, Usual, false),
        [Finishes.Stone] = ("rustic_stone_wall", 1.8f, Usual, false),
        [Finishes.Bark] = ("bark_brown_02", 0.9f, Usual, false),
        [Finishes.Steel] = ("corrugated_iron", 0.5f, 4.5f, true),
        [Finishes.Iron] = ("rusty_corrugated_iron", 0.8f, Usual, false),
        [Finishes.Brick] = ("red_brick_03", 1.4f, Usual, false),
        [Finishes.Thatch] = ("thatch_roof_angled", 0.8f, 3.2f, false),
    };

    static readonly Dictionary<string, Texture2D?> Made = new();

    public static float Across(string grain) => Kinds.TryGetValue(grain, out var k) ? k.Across : 1;

    public static Texture2D? Of(string grain)
    {
        if (Made.TryGetValue(grain, out var had)) return had;
        Texture2D? made = Kinds.TryGetValue(grain, out var k) && RaisedTextures.Read(Folder + k.File + "_diff.jpg") is { } img ? ImageTexture.CreateFromImage(Shades(img, k.Contrast, k.FineOnly)) : null;
        Made[grain] = made;
        return made;
    }

    static Image Shades(Image img, float contrast, bool fineOnly)
    {
        int w = img.GetWidth(), h = img.GetHeight();
        img.ClearMipmaps();
        img.Convert(Image.Format.L8);
        var data = img.GetData();
        byte[]? broad = null;
        if (fineOnly)
        {
            var low = (Image)img.Duplicate();
            low.Resize(BroadCells, BroadCells, Image.Interpolation.Lanczos);
            low.Resize(w, h, Image.Interpolation.Bilinear);
            broad = low.GetData();
        }
        var luma = new float[w * h];
        double sum = 0;
        for (int i = 0; i < luma.Length; i++) sum += luma[i] = (data[i] - (broad?[i] ?? 0)) / (float)byte.MaxValue;
        float mean = (float)(sum / luma.Length);
        var px = new byte[luma.Length * 4];
        for (int i = 0; i < luma.Length; i++)
        {
            byte g = (byte)Math.Round(Math.Clamp(Mean + (luma[i] - mean) * contrast, 0, 1) * byte.MaxValue);
            px[i * 4] = px[i * 4 + 1] = px[i * 4 + 2] = g;
            px[i * 4 + 3] = byte.MaxValue;
        }
        var shades = Image.CreateFromData(w, h, false, Image.Format.Rgba8, px);
        shades.GenerateMipmaps();
        return shades;
    }

    public static void Wear(StandardMaterial3D m, string grain)
    {
        if (Of(grain) is not { } tex) return;
        m.DetailEnabled = true;
        m.DetailAlbedo = tex;
        m.DetailBlendMode = BaseMaterial3D.BlendModeEnum.Mul;
        m.DetailUVLayer = BaseMaterial3D.DetailUV.UV2;
        m.UV2Triplanar = true;
        m.SetMeta(GrainMeta, grain);
    }

    const string GrainMeta = "grain";

    public static Vector3 Fitted(IEnumerable<StandardMaterial3D> worn, Vector3 scale)
    {
        Fit(worn, scale.X);
        return scale;
    }

    public static void Fit(IEnumerable<StandardMaterial3D> worn, float unitsPerMesh)
    {
        foreach (var m in worn)
            if (m.HasMeta(GrainMeta)) m.UV2Scale = Vector3.One * (unitsPerMesh / Across(m.GetMeta(GrainMeta).AsString()));
    }
}
