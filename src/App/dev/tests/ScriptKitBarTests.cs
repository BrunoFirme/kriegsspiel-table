using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Server.Scripting;

namespace KTable.App;

static class ScriptKitBarTests
{
    const string Own = "function on_load()\n  self.menu_add(\"Hit\", \"hit\")\nend\n";

    static readonly KitKind Block = new(2, 1, ["Strength"], ["Fresh", "Shaken"], ["Orders"]);
    static readonly KitKind Bare = new(1, 1, [], [], []);

    [NodeTest]
    static async Task An_example_from_insert_goes_in_beside_the_script_and_one_undo_takes_it_out(TestRunner r)
    {
        var edit = new CodeEdit { Text = Own, CustomMinimumSize = new Vector2(400, 200) };
        string said = "x";
        var bar = new ScriptKitBar(edit, () => Block, s => said = s, Ui.PanelLabels);
        var box = new VBoxContainer();
        box.AddChild(edit);
        var tools = new HBoxContainer();
        bar.AddTo(tools);
        box.AddChild(tools);
        box.AddChild(bar.Form);
        r.AddChild(box);
        await r.Frames(1);
        bar.Insert.EmitSignal(MenuButton.SignalName.AboutToPopup);
        var subs = bar.Insert.GetPopup().GetChildren().OfType<PopupMenu>().ToList();
        Check.Equal(ScriptKit.Examples.Select(e => e.Group).Distinct().Count(), subs.Count, "one submenu a group");
        var example = ScriptKit.Examples.First(e => e.Name == "A number with − and +");
        bar.Put(ScriptKit.Fit(example, Block, edit.Text));
        Check.That(edit.Text.Contains("self.menu_add(\"Hit\", \"hit\")") && edit.Text.Contains("kit_draw()"), "the example goes in beside the script's own lines");
        Check.Equal("", said, "nothing to say when it goes in");
        edit.Undo();
        Check.Equal(Own, edit.Text, "one undo takes the whole example out");
        bar.Put(ScriptKit.Fit(ScriptKit.Examples.First(e => e.Name == "A tick box"), Block, ""));
        edit.Text = "function on_save() return '' end\n";
        bar.Put(ScriptKit.Fit(ScriptKit.Examples.First(e => e.Name == "A tick box"), Block, edit.Text));
        Check.That(said.Length > 0, "a script with its own on_save says why the example stays out");
        box.QueueFree();
    }

    [NodeTest]
    static async Task Examples_the_kind_cannot_run_are_greyed_in_the_menu(TestRunner r)
    {
        var edit = new CodeEdit();
        var bar = new ScriptKitBar(edit, () => Bare, _ => { }, Ui.PanelLabels);
        var tools = new HBoxContainer();
        bar.AddTo(tools);
        r.AddChild(tools);
        await r.Frames(1);
        bar.Insert.EmitSignal(MenuButton.SignalName.AboutToPopup);
        var subs = bar.Insert.GetPopup().GetChildren().OfType<PopupMenu>().ToList();
        int greyed = subs.Sum(s => Enumerable.Range(0, s.ItemCount).Count(s.IsItemDisabled));
        Check.Equal(ScriptKit.Examples.Count(e => e.Missing(Bare) != null), greyed, "each example the kind lacks a counter or states for is greyed");
        tools.QueueFree();
    }

    [NodeTest]
    static async Task The_helper_shows_only_the_rows_its_choice_needs_in_one_label_column_and_adds_working_code(TestRunner r)
    {
        var edit = new CodeEdit { CustomMinimumSize = new Vector2(400, 200) };
        var bar = new ScriptKitBar(edit, () => Block, _ => { }, Ui.PanelLabels);
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(420, 0) };
        box.AddChild(edit);
        var tools = new HBoxContainer();
        bar.AddTo(tools);
        box.AddChild(tools);
        box.AddChild(bar.Form);
        r.AddChild(box);
        bar.Helper.ButtonPressed = true;
        await r.Frames(2);
        Check.That(bar.Form.Visible, "Helper opens the form");
        var problems = UiRuleTests.Problems(bar.Form, "script helper").ToList();
        Check.That(problems.Count == 0, string.Join("\n", problems));
        var shown = bar.Form.GetChildren().OfType<GridContainer>().Single().GetChildren().OfType<Label>().Where(l => l.Visible).Select(l => l.Text).ToList();
        Check.That(shown.Contains("Label") && shown.Contains("Place") && shown.Contains("Text") && !shown.Contains("Counter"), "a button that says a line asks for its label, place and text");
        var add = bar.Form.GetChildren().OfType<MarginContainer>().Single().GetChild<Button>(0);
        add.EmitSignal(BaseButton.SignalName.Pressed);
        Check.That(edit.Text.Contains("on_click") && edit.Text.Contains("game.say"), "Add puts the button and its handler in the script");
        bar.Helper.ButtonPressed = false;
        Check.That(!bar.Form.Visible, "Helper again closes it");
        box.QueueFree();
    }
}
