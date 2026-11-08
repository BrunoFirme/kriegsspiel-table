using KTable.Core.Model;

namespace KTable.Converter;

public sealed partial class ScenarioBuilder
{
    List<PieceLook>? LooksOf(ModuleObj o, PieceInstance inst, PieceDef def, PackOut extras, string layerName, PieceInstance? parent)
    {
        var (states, _) = o.StateList();
        if (states.Count < 2 || states.Count != def.States.Count || def.Faces.Count > 1) return null;
        return states.Select((s, i) => ReferenceEquals(s, o) ? PieceLook.Of(inst) : LookOf(s, i, def, extras, layerName, parent)).ToList();
    }

    PieceLook LookOf(ModuleObj s, int i, PieceDef def, PackOut extras, string layerName, PieceInstance? parent)
    {
        var where = $"{layerName} {s.Name} '{s.Nickname}' {s.Guid}";
        var panels = ObjectPanels(s, extras, where, out var uiLayout, out var uiAssets);
        var colour = s.Name == DefFactory.Figurine ? null : s.Color;
        var lookTint = def.States[i].Tint ?? def.Tint;
        return new PieceLook
        {
            Guid = s.Guid.Length > 0 ? s.Guid : null,
            Label = s.RawNickname.Length > 0 ? s.RawNickname : null,
            Note = s.Description.Length > 0 ? s.Description : null,
            GmNotes = s.GmNotes.Length > 0 && !s.GmNotes.StartsWith(KtMarker.Def, StringComparison.Ordinal) ? s.GmNotes : null,
            Memo = _defs.RewriteUrls(s.Memo, extras, $"{where} memo"),
            Tags = s.Tags,
            Locked = parent == null && s.Locked,
            ScriptState = s.LuaState.Length > 0 ? _defs.RewriteUrls(s.LuaState, extras, $"{where} script state") : "",
            TintOverride = colour is { } c && !string.Equals(c, lookTint, StringComparison.OrdinalIgnoreCase) ? c : null,
            Worth = s.Worth,
            Lines = s.VectorLines.Count > 0 ? LinesOf(s, def) : null,
            Ui = panels.Count > 0 ? panels : null, UiLayout = uiLayout, UiAssets = uiAssets,
            NotSticky = !s.Sticky, NotBoxSelected = !s.DragSelectable, MeasuresMoves = s.MeasureMovement,
        };
    }
}
