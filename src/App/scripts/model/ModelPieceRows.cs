using System;
using System.Linq;
using Godot;
using KTable.Client;
using KTable.Core.Model;

namespace KTable.App;

internal sealed partial class ModelPanels
{
    const int PieceMenuId = 101, MostCopies = 200, MostRanks = 20;
    const double FarthestPlace = 50, LeastSize = 0.05, MostSize = 20;
    const string PieceHint = "A part: another piece of the pack · Repeat lays several in ranks · Delete takes it off";

    void PieceRows(PieceStudio studio, VBoxContainer list)
    {
        var rows = _m.PieceRows;
        int chosen = _m.SelPiece;
        for (int i = 0; i < rows.Count; i++)
        {
            int at = i;
            list.AddChild(ShapeRow(at == chosen, null, PieceName(studio, at), studio.Store.Def(rows[i].Def) == null ? "missing" : "piece", () =>
            {
                _m.SelPartId = ModelStudio.PieceKey(at);
                _m.RefreshSelection();
                studio.RebuildModelPanels();
            }));
        }
    }

    string PieceName(PieceStudio studio, int at)
    {
        var rows = _m.PieceRows;
        string name = studio.Store.Def(rows[at].Def)?.Name ?? rows[at].Def;
        return rows.Count(p => p.Def == rows[at].Def) < 2 ? name : $"{name} {rows.Take(at + 1).Count(p => p.Def == rows[at].Def)}";
    }

    void PieceMenu(PieceStudio studio, PopupMenu pop)
    {
        var sub = new PopupMenu();
        var kinds = Array.Empty<PieceDef>();
        sub.AboutToPopup += () =>
        {
            sub.Clear();
            kinds = _m.Def is { } def ? PartRules.Takes(def, studio.Pack.Pieces, studio.Store.Def).OrderBy(k => k.Name, StringComparer.OrdinalIgnoreCase).ToArray() : Array.Empty<PieceDef>();
            for (int i = 0; i < kinds.Length; i++) sub.AddItem(kinds[i].Name, i);
            if (kinds.Length > 0) return;
            sub.AddItem("No other piece");
            sub.SetItemDisabled(0, true);
        };
        sub.IdPressed += id =>
        {
            if (id >= 0 && id < kinds.Length) _m.AddPiece(kinds[id]);
        };
        pop.AddSubmenuNodeItem("A piece of this pack", sub, PieceMenuId);
    }

    void PieceDetails(PieceStudio studio, VBoxContainer box, int at)
    {
        var part = _m.PieceRows[at];
        var kind = studio.Store.Def(part.Def);
        string name = kind?.Name ?? part.Def;
        var ends = new HBoxContainer();
        ends.AddChild(Icon("dup", "Duplicate this piece", () => _m.DuplicatePiece(at)));
        ends.AddChild(Icon("trash", "Take this piece off   (Delete; Ctrl+Z brings it back)", () => _m.RemovePiece(at)));
        var g = Inspector.Group(box, PieceName(studio, at), ends);
        var form = Ui.Form(Ui.PanelLabels);
        Number(form, "Across", part.X, -FarthestPlace, FarthestPlace, 0.01, "u", "To the right of the middle, in table units", v => _m.SetPiece(at, p => p.X = v));
        Number(form, "Ahead", -part.Z, -FarthestPlace, FarthestPlace, 0.01, "u", "Towards the front from the middle, in table units", v => _m.SetPiece(at, p => p.Z = -v));
        Number(form, "Up", part.Y, -FarthestPlace, FarthestPlace, 0.01, "u", "Above the piece's underside, in table units", v => _m.SetPiece(at, p => p.Y = v));
        Number(form, "Turn", part.Rot, -360, 360, 1, "°", "Clockwise from the piece's own facing", v => _m.SetPiece(at, p => p.Rot = v));
        Number(form, "Size", part.Scale, LeastSize, MostSize, 0.01, "×", "Against the size it has on its own", v => _m.SetPiece(at, p => p.Scale = v));
        form.AddChild(new Label { Text = "Tint" });
        form.AddChild(SizeForm.Tint(part.Tint ?? kind?.Tint ?? "#FFFFFF", "Its colour on this piece", t => _m.SetPiece(at, p => p.Tint = string.Equals(t, kind?.Tint, StringComparison.OrdinalIgnoreCase) ? null : t)));
        if (kind is { States.Count: > 1 }) LookRow(form, kind, part, at);
        Ui.AlignForm(form);
        g.AddChild(form);
        if (kind != null) RepeatGroup(box, name, part, at);
    }

    void LookRow(GridContainer form, PieceDef kind, PartDef part, int at)
    {
        form.AddChild(new Label { Text = "Look" });
        var look = new OptionButton { FitToLongestItem = false, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        for (int i = 0; i < kind.States.Count; i++) look.AddItem(kind.States[i].Name is { Length: > 0 } n ? n : $"Look {i + 1}");
        look.Selected = Math.Clamp(part.State, 0, kind.States.Count - 1);
        look.ItemSelected += i => _m.SetPiece(at, p => p.State = (int)i);
        form.AddChild(look);
    }

    void RepeatGroup(VBoxContainer box, string name, PartDef part, int at)
    {
        var same = _m.PieceRows.Where(p => p.Def == part.Def).ToList();
        var g = Inspector.Group(box, "Repeat");
        var form = Ui.Form(Ui.PanelLabels);
        var count = Number(form, "Count", same.Count, 1, MostCopies, 1, "", $"How many {name} stand on this piece", _ => { });
        var ranks = Number(form, "Ranks", same.Select(p => Math.Round(p.Z, 2)).Distinct().Count(), 1, MostRanks, 1, "", "Rows from front to back", _ => { });
        Ui.AlignForm(form);
        g.AddChild(form);
        g.AddChild(Ui.Btn("Lay out in ranks", () => _m.RepeatPiece(at, (int)count.Value, (int)ranks.Value), $"Every {name} on this piece is replaced by Count of them, spread in ranks over the block"));
    }

    static SpinBox Number(GridContainer form, string label, double value, double min, double max, double step, string suffix, string tip, Action<double> set)
    {
        form.AddChild(new Label { Text = label });
        var box = new SpinBox { MinValue = min, MaxValue = max, Step = step, Value = value, Suffix = suffix, TooltipText = tip, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        box.ValueChanged += v => set(v);
        form.AddChild(box);
        return box;
    }
}
