using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using KTable.Core;
using KTable.Core.Model;

namespace KTable.App;

static class PartsTests
{
    const string U = Layers.Umpire;
    const double FlagTall = 0.6, RoutedTall = 0.5, Base = 0.2, Out = 1.5, In = -0.5;

    static void Unit(PackageStore store)
    {
        var pack = new PackManifest { Id = "parts", Name = "Parts" };
        store.RegisterDef(pack, new PieceDef { Id = "flag", Name = "Flag", Size = new Size2(0.4, 0.4), Thickness = FlagTall });
        store.RegisterDef(pack, new PieceDef
        {
            Id = "unit", Name = "Battalion", Size = new Size2(2, 1), Thickness = Base,
            States = new List<PieceStateDef>
            {
                new() { Name = "Formed", Parts = new() { new PartDef { Def = "flag", X = Out, Y = Base } } },
                new() { Name = "Routed", Thickness = RoutedTall, Parts = new() { new PartDef { Def = "flag", X = In, Y = RoutedTall, Rot = 90 } } },
            },
        });
    }

    static void Place(GameState g) => g.Layers[U].Add(new PieceInstance { Id = "u1", Def = "unit", Pos = new Vec2(-4, 0) });

    [ScreenTest]
    static async Task A_piece_draws_the_parts_of_its_look_is_picked_through_them_and_swaps_them_with_its_look(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 1, prepare: Unit, arrange: Place);
        await r.Frames(10);
        var n = t.Node("u1");
        float floor = n.Position.Y;

        var flag = n.Parts.Count == 1 ? n.Parts[0] : throw new TestFailure($"one part is drawn with the piece: {n.Parts.Count}");
        Check.That(flag.GlobalPosition.IsEqualApprox(new Vector3(-4 - (float)Out, floor + (float)Base, 0)), $"the flag stands on the piece where its state puts it: {flag.GlobalPosition}");
        Check.That(n.Contains(new Vec2(-4 - Out, 0)) && !n.Contains(new Vec2(-4 - Out - 0.4, 0)), "the piece is picked through its flag and nowhere past it");
        Check.That(Mathf.IsEqualApprox(n.TopY - floor, (float)(Base + FlagTall), 0.01f), $"its top is the flag's top: {n.TopY - floor}");

        t.S.Do(new KTable.Core.Protocol.SetState { Layer = U, Id = "u1", State = 1 });
        await r.Frames(5);
        n = t.Node("u1");
        flag = n.Parts.Count == 1 ? n.Parts[0] : throw new TestFailure($"the routed state draws its own part: {n.Parts.Count}");
        Check.That(Mathf.IsEqualApprox(n.Thickness, (float)RoutedTall), $"the routed state stands at its own height: {n.Thickness}");
        Check.That(Mathf.IsEqualApprox(flag.GlobalPosition.X, -4 - (float)In, 0.01f), $"its flag moved to where the routed state puts it: {flag.GlobalPosition}");
        Check.That(!n.Contains(new Vec2(-4 - Out, 0)), "the formed state's flag is gone");
        Check.Equal(2, t.Table.Pieces.Count, "the parts are not pieces of their own");
        Check.Equal(0, t.Errors.Count, string.Join("; ", t.Errors));
    }
}
