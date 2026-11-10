using System.Globalization;
using System.Text;
using System.Text.Json;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Client.Agent;

public sealed partial class TableTools
{
    const double WaitUsual = 30, WaitMost = 120, GatherSeconds = 0.5;
    const int WatchedLines = 60;
    static readonly string[] WaitKinds = { "any", "table", "chat", "console", "dialog", "turn" };

    IEnumerable<AgentTool> WatchTools()
    {
        yield return new AgentTool("wait_for",
            "Waits until something happens at the table, then says what: a change someone else made (table), a line in the chat or a roll (chat), the scripts' console (console), " +
            "a window a script opened for this seat (dialog), the turn ending (turn), or any of them. Follows a live game without asking again and again; returns when it happens or the time is up.",
            Args.Schema(new Arg("what", "string", string.Join(", ", WaitKinds) + " (any if left out)"), new Arg("seconds", "number", $"how long to wait, 1–{WaitMost} ({WaitUsual} if left out)"),
                new Arg("mine", "boolean", "also count this seat's own changes")),
            WaitFor);
    }

    async Task<ToolResult> WaitFor(JsonElement a)
    {
        if (Open(out var why) is not { } s) return why!;
        var kind = (a.Str("what") ?? "any").Trim().ToLowerInvariant();
        if (!WaitKinds.Contains(kind)) return ToolResult.Fail("what is " + string.Join(", ", WaitKinds) + ".");
        double seconds = Math.Clamp(a.Num("seconds") ?? WaitUsual, 1, WaitMost);
        bool mine = a.Flag("mine") == true;
        var seen = new List<Command>();
        long seq = LastSeq(s);
        var dialogs = s.OpenDialogs.Select(d => d.Token).ToHashSet();
        int turn = s.View.Clock.Turn;
        void Saw(Command c)
        {
            foreach (var one in Flat(c))
                if (mine || c.Actor != s.Me.PeerId) seen.Add(one);
        }
        bool Talk(Command c) => c is Say or RollDice;
        bool NewDialog() => s.OpenDialogs.Any(d => !dialogs.Contains(d.Token));
        bool Happened() => kind switch
        {
            "table" => seen.Any(c => !Talk(c) && c is not (ShowDialog or DialogResult)),
            "chat" => seen.Any(Talk),
            "console" => LastSeq(s) > seq,
            "dialog" => NewDialog(),
            "turn" => s.View.Clock.Turn != turn,
            _ => seen.Count > 0 || LastSeq(s) > seq || NewDialog(),
        };
        s.Applied += Saw;
        try
        {
            if (!await Until(Happened, seconds)) return new ToolResult(FormattableString.Invariant($"Nothing happened in {seconds:0} seconds."));
            await _delay(GatherSeconds);
        }
        finally { s.Applied -= Saw; }
        var sb = new StringBuilder();
        if (s.View.Clock.Turn != turn) sb.Append($"Turn {s.View.Clock.Turn} · {s.View.Clock.GameTime}\n");
        foreach (var c in seen.Take(WatchedLines)) sb.Append(Told(s, c)).Append('\n');
        if (seen.Count > WatchedLines) sb.Append($"… and {seen.Count - WatchedLines} more changes\n");
        foreach (var d in s.OpenDialogs.Where(d => !dialogs.Contains(d.Token))) sb.Append($"A script asks: {(d.Title.Length > 0 ? d.Title : d.Kind)} (answer_dialog)\n");
        sb.Append(Since(s, seq, WatchedLines).TrimStart('\n'));
        return new ToolResult(sb.ToString().TrimEnd());
    }

    static IEnumerable<Command> Flat(Command c) => c is Batch b ? b.Items.SelectMany(Flat) : new[] { c };

    static string Told(ClientSession s, Command c)
    {
        var who = c.Actor == 0 ? "a script" : c.Actor == s.Me.PeerId ? "you" : s.Peers.FirstOrDefault(p => p.PeerId == c.Actor)?.Name ?? "someone";
        switch (c)
        {
            case Say say:
                return $"{who} said: {say.Text}";
            case RollDice roll:
                return $"{who} rolled {roll.Count}d{roll.Sides}{(roll.Label.Length > 0 ? " for " + roll.Label : "")}: {string.Join(", ", roll.Results)}";
            case AdvanceClock:
                return $"{who} ended the turn";
        }
        var id = c.GetType().GetProperty("Id")?.GetValue(c) as string;
        var piece = id is { Length: > 0 } && Find(s, id) is { } p ? $" {NameOf(s, p)} ({id})" : id is { Length: > 0 } ? $" {id}" : "";
        var what = string.Concat(c.GetType().Name.Select((ch, i) => i > 0 && char.IsUpper(ch) ? " " + char.ToLower(ch, CultureInfo.InvariantCulture) : ch.ToString().ToLowerInvariant()));
        return $"{who}: {what}{piece}";
    }
}
