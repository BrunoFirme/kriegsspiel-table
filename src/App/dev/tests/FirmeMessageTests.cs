using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Client.Updates;

namespace KTable.App;

static class FirmeMessageTests
{
    static readonly Color Lit = FirmeMessage.Rule(true).BorderColor;

    static Label[] Labels(Node n) => n.FindChildren("*", nameof(Label), true, false).OfType<Label>().ToArray();

    static StyleBoxFlat Rule(Node slot) => (StyleBoxFlat)slot.FindChildren("*", nameof(PanelContainer), true, false).OfType<PanelContainer>().Single().GetThemeStylebox("panel");

    [NodeTest]
    static async Task Firmes_message_sits_lit_the_first_time_it_is_seen_and_calm_once_it_was_seen_before(TestRunner r)
    {
        var file = Path.Combine(Path.GetTempPath(), "ktable-nodetest-" + System.Guid.NewGuid().ToString("N") + ".json");
        var place = Settings.FileAt;
        Settings.FileAt = () => file;
        var column = new VBoxContainer();
        r.AddChild(column);
        try
        {
            var shipped = FirmeNotes.Read(Godot.FileAccess.GetFileAsString("res://" + FirmeNotes.ShippedAt))?.Text ?? "";
            var cfg = new Settings();
            var shown = FirmeMessage.Put(column, cfg);
            await r.Frames(1);
            var texts = Labels(shown.Slot).Select(l => l.Text).ToArray();
            Check.Equal(shipped, string.Join("|", texts), "the shipped message is shown alone");
            Check.That(Settings.Load().SeenNotes.Contains(shipped), "the message is kept as seen");
            shown.Show(new FirmeNote("News", null));
            await r.Frames(1);
            Check.That(Rule(shown.Slot).BorderColor.IsEqualApprox(Lit), "a new message is ruled in brass");
            Check.Equal(new[] { shipped, "News" }.Distinct().Count(), Settings.Load().SeenNotes.Count, "and is kept beside the first");

            var offline = Settings.Load();
            var again = FirmeMessage.Put(column, offline);
            await r.Frames(1);
            Check.Equal(shipped, string.Join("|", Labels(again.Slot).Select(l => l.Text)), "offline, the shipped message shows again");
            Check.That(Rule(again.Slot).BorderColor.IsEqualApprox(FirmeMessage.Rule(false).BorderColor), "a message seen before has the faint rule, even after a newer one");
            Check.Equal(0f, Rule(again.Slot).BgColor.A, "and no fill, so it never reads as a field");
            Check.That(!offline.FirstSight("News"), "the newer one is still known as seen");

            again.Show(null);
            await r.Frames(1);
            Check.Equal(0, again.Slot.GetChildCount(), "no message, no card");
        }
        finally
        {
            column.QueueFree();
            Settings.FileAt = place;
            File.Delete(file);
        }
    }
}
