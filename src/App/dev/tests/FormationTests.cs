using System;
using System.Linq;
using System.Threading.Tasks;
using KTable.Client.Hand;
using KTable.Core.Model;

namespace KTable.App;

static class FormationTests
{
    const string U = Layers.Umpire;

    static readonly string[] Six = { "p1", "p2", "p3", "p4", "p5", "p6" };

    static Vec2 At(OfflineTable t, string id) => t.S.View.Find(U, id)!.Pos;

    static async Task Settled(TestRunner r, OfflineTable t)
    {
        for (int i = 0; i < 120 && (t.Table.Hand.Holding || t.Table.Hand.Marching); i++) await r.Frame();
        await r.Frames(3);
    }

    [NodeTest]
    static async Task Pieces_formed_into_a_column_march_there_and_one_undo_takes_them_back(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 6);
        await r.Frame();
        t.Table.SetTool(Tool.Formation);
        t.Table.Selection.Set(Six);
        int undos = t.S.MyUndoable.Count;
        t.Table.Forms.Form(Formations.Find("column")!);
        Check.That(t.Table.Hand.Marching, "the pieces are on the march");
        await Settled(r, t);
        Check.That(!t.Table.Hand.Holding, "all set down");
        var xs = Six.Select(id => At(t, id).X).ToList();
        var zs = Six.Select(id => At(t, id).Z).ToList();
        Check.That(xs.Max() - xs.Min() < 1e-6, "one behind another: " + string.Join(", ", xs));
        Check.Equal(6, zs.Select(z => Math.Round(z, 4)).Distinct().Count(), "each at its own depth");
        Check.That(Math.Abs(xs.Average() - 3.5) < 1e-6, "round where they stood");
        Check.Equal(undos + 1, t.S.MyUndoable.Count, "one thing to undo");
        t.S.UndoLast();
        await r.Frame();
        for (int i = 0; i < 6; i++) Check.Equal(new Vec2(i + 1, 0), At(t, Six[i]), Six[i] + " back");
    }

    [NodeTest]
    static async Task A_line_turned_a_quarter_wheels_round_its_centre(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 6);
        await r.Frame();
        t.Table.SetTool(Tool.Formation);
        t.Table.Selection.Set(Six);
        t.Table.Forms.Form(Formations.Find("line")!);
        await Settled(r, t);
        var before = Six.Select(id => At(t, id)).ToList();
        Check.That(before.Max(p => p.Z) - before.Min(p => p.Z) < 1e-6, "a line across the front");
        t.Table.Forms.Turn(90);
        await Settled(r, t);
        var after = Six.Select(id => At(t, id)).ToList();
        Check.That(after.Max(p => p.X) - after.Min(p => p.X) < 1e-4, "the line now runs up and down: " + string.Join(" ", after));
        Check.That(Math.Abs(after.Average(p => p.X) - before.Average(p => p.X)) < 1e-4, "round the same centre");
        Check.That(Six.All(id => Math.Abs(t.S.View.Find(U, id)!.RotDeg - 90) < 1e-4), "every piece faces the new way");
    }

    [NodeTest]
    static async Task A_drag_sets_the_front_and_the_ranks_behind_it(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 6);
        await r.Frame();
        t.Table.SetTool(Tool.Formation);
        t.Table.Selection.Set(Six);
        t.Table.Forms.Form(Formations.Find("line")!);
        await Settled(r, t);
        double across = Six.Select(id => t.Node(id).Footprint.X).Max() * (1 + t.Table.Forms.Gap);
        var from = new Vec2(20, 10);
        Check.That(t.Table.Forms.Press(from, adding: false, onPiece: false), "the drag starts on the empty table");
        t.Table.Forms.Drag(new Vec2(20 - 3 * across, 10));
        t.Table.Forms.Release();
        await Settled(r, t);
        var ps = Six.Select(id => At(t, id)).ToList();
        Check.Equal(2, ps.Select(p => Math.Round(p.Z, 4)).Distinct().Count(), "three wide, two ranks deep");
        Check.That(ps.All(p => p.Z >= 10 - 1e-6) || ps.All(p => p.Z <= 10 + 1e-6), "the ranks stand behind the front");
        Check.Equal(3, ps.Count(p => Math.Abs(p.Z - 10) < 1e-4), "the front rank on the dragged line");
    }
}
