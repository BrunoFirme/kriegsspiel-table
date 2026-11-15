using System.Collections.Generic;
using System.Linq;
using Godot;

namespace KTable.App;

sealed class Livery(PieceNode body, TableContext c)
{
    const float Lift = 1.8f, Brightest = 0.9f;

    static readonly Dictionary<(Texture2D, string, Color), ImageTexture?> Made = new();

    readonly Dictionary<int, (Material Was, Material Dyed)> _surfaces = new();
    MeshInstance3D? _on;
    Color? _colour;
    string? _mask;
    bool _asked;

    public void Wear(Color? colour)
    {
        if (colour is not { } wanted)
        {
            Restore();
            return;
        }
        if (Mask() is not { } mask || body.Model is not { } model || !GodotObject.IsInstanceValid(model) || model.Mesh == null) return;
        if (model == _on && wanted == _colour && _surfaces.Count > 0 && _surfaces.All(s => model.GetSurfaceOverrideMaterial(s.Key) == s.Value.Dyed)) return;
        Restore();
        for (int s = 0; s < model.Mesh.GetSurfaceCount(); s++)
        {
            if (model.GetSurfaceOverrideMaterial(s) is not StandardMaterial3D plain || plain.AlbedoTexture is not { } picture || Dye(picture, mask, wanted) is not { } dyedPicture) continue;
            var dyed = (StandardMaterial3D)plain.Duplicate();
            dyed.AlbedoTexture = dyedPicture;
            model.SetSurfaceOverrideMaterial(s, dyed);
            _surfaces[s] = (plain, dyed);
        }
        (_on, _colour) = (model, wanted);
    }

    public bool On(PieceNode node) => node == body;

    public void Restore()
    {
        if (_on != null && GodotObject.IsInstanceValid(_on))
            foreach (var (s, (was, dyed)) in _surfaces)
                if (_on.GetSurfaceOverrideMaterial(s) == dyed) _on.SetSurfaceOverrideMaterial(s, was);
        _surfaces.Clear();
        (_on, _colour) = (null, null);
    }

    string? Mask()
    {
        if (_mask != null || _asked) return _mask;
        _asked = true;
        if (body.Def is not { Livery: { } key } def || c.S.PackOfDef(def.Id) is not { } pack || !pack.Assets.TryGetValue(key, out var asset)) return null;
        string pkg = pack.Id, file = asset.File;
        c.S.Assets.EnsureFile(pkg, file, () => _mask = c.S.Assets.Abs(pkg, file));
        return _mask;
    }

    static ImageTexture? Dye(Texture2D picture, string mask, Color colour)
    {
        var key = (picture, mask, colour);
        if (!Made.TryGetValue(key, out var done)) Made[key] = done = Dyed(picture.GetImage(), Image.LoadFromFile(mask), colour);
        return done;
    }

    internal static ImageTexture? Dyed(Image? picture, Image? mask, Color colour)
    {
        if (picture == null || mask == null || picture.IsEmpty() || mask.IsEmpty()) return null;
        var image = Plain(picture, Image.Format.Rgba8);
        var marks = Plain(mask, Image.Format.L8);
        if (marks.GetSize() != image.GetSize()) marks.Resize(image.GetWidth(), image.GetHeight());
        byte[] pixels = image.GetData(), weights = marks.GetData();
        for (int i = 0; i < weights.Length; i++)
        {
            if (weights[i] == 0) continue;
            int at = i * 4;
            var was = new Color(pixels[at] / 255f, pixels[at + 1] / 255f, pixels[at + 2] / 255f);
            var cloth = Color.FromHsv(colour.H, colour.S, Mathf.Min(was.V * Lift, Brightest));
            var mixed = was.Lerp(cloth, weights[i] / 255f);
            (pixels[at], pixels[at + 1], pixels[at + 2]) = ((byte)mixed.R8, (byte)mixed.G8, (byte)mixed.B8);
        }
        var dyed = Image.CreateFromData(image.GetWidth(), image.GetHeight(), false, Image.Format.Rgba8, pixels);
        dyed.GenerateMipmaps();
        return ImageTexture.CreateFromImage(dyed);
    }

    static Image Plain(Image source, Image.Format format)
    {
        var image = (Image)source.Duplicate();
        if (image.IsCompressed()) image.Decompress();
        image.ClearMipmaps();
        image.Convert(format);
        return image;
    }
}
