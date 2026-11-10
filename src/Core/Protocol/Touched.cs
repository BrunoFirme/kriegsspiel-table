using System.Text;
using System.Text.Json;

namespace KTable.Core.Protocol;

public readonly record struct Reach(string[] Things, string[] SitsOn)
{
    public bool Changes(Reach other) => Things.Any(other.Things.Contains) || SitsOn.Any(other.Things.Contains);
}

public static class Touched
{
    enum Names { Nothing, Thing, Place }

    public static Reach By(Command c) => By(c, Json.SerializeBytes(c));

    public static Reach By(Command c, ReadOnlySpan<byte> json)
    {
        if (c is RestoreTurn or LoadSave) return new Reach([], []);
        var things = new HashSet<string>(StringComparer.Ordinal);
        var places = new HashSet<string>(StringComparer.Ordinal);
        var lists = new Stack<Names>();
        Names? named = null;
        var reader = new Utf8JsonReader(json);
        while (reader.Read())
            switch (reader.TokenType)
            {
                case JsonTokenType.PropertyName:
                    named = What(reader.ValueIsEscaped ? Encoding.UTF8.GetBytes(reader.GetString()!) : reader.ValueSpan);
                    break;
                case JsonTokenType.StartArray:
                    lists.Push(named ?? Names.Nothing);
                    named = null;
                    break;
                case JsonTokenType.StartObject:
                    lists.Push(Names.Nothing);
                    named = null;
                    break;
                case JsonTokenType.EndArray or JsonTokenType.EndObject:
                    lists.Pop();
                    break;
                case JsonTokenType.String:
                    var owner = named ?? (lists.Count > 0 ? lists.Peek() : Names.Nothing);
                    if (owner != Names.Nothing && reader.GetString() is { Length: > 0 } s) (owner == Names.Thing ? things : places).Add(s);
                    named = null;
                    break;
                default:
                    named = null;
                    break;
            }
        if (things.Count == 0) things.Add("kind:" + c.GetType().Name);
        return new Reach(things.ToArray(), places.ToArray());
    }

    static Names What(ReadOnlySpan<byte> name)
    {
        if (name.SequenceEqual("scenarioId"u8)) return Names.Nothing;
        if (name.SequenceEqual("id"u8) || name.SequenceEqual("ids"u8) || name.SequenceEqual("child"u8) || name.SequenceEqual("piece"u8)
            || name.EndsWith("Id"u8) || name.EndsWith("Ids"u8)) return Names.Thing;
        return name.SequenceEqual("holder"u8) || name.SequenceEqual("parent"u8) || name.SequenceEqual("board"u8) ? Names.Place : Names.Nothing;
    }
}
