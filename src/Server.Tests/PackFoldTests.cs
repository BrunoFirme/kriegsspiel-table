using KTable.Core;
using KTable.Core.Model;

namespace KTable.Server.Tests;

public class PackFoldTests
{
    [Fact]
    public void A_save_that_named_a_module_s_extras_pack_opens_whole_once_its_pieces_are_in_the_module_s_pack()
    {
        using var t = new Table();
        var state = Json.Clone(t.Server.State);
        state.Packs.Add("testpack.extras");
        t.Server.LoadSave(state, new List<TurnRecord>(), "Before one pack");
        Assert.Empty(t.Server.MissingPacks);
        Assert.DoesNotContain("testpack.extras", t.Server.State.Packs);
        Assert.All(t.Server.State.Layer(Layers.Umpire), p => Assert.NotNull(t.Store.Def(p.Def)));
    }
}
