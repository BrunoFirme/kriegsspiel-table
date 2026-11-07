namespace KTable.Core.Panels;

public static class PanelResets
{
    const string White = "RGBA(1.000, 1.000, 1.000, 1.000)", Clear = "RGBA(1.000, 1.000, 1.000, 0.000)", Ink = "RGBA(0.196, 0.196, 0.196, 1.000)";
    const string Unset = "-1", Stretch = "(0.00, 0.00)", Full = "(1.00, 1.00)", Middle = "(0.50, 0.50)";

    static readonly Dictionary<string, string> Custom = new(StringComparer.OrdinalIgnoreCase)
    {
        ["width"] = "100%", ["height"] = "100%", ["rectAlignment"] = "100%", ["offsetXY"] = "0 0", ["scale"] = "1 1 1", ["opacity"] = "1", ["outline"] = "None",
        ["outlineSize"] = "1", ["shadow"] = "None", ["shadowDistance"] = "1", ["active"] = "true", ["transition"] = "ColorTint", ["navigation"] = "Automatic",
    };

    static readonly Dictionary<string, string> Layout = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ignoreLayout"] = "False", ["minWidth"] = Unset, ["minHeight"] = Unset, ["preferredWidth"] = Unset, ["preferredHeight"] = Unset, ["flexibleWidth"] = Unset,
        ["flexibleHeight"] = Unset, ["layoutPriority"] = "1",
    };

    static readonly Dictionary<string, string> Words = new(StringComparer.OrdinalIgnoreCase)
    {
        ["color"] = Ink, ["fontSize"] = "14", ["fontStyle"] = "Normal", ["alignment"] = "MiddleCenter", ["font"] = "LegacyRuntime", ["lineSpacing"] = "1", ["supportRichText"] = "True",
        ["resizeTextForBestFit"] = "False", ["resizeTextMinSize"] = "10", ["resizeTextMaxSize"] = "40", ["horizontalOverflow"] = "Wrap", ["verticalOverflow"] = "Truncate",
        ["alignByGeometry"] = "False", ["raycastTarget"] = "False",
    };

    static readonly Dictionary<string, string> Group = new(StringComparer.OrdinalIgnoreCase)
    {
        ["padding"] = "RectOffset (l:0 r:0 t:0 b:0)", ["childAlignment"] = "UpperLeft", ["spacing"] = "0", ["childForceExpandWidth"] = "True", ["childForceExpandHeight"] = "True",
        ["childControlWidth"] = "True", ["childControlHeight"] = "True", ["childScaleWidth"] = "False", ["childScaleHeight"] = "False", ["reverseArrangement"] = "False",
    };

    static readonly Dictionary<string, (string Color, string Type, string Raycast, string? Sprite)> Pictures = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Panel"] = (Clear, "Sliced", "False", null), ["Image"] = (White, "Simple", "False", null), ["Button"] = (White, "Sliced", "True", "UISprite"),
        ["ToggleButton"] = (White, "Sliced", "True", "UISprite"), ["InputField"] = (White, "Sliced", "True", "Sprites/Elements/UISprite_XmlLayout"),
        ["Dropdown"] = (White, "Sliced", "True", "Sprites/Elements/UISprite_XmlLayout"), ["ProgressBar"] = (White, "Simple", "True", "Sprites/Elements/Background_XmlLayout"),
        ["HorizontalLayout"] = (Clear, "Sliced", "False", null), ["VerticalLayout"] = (Clear, "Sliced", "False", null), ["GridLayout"] = (Clear, "Sliced", "False", null),
        ["TableLayout"] = (Clear, "Sliced", "False", null), ["ToggleGroup"] = (Clear, "Sliced", "False", "Background"), ["Mask"] = (White, "Sliced", "False", "UIMask"),
    };

    static readonly string[] Centred = { "Toggle", "InputField", "Slider", "ProgressBar", "Dropdown", "VerticalScrollView", "HorizontalScrollView" };

    public static string Of(string tag, string attribute)
    {
        if (Custom.TryGetValue(attribute, out var c)) return c;
        if (tag is "Text" && Words.TryGetValue(attribute, out var w)) return w;
        if (tag is "Panel" or "HorizontalLayout" or "VerticalLayout" && Group.TryGetValue(attribute, out var g)) return g;
        if (Rect(tag, attribute) is { } r) return r;
        if (Layout.TryGetValue(attribute, out var l) && tag is not ("Row" or "Cell" or "Canvas")) return l;
        if (tag is not "Text" && Pictures.TryGetValue(tag, out var p))
            return attribute.ToLowerInvariant() switch { "color" => p.Color, "type" => p.Type, "raycasttarget" => p.Raycast, "sprite" or "image" => p.Sprite ?? "", _ => "" };
        return "";
    }

    static string? Rect(string tag, string attribute)
    {
        bool centred = Centred.Contains(tag);
        return attribute.ToLowerInvariant() switch
        {
            "anchormin" => centred ? Middle : Stretch,
            "anchormax" => centred ? Middle : Full,
            "pivot" => Middle,
            "anchoredposition" => tag == "Toggle" ? "(0.00, 32.00)" : Stretch,
            _ => null,
        };
    }
}
