using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class ChatLogTests
{
    static async Task<(HudTests.Rig Rig, LogPanel Log)> Opened(TestRunner r)
    {
        var h = await HudTests.Host(r);
        h.Hud.OpenPanel("log");
        await r.Frames(2);
        return (h, h.Hud.LogPanel);
    }

    static string Shown(LogPanel log) => log.Text.GetParsedText();

    static async Task Write(TestRunner r, LogPanel log, string text)
    {
        var field = log.Panel.FindChildren("*", nameof(LineEdit), true, false).OfType<LineEdit>().First(f => f.MaxLength == Say.MostLength);
        field.Text = text;
        field.EmitSignal(LineEdit.SignalName.TextSubmitted, text);
        await r.Frames(2);
    }

    static Button Named(Control under, string text) => under.FindChildren("*", nameof(Button), true, false).OfType<Button>().First(b => b.Text == text);

    [NodeTest]
    static async Task A_line_written_at_the_foot_goes_to_everyone_or_to_the_umpires_alone(TestRunner r)
    {
        var (h, log) = await Opened(r);
        Check.Equal("Chat log", LogPanel.Title, "the panel's name");
        await Write(r, log, "Hold the ridge");
        Check.That(h.Net.Server!.Log[^1] is Say { Text: "Hold the ridge", To: null }, "said to everyone");
        Check.That(Shown(log).Contains("You: Hold the ridge"), "the line shows in the stream");
        var to = log.Panel.FindChildren("*", nameof(OptionButton), true, false).OfType<OptionButton>().First(o => o.ItemCount >= 2 && o.GetItemText(1) == "Umpires");
        to.Select(1);
        to.EmitSignal(OptionButton.SignalName.ItemSelected, 1);
        await Write(r, log, "He lies about the ford");
        Check.That(h.Net.Server.Log[^1] is Say { To: Say.ToUmpires }, "said to the umpires");
        Check.That(Shown(log).Contains("You → Umpires: He lies about the ford"), "the stream says who reads it");
        await Write(r, log, "   ");
        Check.That(h.Net.Server.Log[^1] is Say { Text: "He lies about the ford" }, "an empty line is not sent");
    }

    [NodeTest]
    static async Task The_open_state_shows_kinds_to_pick_game_time_stamps_and_a_heading_where_the_turn_changed(TestRunner r)
    {
        var (h, log) = await Opened(r);
        h.S.Do(new Say { Text = "Before the turn" });
        h.S.Do(new RollDice { Sides = 6, Count = 1, Label = "morale" });
        h.S.Do(new AdvanceClock());
        h.S.Do(new Say { Text = "After the turn" });
        await r.Frames(3);
        Check.That(Shown(log).Contains("morale") && Shown(log).Contains("Before the turn"), "talk and dice in one stream");
        Check.That(Shown(log).Contains("Turn 1"), "a heading where the turn changed");
        var chips = log.Panel.FindChildren("*", nameof(Button), true, false).OfType<Button>().Where(b => b.Text is "Talk" or "Dice" or "Table" or "World").ToList();
        Check.Equal(4, chips.Count, "kind chips");
        Check.That(!chips[0].IsVisibleInTree(), "no chips in the simple state");
        Named(log.Panel, "▸").EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(2);
        Check.That(h.Hud.Ctx.Cfg.LogWide && chips[0].IsVisibleInTree(), "the open state shows the chips");
        Check.That(Shown(log).Contains(h.S.View.Clock.Cal.TimeOfDay(h.S.View.Clock.Now)), "lines carry the game time");
        chips.First(c => c.Text == "Dice").ButtonPressed = false;
        await r.Frames(2);
        Check.That(!Shown(log).Contains("morale") && Shown(log).Contains("After the turn"), "dice lines hidden, talk kept");
        chips.First(c => c.Text == "Dice").ButtonPressed = true;
        await r.Frames(2);
        Check.That(Shown(log).Contains("morale"), "dice lines back");
        Named(log.Panel, "◂").EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(2);
        Check.That(!h.Hud.Ctx.Cfg.LogWide && !chips[0].IsVisibleInTree(), "back to the simple state");
    }

    [NodeTest]
    static async Task Lines_said_while_the_panel_is_shut_are_counted_on_its_button_until_it_is_opened(TestRunner r)
    {
        var (h, log) = await Opened(r);
        h.Hud.ClosePanel("log");
        await r.Frames(2);
        h.S.Do(new Say { Text = "One" });
        h.S.Do(new Say { Text = "Two" });
        h.S.Do(new SetClock { Turn = 0, Seconds = h.S.View.Clock.Now + 60, TurnSeconds = 900 });
        await r.Frames(3);
        Check.Equal(2, log.Unread, "talk lines unread");
        var badge = h.Hud.Strip.Buttons["log"].FindChildren("*", nameof(Label), true, false).OfType<Label>().First();
        Check.That(badge.Visible && badge.Text == "2", "the count on the button");
        h.Hud.OpenPanel("log");
        await r.Frames(2);
        Check.That(log.Unread == 0 && !badge.Visible, "read once opened");
        Check.That(Shown(log).Contains("One") && Shown(log).Contains("Two"), "the lines are in the stream");
    }

    [NodeTest]
    static async Task The_find_box_counts_and_marks_matches_in_the_stream(TestRunner r)
    {
        var (h, log) = await Opened(r);
        h.S.Do(new Say { Text = "The bridge is down" });
        h.S.Do(new Say { Text = "Which bridge?" });
        h.S.Do(new Say { Text = "The ford holds" });
        await r.Frames(3);
        var find = log.Panel.FindChildren("*", "", true, false).OfType<FindBar>().First();
        find.Open("bridge");
        await r.Seconds(0.3);
        Check.Equal(2, find.Total, "matches");
        h.S.Do(new Say { Text = "Another bridge" });
        await r.Frames(3);
        Check.Equal(3, find.Total, "a new line is counted while finding");
    }

    [NodeTest]
    static async Task What_was_said_before_this_sitting_shows_ahead_of_the_new_lines_with_who_said_it_to_whom(TestRunner r)
    {
        var (h, log) = await Opened(r);
        h.Net.Server!.Keep(new RecordEntry { Kind = RecordEntry.Talk, Who = "Ann", Text = "Meet me at the mill", Reach = RecordEntry.ToOne + "Bob", At = 1000, Seconds = h.S.View.Clock.Now });
        h.Net.Server.Keep(new RecordEntry { Kind = RecordEntry.Dice, Who = "Ann", Text = "rolled 1d6: 4", At = 2000, Seconds = h.S.View.Clock.Now });
        h.Net.RequestRecord();
        await r.Frames(3);
        h.S.Do(new Say { Text = "A new line" });
        await r.Frames(3);
        string shown = Shown(log);
        Check.That(shown.Contains("Ann → Bob: Meet me at the mill") && shown.Contains("Ann rolled 1d6: 4"), "the earlier lines, named");
        Check.That(shown.IndexOf("Meet me at the mill", System.StringComparison.Ordinal) < shown.IndexOf("A new line", System.StringComparison.Ordinal), "earlier lines come first");
    }

    static async Task Choose(TestRunner r, LogPanel log, string entry)
    {
        var to = log.Panel.FindChildren("*", nameof(OptionButton), true, false).OfType<OptionButton>().First(o => o.ItemCount >= 2 && o.GetItemText(1) == "Umpires");
        int at = Enumerable.Range(0, to.ItemCount).First(i => to.GetItemText(i) == entry);
        to.Select(at);
        to.EmitSignal(OptionButton.SignalName.ItemSelected, at);
        await r.Frames(1);
    }

    [NodeTest]
    static async Task An_umpire_speaks_as_the_piece_picked_with_a_bubble_over_it_sends_its_wearer_a_hotline_line_and_pins_a_note(TestRunner r)
    {
        var (h, log) = await Opened(r);
        h.Table.Selection.Set(new[] { "p1" });
        await r.Frames(2);
        await Choose(r, log, "Shout as Block");
        await Write(r, log, "To arms!");
        Check.That(h.Net.Server!.Log[^1] is Speak { Id: "p1", Text: "To arms!", Loud: Earshot.Shout, Name: "Block" } said && said.Heard!.Contains("p2"), "spoken as the piece, heard by the piece beside it");
        Check.That(Shown(log).Contains("Block shouts: To arms!"), "the line in the stream");
        Check.Equal(1, h.Table.GetTree().GetNodesInGroup(SpeechBubbles.GroupName).Count, "a bubble over the speaker");
        await Choose(r, log, "Hotline to Block");
        await Write(r, log, "Play him drunk");
        Check.That(h.Net.Server.Log[^1] is Hotline { Id: "p1", Text: "Play him drunk" }, "a hotline line to the piece");
        Check.That(Shown(log).Contains("You → Block (hotline): Play him drunk"), "the hotline line in the stream");
        await Choose(r, log, "Note here");
        await Write(r, log, "The mill burns");
        Check.That(h.Net.Server.Log[^1] is Note { Text: "The mill burns" }, "a note");
        Check.That(Shown(log).Contains("You noted: The mill burns"), "the note in the stream");
        Check.Equal("Sound the retreat when the bridge falls, and\nnot a moment before it.", SpeechBubbles.Wrapped("Sound the retreat when the bridge falls, and not a moment before it."), "a long line wraps");
    }

    [NodeTest]
    static async Task With_two_pieces_picked_an_umpire_whispers_from_the_first_to_the_other_alone(TestRunner r)
    {
        var h = await HudTests.Host(r, pieces: 3);
        h.Hud.OpenPanel("log");
        await r.Frames(2);
        var log = h.Hud.LogPanel;
        h.Table.Selection.Set(new[] { "p1", "p2" });
        await r.Frames(2);
        await Choose(r, log, "Whisper as Block");
        await Choose(r, log, "Whisper to Block");
        await Write(r, log, "The ford is mined");
        Check.That(h.Net.Server!.Log[^1] is Speak { Id: "p1", To: "p2", Loud: Earshot.Whisper } said && said.Heard!.SequenceEqual(new[] { "p2" }), "whispered to the other piece, heard by it alone");
        Check.That(Shown(log).Contains("Block whispers to Block: The ford is mined"), "the line names whom it was whispered to");
        h.Table.Selection.Set(new[] { "p1", "p2", "p3" });
        await r.Frames(2);
        var to = log.Panel.FindChildren("*", nameof(OptionButton), true, false).OfType<OptionButton>().First(o => o.ItemCount >= 2 && o.GetItemText(1) == "Umpires");
        Check.That(Enumerable.Range(0, to.ItemCount).All(i => !to.GetItemText(i).StartsWith("Whisper to", System.StringComparison.Ordinal)), "no whisper to with three picked");
    }

    [NodeTest]
    static async Task A_line_noted_at_a_place_takes_the_view_there_when_clicked_and_near_view_keeps_only_what_happened_close_by(TestRunner r)
    {
        var (h, log) = await Opened(r);
        h.Table.Rig.Target = new Vector3(500, 0, 500);
        await r.Frames(2);
        await Choose(r, log, "Note here");
        await Write(r, log, "The mill burns");
        h.S.Do(new Say { Text = "Said nowhere" });
        h.Table.Rig.Target = Vector3.Zero;
        await r.Frames(3);
        log.Text.EmitSignal(RichTextLabel.SignalName.MetaClicked, "at:500;-500");
        await r.Seconds(0.8);
        Check.That(h.Net.Server!.Log.OfType<Note>().Single().Pos == new Vec2(500, -500), "the note is pinned where the view looked, in map terms");
        Check.That(h.Table.Rig.Target.DistanceTo(new Vector3(500, 0, 500)) < 1, $"the view went to the note's place, it is at {h.Table.Rig.Target}");
        Named(log.Panel, "▸").EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(2);
        Named(log.Panel, "Near view").ButtonPressed = true;
        await r.Frames(2);
        Check.That(Shown(log).Contains("The mill burns") && !Shown(log).Contains("Said nowhere"), "near the view, only what happened there");
        h.Table.Rig.Target = new Vector3(-9000, 0, -9000);
        Named(log.Panel, "Near view").ButtonPressed = false;
        Named(log.Panel, "Near view").ButtonPressed = true;
        await r.Frames(2);
        Check.That(!Shown(log).Contains("The mill burns"), "far from it, the note is not shown");
        Named(log.Panel, "◂").EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(2);
    }

    static OptionButton Picker(LogPanel log, string tip) => log.Panel.FindChildren("*", nameof(OptionButton), true, false).OfType<OptionButton>().First(o => o.TooltipText == tip);

    static string[] Offered(OptionButton pick) => Enumerable.Range(0, pick.ItemCount).Select(pick.GetItemText).ToArray();

    static async Task PickIn(TestRunner r, OptionButton pick, string text)
    {
        int at = System.Array.FindIndex(Offered(pick), t => t == text || t.StartsWith(text + " · ", System.StringComparison.Ordinal));
        Check.That(at >= 0, $"{text} is offered among {string.Join(", ", Offered(pick))}");
        pick.Select(at);
        pick.EmitSignal(OptionButton.SignalName.ItemSelected, at);
        await r.Frames(2);
    }

    static async Task OpenWide(TestRunner r, LogPanel log)
    {
        Named(log.Panel, "▸").EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(2);
    }

    static async Task Shut(TestRunner r, LogPanel log)
    {
        Named(log.Panel, "◂").EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(2);
    }

    [NodeTest]
    static async Task Who_keeps_one_persons_lines_or_the_picked_pieces_and_holds_its_choice_as_lines_arrive(TestRunner r)
    {
        var (h, log) = await Opened(r);
        var now = h.S.View.Clock.Now;
        h.Net.Server!.Keep(new RecordEntry { Kind = RecordEntry.Talk, Who = "Ann", Text = "Ann holds the mill", At = 1000, Seconds = now });
        h.Net.Server.Keep(new RecordEntry { Kind = RecordEntry.Talk, Who = "Bob", Text = "Bob takes the ford", At = 2000, Seconds = now });
        h.Net.Server.Keep(new RecordEntry { Kind = RecordEntry.Speech, Who = "Bob", Text = "Halt there", Piece = "p1", PieceName = "Block", Pos = new Vec2(1, 0), At = 3000, Seconds = now });
        h.Net.RequestRecord();
        await r.Frames(3);
        await OpenWide(r, log);
        var who = Picker(log, "Who");
        Check.That(who.IsVisibleInTree() && Offered(who) is ["Anyone", "Ann", "Bob", "Picked piece"], "Anyone, each person with a line, the picked piece: " + string.Join(", ", Offered(who)));
        await PickIn(r, who, "Ann");
        Check.That(Shown(log).Contains("Ann holds the mill") && !Shown(log).Contains("Bob takes the ford") && !Shown(log).Contains("Halt there"), "only Ann's lines");
        h.S.Do(new Say { Text = "A live line" });
        await r.Frames(3);
        Check.Equal("Ann", who.GetItemText(who.Selected), "the choice holds as a line arrives");
        Check.That(!Shown(log).Contains("A live line") && Shown(log).Contains("Ann holds the mill"), "a line that names nobody is not Ann's");
        h.Table.Selection.Set(new[] { "p1" });
        await PickIn(r, who, "Picked piece");
        Check.That(Shown(log).Contains("Halt there") && !Shown(log).Contains("Ann holds the mill"), "the picked piece's lines");
        h.Table.Selection.Set(new[] { "p2" });
        await r.Frames(2);
        Check.That(!Shown(log).Contains("Halt there"), "another piece picked, its lines go");
        await Shut(r, log);
        Check.That(Shown(log).Contains("Ann holds the mill") && Shown(log).Contains("A live line") && Shown(log).Contains("Halt there"), "the simple state shows every line");
        await OpenWide(r, log);
        await PickIn(r, who, "Anyone");
        Check.That(Shown(log).Contains("Bob takes the ford") && Shown(log).Contains("A live line"), "anyone again");
        await Shut(r, log);
    }

    [NodeTest]
    static async Task When_keeps_this_turn_or_a_past_one_and_holds_its_choice_as_the_turn_goes_on(TestRunner r)
    {
        var (h, log) = await Opened(r);
        int first = h.S.View.Clock.Turn;
        h.S.Do(new Say { Text = "Before the turn" });
        h.S.Do(new AdvanceClock());
        h.S.Do(new Say { Text = "After the turn" });
        await r.Frames(3);
        await OpenWide(r, log);
        var when = Picker(log, "When");
        bool Offers(int turn) => Offered(when).Any(t => t == $"Turn {turn}" || t.StartsWith($"Turn {turn} · ", System.StringComparison.Ordinal));
        Check.That(Offered(when) is ["All", "This turn", ..] && Offers(first) && Offers(first + 1), "All, this turn and each turn with lines: " + string.Join(", ", Offered(when)));
        await PickIn(r, when, "This turn");
        Check.That(Shown(log).Contains("After the turn") && !Shown(log).Contains("Before the turn"), "this turn only");
        h.S.Do(new Say { Text = "Still this turn" });
        await r.Frames(3);
        Check.That(when.GetItemText(when.Selected) == "This turn" && Shown(log).Contains("Still this turn"), "the choice holds as a line arrives");
        await PickIn(r, when, $"Turn {first}");
        Check.That(Shown(log).Contains("Before the turn") && !Shown(log).Contains("After the turn"), "a past turn alone");
        await Shut(r, log);
        Check.That(Shown(log).Contains("Before the turn") && Shown(log).Contains("Still this turn"), "the simple state shows every line");
        await OpenWide(r, log);
        await PickIn(r, when, "All");
        await Shut(r, log);
    }

    [NodeTest]
    static async Task The_host_s_lines_of_who_changed_what_go_into_the_saved_record_under_the_host_s_name(TestRunner r)
    {
        var (h, log) = await Opened(r);
        h.S.Do(new SetClock { Turn = 0, Seconds = h.S.View.Clock.Now + 60, TurnSeconds = 900 });
        await r.Frames(3);
        Check.That(Shown(log).Contains("You set the clock"), "the line as the host reads it");
        var kept = h.Net.Server!.Record.LastOrDefault(e => e.Kind == RecordEntry.Table);
        Check.That(kept != null && kept.Text.StartsWith("Tester set the clock", System.StringComparison.Ordinal), $"kept under the name: {kept?.Text}");
        Check.That(!kept!.Text.Contains('['), "kept as plain words");
    }

    [NodeTest]
    static async Task Who_also_keeps_the_lines_written_this_sitting_by_a_person_or_about_the_picked_piece(TestRunner r)
    {
        var (h, log) = await Opened(r);
        h.S.Do(new Say { Text = "Said by the host" });
        h.S.Do(new Speak { Id = "p1", Text = "Said by the block" });
        h.S.Do(new Speak { Id = "p2", Text = "Said by the other block" });
        await r.Frames(3);
        await OpenWide(r, log);
        await PickIn(r, Picker(log, "Who"), "Tester");
        Check.That(Shown(log).Contains("Said by the host") && Shown(log).Contains("Said by the block"), "the person's own lines of this sitting");
        h.Table.Selection.Set(new[] { "p1" });
        await r.Frames(2);
        await PickIn(r, Picker(log, "Who"), "Picked piece");
        Check.That(Shown(log).Contains("Said by the block") && !Shown(log).Contains("Said by the other block") && !Shown(log).Contains("Said by the host"), "only the picked piece's lines");
        await PickIn(r, Picker(log, "Who"), "Anyone");
        await Shut(r, log);
    }
}
