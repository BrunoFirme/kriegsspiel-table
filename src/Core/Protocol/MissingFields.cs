using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json.Serialization;
using KTable.Core.Model;

namespace KTable.Core.Protocol;

public static class MissingFields
{
    sealed record Field(string Name, PropertyInfo Property, bool MayBeNull, bool ItemsMayBeNull);

    static readonly ConcurrentDictionary<Type, Field[]> Fields = new();
    static readonly NullabilityInfoContext Nullability = new();
    static readonly object NullabilityLock = new();

    public static string? Of(Command? c)
    {
        if (c == null) return "the message carried no command";
        return Walk(c, "") is { } path ? $"{c.GetType().Name} refused: {path} is missing" : null;
    }

    static string? Walk(object o, string at)
    {
        foreach (var f in FieldsOf(o.GetType()))
        {
            var v = f.Property.GetValue(o);
            var here = at.Length == 0 ? Camel(f.Name) : at + "." + Camel(f.Name);
            if (v == null)
            {
                if (!f.MayBeNull) return here;
                continue;
            }
            if (v is string) continue;
            if (v is IDictionary d)
            {
                foreach (DictionaryEntry e in d)
                    if (Item(e.Value, $"{here}[{e.Key}]", f.ItemsMayBeNull) is { } inDict) return inDict;
                continue;
            }
            if (v is IList l)
            {
                for (int i = 0; i < l.Count; i++)
                    if (Item(l[i], $"{here}[{i}]", f.ItemsMayBeNull) is { } inList) return inList;
                continue;
            }
            if (Ours(v.GetType()) && Walk(v, here) is { } inner) return inner;
        }
        return null;
    }

    static string? Item(object? v, string at, bool mayBeNull) =>
        v == null ? (mayBeNull ? null : at) : v is not string && Ours(v.GetType()) ? Walk(v, at) : null;

    static bool Ours(Type t) => t.IsClass && t != typeof(GameState) && t.Namespace?.StartsWith("KTable.Core", StringComparison.Ordinal) == true;

    static Field[] FieldsOf(Type t) => Fields.GetOrAdd(t, static t =>
    {
        lock (NullabilityLock)
            return t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0 && !p.PropertyType.IsValueType
                    && p.GetCustomAttribute<JsonIgnoreAttribute>() == null && p.DeclaringType != typeof(Command))
                .Select(p =>
                {
                    var n = Nullability.Create(p);
                    bool items = (p.DeclaringType == typeof(Batch) && p.Name == nameof(Batch.Items))
                        || n.GenericTypeArguments.Length > 0 && n.GenericTypeArguments[^1].ReadState != NullabilityState.NotNull
                        || n.ElementType is { ReadState: not NullabilityState.NotNull };
                    return new Field(p.Name, p, n.ReadState != NullabilityState.NotNull, items);
                })
                .ToArray();
    });

    static string Camel(string name) => name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name[1..];
}
