using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class AgentUiTests
{
    static Task<(bool error, string text)> Ask(AgentEndpoint agent, string tool, string args) => AgentRecordTests.Ask(agent, tool, args);

    static JsonArray Rows(string read) => JsonNode.Parse(read[(read.IndexOf('\n') + 1)..])!.AsArray();

    static async Task<JsonNode> Find(AgentEndpoint agent, string find, string kind)
    {
        var (_, read) = await Ask(agent, "ui_read", $$"""{"find":"{{find}}"}""");
        return Rows(read).FirstOrDefault(r => (string?)r!["kind"] == kind) ?? throw new TestFailure($"no {kind} {find}: {read}");
    }

    [NodeTest]
    static async Task The_agent_reads_the_app_s_controls_and_uses_a_field_a_choice_a_toggle_and_a_menu_row(TestRunner r)
    {
        var (h, agent) = await AgentRecordTests.Open(r);
        var typed = "";
        var picked = -1L;
        var chosen = -1L;
        var box = new VBoxContainer { Name = "AgentUiBox" };
        var field = new LineEdit { PlaceholderText = "Regiment's name" };
        field.TextChanged += t => typed = t;
        var choice = new OptionButton { TooltipText = "Formation for the agent test" };
        choice.AddItem("Line");
        choice.AddItem("Column");
        choice.ItemSelected += i => picked = i;
        box.AddChild(field);
        box.AddChild(choice);
        h.Hud.AddChild(box);
        await r.Frames(2);

        var row = await Find(agent, "Regiment's name", "field");
        await Ask(agent, "ui_act", $$"""{"ref":"{{row["ref"]}}","action":"set","value":"2nd Hussars"}""");
        Check.Equal("2nd Hussars", typed, "the field is typed into as the person types");
        var pick = await Find(agent, "Formation for the agent test", "choice");
        await Ask(agent, "ui_act", $$"""{"ref":"{{pick["ref"]}}","action":"set","value":"column"}""");
        Check.Equal(1L, picked, "the choice is made");

        bool plates = h.Table.Settings.LabelPlates;
        var toggle = await Find(agent, "Plates behind labels", "toggle");
        var (error, said) = await Ask(agent, "ui_act", $$"""{"ref":"{{toggle["ref"]}}"}""");
        Check.That(!error && h.Table.Settings.LabelPlates != plates, "the sidebar toggle is pressed: " + said);

        var menu = new PopupMenu { Title = "Orders" };
        menu.AddItem("Advance", 7);
        menu.IdPressed += id => chosen = id;
        h.Hud.AddChild(menu);
        menu.Popup();
        await r.Frames(2);
        var advance = await Find(agent, "Advance", "menu_item");
        await Ask(agent, "ui_act", $$"""{"ref":"{{advance["ref"]}}"}""");
        Check.Equal(7L, chosen, "the menu row is picked");
        var (stale, _) = await Ask(agent, "ui_act", $$"""{"ref":"{{row["ref"]}}","action":"set","value":"x"}""");
        Check.That(stale, "a ref from an older read is refused");
    }

    [NodeTest]
    static async Task Fenced_controls_are_neither_listed_nor_reached_and_the_title_screen_is_read_only(TestRunner r)
    {
        var (h, agent) = await AgentRecordTests.Open(r);
        var fenced = AgentFence.Mark(new VBoxContainer { Name = "FencedBox" });
        fenced.AddChild(new Button { Text = "Delete everything for good" });
        h.Hud.AddChild(fenced);
        var export = new ExportDialog(h.S.Store, "scenario", "none") { Title = "Export scenario" };
        h.Hud.AddChild(export);
        export.Popup();
        await r.Frames(2);
        var (_, read) = await Ask(agent, "ui_read", "{}");
        Check.That(!read.Contains("Delete everything for good") && !read.Contains("Export scenario"), "fenced controls and windows are not listed");
        Check.That(!read.Contains("POV capture"), "nor the POV capture button");
        export.Hide();
        await r.Frames(2);
        var (keyRefused, keyWhy) = await Ask(agent, "ui_act", """{"keys":"v"}""");
        Check.That(keyRefused && keyWhy.Contains("POV capture"), "nor its key: " + keyWhy);
        agent.AtTitle = () => true;
        var (title, titleWhy) = await Ask(agent, "ui_act", """{"keys":"Escape"}""");
        Check.That(title && titleWhy.Contains("title screen"), "nothing is used on the title screen: " + titleWhy);
        var (readError, _) = await Ask(agent, "ui_read", "{}");
        Check.That(!readError, "but it is read");
    }

    [NodeTest]
    static async Task The_password_field_is_neither_listed_nor_typed_into(TestRunner r)
    {
        var (h, agent) = await AgentRecordTests.Open(r);
        h.Net.Door(new KTable.Core.Protocol.SetPassword { Password = "fox" });
        var owner = new Window { Title = "Joining test" };
        h.Hud.AddChild(owner);
        var row = (Container)InviteDialog.PasswordRow(h.Hud.Ctx, owner);
        owner.AddChild(row);
        owner.Popup();
        await r.Frames(2);
        var (_, read) = await Ask(agent, "ui_read", "{}");
        Check.That(!read.Contains("fox") && !read.Contains("Copy the password"), "no password control is listed: " + read);
        row.GetChild<LineEdit>(1).GrabFocus();
        await r.Frames(1);
        var (refused, why) = await Ask(agent, "ui_act", """{"text":"wolf"}""");
        Check.That(refused && why.Contains("out of the agent's reach"), "typing into the password is refused: " + why);
        Check.Equal("fox", h.S.Access?.Password ?? "", "and the password stays");
    }

    [NodeTest]
    static async Task The_agent_reads_the_guides_the_app_s_log_and_the_diagnostics(TestRunner r)
    {
        var (_, agent) = await AgentRecordTests.Open(r);
        var (_, list) = await Ask(agent, "read_guide", "{}");
        Check.That(list.Contains("start.md") && list.Contains("player-guide.md"), "the guides are listed: " + list);
        var (error, found) = await Ask(agent, "read_guide", """{"guide":"The player's guide","find":"the"}""");
        Check.That(!error && found.Contains("paragraph"), "a guide is searched: " + found[..System.Math.Min(200, found.Length)]);
        var (missing, _) = await Ask(agent, "read_guide", """{"guide":"no such guide"}""");
        Check.That(missing, "a guide that is not there is refused");
        var (logError, log) = await Ask(agent, "read_app_log", "{}");
        Check.That(!logError, "the log is read or said to be empty: " + log);
        var (_, diagnostics) = await Ask(agent, "diagnostics", "{}");
        Check.That(diagnostics.Contains("Kriegsspiel Table"), "the diagnostics come back: " + diagnostics);
    }
}
