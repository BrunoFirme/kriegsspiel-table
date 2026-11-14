using System.Linq;
using System.Threading.Tasks;
using KTable.Core.Protocol;

namespace KTable.App;

static class SelectionReportTests
{
    static ReportSelection[] Reports(OfflineTable t) => t.Server.Log.SelectMany(c => c is Batch b ? b.Items : new() { c }).OfType<ReportSelection>().ToArray();

    [NodeTest]
    static async Task A_resting_table_does_not_look_through_its_pieces_for_the_selection_each_frame_until_the_ring_is_in_hand(TestRunner r)
    {
        const int Resting = 20;
        var t = OfflineTable.Make(r, pieces: 6);
        await r.Frames(3);
        t.Table.Selection.Set(t.S.View.Layer(KTable.Core.Model.Layers.Umpire).Select(p => p.Id).ToList());
        await r.Frames(3);
        int before = t.Table.Selection.Listed;
        await r.Frames(Resting);
        Check.Equal(before, t.Table.Selection.Listed, "with the Select tool in hand and nothing moving, no frame lists the selection");
        t.Table.SetTool(Tool.Transform);
        await r.Frames(Resting);
        Check.That(t.Table.Selection.Listed >= before + Resting / 2, $"the ring measures what is selected while its tool is in hand: {t.Table.Selection.Listed - before} listings");
        t.Table.SetTool(Tool.Select);
        await r.Frames(3);
        before = t.Table.Selection.Listed;
        await r.Frames(Resting);
        Check.Equal(before, t.Table.Selection.Listed, "and it rests again once the ring is put away");
        t.Table.QueueFree();
    }

    [NodeTest]
    static async Task An_umpires_selection_is_reported_to_the_server_as_it_changes_and_not_again_when_it_stays(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 3);
        await r.Frames(2);
        int before = Reports(t).Length;
        t.Table.Selection.Set(new[] { "p1", "p2" });
        t.Table.Selection.Set(new[] { "p2" }, add: true);
        await r.Frames(2);
        var sent = Reports(t).Skip(before).ToArray();
        Check.Equal(1, sent.Length, "two changes in one frame go as one report");
        Check.That(sent[0].Selected.OrderBy(x => x).SequenceEqual(new[] { "p1", "p2" }), "the report names the selected pieces");
        t.Table.Selection.Set(new[] { "p1", "p2" });
        await r.Frames(2);
        Check.Equal(1, Reports(t).Length - before, "the same selection again is not sent again");
        t.Table.Selection.Set(System.Array.Empty<string>());
        await r.Frames(2);
        Check.That(Reports(t).Last().Selected.Count == 0, "clearing the selection is reported too");
        Check.That(t.Errors.Count == 0, "no command refused: " + string.Join("; ", t.Errors));
        t.Table.QueueFree();
    }
}
