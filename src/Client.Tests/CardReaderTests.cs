using KTable.Core;
using KTable.Core.Model;

namespace KTable.Client.Tests;

public class CardReaderTests
{
    [Fact]
    public void A_scenarios_card_is_its_head_and_piece_count_or_nothing_for_a_file_that_will_not_do()
    {
        var dir = TestFolders.New();
        var path = Path.Combine(dir, "scenario.json");
        Assert.Equal((null, 0), CardReader.Card(path));
        File.WriteAllText(path, "{\"id\":\"feud\",\"name\":\"The Feud\",\"packs\":[\"p\"],\"sheets\":[],\"umpireBoard\":[{},{},{}]}");
        var (s, n) = CardReader.Card(path);
        Assert.Equal(("The Feud", 3), (s!.Name, n));
        Assert.Equal(new[] { "p" }, s.Packs);

        var noBoard = Path.Combine(dir, "noboard.json");
        File.WriteAllText(noBoard, "{\"id\":\"x\",\"name\":\"X\",\"packs\":[],\"sheets\":[],\"umpireBoard\":null}");
        Assert.Equal((null, 0), CardReader.Card(noBoard));
        var torn = Path.Combine(dir, "torn.json");
        File.WriteAllText(torn, "{\"id\":\"x\",\"name\":");
        Assert.Equal((null, 0), CardReader.Card(torn));
        var tooNew = Path.Combine(dir, "new.json");
        File.WriteAllText(tooNew, $"{{\"format\":{Scenario.CurrentFormat + 1},\"id\":\"x\",\"name\":\"X\",\"packs\":[],\"sheets\":[],\"umpireBoard\":[]}}");
        Assert.Equal((null, 0), CardReader.Card(tooNew));
    }

    [Fact]
    public void A_card_is_read_again_only_when_its_file_changes_time_or_length()
    {
        var path = Path.Combine(TestFolders.New(), "scenario.json");
        File.WriteAllText(path, "{\"id\":\"a\",\"name\":\"Aaaa\",\"packs\":[],\"sheets\":[],\"umpireBoard\":[]}");
        var at = File.GetLastWriteTimeUtc(path);
        Assert.Equal("Aaaa", CardReader.Card(path).s!.Name);
        File.WriteAllText(path, "{\"id\":\"a\",\"name\":\"Bbbb\",\"packs\":[],\"sheets\":[],\"umpireBoard\":[]}");
        File.SetLastWriteTimeUtc(path, at);
        Assert.Equal("Aaaa", CardReader.Card(path).s!.Name);
        File.SetLastWriteTimeUtc(path, at.AddSeconds(2));
        Assert.Equal("Bbbb", CardReader.Card(path).s!.Name);
        File.WriteAllText(path, "{\"id\":\"a\",\"name\":\"Ccccc\",\"packs\":[],\"sheets\":[],\"umpireBoard\":[]}");
        File.SetLastWriteTimeUtc(path, at.AddSeconds(2));
        Assert.Equal("Ccccc", CardReader.Card(path).s!.Name);
    }

    [Fact]
    public void A_saves_card_says_its_table_scenario_clock_and_mirror_or_why_it_cannot_be_read()
    {
        var dir = TestFolders.New();
        var path = Path.Combine(dir, "live.json");
        Assert.Equal(new SaveCard(null, null, null, null, "the file is missing"), CardReader.Save(path));
        var save = new SaveFile { ScenarioId = "feud", MirrorOf = "host-1" };
        save.Snapshot.Name = "Pogue vs Boggs";
        save.Snapshot.Clock.Turn = 7;
        Json.Save(path, save);
        var card = CardReader.Save(path);
        Assert.Equal(("Pogue vs Boggs", "feud", 7, "host-1"), (card.Name, card.ScenarioId, card.Clock!.Turn, card.MirrorOf));
        Assert.Null(card.Error);
        Assert.Same(card, CardReader.Save(path));

        var bad = Path.Combine(dir, "bad.json");
        File.WriteAllText(bad, "[]");
        var badCard = CardReader.Save(bad);
        Assert.Null(badCard.Name);
        Assert.False(string.IsNullOrEmpty(badCard.Error));

        var torn = Path.Combine(dir, "torn.json");
        var whole = File.ReadAllText(path);
        File.WriteAllText(torn, whole[..(whole.Length - 20)]);
        Assert.False(string.IsNullOrEmpty(CardReader.Save(torn).Error));
    }
}
