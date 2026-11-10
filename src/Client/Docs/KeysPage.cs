using System.Text;
using KTable.Client.Prefs;

namespace KTable.Client.Docs;

public static class KeysPage
{
    public const string File = "keys.md";

    public static string Markdown(Keymap keys)
    {
        string K(string action) => keys.Label(action);
        var sb = new StringBuilder();
        sb.Append("# Keys and mouse\n\n");
        sb.Append("Your bindings; **Settings → Keys** changes them. The same keys mean the same everywhere: ")
            .Append($"**{K("turnLeft")} {K("turnRight")}** turn (Shift by 1°), **{K("smaller")} {K("larger")}** or **Ctrl+wheel** size, ")
            .Append($"**{K("statePrev")} {K("stateNext")}** step one less or one more, and the **wheel** zooms, but over selected pieces, where it turns them.\n\n");

        sb.Append("## At the table\n\n");
        Table(sb, "Pieces", new[]
        {
            ("Left-drag", "pick up; drops with physics when let go"),
            ("Right-click", "everything that can be done to a piece; on the empty table, paste and the tools"),
            ($"{K("turnLeft")}  {K("turnRight")}", "turn 15° (Shift 1°)"),
            ($"{K("larger")}  {K("smaller")}", "larger, smaller"),
            (K("flip") + ", middle-click", "turn over"),
            (K("delete"), "delete; a locked piece only from its right-click menu"),
            ("Ctrl+1 … 9", "form the selection into that many rows"),
            ($"{K("cut")}  {K("copy")}  {K("paste")}", "cut, copy; paste at the pointer"),
            (K("selectAll"), "select all"),
            ($"{K("statePrev")}  {K("stateNext")}", "previous and next look"),
            ("1 … 9  0", "set the look of the piece under the pointer; a die, its side"),
            ("Wheel", "over a selected piece, turn 10° (Ctrl 2°, Shift ½°); elsewhere, zoom"),
            ("Shift / Ctrl+click", "add to the selection"),
            ("Drag the table", "box-select, leaving locked pieces out"),
            (K("roll"), "roll the dice under the pointer or selected"),
            ("Esc", "put down what is in hand; again, the menu"),
        });
        Table(sb, "View", new[]
        {
            ("Right-drag", "orbit"),
            ("Middle-drag, Space+drag, WASD", "pan"),
            ("Wheel", "zoom"),
            (K("map2d"), "flat map or 3D table"),
            (K("frame"), "frame the whole map"),
            (K("labelPlates"), "plates behind labels beside pieces"),
        });
        Table(sb, "Tools", new[]
        {
            (K("ruler"), "ruler"),
            (K("gizmo"), "turn / resize ring"),
            (K("formations"), "form the selected pieces up; the panel shows each formation's key"),
            ($"{K("smaller")}  {K("larger")}  {K("statePrev")}  {K("stateNext")}", "in the formations tool: spacing, ranks or abreast"),
            (K("capture"), "capture a region to the clipboard"),
            (K("captureAgain"), "capture the same region again"),
            (K("sighting"), $"POV capture from the selected piece; Ctrl+wheel or {K("smaller")} {K("larger")} set its reach"),
            ($"{K("turnLeft")}  {K("turnRight")}  {K("smaller")}  {K("larger")}", "with a decal picked: turn it (Shift by 1°), size it; Ctrl+wheel sizes it too"),
            ($"{K("smaller")}  {K("larger")}", "with the relief tool: brush size; Ctrl+wheel too"),
            ("Alt+click", "pick out a map sheet"),
            ($"{K("undo")}  {K("redo")}", "undo, redo (umpire)"),
            (K("mute"), "sound on / off"),
            (K("saves"), "the scenario's saves (host)"),
            ("F1", "help; over a tool, that tool's section"),
        });
        Table(sb, "Scripts", new[]
        {
            (K("scriptWindow"), "the scripts window"),
            (K("scriptConsole"), "the script console"),
            ("F1 in a script", "the API reference at that word"),
        });

        sb.Append("## Lists\n\n");
        Table(sb, "Scenarios, packs, saves and trash", new[]
        {
            ("Ctrl+click", "pick one more, or drop one"),
            ("Shift+click", "pick everything between"),
            ("Ctrl+A", "pick all"),
            ("Delete", "delete what is picked"),
        });

        Studio(sb);
        return sb.ToString();
    }

    static void Studio(StringBuilder sb)
    {
        sb.Append("## Piece studio\n\n");
        Table(sb, "Anywhere", new[]
        {
            ("Ctrl+N", "new piece"),
            ("Alt+1 / 2 / 3", "the views: 3D, 2D, Try it"),
            ("Ctrl+S", "save now (it also saves a second after each change)"),
            ("Ctrl+Z  Ctrl+Y", "undo, redo: strokes, shapes and the piece's settings alike"),
            ("F", "turn the piece over"),
            ("Ctrl+Enter", "save and run the script"),
            ("F1", "this page"),
        });
        Table(sb, "2D", new[]
        {
            ("V B E U T G I", "move, brush, rubber, shapes, text, fill, colour picker"),
            ("C O N M H", "crop, outline, place name, counters and text, measure, hand"),
            ("−  =", "smaller / larger brush"),
            ("X", "swap the two colours"),
            ("Alt+click", "take a colour off the picture"),
            ("Space-drag, middle-drag", "move the view"),
            ("Wheel", "zoom; Ctrl+wheel brush size"),
            ("Ctrl+C  Ctrl+V  Ctrl+D", "copy, paste, duplicate a layer"),
            ("Ctrl+0  Ctrl+1", "fit, actual pixels"),
            ("Ctrl+'  Ctrl+;  Y", "grid, snap, symmetry"),
            ("Ctrl+right-click", "copy the point in piece units, for a script"),
        });
        Table(sb, "3D", new[]
        {
            ("H V B E G I D", "look round, move shapes, paint, rub out, fill, colour picker, decal"),
            ("Drag, right-drag, middle-drag", "turn the model, slide, come closer"),
            ("−  =", "brush or decal size; Ctrl+wheel too"),
            ("Q  E", "with a decal picture held: turn it 15° (Shift 1°)"),
            ("Home", "frame the model"),
        });
        Table(sb, "Try it", new[] { ("As at the table", "drag, right-click, middle-click turns over, [ ] change the look") });
    }

    static void Table(StringBuilder sb, string title, (string keys, string what)[] rows)
    {
        sb.Append("### ").Append(title).Append("\n\n| Keys | Does |\n|---|---|\n");
        foreach (var (keys, what) in rows)
            sb.Append("| ").Append(Cap(keys)).Append(" | ").Append(what.Replace("|", "\\|")).Append(" |\n");
        sb.Append('\n');
    }

    static string Cap(string keys) =>
        string.Join(" ", keys.Split("  ", StringSplitOptions.RemoveEmptyEntries).Select(k => "`" + k.Trim().Replace("`", "ˋ") + "`"));
}
