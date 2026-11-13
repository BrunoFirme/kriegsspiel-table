using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Client.Updates;

namespace KTable.App;

static class FirmeMessageTests
{
    static readonly Color Lit = TitleStyle.CardStyle(true).BgColor;

    static Label[] Labels(Node n) => n.FindChildren("*", nameof(Label), true, false).OfType<Label>().ToArray();

    static StyleBoxFlat Fill(Node slot) => (StyleBoxFlat)slot.FindChildren("*", nameof(PanelContainer), true, false).OfType<PanelContainer>().Single().GetThemeStylebox("panel");

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
            var cfg = new Settings();
            var shown = FirmeMessage.Put(column, cfg);
            await r.Frames(1);
            var texts = Labels(shown.Slot).Select(l => l.Text).ToArray();
            Check.Equal("FROM FIRME", texts[0], "the card is signed");
            Check.Equal("Coming to Steam!", texts[1], "the shipped message is shown");
            Check.Equal("Coming to Steam!", Settings.Load().SeenNote, "the message is kept as seen");
            Check.That(Fill(shown.Slot).BgColor.IsEqualApprox(Lit), "a new message sits on the chosen-card fill");

            var later = new Settings();
            var again = FirmeMessage.Put(column, later);
            later.SeenNote = "Old news";
            again.Show(new FirmeNote("Old news", null));
            await r.Frames(1);
            Check.That(Fill(again.Slot).BgColor.IsEqualApprox(TitleStyle.CardStyle(false).BgColor), "a message seen before sits on the plain card fill");
            Check.Equal("Old news", later.SeenNote, "showing it again changes nothing");

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
