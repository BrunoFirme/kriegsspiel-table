using System.Text.Json.Nodes;
using KTable.Core;
using KTable.Core.Model;

namespace KTable.Converter.Tests;

public sealed partial class ConverterTests
{
    [Fact]
    public void A_player_pawn_is_a_round_piece_in_its_colour()
    {
        var cfg = TempConfig();
        var rep = new Report();
        var defs = new DefFactory(cfg, new AssetStore(cfg, rep), rep);
        var pack = new PackOut(cfg.Output, "t", "T", "t", null);
        var pawn = Obj("PlayerPawn", "Blue", 1);
        pawn["ColorDiffuse"] = new JsonObject { ["r"] = 0.1, ["g"] = 0.2, ["b"] = 0.9 };

        var def = defs.DefFor(new TtsObj(pawn), pack)!.Def;

        Assert.Equal(("circle", "#1A33E6", true), (def.Shape, def.Tint, def.TintsPictures));
        Assert.Contains("pawn", def.Tags);
        Assert.Equal(DefFactory.PawnAcross, def.Size.W);
        Assert.Equal(DefFactory.PawnHeight, def.Thickness);
    }
}
