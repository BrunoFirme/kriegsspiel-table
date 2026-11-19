using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class DressTests
{
    static AcceptDialog Dialog(TestRunner r) =>
        r.GetTree().Root.FindChildren("*", "AcceptDialog", true, false).OfType<AcceptDialog>().Last(d => d.Title.StartsWith("Dress", System.StringComparison.Ordinal));

    static T Beside<T>(Node under, string label) where T : Control =>
        under.FindChildren("*", "Label", true, false).OfType<Label>().Where(l => l.Text == label).Select(l => l.GetParent().GetChild(l.GetIndex() + 1)).OfType<T>().First();

    [NodeTest]
    static async Task The_fresh_body_stands_undressed_and_an_umpire_dresses_it_part_by_part_and_keeps_the_look_for_another_table(TestRunner r)
    {
        var h = await HudTests.Host(r, pieces: 0, packs: [FirmeEffects.PackId], prepare: StandardPacks.Ensure);
        Check.That(h.S.Store.Def(FirmeEffects.FreshBody) is { Mesh: CrowdPiece.Made }, "Firme's Effects and Props holds the fresh body");
        Check.That(h.S.Store.Def(FirmeEffects.PackId + ".tavern") != null && h.S.Store.Pack(FirmeEffects.PackId)!.Pieces.Any(RoundTracker.Is), "beside the Tavern and the round tracker");
        h.S.Do(new SpawnPiece { Layer = Layers.Umpire, Id = "body", Def = FirmeEffects.FreshBody, Pos = new Vec2(0, 0) });
        await r.Frames(4);
        Check.That(h.Table.Pieces.NodeOf(Layers.Umpire, "body")?.Model?.Mesh != null, "it is drawn as an undressed person");
        DressDialog.Piece(h.Hud.Ctx, Layers.Umpire, h.S.View.Find(Layers.Umpire, "body")!);
        await r.Frames(2);
        var dlg = Dialog(r);
        var hat = Beside<OptionButton>(dlg, "Hat");
        int tricorne = Enumerable.Range(0, hat.ItemCount).Single(i => hat.GetItemText(i) == "Tricorne");
        hat.Select(tricorne);
        hat.EmitSignal(OptionButton.SignalName.ItemSelected, tricorne);
        await r.Frames(3);
        Check.Equal("tricorne", h.S.View.Find(Layers.Umpire, "body")!.Look?.Part(Wardrobe.Hat)?.Kind, "a hat picked is worn at once");
        var name = dlg.FindChildren("*", "LineEdit", true, false).OfType<LineEdit>().First(e => e.PlaceholderText == "Name");
        name.Text = "Watchman";
        dlg.FindChildren("*", "Button", true, false).OfType<Button>().First(b => b.Text == "Save").EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(2);
        Check.Equal("tricorne", h.Hud.Cfg.Looks?.GetValueOrDefault("Watchman")?.Part(Wardrobe.Hat)?.Kind, "a look is kept on this PC for any table");
        h.Hud.Cfg.Looks = null;
        h.S.UndoLast();
        await r.Frames(2);
        Check.That(h.S.View.Find(Layers.Umpire, "body")!.Look == null, "and undo takes the hat off again");
        var figure = dlg.FindChildren("*", "SubViewportContainer", true, false).OfType<FigureTurntable>().Single();
        Check.That(figure.FindChildren("*", "MeshInstance3D", true, false).Count == 1, "the body being dressed turns beside the pages");
        var people = Beside<OptionButton>(dlg, "Nationality");
        int continental = Enumerable.Range(0, people.ItemCount).Single(i => people.GetItemText(i) == "Continental");
        people.Select(continental);
        people.EmitSignal(OptionButton.SignalName.ItemSelected, continental);
        await r.Frames(3);
        var dressed = h.S.View.Find(Layers.Umpire, "body")!.Look;
        Check.That(dressed?.Part(Wardrobe.Thigh)?.Kind == "breeches" || dressed?.Part(Wardrobe.Robe)?.Kind == "skirt", "a nationality picked on the Garments page dresses the body whole from it");
        Check.That(figure.FindChildren("*", "MeshInstance3D", true, false).Count(m => !m.IsQueuedForDeletion()) == 1, "and the turning figure wears it");
        dlg.Hide();
    }

    [NodeTest]
    static Task A_player_gets_the_fresh_body_and_dresses_only_their_own_as_the_umpire_allows(TestRunner r)
    {
        var fresh = AvatarBody.OfPiece(FirmeEffects.FreshBody);
        var bare = new PlayerRights { Take = false };
        Check.Equal(fresh, Net.BodyGranted(Role.Player, bare, null, fresh), "a player without a body of their own gets the fresh body");
        Check.That(Net.BodyGranted(Role.Player, bare, null, AvatarBody.OfPiece("std.figure")) == null, "and no other kind");
        var tricorne = CrowdWardrobe.Fresh();
        tricorne.Wear(new Garment { Part = Wardrobe.Hat, Kind = "tricorne" });
        var whole = CrowdWardrobe.Offer(null, null);
        var gothicPoor = CrowdWardrobe.Offer(CrowdDress.Flat(Nationalities.Named("gothic")!), CrowdEntry.Poor);
        Check.That(!Net.DressGranted(Role.Player, new PlayerRights(), true, tricorne, whole), "a player dresses only when the umpire allows it");
        Check.That(Net.DressGranted(Role.Player, new PlayerRights { Dress = true }, true, tricorne, whole), "then from the whole wardrobe");
        Check.That(!Net.DressGranted(Role.Player, new PlayerRights { Dress = true }, true, tricorne, gothicPoor), "or only what the nationality and wealth chosen wear");
        Check.That(!Net.DressGranted(Role.Player, new PlayerRights { Dress = true }, false, CrowdWardrobe.Fresh(), whole), "and only their own body");
        Check.That(Net.DressGranted(Role.Umpire, new PlayerRights(), false, tricorne, gothicPoor), "an umpire dresses any body");
        return Task.CompletedTask;
    }
}
