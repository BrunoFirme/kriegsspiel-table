using System.Text.Json;
using KTable.Core.Protocol;

namespace KTable.Client.Agent;

public sealed class TalkTools
{
    static readonly string[] Loudly = { "whisper", "say", "shout" };
    const string Umpires = "umpires";

    readonly TableTools _t;

    public TalkTools(TableTools t) => _t = t;

    public IEnumerable<AgentTool> Tools()
    {
        yield return new AgentTool("say",
            "Writes a line in the Chat log: to everyone, to the umpires or to one person; or, with as, a piece says it and the pieces within earshot hear.",
            Args.Schema(new Arg("text", "string", "the line", true), new Arg("colour", "string", "#rrggbb"), new Arg("to", "string", "umpires, or a person's name"),
                new Arg("as", "string", "a piece's id, or a crowd person's id (town:number): it speaks the line"), new Arg("loudness", "string", "with as: whisper, say or shout (about 2, 15, 60 m)"),
                new Arg("to_piece", "string", "with a whisper: the one piece's id that hears it")), Say);
    }

    async Task<ToolResult> Say(JsonElement a)
    {
        if (_t.Open(out var why) is not { } s) return why!;
        if (!s.IsUmpire) return ToolResult.Fail("Only umpires write in the Chat log.");
        var text = a.Str("text")?.Trim() ?? "";
        if (text.Length == 0) return ToolResult.Fail("text is needed.");
        if (a.Str("as") is { Length: > 0 } piece)
        {
            int loud = Array.IndexOf(Loudly, (a.Str("loudness") ?? "say").Trim().ToLowerInvariant());
            if (loud < 0) return ToolResult.Fail("loudness is whisper, say or shout.");
            bool crowd = s.View.Find(KTable.Core.Model.Layers.Umpire, piece) == null && KTable.Core.Model.CrowdPiece.Parse(piece) != null;
            if (!crowd && s.View.Find(KTable.Core.Model.Layers.Umpire, piece) == null) return ToolResult.Fail($"No piece {piece}.");
            string? listener = a.Str("to_piece")?.Trim() is { Length: > 0 } whom ? whom : null;
            if (listener != null && loud != Earshot.Whisper) return ToolResult.Fail("to_piece goes with a whisper.");
            if (listener != null && s.View.Find(KTable.Core.Model.Layers.Umpire, listener) == null) return ToolResult.Fail($"No piece {listener}.");
            s.Do(new Speak { Layer = crowd ? KTable.Core.Model.CrowdPiece.Layer : KTable.Core.Model.Layers.Umpire, Id = piece, Text = text, Loud = loud, To = listener });
            await _t.Landed(s);
            return new ToolResult("Spoken.");
        }
        string? to = null;
        if (a.Str("to")?.Trim() is { Length: > 0 } reader)
        {
            var named = s.Peers.FirstOrDefault(p => string.Equals(p.Name, reader, StringComparison.OrdinalIgnoreCase));
            if (!reader.Equals(Umpires, StringComparison.OrdinalIgnoreCase) && named == null) return ToolResult.Fail($"Nobody named {reader} is at the table.");
            to = named != null ? $"p:{named.PeerId}" : KTable.Core.Protocol.Say.ToUmpires;
        }
        s.Do(new Say { Text = text, Color = a.Str("colour"), To = to });
        await _t.Landed(s);
        return new ToolResult("Written in the Chat log.");
    }
}
