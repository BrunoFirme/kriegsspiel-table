using KTable.Core;
using KTable.Core.Model;

namespace KTable.Client;

public static class StateLooks
{
    public readonly record struct AddOn(string? Mesh, string? Texture, string? Finish);

    public static PieceStateDef? At(PieceDef d, int state) => state >= 0 && state < d.States.Count ? d.States[state] : null;

    public static int Clamp(PieceDef d, int state) => d.States.Count == 0 ? 0 : Math.Clamp(state, 0, d.States.Count - 1);

    public static string Name(PieceDef d, int state) => At(d, state)?.Name is { Length: > 0 } n ? n : $"State {state + 1}";

    public static Size2 SizeOf(PieceDef d, int state) => At(d, state)?.Size ?? d.Size;

    public static string ShapeOf(PieceDef d, int state) => At(d, state)?.Shape ?? d.Shape;

    public static double ThicknessOf(PieceDef d, int state) => At(d, state)?.Thickness ?? d.Thickness;

    public static string TintOf(PieceDef d, int state) => At(d, state)?.Tint ?? d.Tint;

    public static bool TintsPicturesOf(PieceDef d, int state) => At(d, state)?.TintsPictures ?? d.TintsPictures;

    public static string? MeshOf(PieceDef d, int state) => At(d, state)?.Mesh ?? d.Mesh;

    public static string? TextureOf(PieceDef d, int state) => At(d, state)?.Texture ?? d.Texture;

    public static string? FinishOf(PieceDef d, int state)
    {
        var st = At(d, state);
        return st?.FinishTexture ?? (TextureOf(d, state) == d.Texture ? d.FinishTexture : null);
    }

    public static bool OwnAddOns(PieceDef d) => d.States.Any(s => s.AddOnMesh != null);

    public static AddOn AddOnOf(PieceDef d, int state)
    {
        if (!OwnAddOns(d)) return new(d.AddOnMesh, d.AddOnTexture, d.AddOnFinishTexture);
        return At(d, state) is { } s ? new(s.AddOnMesh, s.AddOnTexture, s.AddOnFinishTexture) : default;
    }

    public static void SetAddOn(PieceDef d, int state, AddOn a)
    {
        if (At(d, state) is not { } st)
        {
            (d.AddOnMesh, d.AddOnTexture, d.AddOnFinishTexture) = (a.Mesh, a.Texture, a.Finish);
            return;
        }
        SplitAddOns(d);
        (st.AddOnMesh, st.AddOnTexture, st.AddOnFinishTexture) = (a.Mesh, a.Texture, a.Finish);
        MirrorFirstAddOn(d);
    }

    static void SplitAddOns(PieceDef d)
    {
        if (OwnAddOns(d)) return;
        foreach (var s in d.States) (s.AddOnMesh, s.AddOnTexture, s.AddOnFinishTexture) = (d.AddOnMesh, d.AddOnTexture, d.AddOnFinishTexture);
    }

    static void MirrorFirstAddOn(PieceDef d)
    {
        if (d.States.Count == 0) return;
        var first = d.States[0];
        (d.AddOnMesh, d.AddOnTexture, d.AddOnFinishTexture) = OwnAddOns(d) ? (first.AddOnMesh, first.AddOnTexture, first.AddOnFinishTexture) : (null, null, null);
    }

    public static void SetSize(PieceDef d, int state, Size2 size)
    {
        if (state > 0 && At(d, state) is { } st) { st.Size = size; return; }
        PinOthers(d);
        d.Size = size;
        if (At(d, 0) is { } first) first.Size = null;
    }

    public static void SetShape(PieceDef d, int state, string shape)
    {
        if (state > 0 && At(d, state) is { } st) { st.Shape = shape; return; }
        PinOthers(d);
        d.Shape = shape;
        if (At(d, 0) is { } first) first.Shape = null;
    }

    public static void SetThickness(PieceDef d, int state, double thickness)
    {
        if (state > 0 && At(d, state) is { } st) { st.Thickness = thickness; return; }
        PinOthers(d);
        d.Thickness = thickness;
        if (At(d, 0) is { } first) first.Thickness = null;
    }

    public static void SetTint(PieceDef d, int state, string tint)
    {
        if (state > 0 && At(d, state) is { } st) { st.Tint = tint; return; }
        PinOthers(d);
        d.Tint = tint;
        if (At(d, 0) is { } first) first.Tint = null;
    }

    public static void SetTintsPictures(PieceDef d, int state, bool on)
    {
        if (state > 0 && At(d, state) is { } st) { st.TintsPictures = on; return; }
        PinOthers(d);
        d.TintsPictures = on;
        if (At(d, 0) is { } first) first.TintsPictures = null;
    }

    public static void PinOthers(PieceDef d)
    {
        for (int i = 1; i < d.States.Count; i++)
        {
            var s = d.States[i];
            s.Size ??= d.Size;
            s.Shape ??= d.Shape;
            s.Thickness ??= d.Thickness;
            s.Tint ??= d.Tint;
            s.TintsPictures ??= d.TintsPictures;
            if (d.Kind == "model")
            {
                s.Mesh ??= d.Mesh;
                if (s.Texture == null)
                {
                    s.Texture = d.Texture;
                    s.FinishTexture ??= d.FinishTexture;
                }
                else if (s.Texture == d.Texture) s.FinishTexture ??= d.FinishTexture;
            }
            else if (s.Image == null && s.Texture == null && d.Image != null)
            {
                s.Image = d.Image;
                s.Outline ??= d.Outline;
            }
        }
        SplitAddOns(d);
    }

    public static PieceStateDef Copy(PieceDef d, int state)
    {
        var from = d.States[state];
        var copy = Json.Clone(from);
        copy.Name = (from.Name.Length > 0 ? from.Name : $"State {state + 1}") + " copy";
        copy.Module = null;
        if (state > 0) return copy;
        copy.Image ??= d.Image;
        copy.Back ??= d.Back;
        copy.Texture ??= d.Texture;
        copy.Size ??= d.Size;
        copy.Shape ??= d.Shape;
        copy.Thickness ??= d.Thickness;
        copy.Tint ??= d.Tint;
        copy.TintsPictures ??= d.TintsPictures;
        if (copy.Image == d.Image) copy.Outline ??= d.Outline;
        if (d.Kind != "model") return copy;
        copy.Mesh ??= d.Mesh;
        if (copy.Texture == d.Texture) copy.FinishTexture ??= d.FinishTexture;
        return copy;
    }

    public static PieceStateDef FirstOf(PieceDef d) => new()
    {
        Name = d.Name,
        Image = d.Image,
        Back = d.Back,
        Texture = d.Texture,
        Mesh = d.Kind == "model" ? d.Mesh : null,
    };

    public static void Remove(PieceDef d, int state)
    {
        if (At(d, state) == null) return;
        bool own = OwnAddOns(d);
        d.States.RemoveAt(state);
        if (d.States.Count == 0) return;
        if (state == 0) PromotePrints(d);
        if (own)
        {
            var first = d.States[0];
            (d.AddOnMesh, d.AddOnTexture, d.AddOnFinishTexture) = (first.AddOnMesh, first.AddOnTexture, first.AddOnFinishTexture);
        }
        if (d.States.Count > 1) return;
        var only = d.States[0];
        if (only.Image != null)
        {
            d.Image = only.Image;
            d.Outline = only.Outline;
        }
        d.Back = only.Back;
        d.Texture = only.Texture ?? d.Texture;
        if (only.FinishTexture != null || only.Texture != null) d.FinishTexture = only.FinishTexture ?? (only.Texture == d.Texture ? d.FinishTexture : null);
        if (d.Kind == "model" && only.Mesh != null) d.Mesh = only.Mesh;
        if (only.Shape != null) d.Shape = only.Shape;
        if (only.Size is { } size) d.Size = size;
        if (only.Thickness is { } thickness) d.Thickness = thickness;
        if (only.Tint != null) d.Tint = only.Tint;
        if (only.TintsPictures is { } pictures) d.TintsPictures = pictures;
        d.States.Clear();
    }

    public static string PrintKey(CounterDef c) => "c:" + c.Name;

    public static string PrintKey(TextLineDef t) => "t:" + t.Name;

    public static PrintPlace? PrintOf(PieceDef d, int state, CounterDef c) => PrintOf(d, state, PrintKey(c), c.Place);

    public static PrintPlace? PrintOf(PieceDef d, int state, TextLineDef t) => PrintOf(d, state, PrintKey(t), t.Place);

    static PrintPlace? PrintOf(PieceDef d, int state, string key, PrintPlace? first) =>
        state > 0 && At(d, state)?.Prints is { } own && own.TryGetValue(key, out var p) ? (p.Side == PrintPlace.Hidden ? null : p) : first;

    public static void SetPrint(PieceDef d, int state, CounterDef c, PrintPlace? place) => SetPrint(d, state, PrintKey(c), place, p => c.Place = p);

    public static void SetPrint(PieceDef d, int state, TextLineDef t, PrintPlace? place) => SetPrint(d, state, PrintKey(t), place, p => t.Place = p);

    static void SetPrint(PieceDef d, int state, string key, PrintPlace? place, Action<PrintPlace?> first)
    {
        if (state > 0 && At(d, state) is { } st)
        {
            (st.Prints ??= new())[key] = place ?? new PrintPlace { Side = PrintPlace.Hidden };
            return;
        }
        PinPrints(d);
        first(place);
    }

    public static void PinPrints(PieceDef d)
    {
        var firsts = d.Counters.Select(c => (PrintKey(c), c.Place)).Concat(d.TextLines.Select(t => (PrintKey(t), t.Place))).ToList();
        for (int i = 1; i < d.States.Count; i++)
            foreach (var (key, place) in firsts)
                if (d.States[i].Prints?.ContainsKey(key) != true) (d.States[i].Prints ??= new())[key] = place?.Copy() ?? new PrintPlace { Side = PrintPlace.Hidden };
    }

    static void PromotePrints(PieceDef d)
    {
        if (d.States[0].Prints is not { } own) return;
        PinPrints(d);
        foreach (var c in d.Counters) if (own.TryGetValue(PrintKey(c), out var p)) c.Place = p.Side == PrintPlace.Hidden ? null : p;
        foreach (var t in d.TextLines) if (own.TryGetValue(PrintKey(t), out var p)) t.Place = p.Side == PrintPlace.Hidden ? null : p;
        d.States[0].Prints = null;
    }

    public static void Rename(PieceDef d, CounterDef c, string name) => Rekey(d, PrintKey(c), "c:" + name, () => c.Name = name);

    public static void Rename(PieceDef d, TextLineDef t, string name) => Rekey(d, PrintKey(t), "t:" + name, () => t.Name = name);

    static void Rekey(PieceDef d, string was, string now, Action rename)
    {
        rename();
        if (was == now) return;
        foreach (var own in d.States.Select(s => s.Prints).OfType<Dictionary<string, PrintPlace>>())
            if (own.Remove(was, out var p)) own[now] = p;
    }

    public static void Forget(PieceDef d, CounterDef c) => ForgetPrint(d, PrintKey(c));

    public static void Forget(PieceDef d, TextLineDef t) => ForgetPrint(d, PrintKey(t));

    static void ForgetPrint(PieceDef d, string key)
    {
        foreach (var s in d.States)
            if (s.Prints != null && s.Prints.Remove(key) && s.Prints.Count == 0) s.Prints = null;
    }

    public static bool MeshShared(IEnumerable<PieceDef> pieces, PieceDef d, int state, string sha)
    {
        int own = Clamp(d, state);
        for (int i = 0; i < Math.Max(1, d.States.Count); i++)
            if (i != own && (MeshOf(d, i) == sha || AddOnOf(d, i).Mesh == sha)) return true;
        return pieces.Any(o => o != d && MeshesOf(o).Contains(sha));
    }

    static IEnumerable<string?> MeshesOf(PieceDef d) => new[] { d.Mesh, d.AddOnMesh }.Concat(d.States.SelectMany(s => new[] { s.Mesh, s.AddOnMesh }));
}
