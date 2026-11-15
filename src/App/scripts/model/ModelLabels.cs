using System;
using System.Collections.Generic;
using System.Linq;
using KTable.Core.Model;

namespace KTable.App;

public partial class ModelStudio
{
    const string LabelKeyHead = "text:", NewLabelName = "Label";
    const double LabelClear = 0.15;

    internal static string LabelKey(int index) => LabelKeyHead + index;

    internal IReadOnlyList<TextLineDef> TextLines => _def?.TextLines ?? (IReadOnlyList<TextLineDef>)Array.Empty<TextLineDef>();

    internal int SelLabel =>
        _selPart is { } key && key.StartsWith(LabelKeyHead, StringComparison.Ordinal) && int.TryParse(key.AsSpan(LabelKeyHead.Length), out int at) && at >= 0 && at < TextLines.Count
            && TextLines[at].Float != null ? at : -1;

    bool Floats => _def?.TextLines.Any(t => t.Float != null) == true;

    internal static FloatPlace FloatAbove(double top) => new() { Y = Math.Round(top + LabelClear, 3) };

    double TopNow(PieceDef d) => WholeBounds() is not { } whole ? d.Thickness
        : _flat ? whole.End.Y : (whole.End.Y - (DrawnBounds()?.Position.Y ?? whole.Position.Y)) * _doc.Scale;

    static string FreshLabelName(PieceDef d)
    {
        if (d.TextLines.All(t => t.Name != NewLabelName)) return NewLabelName;
        int n = 2;
        while (d.TextLines.Any(t => t.Name == $"{NewLabelName} {n}")) n++;
        return $"{NewLabelName} {n}";
    }

    internal void FloatLabel(int index)
    {
        if (_def is not { } d || _studio.Sel != d || index >= d.TextLines.Count) return;
        int at = index < 0 ? d.TextLines.Count : index;
        double top = TopNow(d);
        _studio.Apply(x =>
        {
            if (index < 0) x.TextLines.Add(new TextLineDef { Name = FreshLabelName(x), Default = x.Name });
            x.TextLines[at].Float = FloatAbove(top);
        }, inspector: true, what: "a text floating over the piece");
        Chosen(LabelKey(at));
    }

    internal void SetLabel(int at, Action<TextLineDef> set, string what)
    {
        if (_def is not { } d || _studio.Sel != d || at < 0 || at >= d.TextLines.Count || d.TextLines[at].Float == null) return;
        _studio.Apply(x => set(x.TextLines[at]), what: what);
        ShowPieces();
        RefreshSelection();
    }

    internal void GroundLabel(int at)
    {
        if (_def is not { } d || _studio.Sel != d || at < 0 || at >= d.TextLines.Count) return;
        _studio.Apply(x => x.TextLines[at].Float = null, inspector: true, what: "a text taken off the piece");
        Chosen(_doc.Parts.FirstOrDefault()?.Id);
    }

    void Chosen(string? key)
    {
        _selPart = key;
        ShowPieces();
        RefreshSelection();
        _studio.RebuildModelPanels();
    }
}
