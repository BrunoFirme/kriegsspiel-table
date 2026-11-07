using KTable.Client.Panels;
using KTable.Core.Panels;

namespace KTable.Client.Tests;

public class PanelInteractionTests
{
    sealed class Meter : IPanelMeter
    {
        public V2 Measure(TextProps text, string words, float wrapWidth) => new(words.Length * text.Size * 0.5f, text.Size);
        public V2? PictureSize(string sprite) => null;
    }

    static readonly V2 Screen = new(1920, 1080);

    static PanelScene Scene(string xml, Dictionary<string, string>? local = null, UiLayout? layout = null)
    {
        layout ??= PanelBuild.Of(xml).Layout;
        return PanelScene.Build(layout, Screen, new PanelContext(layout.Colors, "Black", true, true), new Meter(), local);
    }

    const string TallList = "<VerticalScrollView id=\"v\" width=\"200\" height=\"100\"{0}><VerticalLayout height=\"400\"><Text>a</Text></VerticalLayout></VerticalScrollView>";

    static PanelBox View(string extra = "", Dictionary<string, string>? local = null) => Scene(string.Format(TallList, extra), local).ById("v")!;

    [Fact]
    public void A_wheel_notch_scrolls_one_unit_times_the_scroll_sensitivity_and_stops_at_the_ends()
    {
        var plain = new PanelScrollMotion(View(), V2.Zero);
        plain.Wheel(new V2(0, -1));
        Assert.Equal(1, plain.Offset.Y, 3);
        plain.Wheel(new V2(0, 1));
        plain.Wheel(new V2(0, 1));
        Assert.Equal(0, plain.Offset.Y, 3);
        var quick = new PanelScrollMotion(View(" scrollSensitivity=\"30\""), V2.Zero);
        for (int i = 0; i < 20; i++) quick.Wheel(new V2(0, -1));
        Assert.Equal(PanelScrolls.Measure(View()).Room.Y, quick.Offset.Y, 3);
    }

    [Fact]
    public void A_flung_list_keeps_gliding_after_release_and_slows_to_a_stop()
    {
        var m = new PanelScrollMotion(View(), V2.Zero);
        m.BeginDrag(V2.Zero);
        for (int i = 1; i <= 5; i++)
        {
            m.Drag(new V2(0, 10 * i));
            m.Step(0.016f);
        }
        m.EndDrag();
        var released = m.Offset.Y;
        m.Step(0.016f);
        Assert.True(m.Offset.Y > released, "it glides on after the release");
        for (int i = 0; i < 600 && m.Moving; i++) m.Step(0.016f);
        Assert.False(m.Moving);
    }

    [Fact]
    public void An_elastic_list_stretches_past_its_end_while_dragged_and_springs_back_after()
    {
        var m = new PanelScrollMotion(View(" movementType=\"Elastic\""), V2.Zero);
        m.BeginDrag(V2.Zero);
        m.Drag(new V2(0, -50));
        Assert.InRange(m.Offset.Y, -49.9f, -0.1f);
        m.EndDrag();
        for (int i = 0; i < 200 && m.Moving; i++) m.Step(0.016f);
        Assert.Equal(0, m.Offset.Y, 1);
    }

    [Fact]
    public void A_clamped_list_never_drags_past_its_ends()
    {
        var m = new PanelScrollMotion(View(), V2.Zero);
        m.BeginDrag(V2.Zero);
        m.Drag(new V2(0, -50));
        Assert.Equal(0, m.Offset.Y, 3);
    }

    [Fact]
    public void Dragging_a_scrollbar_handle_to_the_bottom_shows_the_end_of_the_content()
    {
        var local = new Dictionary<string, string>();
        var v = View("", local);
        var bar = v.Part("Scrollbar Vertical")!;
        var area = bar.Part("Sliding Area")!;
        var bottom = new V2(0, area.RectMin.Y - 1000);
        float value = PanelScrollMotion.BarValue(bar, 1, bottom, V2.Zero);
        Assert.Equal(0, value, 3);
        var to = PanelScrollMotion.FromBar(v, 1, value, V2.Zero);
        Assert.Equal(PanelScrolls.Measure(v).Room.Y, to.Y, 3);
        Assert.Equal(to, PanelScrolls.Move(v, to));
    }

    const string Choices = "<Dropdown id=\"d\" width=\"160\" height=\"30\" itemTextColor=\"#FF0000\"{0}><Option>One</Option><Option selected=\"true\">Two</Option><Option>Three</Option></Dropdown>";

    [Fact]
    public void A_dropdown_opens_its_list_just_under_itself_one_thirty_high_row_per_option_with_the_chosen_one_ticked()
    {
        var s = Scene(string.Format(Choices, ""));
        var d = s.ById("d")!;
        var list = PanelLists.Open(s, d, 1);
        var (x, y, w, h) = s.Screen(list);
        var (dx, dy, dw, dh) = s.Screen(d);
        Assert.Equal(dx, x, 2);
        Assert.Equal(dw, w, 2);
        Assert.Equal(dy + dh - 2, y, 2);
        Assert.Equal(3 * 30 - 2, h, 2);
        var rows = list.Part("Content")!.Children;
        Assert.Equal(3, rows.Count);
        Assert.All(rows, r => Assert.Equal(30, r.RectSize.Y, 2));
        Assert.Equal(new[] { 0f, 1f, 0f }, rows.Select(r => r.Part("Item Checkmark")!.Opacity));
        Assert.Equal("Two", rows[1].Part("Item Label")!.Text!.Text);
        Assert.Equal(new Paint(1, 0, 0, 1), rows[1].Part("Item Label")!.Text!.Color);
        Assert.False(list.Part("Scrollbar Vertical")!.Active);
    }

    [Fact]
    public void A_dropdown_near_the_bottom_of_the_screen_opens_its_list_upwards()
    {
        var s = Scene(string.Format(Choices, " rectAlignment=\"LowerCenter\""));
        var d = s.ById("d")!;
        var list = PanelLists.Open(s, d, 0);
        var (_, y, _, h) = s.Screen(list);
        var (_, dy, _, _) = s.Screen(d);
        Assert.Equal(dy + 2, y + h, 2);
    }

    [Fact]
    public void A_long_dropdown_list_stops_at_its_dropdown_height_and_shows_a_scrollbar()
    {
        var options = string.Concat(Enumerable.Range(0, 10).Select(i => $"<Option>o{i}</Option>"));
        var s = Scene($"<Dropdown id=\"d\" width=\"160\" height=\"30\">{options}</Dropdown>");
        var list = PanelLists.Open(s, s.ById("d")!, 0);
        Assert.Equal(150, list.RectSize.Y, 2);
        Assert.True(list.Part("Scrollbar Vertical")!.Active);
    }

    [Fact]
    public void A_pin_field_takes_only_digits_on_one_line_and_shows_stars()
    {
        var s = Scene("<InputField id=\"i\" contentType=\"Pin\" text=\"1234\"/>");
        var i = s.ById("i")!;
        var rules = PanelInputs.Of(i);
        Assert.Equal("Integer", rules.Validation);
        Assert.True(rules.OneLine);
        Assert.True(rules.Secret);
        Assert.Equal("****", i.Part("Text")!.Text!.Text);
    }

    [Fact]
    public void A_hover_class_brings_its_defaults_while_the_pointer_is_over_and_they_go_when_it_leaves()
    {
        const string Xml = "<Defaults><Image class=\"lit\" color=\"#FF0000\"/></Defaults><Image id=\"i\" width=\"10\" height=\"10\" hoverClass=\"lit\"/>";
        var layout = PanelBuild.Of(Xml).Layout;
        var local = new Dictionary<string, string>();
        var box = Scene(Xml, local, layout).ById("i")!;
        Assert.True(PanelClasses.Change(local, box, PanelClasses.Hover, true));
        var lit = Scene(Xml, local, PanelClasses.Apply(layout, local)).ById("i")!;
        Assert.Equal(new Paint(1, 0, 0, 1), lit.Image!.Color);
        Assert.True(PanelClasses.Change(local, box, PanelClasses.Hover, false));
        var plain = Scene(Xml, local, PanelClasses.Apply(layout, local)).ById("i")!;
        Assert.Equal(Paint.White, plain.Image!.Color);
        Assert.DoesNotContain(local.Keys, k => k.StartsWith(PanelClasses.Prefix));
        Assert.Empty(layout.ById("i")!.Classes);
    }

    const string Folds = "<Accordion id=\"a\" width=\"300\" height=\"400\"><AccordionSection id=\"s1\" expanded=\"true\"><AccordionSectionHeader id=\"h1\"><AccordionSectionHeaderIcon id=\"i1\"/></AccordionSectionHeader>"
        + "<AccordionSectionContent id=\"c1\"><Text>One</Text></AccordionSectionContent></AccordionSection><AccordionSection id=\"s2\" contentHeight=\"50\"><AccordionSectionHeader id=\"h2\"/>"
        + "<AccordionSectionContent id=\"c2\"><Button id=\"b2\">Go</Button></AccordionSectionContent></AccordionSection></Accordion>";

    [Fact]
    public void An_accordion_shows_a_grey_sixty_four_high_header_per_section_and_opens_only_the_expanded_ones()
    {
        var s = Scene(Folds);
        Assert.Equal(64 + 128, s.Screen(s.ById("s1")!).H, 2);
        Assert.Equal(64, s.Screen(s.ById("s2")!).H, 2);
        Assert.Equal(128, s.Screen(s.ById("c1")!).H, 2);
        Assert.Equal(0, s.Screen(s.ById("c2")!).H, 2);
        Assert.Equal(new Paint(0.5f, 0.5f, 0.5f, 1), s.ById("h1")!.Image!.Color);
        Assert.Equal(PanelAccordions.Expanded, s.ById("i1")!.Image!.Sprite);
        Assert.False(s.ById("b2")!.Select!.Interactable);
        var layout = PanelBuild.Of(Folds).Layout;
        Assert.Contains("expanded", layout.ById("s1")!.Classes);
        Assert.Contains("collapsed", layout.ById("s2")!.Classes);
    }

    [Fact]
    public void A_section_half_way_through_opening_shows_half_its_content_height()
    {
        var local = new Dictionary<string, string>();
        var first = Scene(Folds, local).ById("s2")!;
        local[PanelAccordions.Key(first)] = "True";
        local[PanelAccordions.Opening + PanelAccordions.Key(first)] = "0.5";
        var s = Scene(Folds, local);
        Assert.Equal(64 + 25, s.Screen(s.ById("s2")!).H, 2);
        Assert.True(s.ById("b2")!.Select!.Interactable);
    }

    [Fact]
    public void A_data_table_takes_ninety_six_units_and_draws_nothing_because_nothing_in_the_tabletop_ever_gives_it_rows()
    {
        var s = Scene("<VerticalLayout width=\"300\" height=\"300\" childForceExpandHeight=\"false\"><DataTable id=\"d\"/></VerticalLayout>");
        var d = s.ById("d")!;
        Assert.Equal(96, s.Screen(d).H, 2);
        Assert.Equal(0, d.Image!.Color.A);
        Assert.False(d.Image.RaycastTarget);
    }

    [Fact]
    public void Text_mesh_pro_text_draws_no_letters_since_the_tabletop_ships_no_text_mesh_pro_font_but_its_input_field_keeps_its_white_box()
    {
        var s = Scene("<TextMeshPro id=\"t\">Hello</TextMeshPro><TextMeshProInputField id=\"i\"/>");
        var t = s.ById("t")!;
        Assert.Null(t.Text);
        Assert.Equal(100, s.Screen(t).W, 2);
        var i = s.ById("i")!;
        Assert.Equal(Paint.White, i.Image!.Color);
        Assert.Equal(PanelPrefabs.ToolSprite, i.Image.Sprite);
    }

    [Fact]
    public void A_slider_value_is_written_with_seven_significant_digits_as_the_tabletop_s_runtime_writes_a_float()
    {
        Assert.Equal("0.3333333", PanelValues.FloatText(1f / 3));
        Assert.Equal("7.5", PanelValues.FloatText(7.5f));
        Assert.Equal("1E+07", PanelValues.FloatText(1e7f));
    }

    [Fact]
    public void A_slider_s_handle_background_and_fill_area_take_the_sizes_padding_and_mask_it_names()
    {
        var s = Scene("<Slider id=\"s\" width=\"200\" height=\"20\" handleWidth=\"30\" sliderBackgroundHeight=\"6\" fillPadding=\"5 7 2 3\" fillMaskImage=\"crest\"/>");
        var b = s.ById("s")!;
        Assert.Equal(30, b.Part("Handle")!.RectSize.X, 2);
        Assert.Equal(6, b.Part("Background")!.RectSize.Y, 2);
        var area = b.Part("Fill Area")!;
        Assert.Equal(200 - 5 - 7, area.RectSize.X, 2);
        Assert.Equal(10 - 2 - 3, area.RectSize.Y, 2);
        Assert.True(area.Mask);
        Assert.False(area.ShowMaskGraphic);
        Assert.Equal("crest", area.Image!.Sprite);
    }

    [Fact]
    public void Set_native_size_gives_a_picture_its_own_pixel_size_and_an_aspect_fitter_keeps_its_ratio()
    {
        var s = Scene("<Image id=\"n\" image=\"Sprites/Elements/Checkmark\" setNativeSize=\"true\"/>"
            + "<Panel id=\"w\" width=\"200\" height=\"10\" aspectRatio=\"2\" aspectMode=\"WidthControlsHeight\"/>"
            + "<Panel width=\"400\" height=\"100\"><Panel id=\"f\" aspectRatio=\"1:1\" aspectMode=\"FitInParent\"/></Panel>");
        Assert.Equal((40f, 40f), (s.Screen(s.ById("n")!).W, s.Screen(s.ById("n")!).H));
        Assert.Equal(100, s.Screen(s.ById("w")!).H, 2);
        Assert.Equal((100f, 100f), (s.Screen(s.ById("f")!).W, s.Screen(s.ById("f")!).H));
    }

    [Fact]
    public void A_scroll_view_s_mask_image_shapes_its_viewport()
    {
        var s = Scene("<VerticalScrollView id=\"v\" maskImage=\"crest\"><Panel height=\"100\"/></VerticalScrollView>");
        var port = s.ById("v")!.Part("Viewport")!;
        Assert.True(port.Mask);
        Assert.Equal("crest", port.Image!.Sprite);
    }

    [Fact]
    public void A_text_mesh_pro_dropdown_opens_its_own_template_twenty_high_rows_with_a_black_tick_and_no_words()
    {
        var s = Scene("<TextMeshProDropdown id=\"d\"><TMP_Option text=\"One\"/><TMP_Option text=\"Two\" selected=\"true\"/><TMP_Option text=\"Three\"/></TextMeshProDropdown>");
        var d = s.ById("d")!;
        Assert.Equal(new[] { "One", "Two", "Three" }, d.Element!.Options!.Select(o => o.Value));
        Assert.Equal(1, PanelScene.DropdownIndex(d, d.Element, null));
        var list = PanelLists.Open(s, d, 1);
        var rows = list.Part("Content")!.Children;
        Assert.Equal(3 * 20 + 8, list.RectSize.Y, 2);
        Assert.All(rows, r => Assert.Equal(20, r.RectSize.Y, 2));
        Assert.All(rows, r => Assert.Null(r.Part("Item Label")));
        Assert.Equal(Paint.Black, rows[1].Part("Item Checkmark")!.Image!.Color);
        Assert.Equal(1, rows[1].Part("Item Checkmark")!.Opacity);
    }

    [Fact]
    public void A_dropdown_s_item_height_sets_its_rows_and_its_content_together()
    {
        var s = Scene(string.Format(Choices, " itemHeight=\"40\""));
        var list = PanelLists.Open(s, s.ById("d")!, 0);
        Assert.Equal(3 * 40, list.RectSize.Y, 2);
    }

    const string Stack = "<Accordion id=\"a\" width=\"300\" height=\"400\" allowSectionReordering=\"true\">"
        + "<AccordionSection id=\"s1\"><AccordionSectionHeader/><AccordionSectionContent/></AccordionSection>"
        + "<AccordionSection id=\"s2\"><AccordionSectionHeader/><AccordionSectionContent/></AccordionSection>"
        + "<AccordionSection id=\"s3\"><AccordionSectionHeader/><AccordionSectionContent/></AccordionSection></Accordion>";

    [Fact]
    public void A_reorderable_accordion_lets_its_sections_drag_and_places_the_gap_by_the_nearest_section_s_middle()
    {
        var s = Scene(Stack);
        var sections = new[] { s.ById("s1")!, s.ById("s2")!, s.ById("s3")! };
        Assert.All(sections, x => Assert.Equal("true", x.Extra["allowdragging"]));
        Assert.Equal(2, PanelAccordions.Slot(sections, sections[0], sections[2].PivotAt.Y - 1));
        Assert.Equal(1, PanelAccordions.Slot(sections, sections[0], sections[2].PivotAt.Y + 1));
        Assert.Equal(0, PanelAccordions.Slot(sections, sections[2], sections[0].PivotAt.Y + 1));
    }

    [Fact]
    public void A_section_dropped_after_the_others_lands_one_place_short_as_the_game_counts_it_and_the_order_holds_after_a_redraw()
    {
        var local = new Dictionary<string, string>();
        var s = Scene(Stack, local);
        var sections = new[] { s.ById("s1")!, s.ById("s2")!, s.ById("s3")! };
        var order = PanelAccordions.Dropped(sections, sections[0], 2);
        Assert.Equal(new[] { "s2", "s1", "s3" }, order.Select(x => x.Element!.Get("id")));
        Assert.Equal(new[] { "s3", "s1", "s2" }, PanelAccordions.Dropped(sections, sections[2], 0).Select(x => x.Element!.Get("id")));
        local[PanelAccordions.Order + PanelScene.Key(s.ById("a")!.Path!)] = string.Join(" ", order.Select(x => PanelScene.Key(x.Path!)));
        var again = Scene(Stack, local);
        Assert.Equal(new[] { "s2", "s1", "s3" }, again.ById("a")!.Children.Where(c => c.Tag == PanelAccordions.Section).Select(c => c.Element!.Get("id")));
        Assert.True(again.Screen(again.ById("s2")!).Y < again.Screen(again.ById("s1")!).Y);
    }

    [Fact]
    public void A_button_sized_by_a_layout_group_lays_its_label_out_at_the_button_s_full_height()
    {
        var s = Scene("<Panel width=\"400\" height=\"400\"><VerticalLayout childForceExpandHeight=\"false\"><HorizontalLayout preferredHeight=\"36\"><Button id=\"b1\">Send</Button>"
            + "<Button>Clear</Button></HorizontalLayout><Button id=\"solo\" preferredHeight=\"36\">Solo</Button></VerticalLayout></Panel>");
        foreach (var id in new[] { "b1", "solo" })
        {
            var button = s.ById(id)!;
            var label = button.Part("Text Cell")!.Part("Text")!;
            Assert.True(button.RectSize.Y >= 36);
            Assert.Equal(button.RectSize.Y, label.RectSize.Y, 2);
        }
    }
}
