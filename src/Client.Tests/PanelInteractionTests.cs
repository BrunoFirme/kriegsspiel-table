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
}
