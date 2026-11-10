using Godot;

namespace KTable.App;

public static class Fonts
{
    static Font? _face;

    public static Font Face => _face ??= Make();

    static Font? _hand;

    public static Font Hand => _hand ??= new SystemFont { FontNames = new[] { "Segoe Print", "Comic Neue", "Arial", "Liberation Sans" }, Fallbacks = new Godot.Collections.Array<Font> { Face } };

    public const int FaceSize = 192;

    static Font? _widget;

    public static Font Widget => _widget ??= ResourceLoader.Load<FontFile>(WidgetFile) is { } face ? WithFallback(face) : Face;

    const string WidgetFile = "res://fonts/table/Amaranth-Regular.ttf";

    static Font WithFallback(FontFile face)
    {
        face.Fallbacks = new Godot.Collections.Array<Font> { Face };
        return face;
    }

    static Font Make()
    {
        var main = new SystemFont { FontNames = new[] { "Arial", "Liberation Sans", "Noto Sans", "DejaVu Sans" } };
        var fallbacks = new Godot.Collections.Array<Font>
        {
            new SystemFont { FontNames = new[] { "Segoe UI Symbol" } },
            new SystemFont { FontNames = new[] { "DejaVu Sans", "Noto Sans Symbols2", "Noto Sans Symbols" } },
            new SystemFont { FontNames = new[] { "Consolas", "DejaVu Sans Mono", "Noto Sans Mono" } },
        };
        main.Fallbacks = fallbacks;
        return main;
    }
}
