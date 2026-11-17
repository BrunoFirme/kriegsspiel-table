using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using KTable.Client.Agent;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed class AgentMessageTools
{
    const int Shown = 60, WaitSeconds = 10;
    static readonly string[] KindWords = { RecordEntry.Talk, RecordEntry.Speech, RecordEntry.Direct, RecordEntry.Noted, RecordEntry.Dice, RecordEntry.World };

    readonly AgentTableTools _tools;

    public AgentMessageTools(AgentTableTools tools) => _tools = tools;

    public IEnumerable<AgentTool> Tools()
    {
        yield return new AgentTool("read_messages",
            "The saved record behind the Chat log as this seat gets it. Lines are numbered; since (the last read) gives what is new.",
            Args.Schema(new Arg("kind", "string", "talk, speech, hotline, note, dice or world"), new Arg("who", "string", "a person"), new Arg("piece", "string", "a piece's id"),
                new Arg("turn", "number", "a turn"), new Arg("since", "number", "after this number"), new Arg("last", "number", $"the newest so many ({Shown})"),
                new Arg("near", Args.Numbers, "[x, z, radius] in map units")),
            ReadMessages);
    }

    async Task<ToolResult> ReadMessages(JsonElement a)
    {
        if (_tools.Table(out var why) is not var (s, hud)) return why!;
        var kind = a.Str("kind")?.Trim().ToLowerInvariant();
        if (kind is { Length: > 0 } && !KindWords.Contains(kind)) return ToolResult.Fail($"kind is {string.Join(", ", KindWords)}.");
        var around = Near(a);
        bool near = around != null;
        if (near && (around is not [_, _, > 0] || around.Any(double.IsNaN))) return ToolResult.Fail("near is [x, z, radius], radius above 0.");
        if (await Record(s, hud) is not { } record) return ToolResult.Fail("The host did not send the record.");
        var who = a.Str("who")?.Trim();
        var piece = a.Str("piece")?.Trim();
        var turn = a.Num("turn") is { } t ? (int?)t : null;
        long since = (long)(a.Num("since") ?? 0);
        var place = near ? new Vec2(around![0], around[1]) : default;
        double radius = near ? around![2] : 0;
        var found = record.Where(e => e.At > since && (kind is not { Length: > 0 } || e.Kind == kind) && (turn == null || e.Turn == turn)
            && (who is not { Length: > 0 } || string.Equals(e.Who, who, StringComparison.OrdinalIgnoreCase))
            && (piece is not { Length: > 0 } || e.Piece == piece)
            && (!near || e.Pos is { } at && (at - place).Length <= radius)).ToList();
        int last = Math.Clamp((int)(a.Num("last") ?? Shown), 1, RecordEntry.MostSent);
        if (found.Count == 0) return new ToolResult("No line of the record matches.");
        var shown = found.Skip(Math.Max(0, found.Count - last)).ToList();
        int skipped = found.Count - shown.Count;
        var lines = shown.Select(e => Line(e, s.View.Clock.Cal));
        return new ToolResult(Hearsay.Marked((skipped > 0 ? $"{skipped} earlier lines left out.\n" : "") + string.Join("\n", lines)));
    }

    static List<double>? Near(JsonElement a)
    {
        if (a.ValueKind != JsonValueKind.Object || !a.TryGetProperty("near", out var near)) return null;
        if (near.ValueKind != JsonValueKind.Array) return new List<double>();
        return near.EnumerateArray().Select(n => n.ValueKind == JsonValueKind.Number ? n.GetDouble() : double.NaN).ToList();
    }

    static async Task<IReadOnlyList<RecordEntry>?> Record(Session s, Hud hud)
    {
        var got = new TaskCompletionSource<IReadOnlyList<RecordEntry>>();
        void Took(IReadOnlyList<RecordEntry> list) => got.TrySetResult(list);
        s.RecordArrived += Took;
        try
        {
            hud.N.RequestRecord();
            var done = await Task.WhenAny(got.Task, Task.Delay(TimeSpan.FromSeconds(WaitSeconds)));
            return done == got.Task ? await got.Task : null;
        }
        finally
        {
            s.RecordArrived -= Took;
        }
    }

    static string Line(RecordEntry e, CalendarDef cal)
    {
        string who = e.Who ?? "";
        string piece = $"{e.PieceName ?? "a piece"} [{e.Piece}]";
        string about = e.Kind switch
        {
            RecordEntry.Speech => $"{piece} {Earshot.Verb(e.Loud)}{(who.Length > 0 ? $" (by {who})" : "")}",
            RecordEntry.Direct => $"{who} → {piece} (hotline)",
            RecordEntry.Noted => $"{who} noted",
            RecordEntry.Talk => who + (e.Reach == RecordEntry.ToUmpires ? " → Umpires" : e.ToName is { } to ? " → " + to : ""),
            _ => who,
        };
        string at = e.Pos is { } p ? string.Create(CultureInfo.InvariantCulture, $" @ {p.X:0.#}, {p.Z:0.#}") : "";
        string text = Hearsay.Held(e.Text);
        string said = e.Kind is RecordEntry.Dice or RecordEntry.World ? $"{about} {text}".Trim() : $"{about}: {text}";
        return $"{e.At}  turn {e.Turn} · {cal.TimeOfDay(e.Seconds)} · {e.Kind}{at}  {said}";
    }
}
