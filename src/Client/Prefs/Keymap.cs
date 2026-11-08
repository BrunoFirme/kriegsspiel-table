namespace KTable.Client.Prefs;

public sealed class Keymap
{
    public static readonly (string action, string label, string def)[] Actions =
    {
        ("ruler", "Ruler", "M"), ("gizmo", "Turn / resize tool", "T"), ("formations", "Formations tool", "G"), ("flip", "Turn over (show the back)", "F"), ("sighting", "POV capture (umpire)", "V"),
        ("capture", "Capture a region", "Ctrl+Shift+C"), ("captureAgain", "Capture the same region again", "Ctrl+Shift+X"),
        ("map2d", "Map view / 3D table", "F2"), ("frame", "Frame the map", "Home"), ("labelPlates", "Plates behind labels beside pieces", "N"),
        ("turnLeft", "Turn left 15° (Shift 1°)", "Q"), ("turnRight", "Turn right 15° (Shift 1°)", "E"),
        ("roll", "Roll dice", "R"), ("larger", "Larger", "Equal"), ("smaller", "Smaller", "Minus"),
        ("delete", "Delete", "Delete"), ("undo", "Undo (umpire)", "Ctrl+Z"), ("redo", "Redo (umpire)", "Ctrl+Y"),
        ("cut", "Cut", "Ctrl+X"), ("copy", "Copy", "Ctrl+C"), ("paste", "Paste at the pointer", "Ctrl+V"), ("selectAll", "Select all", "Ctrl+A"),
        ("statePrev", "Previous look", "BracketLeft"), ("stateNext", "Next look", "BracketRight"),
        ("scriptConsole", "Script console (umpire)", "QuoteLeft"), ("scriptWindow", "Scripts window (umpire)", "Ctrl+Shift+L"),
        ("mute", "Sound on / off", "Ctrl+M"), ("saves", "Saves (host)", "Ctrl+S"),
        ("form:line", "Formations tool: line", "R"), ("form:stretch", "Formations tool: stretch in a line", "Shift+R"), ("form:chequer", "Formations tool: chequer", "X"),
        ("form:crescent", "Formations tool: crescent", "Shift+C"), ("form:echelon-right", "Formations tool: echelon right", "Z"), ("form:echelon-left", "Formations tool: echelon left", "Shift+Z"),
        ("form:open", "Formations tool: open order", "Shift+X"), ("form:column", "Formations tool: column", "T"), ("form:staggered-column", "Formations tool: staggered column", "Shift+T"),
        ("form:herringbone", "Formations tool: herringbone", "H"), ("form:square", "Formations tool: square", "Shift+B"), ("form:ring", "Formations tool: ring", "C"),
        ("form:wedge", "Formations tool: wedge", "Y"), ("form:vee", "Formations tool: vee", "V"), ("form:diamond", "Formations tool: diamond", "F"),
    };

    public Dictionary<string, string> Keys { get; set; } = new();

    public string Of(string action)
    {
        if (Keys.TryGetValue(action, out var k) && k.Length > 0) return k;
        if (action.StartsWith("script:")) return ScriptDefault(action[7..]);
        return Array.Find(Actions, a => a.action == action).def ?? "";
    }

    public static string ScriptDefault(string label)
    {
        var t = label.TrimEnd();
        if (t.StartsWith("Scripting button ")) { var n = t[17..]; return n == "10" ? "Kp 0" : "Kp " + n; }
        return t.Length > 0 && char.IsDigit(t[^1]) ? t[^1].ToString() : "";
    }
    public void Set(string action, string key) => Keys[action] = key;
    public void Reset() => Keys.Clear();

    public bool Matches(string described, string action)
    {
        var want = Of(action);
        if (want.Length == 0) return false;
        var have = described;
        if (have.Length == 0) return false;
        bool shiftFree = action is "turnLeft" or "turnRight" or "roll" or "larger" or "smaller";
        if (shiftFree) { have = have.Replace("Shift+", ""); want = want.Replace("Shift+", ""); }
        if (have == want) return true;
        return (action == "larger" && have == "Kp Add") || (action == "smaller" && have == "Kp Subtract");
    }

    public string Label(string action) => Of(action).Replace("BracketLeft", "[").Replace("BracketRight", "]").Replace("Equal", "=").Replace("Minus", "−").Replace("QuoteLeft", "`");
}
