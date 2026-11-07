using KTable.Client.Panels;
using KTable.Core.Panels;

namespace KTable.Client.Tests;

public class PanelSceneTests
{
    sealed class Meter : IPanelMeter
    {
        public V2 Measure(TextProps text, string words, float wrapWidth)
        {
            float w = words.Length * text.Size * 0.5f;
            int lines = wrapWidth > 0 && w > wrapWidth ? (int)Math.Ceiling(w / wrapWidth) : 1;
            return new V2(wrapWidth > 0 ? Math.Min(w, wrapWidth) : w, lines * text.Size);
        }

        public V2? PictureSize(string sprite) => sprite == "crest" ? new V2(64, 32) : null;
    }

    static readonly V2 Screen = new(1920, 1080);
    static PanelContext Viewer(string color = "Black", bool host = true) => new(new Dictionary<string, string>(), color, host, host);

    static PanelScene Scene(string xml, PanelContext? ctx = null, Dictionary<string, string>? local = null)
    {
        var layout = PanelBuild.Of(xml).Layout;
        return PanelScene.Build(layout, Screen, ctx ?? new PanelContext(layout.Colors, "Black", true, true), new Meter(), local);
    }

    static (float X, float Y, float W, float H) At(PanelScene s, string id) => s.Screen(s.ById(id)!);

    [Fact]
    public void A_sized_panel_sits_in_the_middle_of_the_screen()
    {
        var s = Scene("<Panel id=\"p\" width=\"400\" height=\"200\"/>");
        Assert.Equal((760f, 440f, 400f, 200f), At(s, "p"));
    }

    [Fact]
    public void Alignment_and_offset_place_a_panel_from_a_corner_with_y_going_up()
    {
        var s = Scene("<Panel id=\"p\" width=\"100\" height=\"50\" rectAlignment=\"UpperLeft\" offsetXY=\"10 -20\"/><Panel id=\"q\" width=\"100\" height=\"50\" rectAlignment=\"LowerRight\" offsetXY=\"-5 5\"/>");
        Assert.Equal((10f, 20f, 100f, 50f), At(s, "p"));
        Assert.Equal((1815f, 1025f, 100f, 50f), At(s, "q"));
    }

    [Fact]
    public void A_width_in_percent_takes_that_share_of_the_parent_about_the_alignment()
    {
        var s = Scene("<Panel id=\"p\" width=\"50%\" height=\"100\"/><Panel id=\"l\" width=\"25%\" height=\"100\" rectAlignment=\"MiddleLeft\"/>");
        Assert.Equal((480f, 490f, 960f, 100f), At(s, "p"));
        Assert.Equal((0f, 490f, 480f, 100f), At(s, "l"));
    }

    [Fact]
    public void A_bare_text_fills_its_parent_in_dark_grey_fourteen_points_centred()
    {
        var s = Scene("<Panel width=\"200\" height=\"100\"><Text id=\"t\">Hi</Text></Panel>");
        var t = s.ById("t")!;
        Assert.Equal((860f, 490f, 200f, 100f), At(s, "t"));
        Assert.Equal(new Paint(0.19608f, 0.19608f, 0.19608f, 1), t.Text!.Color);
        Assert.Equal(14, t.Text.Size);
        Assert.Equal(Anchor.MiddleCenter, t.Text.Alignment);
        Assert.Equal("Hi", t.Text.Text);
    }

    [Fact]
    public void A_toggle_on_its_own_sits_thirty_two_above_the_middle_at_its_prefab_size()
    {
        var s = Scene("<Panel width=\"400\" height=\"400\"><Toggle id=\"t\"/></Panel>");
        Assert.Equal((880f, 498f, 160f, 20f), At(s, "t"));
    }

    [Fact]
    public void A_vertical_layout_shares_its_height_among_children_after_padding_and_spacing()
    {
        var s = Scene("<VerticalLayout width=\"200\" height=\"230\" padding=\"10 20 30 0\" spacing=\"20\"><Panel id=\"a\"/><Panel id=\"b\"/></VerticalLayout>");
        var a = At(s, "a");
        var b = At(s, "b");
        Assert.Equal(860f + 10, a.X);
        Assert.Equal(170f, a.W);
        Assert.Equal(90f, a.H);
        Assert.Equal(425f + 30, a.Y);
        Assert.Equal(a.Y + 90 + 20, b.Y);
    }

    [Fact]
    public void A_panel_given_padding_lays_its_children_side_by_side()
    {
        var s = Scene("<Panel width=\"200\" height=\"100\" padding=\"10\"><Panel id=\"a\"/><Panel id=\"b\"/></Panel>");
        Assert.Equal((870f, 500f, 90f, 80f), At(s, "a"));
        Assert.Equal((960f, 500f, 90f, 80f), At(s, "b"));
        var plain = Scene("<Panel width=\"200\" height=\"100\"><Panel id=\"a\"/><Panel id=\"b\"/></Panel>");
        Assert.Equal(At(plain, "a"), At(plain, "b"));
    }

    [Fact]
    public void A_layout_element_size_outranks_what_the_words_want()
    {
        var s = Scene("<VerticalLayout width=\"200\" height=\"300\" childForceExpandHeight=\"false\"><Text id=\"a\" fontSize=\"10\">abc</Text><Text id=\"b\" preferredHeight=\"40\">abc</Text></VerticalLayout>");
        Assert.Equal(10f, At(s, "a").H);
        Assert.Equal(40f, At(s, "b").H);
    }

    [Fact]
    public void A_grid_puts_cells_in_rows_as_wide_as_fit()
    {
        var s = Scene("<GridLayout width=\"250\" height=\"300\" cellSize=\"100 50\" spacing=\"10 5\"><Panel id=\"a\"/><Panel id=\"b\"/><Panel id=\"c\"/></GridLayout>");
        var a = At(s, "a");
        Assert.Equal((835f, 390f, 100f, 50f), a);
        Assert.Equal(a.X + 110, At(s, "b").X);
        Assert.Equal(a.Y + 55, At(s, "c").Y);
        Assert.Equal(a.X, At(s, "c").X);
    }

    [Fact]
    public void A_button_s_words_are_black_until_coloured_and_a_text_inside_takes_their_place()
    {
        var s = Scene("<Button id=\"b\" width=\"100\" height=\"30\">Go</Button><Button id=\"c\" width=\"100\" height=\"30\" textColor=\"#FF0000\">Go</Button><Button id=\"d\"><Text id=\"dt\" color=\"blue\">Own</Text></Button>");
        Assert.Equal(Paint.Black, s.ById("b")!.Part("Text Cell")!.Part("Text")!.Text!.Color);
        Assert.Equal("Go", s.ById("b")!.Part("Text Cell")!.Part("Text")!.Text!.Text);
        Assert.Equal(new Paint(1, 0, 0, 1), s.ById("c")!.Part("Text Cell")!.Part("Text")!.Text!.Color);
        Assert.Same(s.ById("dt"), s.ById("d")!.Part("Text Cell")!.Children[0]);
    }

    [Fact]
    public void Colours_read_as_the_tabletop_reads_them()
    {
        var named = PanelBuild.Of("<Defaults><Color name=\"Mine\" color=\"#102030\"/></Defaults>").Layout.Colors;
        Assert.Equal(Paint.Clear, PanelValues.Color("#FFF", named));
        Assert.Equal(Paint.Bytes(0xDA, 0x19, 0x18), PanelValues.Color("Red", named));
        Assert.Equal(Paint.Bytes(0x10, 0x20, 0x30), PanelValues.Color("mine", named));
        Assert.Equal(new Paint(0.5f, 0, 0, 0.25f), PanelValues.Color("rgba(0.5,0,0,0.25)", named));
        Assert.Equal(new Paint(0, 1, 1, 1), PanelValues.Color("Cyan", named));
        Assert.Equal(Paint.Bytes(0x71, 0x3B, 0x17), PanelValues.Color("brown", named));
        Assert.Null(PanelValues.TryColor("#GGHHII", named));
        Assert.Equal(Paint.Clear, PanelValues.Color("nonsense", named));
    }

    [Fact]
    public void Number_lists_split_on_spaces_commas_and_x_so_a_comma_and_space_read_an_empty_zero_and_padding_repeats_its_last_side()
    {
        Assert.Equal(new V2(10, 20), PanelValues.Vector2("10x20"));
        Assert.Equal(new V2(10, 0), PanelValues.Vector2("(10, 20)"));
        Assert.Equal(new V2(7, 7), PanelValues.Vector2("7"));
        Assert.Equal(new Offsets(5, 10, 10, 10), PanelValues.Padding("5 10"));
        Assert.Equal(new V3(2, 2, 2), PanelValues.Vector3("2"));
        Assert.Equal(new ColorBlock(Paint.Black, Paint.Clear, Paint.White, Paint.Clear, new Paint(1, 1, 1, 0.5f), 1, 0), PanelValues.Block("#000000", null));
    }

    [Fact]
    public void A_picture_without_its_own_colour_shows_white()
    {
        var s = Scene("<Panel id=\"p\" image=\"crest\"/><Panel id=\"q\" image=\"crest\" color=\"#00FF00\"/>");
        Assert.Equal(Paint.White, s.ById("p")!.Image!.Color);
        Assert.Equal("crest", s.ById("p")!.Image!.Sprite);
        Assert.Equal(new Paint(0, 1, 0, 1), s.ById("q")!.Image!.Color);
    }

    [Fact]
    public void An_element_with_a_visibility_shows_only_to_the_seats_it_names()
    {
        const string Xml = "<Panel id=\"p\" visibility=\"Red|host\"/>";
        Assert.True(Scene(Xml, Viewer("Red", host: false)).ById("p")!.Active);
        Assert.True(Scene(Xml, Viewer("Black", host: true)).ById("p")!.Active);
        Assert.False(Scene(Xml, Viewer("Blue", host: false)).ById("p")!.Active);
    }

    [Fact]
    public void A_progress_bar_fills_to_its_percentage_and_writes_it_with_two_places()
    {
        var s = Scene("<ProgressBar id=\"p\" percentage=\"25\"/><Slider id=\"s\" minValue=\"0\" maxValue=\"10\" value=\"5\"/>");
        var fill = s.ById("p")!.Part("Fill")!;
        Assert.Equal(0.25f, fill.AnchorMax.X);
        Assert.Equal("25.00%", s.ById("p")!.Part("PercentageText")!.Text!.Text);
        Assert.Equal(0.5f, s.ById("s")!.Part("Handle")!.AnchorMin.X);
    }

    [Fact]
    public void A_table_shares_its_width_among_cells_and_its_height_among_rows()
    {
        var s = Scene("<TableLayout width=\"300\" height=\"100\" cellSpacing=\"0\"><Row><Cell id=\"a\"/><Cell id=\"b\" columnSpan=\"2\"/></Row><Row preferredHeight=\"40\"><Cell id=\"c\"/></Row></TableLayout>");
        Assert.Equal(100f, At(s, "a").W);
        Assert.Equal(200f, At(s, "b").W);
        Assert.Equal(60f, At(s, "a").H);
        Assert.Equal(40f, At(s, "c").H);
    }

    [Fact]
    public void What_was_typed_ticked_or_picked_on_this_screen_shows_over_the_shared_value()
    {
        var layout = "<Toggle id=\"t\" isOn=\"false\"/><InputField id=\"i\" text=\"a\"/><Dropdown id=\"d\"><Option>A</Option><Option>B</Option></Dropdown>";
        var s = Scene(layout, local: new() { ["0"] = "True", ["1"] = "typed", ["2"] = "1" });
        Assert.True(s.ById("t")!.Part("Checkmark")!.Active);
        Assert.Equal("typed", s.ById("i")!.Part("Text")!.Text!.Text);
        Assert.False(s.ById("i")!.Part("Placeholder")!.Active);
        Assert.Equal("B", s.ById("d")!.Part("Label")!.Text!.Text);
        var shared = Scene(layout);
        Assert.False(shared.ById("t")!.Part("Checkmark")!.Active);
        Assert.Equal("A", shared.ById("d")!.Part("Label")!.Text!.Text);
    }

    [Fact]
    public void A_show_or_hide_plays_the_element_s_own_animation_and_a_hiding_one_stays_drawn_until_it_ends()
    {
        var layout = PanelBuild.Of("<Panel id=\"p\" hideAnimation=\"FadeOut\" showAnimation=\"Grow\" animationDuration=\"0.5\"/><Panel id=\"q\"/>").Layout;
        var hide = new KTable.Core.Protocol.Batch
        {
            Items = { new KTable.Core.Protocol.SetUiAttributes { Id = "p", Kind = PanelOps.Hide }, new KTable.Core.Protocol.SetUiAttributes { Id = "q", Kind = PanelOps.Hide } },
        };
        var move = Assert.Single(PanelMoves.Of(hide, layout, null));
        Assert.Equal(("FadeOut", false, 0.5f), (move.Animation, move.Showing, move.Duration));
        Assert.Equal("Grow", Assert.Single(PanelMoves.Of(new KTable.Core.Protocol.SetUiAttributes { Id = "p", Kind = PanelOps.Show }, layout, null)).Animation);
        layout.ById("p")!.Active = PanelOps.Off;
        var kept = PanelScene.Build(layout, Screen, Viewer(), new Meter(), null, new HashSet<string> { "0" });
        Assert.True(kept.ById("p")!.Active);
        Assert.False(PanelScene.Build(layout, Screen, Viewer(), new Meter()).ById("p")!.Active);
    }
}
