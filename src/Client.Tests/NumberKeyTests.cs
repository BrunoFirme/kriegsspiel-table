using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Client.Tests;

public class NumberKeyTests
{
    readonly ClientSession _s = new(new PackageStore(TestFolders.New()));

    public NumberKeyTests() => _s.OnJoined(new JoinResult { Ok = true, PeerId = 2, Role = Role.Umpire, View = new GameState(), Created = "2026-01-01T00:00:00.0000000Z" });

    [Fact]
    public void A_number_over_a_scripted_piece_goes_to_its_script_when_the_table_has_no_global_script()
    {
        var scripted = new PieceInstance { Id = "p-000001", Def = "d", Script = "function on_number_typed(who, n) end" };
        var plain = new PieceInstance { Id = "p-000002", Def = "d" };
        var cmds = NumberKey.Commands(_s, new[] { (Layers.Umpire, scripted), (Layers.Umpire, plain) }, 3);
        var typed = Assert.IsType<NumberTyped>(Assert.Single(cmds));
        Assert.Equal(("p-000001", 3), (typed.Id, typed.Number));
        Assert.True(NumberKey.ToScript(_s, Layers.Umpire, scripted));
        Assert.False(NumberKey.ToScript(_s, Layers.Umpire, plain));
    }
}
