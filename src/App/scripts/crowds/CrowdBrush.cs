using System.Collections.Generic;
using System.Linq;
using KTable.Client;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class CrowdBrush
{
    public static void Stroke(TableContext c, List<Vec2>? stroke, BuildPrefs p)
    {
        if (stroke is not { Count: > 0 } || !c.S.IsLead) return;
        if (p.Activity() == BuildPrefs.Peopled) Populate(c, stroke, p.Brush, p.Level);
        else Clear(c, stroke, p.Brush, p.Clearing);
    }

    static bool Under(IReadOnlyList<Spot> stroke, Spot at, double radius) =>
        stroke.Count == 1 ? (at - stroke[0]).Length <= radius : Enumerable.Range(0, stroke.Count - 1).Any(i => at.FromSegment(stroke[i], stroke[i + 1]) <= radius);

    static void Populate(TableContext c, List<Vec2> stroke, double radius, int level)
    {
        var view = c.S.View;
        var batch = new Batch();
        foreach (var thing in view.Built.Where(b => b.Kind == BuiltKinds.Settlement))
        {
            if (view.Sheets.FirstOrDefault(s => s.Id == thing.Sheet) is not { } sheet) continue;
            var spots = stroke.Select(w => Spot.Of(BuiltGeometry.ToSheet(sheet, w))).ToList();
            var parts = thing.Parts.Select((part, i) => (part, i))
                .Where(x => x.part.Kind == PartKinds.Block && x.part.At.Count >= 2 && Population.Of(x.part) != level && Under(spots, BuiltSite.Round(x.part).At, radius))
                .Select(x => x.i).ToList();
            if (parts.Count > 0) batch.Items.Add(new SetPopulation { Thing = thing.Id, Parts = parts, Levels = parts.Select(_ => level).ToList() });
        }
        if (batch.Items.Count == 1) c.S.Do(batch.Items[0]);
        else if (batch.Items.Count > 1) c.S.Do(batch);
    }

    static void Clear(TableContext c, List<Vec2> stroke, double radius, bool shut)
    {
        var strokes = c.S.View.Crowds?.Clear?.Select(s => s.Clone()).ToList() ?? new();
        int was = strokes.Count;
        if (shut)
        {
            if (strokes.Count >= CrowdState.MostStrokes) return;
            int every = stroke.Count / ClearStroke.MostPoints + 1;
            strokes.Add(new ClearStroke { Points = stroke.Where((_, i) => i % every == 0 || i == stroke.Count - 1).ToList(), Radius = radius });
        }
        else strokes.RemoveAll(s => stroke.Any(s.Covers) || s.Points.Any(q => stroke.Any(p => (p - q).Length <= radius)));
        if (shut || strokes.Count != was) c.S.Do(new SetKeepClear { Strokes = strokes });
    }
}
