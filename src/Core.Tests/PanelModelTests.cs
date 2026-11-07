using KTable.Core.Model;
using KTable.Core.Panels;

namespace KTable.Core.Tests;

public class PanelModelTests
{
    static UiLayout Built(string xml) => PanelBuild.Of(xml).Layout;

    [Fact]
    public void The_written_panels_put_each_element_on_its_own_lines_with_doubled_indents()
    {
        var data = PanelData.Parse("<Panel id=\"p\"><Text id=\"t\">Hi</Text><Image id=\"i\"/></Panel><Text>Two</Text>")!;
        Assert.Equal("<Panel id=\"p\">\r\n    <Text id=\"t\">Hi</Text>\r\n    <Image id=\"i\" />\r\n</Panel>\n<Text>Two</Text>\n", PanelData.Write(data));
    }

    [Fact]
    public void A_value_is_the_first_text_only_unless_the_element_holds_rich_text_whose_lone_spaces_between_tags_are_lost()
    {
        var data = PanelData.Parse("<Button id=\"a\">  Go  <Image/>later</Button><Text id=\"b\">Hello <b>bold</b> <i>x</i></Text><Panel id=\"c\"><Text/></Panel>")!;
        Assert.Equal("  Go  ", PanelData.ById(data, "a")!.Value);
        Assert.Single(PanelData.ById(data, "a")!.Children);
        Assert.Equal("Hello <b>bold</b><i>x</i>", PanelData.ById(data, "b")!.Value);
        Assert.Empty(PanelData.ById(data, "b")!.Children);
        Assert.Null(PanelData.ById(data, "c")!.Value);
        Assert.Equal("<Text id=\"b\">Hello <b>bold</b><i>x</i></Text>\n", PanelData.Write(new List<UiNode> { PanelData.ById(data, "b")! }));
    }

    [Fact]
    public void Attribute_names_match_in_any_case_but_data_ids_match_exactly()
    {
        var data = PanelData.Parse("<Panel ID=\"Main\" Width=\"40\"/>")!;
        Assert.Equal("40", data[0].Attributes["width"]);
        Assert.NotNull(PanelData.ById(data, "Main"));
        Assert.Null(PanelData.ById(data, "main"));
        Assert.NotNull(Built("<Panel id=\"Main\"/>").ById("MAIN"));
    }

    [Fact]
    public void Panels_that_do_not_read_as_xml_give_no_data_and_a_broken_layout()
    {
        Assert.Null(PanelData.Parse("<Panel>"));
        Assert.NotNull(Built("<Panel>").Broken);
        Assert.True(PanelData.IsNothing(PanelData.Blank));
        Assert.True(PanelData.IsNothing(""));
        Assert.False(PanelData.IsNothing("<Panel/>"));
    }

    [Fact]
    public void Content_becomes_the_live_text_trimmed_but_a_text_element_reads_its_inside_as_its_words()
    {
        var l = Built("<Button id=\"b\">  Go   now  </Button><Text id=\"t\">  Hello   <b>you</b> <br/>there </Text>");
        Assert.Equal("Go now", l.ById("b")!.Get("text"));
        Assert.Null(l.ById("t")!.Get("text"));
        Assert.Equal("Hello <b>you</b>\nthere", l.ById("t")!.Inner);
    }

    [Fact]
    public void An_element_that_follows_words_inside_its_parent_is_passed_over_as_the_game_does()
    {
        var l = Built("<Panel id=\"p\">Words<Image id=\"skipped\"/><Image id=\"kept\"/></Panel>");
        Assert.Null(l.ById("skipped"));
        Assert.NotNull(l.ById("kept"));
        Assert.Equal("Words", l.ById("p")!.Get("text"));
    }

    [Fact]
    public void An_unknown_tag_is_left_out_with_what_it_holds_and_named_in_the_chat()
    {
        var (l, said) = PanelBuild.Of("<Panel id=\"p\"><Fancy id=\"f\"><Text id=\"t\"/></Fancy></Panel><text id=\"lower\"/>");
        Assert.Null(l.ById("f"));
        Assert.Null(l.ById("t"));
        Assert.Equal("Text", l.ById("lower")!.Tag);
        Assert.Contains(said, s => s.StartsWith("[XmlLayout] Unknown tag 'Fancy'."));
    }

    [Fact]
    public void Defaults_reach_only_the_elements_that_close_after_them()
    {
        var l = Built("<Text id=\"before\"/><Defaults><Text color=\"red\" fontSize=\"30\"/></Defaults><Text id=\"after\" fontSize=\"12\"/>");
        Assert.Null(l.ById("before")!.Get("color"));
        Assert.Equal("red", l.ById("after")!.Get("color"));
        Assert.Equal("12", l.ById("after")!.Get("fontSize"));
        Assert.Equal(new[] { "id", "fontSize" }, l.ById("after")!.Own);
    }

    [Fact]
    public void The_tabletop_s_own_defaults_give_every_button_its_click_sound_and_name_its_colours()
    {
        var l = Built("<Button id=\"b\">Go</Button><Toggle id=\"t\"/>");
        Assert.Equal("Defaults/Sounds/ClickUI", l.ById("b")!.Get("onClickSound"));
        Assert.Equal("0.3", l.ById("t")!.Get("audioVolume"));
        Assert.Equal("#DA1918", l.Colors["Red"]);
        Assert.Equal("#713B17\\", l.Colors["brown"]);
    }

    [Fact]
    public void Class_defaults_follow_the_parent_and_ancestor_selectors()
    {
        var l = Built("<Defaults><Text class=\"@Panel>big\" fontSize=\"40\"/><Text class=\"outer:deep\" color=\"blue\"/><Text class=\"Plain\" fontStyle=\"Bold\"/></Defaults>"
            + "<Panel><Text id=\"a\" class=\"big\"/></Panel><VerticalLayout><Text id=\"b\" class=\"big\"/></VerticalLayout>"
            + "<Panel class=\"outer\"><Panel><Text id=\"c\" class=\"deep plain\"/></Panel></Panel>");
        Assert.Equal("40", l.ById("a")!.Get("fontSize"));
        Assert.Null(l.ById("b")!.Get("fontSize"));
        Assert.Equal("blue", l.ById("c")!.Get("color"));
        Assert.Equal("Bold", l.ById("c")!.Get("fontStyle"));
        Assert.Equal(new[] { "deep", "plain" }, l.ById("c")!.Classes);
    }

    [Fact]
    public void A_class_rule_changed_after_an_element_used_it_stays_as_it_was_for_the_rest_of_that_build()
    {
        var l = Built("<Defaults><Text class=\"x\" color=\"red\"/></Defaults><Text id=\"a\" class=\"x\"/><Defaults><Text class=\"x\" color=\"blue\"/></Defaults><Text id=\"b\" class=\"x\"/>");
        Assert.Equal("red", l.ById("a")!.Get("color"));
        Assert.Equal("red", l.ById("b")!.Get("color"));
    }

    [Fact]
    public void Of_two_elements_with_one_id_the_one_that_closes_later_answers_and_the_chat_names_it()
    {
        var (l, said) = PanelBuild.Of("<Panel id=\"a\" width=\"1\"><Text id=\"a\"/></Panel>");
        Assert.Equal("Panel", l.ById("a")!.Tag);
        Assert.Contains("[XmlLayout] Ignoring duplicate id value 'a. Id values must be unique.", said);
    }

    [Fact]
    public void A_button_s_words_go_onto_its_first_child_and_a_toggle_s_child_is_laid_out_as_a_label()
    {
        var l = Built("<Button id=\"b\" text=\"Go\"><Text id=\"bt\"/></Button><Toggle id=\"t\"><Text id=\"tt\">Lock</Text></Toggle><Toggle id=\"u\"><Text id=\"ut\"/></Toggle>");
        Assert.Equal("Go", l.ById("bt")!.Get("text"));
        Assert.Contains("text", l.ById("bt")!.Own);
        Assert.Equal("MiddleLeft", l.ById("tt")!.Get("alignment"));
        Assert.Equal("23 5 2 1", l.ById("tt")!.Get("padding"));
        Assert.Equal("1", l.ById("tt")!.Get("flexibleWidth"));
        Assert.Equal("false", l.ById("ut")!.Get("active"));
    }

    [Fact]
    public void A_panel_with_a_picture_takes_clicks()
    {
        var l = Built("<Panel id=\"p\" image=\"crest\"/><Panel id=\"q\"/>");
        Assert.Equal("true", l.ById("p")!.Get("raycastTarget"));
        Assert.Null(l.ById("q")!.Get("raycastTarget"));
    }

    [Fact]
    public void Setting_the_class_attribute_changes_the_written_panels_only_and_warns()
    {
        var data = PanelData.Parse("<Panel id=\"p\" class=\"a\"/>")!;
        var l = Built("<Panel id=\"p\" class=\"a\"/>");
        var said = PanelOps.Run(data, l, PanelOps.SetAttribute, "p", new Dictionary<string, string> { ["class"] = "b" }, null);
        Assert.Equal("b", data[0].Attributes["class"]);
        Assert.Equal("a", l.ById("p")!.Get("class"));
        Assert.Contains(PanelOps.ClassWarning, said);
    }

    [Fact]
    public void Setting_an_id_answers_to_the_new_id_and_no_longer_to_the_old()
    {
        var data = PanelData.Parse("<Panel id=\"p\"/>")!;
        var l = Built("<Panel id=\"p\"/>");
        PanelOps.Run(data, l, PanelOps.SetAttribute, "p", new Dictionary<string, string> { ["id"] = "q" }, null);
        Assert.NotNull(l.ById("q"));
        Assert.Null(l.ById("p"));
        Assert.Equal("q", data[0].Id);
    }

    [Fact]
    public void A_value_shows_on_a_text_element_only_and_leaves_its_text_attribute()
    {
        const string Xml = "<Text id=\"t\" text=\"old\"/><Button id=\"b\">old</Button>";
        var data = PanelData.Parse(Xml)!;
        var l = Built(Xml);
        PanelOps.Run(data, l, PanelOps.SetValue, "t", new Dictionary<string, string>(), "  a &amp; b<br/>c ");
        PanelOps.Run(data, l, PanelOps.SetValue, "b", new Dictionary<string, string>(), "new");
        Assert.Equal("a & b\nc", l.ById("t")!.Shown);
        Assert.Equal("old", l.ById("t")!.Get("text"));
        Assert.Equal("  a &amp; b<br/>c ", PanelData.ById(data, "t")!.Value);
        Assert.Null(l.ById("b")!.Shown);
        Assert.Equal("new", PanelData.ById(data, "b")!.Value);
    }

    [Fact]
    public void Showing_inside_a_hidden_parent_turns_the_element_on_but_leaves_its_attribute_and_hiding_there_does_nothing()
    {
        const string Xml = "<Panel id=\"p\" active=\"false\"><Text id=\"t\" active=\"false\"/><Text id=\"u\"/></Panel>";
        var data = PanelData.Parse(Xml)!;
        var l = Built(Xml);
        PanelOps.Run(data, l, PanelOps.Show, "t", new Dictionary<string, string>(), null);
        PanelOps.Run(data, l, PanelOps.Hide, "u", new Dictionary<string, string>(), null);
        Assert.Equal(PanelOps.On, l.ById("t")!.Active);
        Assert.Equal("false", l.ById("t")!.Get("active"));
        Assert.Null(l.ById("u")!.Active);
        Assert.Equal("true", PanelData.ById(data, "t")!.Attributes["active"]);
        Assert.Equal("false", PanelData.ById(data, "u")!.Attributes["active"]);
        PanelOps.Run(data, l, PanelOps.Show, "p", new Dictionary<string, string>(), null);
        PanelOps.Run(data, l, PanelOps.Hide, "u", new Dictionary<string, string>(), null);
        Assert.Equal(PanelOps.Off, l.ById("u")!.Active);
        Assert.Equal("false", l.ById("u")!.Get("active"));
    }

    [Fact]
    public void A_new_class_brings_its_defaults_and_a_class_taken_away_gives_back_the_element_s_first_value_as_the_engine_writes_it()
    {
        const string Xml = "<Defaults><Text class=\"warn\" color=\"red\" fontSize=\"20\"/></Defaults><Text id=\"t\" fontSize=\"12\"/>";
        var data = PanelData.Parse(Xml)!;
        var l = Built(Xml);
        PanelOps.Run(data, l, PanelOps.SetClass, "t", new Dictionary<string, string>(), "warn");
        Assert.Equal("red", l.ById("t")!.Get("color"));
        Assert.Equal("12", l.ById("t")!.Get("fontSize"));
        PanelOps.Run(data, l, PanelOps.SetClass, "t", new Dictionary<string, string>(), "other");
        Assert.Equal("RGBA(0.196, 0.196, 0.196, 1.000)", l.ById("t")!.Get("color"));
        Assert.Equal("other", PanelData.ById(data, "t")!.Attributes["class"]);
    }

    [Fact]
    public void Language_codes_pick_the_english_words_else_the_first_language_in_the_game_s_own_list_and_escapes_become_letters()
    {
        Assert.Equal("Hello", PanelText.Localize("{fr}Bonjour{en}Hello"));
        Assert.Equal("Bonjour", PanelText.Localize("{fr}Bonjour"));
        Assert.Equal("Hallo", PanelText.Localize("{pt-BR}Olá{de}Hallo"));
        Assert.Equal("Olá", PanelText.Localize("{pt-BR}Olá{xx}No"));
        Assert.Equal("no codes {here}", PanelText.Localize("no codes {here}"));
        Assert.Equal("é", PanelText.Decode("\\u00e9"));
    }

    [Fact]
    public void A_panel_tree_that_round_trips_through_json_keeps_letter_case_free_attributes()
    {
        var l = Built("<Panel id=\"p\" Width=\"5\"/>");
        var back = Json.Clone(l);
        Assert.Equal("5", back.ById("P")!.Get("width"));
        var n = Json.Clone(PanelData.Parse("<Panel Width=\"5\">v</Panel>")![0]);
        Assert.Equal("5", n.Attributes["WIDTH"]);
        Assert.Equal("v", n.Value);
    }
}
