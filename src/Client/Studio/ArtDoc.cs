using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using KTable.Core;

namespace KTable.Client.Studio;

public sealed class ArtDoc
{
    public int Version { get; set; } = 1;
    public int W { get; set; } = 512;
    public int H { get; set; } = 512;
    public string? Bg { get; set; }
    public List<ArtLayer> Layers { get; set; } = new();
    public List<float>? Outline { get; set; }
    public List<float> Measures { get; set; } = new();
    public string? Selected { get; set; }

    public ArtLayer? Layer(string? id) => id == null ? null : Layers.FirstOrDefault(l => l.Id == id);
    public static string NewId() => Repeatable.NewGuid().ToString("N")[..10];

    [JsonIgnore] public ArtLayer? Sel => Layer(Selected);

    public string NextName(string stem)
    {
        int n = 1;
        while (Layers.Any(l => l.Name == (n == 1 ? stem : $"{stem} {n}"))) n++;
        return n == 1 ? stem : $"{stem} {n}";
    }

    public void Insert(ArtLayer l, int? at = null)
    {
        int i = at ?? (Sel is { } s ? Layers.IndexOf(s) + 1 : Layers.Count);
        Layers.Insert(Math.Clamp(i, 0, Layers.Count), l);
        Selected = l.Id;
    }

    public bool Swap(ArtLayer l, int dir)
    {
        int i = Layers.IndexOf(l), j = i + dir;
        if (j < 0 || j >= Layers.Count) return false;
        (Layers[i], Layers[j]) = (Layers[j], Layers[i]);
        return true;
    }

    public string? Remove(ArtLayer l)
    {
        int i = Layers.IndexOf(l);
        Layers.Remove(l);
        return Layers.Count == 0 ? null : Layers[Math.Clamp(i - 1, 0, Layers.Count - 1)].Id;
    }

    public ArtLayer TakeIn(ArtLayer l, float offset)
    {
        l.Id = NewId();
        l.Name = NextName(l.Name);
        l.X += offset;
        l.Y += offset;
        return l;
    }

    public void ReplacePair(int i, ArtLayer merged)
    {
        Layers.RemoveAt(i);
        Layers.RemoveAt(i - 1);
        Layers.Insert(i - 1, merged);
    }
}

public sealed class ArtLayer
{
    public string Id { get; set; } = ArtDoc.NewId();
    public string Name { get; set; } = "Layer";
    public string Kind { get; set; } = "paint";
    public bool Visible { get; set; } = true;
    public bool Locked { get; set; }
    public float Opacity { get; set; } = 1;
    public float X { get; set; }
    public float Y { get; set; }
    public float Rot { get; set; }
    public float Sx { get; set; } = 1;
    public float Sy { get; set; } = 1;

    public string? Src { get; set; }
    public int SrcW { get; set; }
    public int SrcH { get; set; }
    public float[]? Crop { get; set; }

    public string? Text { get; set; }
    public string Font { get; set; } = "Segoe UI";
    public bool Bold { get; set; } = true;
    public bool Italic { get; set; }
    public float Size { get; set; } = 48;
    public string Color { get; set; } = "#ffffffff";
    public string? OutlineColor { get; set; }
    public float OutlineW { get; set; }

    public BlockSpec? Block { get; set; }

    public List<PaintOp> Ops { get; set; } = new();

}

public sealed class PaintOp
{
    public string K { get; set; } = "path";
    public List<float> P { get; set; } = new();
    public string C { get; set; } = "#000000ff";
    public float W { get; set; } = 8;
    public string? Fc { get; set; }
    public bool E { get; set; }
    public string? M { get; set; }
    public float[]? Mx { get; set; }
    public string? G { get; set; }
    public float? R { get; set; }
    public string? S { get; set; }

    public static List<float> Flat(IEnumerable<Vector2> pts)
    {
        var l = new List<float>();
        foreach (var p in pts) { l.Add(MathF.Round(p.X, 2)); l.Add(MathF.Round(p.Y, 2)); }
        return l;
    }
    public PaintOp Clone() => new() { K = K, P = new(P), C = C, W = W, Fc = Fc, E = E, M = M, Mx = Mx == null ? null : (float[])Mx.Clone(), G = G, R = R, S = S };
}

public sealed class BlockSpec
{
    public float BW { get; set; } = 512;
    public float BH { get; set; } = 256;
    public string Shape { get; set; } = "box";
    public string Fill { get; set; } = "#335ca8ff";
    public string Border { get; set; } = "#f5f2e6ff";
    public float BorderPx { get; set; } = 10;
    public string Label { get; set; } = "";
    public string LabelColor { get; set; } = "#faf7edff";
    public string Font { get; set; } = "bold";
    public string Symbol { get; set; } = "none";
    public string SymbolText { get; set; } = "";
    public string SymbolColor { get; set; } = "#faf7edff";
    public float SymbolScale { get; set; } = 1;
    public BlockSpec Clone() => (BlockSpec)MemberwiseClone();
}

public static class ArtJson
{
    static readonly JsonSerializerOptions Opts = new() { WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public static string Serialize(ArtDoc d) => JsonSerializer.Serialize(d, Opts);
    public static ArtDoc? Deserialize(string s)
    {
        try { return JsonSerializer.Deserialize<ArtDoc>(s, Opts); }
        catch (JsonException) { return null; }
    }
}
