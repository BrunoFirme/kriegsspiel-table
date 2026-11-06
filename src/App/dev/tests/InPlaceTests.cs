using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace KTable.App;

static class InPlaceTests
{
    static async Task<(AcceptDialog Window, VBoxContainer Box)> Owner(TestRunner r)
    {
        var w = new AcceptDialog { Title = "Owner" };
        var box = new VBoxContainer();
        w.AddChild(box);
        r.AddChild(w);
        w.PopupCentered(new Vector2I(520, 320));
        await r.Frames(2);
        return (w, box);
    }

    static int Windows(TestRunner r) => r.GetTree().Root.FindChildren("*", "Window", true, false).Count;

    static SheetScrim? Sheet(Window w) => w.GetChildren(true).OfType<SheetScrim>().FirstOrDefault(s => !s.IsQueuedForDeletion());

    static Button ButtonIn(Node n, string text) => n.FindChildren("*", "Button", true, false).OfType<Button>().First(b => b.Text == text);

    [NodeTest]
    static async Task A_question_asked_inside_a_window_is_answered_in_that_window(TestRunner r)
    {
        var (w, box) = await Owner(r);
        int before = Windows(r);
        bool yes = false, no = false, closed = false;
        var q = Ui.Confirm(box, "Delete the save?", "Delete", () => yes = true, "It goes to the trash.");
        q.Canceled += () => no = true;
        q.Closed += () => closed = true;
        await r.Frames(1);
        Check.Equal(before, Windows(r), "windows open while the question is asked");
        Check.That(Sheet(w) != null, "the question sits in the window that asked it");
        Check.That(!w.DialogCloseOnEscape, "Escape answers the question, not the window");
        ButtonIn(Sheet(w)!, "Delete").EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(1);
        Check.That(yes && !no && closed, "Delete runs the action and closes the question");
        Check.That(Sheet(w) == null && w.DialogCloseOnEscape, "the window is as it was");
        w.QueueFree();
    }

    [NodeTest]
    static async Task Escape_cancels_a_question_and_a_third_choice_runs_its_own_action(TestRunner r)
    {
        var (w, box) = await Owner(r);
        bool yes = false, no = false, other = false, closed = false;
        var q = Ui.Confirm(box, "The file holds another script", "Use the file", () => yes = true);
        q.CancelButtonText = "Keep writing";
        q.Canceled += () => no = true;
        Check.That(ButtonIn(Sheet(w)!, "Keep writing") != null, "the cancel button takes its new name");
        Sheet(w)!._Input(new InputEventAction { Action = "ui_cancel", Pressed = true });
        await r.Frames(1);
        Check.That(no && !yes, "Escape cancels");
        var q2 = Ui.Confirm(box, "The file holds another script", "Use the file", () => yes = true);
        q2.Canceled += () => no = false;
        q2.Closed += () => closed = true;
        q2.Also("Write this one over it", () => other = true);
        ButtonIn(Sheet(w)!, "Write this one over it").EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(1);
        Check.That(other && closed && !yes && no, "the third choice runs alone and closes the question");
        w.QueueFree();
    }

    [NodeTest]
    static async Task A_question_asked_over_the_table_is_its_own_dialog(TestRunner r)
    {
        var host = new Control();
        r.AddChild(host);
        int before = Windows(r);
        bool yes = false;
        Ui.Confirm(host, "Quit?", "Quit anyway", () => yes = true);
        await r.Frames(2);
        var dlg = r.GetTree().Root.FindChildren("*", "ConfirmationDialog", true, false).OfType<ConfirmationDialog>().Last();
        Check.Equal(before + 1, Windows(r), "windows open");
        dlg.GetOkButton().EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(2);
        Check.That(yes, "OK runs the action");
        host.QueueFree();
    }

    [NodeTest]
    static async Task A_sub_page_replaces_the_page_and_back_brings_it_back(TestRunner r)
    {
        var holder = new VBoxContainer();
        var page = new VBoxContainer();
        page.AddChild(new Label { Text = "AI agent" });
        holder.AddChild(page);
        holder.AddChild(new Label { Text = "Sound" });
        r.AddChild(holder);
        var body = InPlace.SubPage(page, "Connect an AI agent", "AI agent");
        body.AddChild(new Label { Text = "Step 1" });
        await r.Frames(1);
        Check.That(!page.Visible && body.IsVisibleInTree(), "the sub-page shows in the page's place");
        Check.Equal(page.GetIndex() + 1, body.GetParent<Control>().GetIndex(), "position of the sub-page");
        var second = InPlace.SubPage(page, "Technical settings", "AI agent");
        await r.Frames(1);
        Check.That(!GodotObject.IsInstanceValid(body) || body.IsQueuedForDeletion(), "a second sub-page replaces the first");
        ButtonIn(second.GetParent(), "‹ AI agent").EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(1);
        Check.That(page.Visible && (!GodotObject.IsInstanceValid(second) || second.IsQueuedForDeletion()), "back shows the page again");
        holder.QueueFree();
    }
}
