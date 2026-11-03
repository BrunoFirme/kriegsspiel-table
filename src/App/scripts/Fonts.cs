using Godot;

namespace KTable.App;

public static class Fonts
{
    static Font? _tts;

    public static Font Tts => _tts ??= Make();

    public const int FaceSize = 192;

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
