using KTable.Client.Panels;
using KTable.Core.Model;
using KTable.Core.Panels;

namespace KTable.Client.Tests;

public class PanelSidesTests
{
    sealed class Meter : IPanelMeter
    {
        public V2 Measure(TextProps text, string words, float wrapWidth) => new(words.Length * text.Size * 0.5f, text.Size);
        public V2? PictureSize(string sprite) => null;
    }

    static PanelScene Scene(string xml)
    {
        var layout = PanelBuild.Of(xml).Layout;
        return PanelScene.Build(layout, new V2(PanelScene.PieceCanvas, PanelScene.PieceCanvas), new PanelContext(layout.Colors, "Black", true, true), new Meter(), null);
    }

    const string Block = """
        <Panel id="root" width="400" height="400">
          <Text id="front" position="0 75 -11" rotation="180 180 0">Front</Text>
          <Button id="plus" position="0 -35 5" rotation="180 0 0">+</Button>
          <Panel id="turned" position="0 0 2" rotation="180 0 0">
            <Text id="inner" position="0 0 -4">Inner</Text>
          </Panel>
          <Text id="flush">Flush</Text>
        </Panel>
        """;

    [Fact]
    public void An_element_below_a_tile_s_bottom_is_on_its_underside_and_one_above_or_level_stays_on_top()
    {
        var s = Scene(Block);
        var tile = new PieceDef { ModuleType = "Custom_Tile", ModuleRise = 1, ModuleOrigin = 0 };
        double bottom = PanelSides.Bottom(tile)!.Value;
        Assert.Equal(0, bottom);
        Assert.False(PanelSides.Under(s.ById("front")!, bottom));
        Assert.True(PanelSides.Under(s.ById("plus")!, bottom));
        Assert.False(PanelSides.Under(s.ById("flush")!, bottom));
        Assert.True(PanelSides.AnyUnder(s.ById("root")!, bottom));
        Assert.True(PanelSides.FacesAway(s.ById("plus")!));
        Assert.False(PanelSides.FacesAway(s.ById("front")!));
    }

    [Fact]
    public void A_parent_turned_to_face_away_carries_its_children_s_depth_the_other_way()
    {
        var s = Scene(Block);
        Assert.Equal(6, PanelSides.Depth(s.ById("inner")!), 3);
        Assert.True(PanelSides.Under(s.ById("inner")!, 0));
    }

    [Fact]
    public void A_token_s_bottom_is_half_its_thickness_below_its_middle_and_a_piece_made_here_has_no_underside()
    {
        var token = new PieceDef { ModuleType = "Custom_Token", ModuleRise = 2, ModuleOrigin = 0.1 };
        Assert.Equal(-0.05, PanelSides.Bottom(token)!.Value, 6);
        Assert.Null(PanelSides.Bottom(new PieceDef()));
        Assert.Null(PanelSides.Bottom(null));
    }
}
