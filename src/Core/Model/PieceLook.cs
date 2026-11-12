using System.Text.Json.Serialization;

namespace KTable.Core.Model;

public sealed class PieceLook
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Guid { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Label { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Note { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? GmNotes { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public string Memo { get; set; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<string>? Tags { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Locked { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Script { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public string ScriptState { get; set; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int Worth { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<LineDef>? Lines { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<UiNode>? Ui { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public KTable.Core.Panels.UiLayout? UiLayout { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<KTable.Core.Panels.UiAsset>? UiAssets { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool NotSticky { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool NotBoxSelected { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool MeasuresMoves { get; set; }

    public PieceLook Clone() => new()
    {
        Guid = Guid, Label = Label, Note = Note, GmNotes = GmNotes, Memo = Memo, Tags = Tags?.ToList(), Locked = Locked, Script = Script, ScriptState = ScriptState,
        Worth = Worth, Lines = Lines?.Select(l => l.Clone()).ToList(), Ui = Ui?.Select(n => n.Clone()).ToList(),
        UiLayout = UiLayout?.Clone(), UiAssets = UiAssets?.Select(a => a.Clone()).ToList(), NotSticky = NotSticky, NotBoxSelected = NotBoxSelected, MeasuresMoves = MeasuresMoves,
    };

    public static PieceLook Of(PieceInstance p) => new()
    {
        Guid = p.Guid, Label = p.Label, Note = p.Note, GmNotes = p.GmNotes, Memo = p.Memo, Tags = p.Tags?.ToList(), Locked = p.Locked, Script = p.Script, ScriptState = p.ScriptState,
        Worth = p.Worth, Lines = p.Lines.Count > 0 ? p.Lines.Select(l => l.Clone()).ToList() : null,
        Ui = p.Ui.Count > 0 ? p.Ui.Select(n => n.Clone()).ToList() : null, UiLayout = p.UiLayout?.Clone(), UiAssets = p.UiAssets?.Select(a => a.Clone()).ToList(),
        NotSticky = p.NotSticky, NotBoxSelected = p.NotBoxSelected, MeasuresMoves = p.MeasuresMoves,
    };

    public void Wear(PieceInstance p)
    {
        (p.Guid, p.Label, p.Note, p.GmNotes, p.Memo, p.Tags, p.Locked) = (Guid, Label, Note, GmNotes, Memo, Tags?.ToList(), Locked);
        (p.Script, p.ScriptState, p.Worth) = (Script, ScriptState, Worth);
        p.Lines = Lines?.Select(l => l.Clone()).ToList() ?? new();
        p.Ui = Ui?.Select(n => n.Clone()).ToList() ?? new();
        (p.UiLayout, p.UiAssets) = (UiLayout?.Clone(), UiAssets?.Select(a => a.Clone()).ToList());
        (p.NotSticky, p.NotBoxSelected, p.MeasuresMoves) = (NotSticky, NotBoxSelected, MeasuresMoves);
        p.Widgets = new();
        p.MenuItems = new();
    }

    static bool ModuleLooks(PieceDef? def) => def is { States.Count: > 1 } && def.States.Any(s => s.Module != null);

    public static bool Whole(PieceInstance p, PieceDef? def) => p.Looks != null || ModuleLooks(def);

    public static List<PieceLook>? FromKind(PieceInstance p, PieceDef def)
    {
        if (p.Looks != null || !ModuleLooks(def) || p.State < 0 || p.State >= def.States.Count) return null;
        return def.States.Select((s, i) =>
        {
            var look = Of(p);
            if (i == p.State) return look;
            (look.Lines, look.Ui, look.UiLayout, look.UiAssets) = (null, null, null, null);
            look.ScriptState = s.Module?.ScriptState ?? "";
            return look;
        }).ToList();
    }

    public static bool Swap(PieceInstance p, int to, string? leavingScriptState)
    {
        if (p.Looks is not { } looks || to < 0 || to >= looks.Count || p.State < 0 || p.State >= looks.Count || to == p.State) return false;
        var leaving = Of(p);
        if (leavingScriptState != null) leaving.ScriptState = leavingScriptState;
        looks[p.State] = leaving;
        looks[to].Wear(p);
        p.State = to;
        return true;
    }
}
