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

    public static DynValue Speak(ScriptHost host, string id, DynValue text, DynValue loud)
    {
        host.Emit(new Speak { Layer = Layers.Umpire, Id = id, Text = text.CastToString() ?? "", Loud = Loud(loud) });
        return DynValue.Nil;
    }

    public static void Heard(ScriptHost host, Speak said)
    {
        foreach (string id in said.Heard ?? new())
            if (host.PieceScripts.TryGetValue(id, out var ps)) host.Call(ps, "on_heard", host.Handle(said.Id), S(said.Text), S(Words[Earshot.Loudness(said.Loud)]));
    }
}
