using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class EffectTintTests
{
    const string U = Layers.Umpire;

    static bool OffersTint(Node root) => WindowTests.Descendants(root).OfType<PopupPanel>().Where(p => p.Visible)
        .Any(card => WindowTests.Descendants(card).OfType<Label>().Any(l => l.Text == "Tint"));

    static async Task<bool> MenuTints(HudTests.Rig h, TestRunner r, string id)
    {
        h.Table.Selection.Set(new[] { id });
        h.Hud.Menus.Show(new Vector2(300, 300), h.Table.Pieces.NodeOf(U, id) ?? throw new TestFailure($"{id} is on the table"));
        await r.Frames(2);
        bool offered = OffersTint(h.Hud.Ctx.Root);
        foreach (var p in WindowTests.Descendants(h.Hud.Ctx.Root).OfType<PopupPanel>().Where(p => p.Visible).ToList()) p.Hide();
        await r.Frames(1);
        return offered;
    }

    [NodeTest]
    static async Task Signal_smoke_and_a_star_shell_take_a_tint_rain_and_a_bivouac_s_fires_keep_their_colour(TestRunner r)
    {
        var h = await HudTests.Host(r, pieces: 0, packs: new[] { FirmeEffects.PackId }, prepare: StandardPacks.Ensure);
        h.S.Do(new SpawnPiece { Layer = U, Def = FirmeEffects.IdOf("Signal smoke"), Id = "smoke", Pos = new Vec2(2, 2) });
        h.S.Do(new SpawnPiece { Layer = U, Def = FirmeEffects.IdOf("Star shell"), Id = "flare", Pos = new Vec2(8, 2) });
        h.S.Do(new SpawnPiece { Layer = U, Def = FirmeEffects.IdOf("Rain shower"), Id = "rain", Pos = new Vec2(2, 12) });
        h.S.Do(new SpawnPiece { Layer = U, Def = FirmeEffects.PackId + "." + PackEdit.Slug("Bivouac"), Id = "camp", Pos = new Vec2(12, 12) });
        await r.Seconds(0.3);
        PieceNode Node(string id) => h.Table.Pieces.NodeOf(U, id) ?? throw new TestFailure($"{id} is on the table");

        Check.That(await MenuTints(h, r, "smoke"), "Signal smoke offers Tint on its right-click");
        Check.That(await MenuTints(h, r, "flare"), "the star shell offers Tint");
        Check.That(!await MenuTints(h, r, "rain"), "rain offers no Tint");
        Check.That(await MenuTints(h, r, "camp"), "a bivouac is tinted as any piece");

        Check.Equal("#D9623A", Node("smoke").PlacedEffect.Color, "signal smoke placed in its own orange");
        h.S.Do(new SetTint { Layer = U, Id = "smoke", Value = "#2e8b45" });
        h.S.Do(new SetTint { Layer = U, Id = "flare", Value = "#c0392b" });
        h.S.Do(new SetTint { Layer = U, Id = "rain", Value = "#c0392b" });
        h.S.Do(new SetTint { Layer = U, Id = "camp", Value = "#2a6ebb" });
        await r.Frames(3);
        Check.Equal("#2e8b45", Node("smoke").PlacedEffect.Color, "signal smoke rebuilt green");
        Check.That(Node("smoke").Emitters > 0, "and still smoking");
        Check.Equal("#c0392b", Node("flare").PlacedEffect.Color, "the star shell burns red");
        Check.Equal(null, Node("rain").PlacedEffect.Color, "rain keeps its own colour");
        Check.Equal(null, Node("camp").PlacedEffect.Color, "the bivouac's fires keep theirs when its tents are tinted");
        Check.That(Node("smoke").TintShown().IsEqualApprox(Coords.ParseColor("#2e8b45", Colors.White)), "the menu marks the green it shows");

        h.S.UndoLast();
        h.S.UndoLast();
        h.S.UndoLast();
        h.S.UndoLast();
        await r.Frames(3);
        Check.Equal("#D9623A", Node("smoke").PlacedEffect.Color, "undo gives back its orange");
        Check.Equal(null, Node("flare").PlacedEffect.Color, "and the star shell its white");
    }
}
