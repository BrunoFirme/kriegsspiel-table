using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class PlayerCardTests
{
    const ulong LookMs = 3000;

    static Button? Chip(HudTests.Rig h) =>
        h.Hud.World!.Panel.FindChildren("*", "Button", true, false).OfType<Button>().FirstOrDefault(b => b.Icon == Ui.Dot && b.IsVisibleInTree());

    static async Task<Button> ChipShown(TestRunner r, HudTests.Rig h)
    {
        ulong until = Time.GetTicksMsec() + LookMs;
        while (Chip(h) == null && Time.GetTicksMsec() < until) await r.Frames(1);
        return Chip(h) ?? throw new TestFailure("no player chip on the bar");
    }

    static PopupPanel Card(HudTests.Rig h) =>
        WindowTests.Descendants(h.Hud.Ctx.Root).OfType<PopupPanel>().FirstOrDefault(p => p.Visible) ?? throw new TestFailure("no card is up");

    static Button Row(PopupPanel card, string text) =>
        card.FindChildren("*", "Button", true, false).OfType<Button>().FirstOrDefault(b => b.FindChildren("*", "Label", true, false).OfType<Label>().Any(l => l.Text == text))
        ?? throw new TestFailure($"no row {text}");

    static bool Has(PopupPanel card, string text) => card.FindChildren("*", "Label", true, false).OfType<Label>().Any(l => l.Text == text);

    [NodeTest]
    static async Task A_player_s_chip_on_the_bar_opens_their_card_with_their_permissions_the_same_card_as_their_body(TestRunner r)
    {
        var h = await HudTests.Host(r);
        h.S.OnPeers(new PeerList { Peers = [new PeerInfo { PeerId = 1, Name = h.S.Me.Name, Role = Role.Umpire }, new PeerInfo { PeerId = 5, Name = "Ann", Role = Role.Player }] });
        (await ChipShown(r, h)).EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(2);
        var card = Card(h);
        Check.That(Has(card, "Put Ann out…"), "the card is Ann's");
        Check.That(!Has(card, "Change body"), "players do not choose their own body");
        Check.That(!h.S.View.RightsOf("Ann").Fly, "players start on the ground");
        Row(card, "Fly").EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(2);
        Check.That(h.S.View.RightsOf("Ann").Fly, "the Fly toggle lets Ann fly");
        Check.That(!(h.S.View.Everyone ?? new PlayerRights()).Fly, "and nobody else");
        Check.That(card.Visible && Row(card, "Fly").FindChildren("*", "Label", true, false).OfType<Label>().First().Text == "✓", "the card stays up showing the tick");
        card.Hide();
        h.Table.Avatars.Bodies = [new BodyOf("p1", 5, "Ann", 0)];
        (await ChipShown(r, h)).EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(2);
        card = Card(h);
        Check.That(Has(card, "Properties…") && Has(card, "Put Ann out…") && Has(card, "Fly"), "with a body, the chip opens the body's own right-click card with Ann's rows in it");
        Check.That(h.Table.SelectedNodes.Select(n => n.Piece.Id).SequenceEqual(["p1"]), "and it acts on her body");
        card.Hide();
    }
}
