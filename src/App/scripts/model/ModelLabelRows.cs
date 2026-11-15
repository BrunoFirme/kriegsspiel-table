using System.Collections.Generic;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

internal sealed partial class ModelPanels
{
    const int TextMenuId = 102, NewTextLineId = 1000;
    const string FacesView = "view", FacesFront = "front";

    static readonly (string Key, string Text, string Tip)[] FacingChoices =
    {
        (FacesView, "View", "Turns to face whoever looks at it"),
        (FacesFront, "Front", "Stands upright, reading from the piece's front"),
    };

    void LabelRows(PieceStudio studio, VBoxContainer list)
    {
        var lines = _m.TextLines;
        int chosen = _m.SelLabel;
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].Float == null) continue;
            int at = i;
            list.AddChild(ShapeRow(at == chosen, null, lines[i].Name, "text", () =>
            {
                _m.SelPartId = ModelStudio.LabelKey(at);
                _m.RefreshSelection();
                studio.RebuildModelPanels();
            }));
        }
    }

    void TextMenu(PopupMenu pop)
    {
        var sub = new PopupMenu();
        var lines = new List<int>();
        sub.AboutToPopup += () =>
        {
            sub.Clear();
            lines.Clear();
            var all = _m.TextLines;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].Float != null) continue;
                sub.AddItem(all[i].Name, lines.Count);
                lines.Add(i);
            }
            if (lines.Count > 0) sub.AddSeparator();
            sub.AddItem("A new text line", NewTextLineId);
        };
        sub.IdPressed += id =>
        {
            if (id == NewTextLineId) _m.FloatLabel(-1);
            else if (id >= 0 && id < lines.Count) _m.FloatLabel(lines[(int)id]);
        };
        pop.AddSubmenuNodeItem("Text over the piece", sub, TextMenuId);
    }

    void LabelDetails(VBoxContainer box, int at)
    {
        var line = _m.TextLines[at];
        var f = line.Float!;
        var ends = new HBoxContainer();
        ends.AddChild(Icon("trash", "Take it off the piece; the text line stays   (Delete; Ctrl+Z brings it back)", () => _m.GroundLabel(at)));
        var g = Inspector.Group(box, line.Name, ends);
        var form = Ui.Form(Ui.PanelLabels);
        form.AddChild(new Label { Text = "Text" });
        var text = new LineEdit { Text = line.Default, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TooltipText = "What it reads until someone at the table writes another" };
        void Commit() { if (text.Text != line.Default) _m.SetLabel(at, t => t.Default = text.Text, "a text line's start"); }
        text.TextSubmitted += _ => Commit();
        text.FocusExited += Commit;
        form.AddChild(text);
        void Place(System.Action<FloatPlace> set) => _m.SetLabel(at, t => set(t.Float!), "where the text floats");
        Number(form, "Across", f.X, -FloatPlace.Reach, FloatPlace.Reach, 0.01, "u", "To the right of the middle, in table units", v => Place(p => p.X = v));
        Number(form, "Ahead", -f.Z, -FloatPlace.Reach, FloatPlace.Reach, 0.01, "u", "Towards the front from the middle, in table units", v => Place(p => p.Z = -v));
        Number(form, "Up", f.Y, -FloatPlace.Reach, FloatPlace.Reach, 0.01, "u", "Above the piece's underside, in table units", v => Place(p => p.Y = v));
        Number(form, "Size", f.Size, FloatPlace.LeastSize, FloatPlace.MostSize, 0.01, "u", "How tall its letters stand, in table units", v => Place(p => p.Size = v));
        form.AddChild(new Label { Text = "Facing" });
        form.AddChild(Ui.Chips(FacingChoices, f.FacesFront ? FacesFront : FacesView, FacingChoices.Length, k => Place(p => p.FacesFront = k == FacesFront)));
        form.AddChild(new Label { Text = "Colour" });
        form.AddChild(SizeForm.Tint(line.Color ?? "#FFFFFF", "The colour it reads in", c => _m.SetLabel(at, t => t.Color = c, "a text line's colour")));
        Ui.AlignForm(form);
        g.AddChild(form);
    }
}
