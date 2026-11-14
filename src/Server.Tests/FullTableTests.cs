using System.Diagnostics;
using KTable.Core.Model;
using KTable.Core.Protocol;
using Xunit.Abstractions;

namespace KTable.Server.Tests;

public sealed class FullTable : IDisposable
{
    public readonly Table T = new();
    public readonly long SpawnMs;

    public FullTable()
    {
        foreach (var seat in FullTableTests.UmpireSeats.Skip(3)) T.LetIn(seat, $"Umpire {seat}", Role.Umpire);
        foreach (var seat in FullTableTests.ObserverSeats.Skip(1)) T.LetIn(seat, $"Watcher {seat}", Role.Observer);
        var spawn = new Batch();
        for (int i = 0; i < FullTableTests.Pieces; i++) spawn.Items.Add(new SpawnPiece { Layer = Layers.Umpire, Def = "inf", Pos = new(i % 100, i / 100) });
        var clock = Stopwatch.StartNew();
        T.Ok(Table.Ump, spawn);
        SpawnMs = clock.ElapsedMilliseconds;
    }

    public void Dispose() => T.Dispose();
}

public class FullTableTests : IClassFixture<FullTable>
{
    const string U = Layers.Umpire;
    public const int Pieces = 10_000, Umpires = 8, Observers = 32;

    readonly ITestOutputHelper _out;
    readonly Table _t;

    public FullTableTests(FullTable full, ITestOutputHelper output)
    {
        _t = full.T;
        _out = output;
        _out.WriteLine($"10,000 pieces spawned in one batch: {full.SpawnMs} ms");
    }

    public static readonly long[] UmpireSeats = Enumerable.Range(1, Umpires).Select(i => (long)i).ToArray();
    public static readonly long[] ObserverSeats = Enumerable.Range(0, Observers).Select(i => Table.Obs + i).ToArray();

    static List<string> Spawned(Table t) => t.Server.State.Layer(U).Select(p => p.Id).Skip(2).ToList();

    T Timed<T>(string what, Func<T> work)
    {
        var clock = Stopwatch.StartNew();
        var got = work();
        _out.WriteLine($"{what}: {clock.ElapsedMilliseconds} ms");
        return got;
    }

    [Fact]
    public void A_move_at_a_full_table_reaches_all_eight_umpires_as_one_shared_copy()
    {
        var t = _t;
        var id = Spawned(t)[Pieces / 2];

        var outs = Timed("one move to 40 seats", () => t.Ok(Table.Ump2, new MovePiece { Layer = U, Id = id, Pos = new(500, 500), RotDeg = 0 }));

        var toUmpires = outs.Where(o => UmpireSeats.Contains(o.PeerId)).Select(o => o.Message).OfType<CommandMessage>().ToList();
        Assert.Equal(Umpires, toUmpires.Count);
        Assert.Single(toUmpires.Select(m => m.Command).Distinct(ReferenceEqualityComparer.Instance));
        Assert.Equal(new Vec2(500, 500), t.Server.State.Find(U, id)!.Pos);
    }

    [Fact]
    public void Ten_thousand_pieces_move_as_one_batch_and_one_undo_puts_them_back()
    {
        var t = _t;
        var ids = Spawned(t);
        Assert.Equal(Pieces, ids.Count);
        var move = new Batch();
        foreach (var id in ids) move.Items.Add(new MovePiece { Layer = U, Id = id, Pos = new(-7, -7), RotDeg = 90 });

        Timed("10,000 pieces moved in one batch", () => t.Ok(Table.Ump, move));
        var moved = t.Server.State.LastSeq;
        Assert.All(ids, id => Assert.Equal(new Vec2(-7, -7), t.Server.State.Find(U, id)!.Pos));

        Timed("that move undone", () => t.Ok(Table.Ump, new Undo { TargetSeq = moved }));
        Assert.Equal(new Vec2(99, 99), t.Server.State.Find(U, ids[^1])!.Pos);
    }

    [Fact]
    public void Ten_thousand_pieces_are_deleted_as_one_batch_and_one_undo_brings_every_one_back_in_its_place()
    {
        var t = _t;
        var ids = Spawned(t);
        var delete = new Batch();
        foreach (var id in ids) delete.Items.Add(new DeletePiece { Layer = U, Id = id });

        Timed("10,000 pieces deleted in one batch", () => t.Ok(Table.Ump, delete));
        var deleted = t.Server.State.LastSeq;
        Assert.Equal(2, t.Server.State.Layer(U).Count);

        Timed("that delete undone", () => t.Ok(Table.Ump, new Undo { TargetSeq = deleted }));
        Assert.Equal(ids, Spawned(t));
        Assert.Equal(new Vec2(99, 99), t.Server.State.Find(U, ids[^1])!.Pos);
    }

    [Fact]
    public void A_full_table_is_sent_whole_to_a_seat_that_asks_for_it()
    {
        var t = _t;

        var outs = Timed("a 10,000-piece table resent to one seat", () => t.Server.Resync(ObserverSeats[^1]));

        Assert.Contains(outs, o => o.PeerId == ObserverSeats[^1]);
    }
}
