using KTable.Core.Model;
using KTable.Core.Protocol;
using MoonSharp.Interpreter;
using static KTable.Server.Scripting.LuaArgs;

namespace KTable.Server.Scripting;

static class ScriptSpeech
{
    static readonly string[] Words = { "whisper", "say", "shout" };

    static int Loud(DynValue v)
    {
        if (v.Type == DataType.Number) return Earshot.Loudness((int)v.Number);
        int named = Array.IndexOf(Words, v.CastToString()?.ToLowerInvariant() ?? "");
        return named < 0 ? Earshot.Say : named;
    }

    public static DynValue Speak(ScriptHost host, string id, DynValue text, DynValue loud, DynValue to, string layer = Layers.Umpire)
    {
        string? whom = to.IsNil() ? null : IdOf(to) is { Length: > 0 } listener ? listener : null;
        host.Emit(new Speak { Layer = layer, Id = id, Text = text.CastToString() ?? "", Loud = Loud(loud), To = whom });
        return DynValue.Nil;
    }

    public static void Heard(ScriptHost host, Speak said)
    {
        foreach (string id in said.Heard ?? new())
            if (host.PieceScripts.TryGetValue(id, out var ps))
                host.Call(ps, "on_heard", said.Layer == CrowdPiece.Layer ? new ScriptCrowds(host).Find(said.Id) : host.Handle(said.Id), S(said.Text), S(Words[Earshot.Loudness(said.Loud)]));
    }
}
