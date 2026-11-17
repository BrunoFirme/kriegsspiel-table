using System.Collections;
using System.Reflection;

namespace KTable.Core.Tests;

public sealed class CopiesAreWholeTests
{
    const int Deepest = 4;

    public static TheoryData<Type> Copied()
    {
        var data = new TheoryData<Type>();
        foreach (var t in typeof(Json).Assembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true } && CloneOf(t) != null && t.GetConstructor(Type.EmptyTypes) != null)
            .OrderBy(t => t.FullName, StringComparer.Ordinal))
            data.Add(t);
        return data;
    }

    static MethodInfo? CloneOf(Type t) =>
        t.GetMethod("Clone", BindingFlags.Public | BindingFlags.Instance, Type.EmptyTypes) is { } m && m.ReturnType == t ? m : null;

    [Fact]
    public void Every_copy_routine_in_the_model_is_found()
    {
        int found = ((IEnumerable)Copied()).Cast<object>().Count();
        Assert.True(found >= 25, $"only {found} types with a Clone were found");
    }

    [Theory, MemberData(nameof(Copied))]
    public void A_copy_carries_every_property_of_what_it_copies(Type type)
    {
        int next = 1;
        var original = Filled(type, 0, ref next)!;
        var copy = CloneOf(type)!.Invoke(original, null);
        var missing = new List<string>();
        Compare(original, copy, type.Name, missing);
        Assert.True(missing.Count == 0, $"{type.Name}.Clone() loses: {string.Join(", ", missing)}");
    }

    static object? Filled(Type t, int depth, ref int next)
    {
        if (Nullable.GetUnderlyingType(t) is { } inner) return Filled(inner, depth, ref next);
        if (t == typeof(string)) return "v" + next++;
        if (t == typeof(bool)) return true;
        if (t.IsEnum) return Enum.GetValues(t).Cast<object>().Last();
        if (t.IsPrimitive) return Convert.ChangeType(next++ % 100 + 1, t);
        if (t == typeof(decimal)) return (decimal)next++;
        if (depth > Deepest) return null;
        if (t.IsArray)
        {
            var one = Array.CreateInstance(t.GetElementType()!, 1);
            one.SetValue(Filled(t.GetElementType()!, depth + 1, ref next), 0);
            return one;
        }
        if (t.IsAbstract || t.IsInterface || t == typeof(object)) return null;
        if (t.IsGenericType && typeof(IDictionary).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) != null)
        {
            var map = (IDictionary)Activator.CreateInstance(t)!;
            var args = t.GetGenericArguments();
            if (Filled(args[0], depth + 1, ref next) is { } key && Filled(args[1], depth + 1, ref next) is { } held) map[key] = held;
            return map;
        }
        if (t.IsGenericType && typeof(IEnumerable).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) != null && t.GetMethod("Add") is { } add)
        {
            var many = Activator.CreateInstance(t)!;
            if (Filled(t.GetGenericArguments()[0], depth + 1, ref next) is { } item) add.Invoke(many, [item]);
            return many;
        }
        if (t.IsValueType && t.GetConstructors().OrderByDescending(c => c.GetParameters().Length).FirstOrDefault() is { } made && made.GetParameters().Length > 0)
        {
            var values = new List<object?>();
            foreach (var p in made.GetParameters()) values.Add(Filled(p.ParameterType, depth + 1, ref next) ?? Default(p.ParameterType));
            return made.Invoke(values.ToArray());
        }
        if (t.GetConstructor(Type.EmptyTypes) == null && !t.IsValueType) return null;
        var o = Activator.CreateInstance(t)!;
        foreach (var p in Settable(t))
        {
            var value = p.PropertyType == typeof(bool) ? !(bool)p.GetValue(o)! : Filled(p.PropertyType, depth + 1, ref next);
            if (value != null) p.SetValue(o, value);
        }
        return o;
    }

    static object? Default(Type t) => t.IsValueType ? Activator.CreateInstance(t) : null;

    static IEnumerable<PropertyInfo> Settable(Type t) =>
        t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetIndexParameters().Length == 0 && p.CanRead && p.SetMethod is { IsPublic: true });

    static void Compare(object? a, object? b, string path, List<string> missing)
    {
        if (a == null || b == null) { if (a != b) missing.Add(path); return; }
        var t = a.GetType();
        if (t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal)) { if (!a.Equals(b)) missing.Add(path); return; }
        if (a is IDictionary da && b is IDictionary db)
        {
            if (da.Count != db.Count) { missing.Add(path); return; }
            foreach (DictionaryEntry e in da)
                if (!db.Contains(e.Key)) missing.Add($"{path}[{e.Key}]");
                else Compare(e.Value, db[e.Key], $"{path}[{e.Key}]", missing);
            return;
        }
        if (a is IEnumerable ea && b is IEnumerable eb)
        {
            var la = ea.Cast<object?>().ToList();
            var lb = eb.Cast<object?>().ToList();
            if (la.Count != lb.Count) { missing.Add(path); return; }
            for (int i = 0; i < la.Count; i++) Compare(la[i], lb[i], $"{path}[{i}]", missing);
            return;
        }
        foreach (var p in Settable(t)) Compare(p.GetValue(a), p.GetValue(b), path + "." + p.Name, missing);
    }
}
