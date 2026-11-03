using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Core.Tests;

public class SavedShapeTests
{
    static readonly Type[] Roots =
    {
        typeof(GameState), typeof(SaveFile), typeof(TurnRecord), typeof(Command), typeof(Message),
        typeof(PackManifest), typeof(PieceDef), typeof(Scenario),
    };

    static string SnapshotPath => Path.Combine(SourceDir(), "Snapshots", "save-shape.txt");

    static string SourceDir()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "Core.Tests.csproj"))) return d.FullName;
        throw new DirectoryNotFoundException("Core.Tests.csproj not found above " + AppContext.BaseDirectory);
    }

    [Fact]
    public void The_saved_shape_changes_only_with_the_save_format()
    {
        var shape = Shape();
        if (!File.Exists(SnapshotPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SnapshotPath)!);
            File.WriteAllText(SnapshotPath, $"format {SaveFile.CurrentFormat}\n" + string.Join("\n", shape) + "\n");
            Assert.Fail($"snapshot written, review and commit: {SnapshotPath}");
        }
        var stored = File.ReadAllText(SnapshotPath).Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
        Assert.True(stored.Count > 0 && stored[0].StartsWith("format "), "the snapshot has no format line: delete it and run again");
        int format = int.Parse(stored[0]["format ".Length..]);
        var was = stored.Skip(1).ToList();
        Assert.True(format <= SaveFile.CurrentFormat, $"the snapshot was taken at format {format} but SaveFile.CurrentFormat is {SaveFile.CurrentFormat}: the format went backwards");
        Assert.True(format == SaveFile.CurrentFormat,
            $"SaveFile.CurrentFormat is {SaveFile.CurrentFormat} and the snapshot is of format {format}: re-take the snapshot (delete {SnapshotPath} and run again), and add the old-save fixture for format {SaveFile.CurrentFormat} under Fixtures/formats/{SaveFile.CurrentFormat}/");
        var removed = was.Except(shape).ToList();
        var added = shape.Except(was).ToList();
        Assert.True(removed.Count == 0 && added.Count == 0,
            $"the shape of what is saved or sent changed, and SaveFile.CurrentFormat is still {format}.\n"
            + string.Concat(removed.Select(l => "- " + l + "\n")) + string.Concat(added.Select(l => "+ " + l + "\n"))
            + $"Bump SaveFile.CurrentFormat to {format + 1} (and add a migration if old saves need one), add an old-save fixture for the new format under "
            + $"Fixtures/formats/{format + 1}/, then delete {SnapshotPath} to re-take it.");
    }

    [Fact]
    public void The_shape_reaches_every_command_and_message()
    {
        var shape = Shape();
        foreach (var a in typeof(Command).GetCustomAttributes<JsonDerivedTypeAttribute>())
            Assert.Contains($"  derived {a.TypeDiscriminator} = {a.DerivedType.Name}", shape);
        foreach (var a in typeof(Message).GetCustomAttributes<JsonDerivedTypeAttribute>())
            Assert.Contains($"  derived {a.TypeDiscriminator} = {a.DerivedType.Name}", shape);
        Assert.Contains("type Wind object", shape);
    }

    static JsonSerializerContext Context() =>
        (JsonSerializerContext)(typeof(Json).GetField("Ctx", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null)
            ?? throw new InvalidOperationException("Json.Ctx is gone: point this test at the serializer the app uses"));

    internal static List<string> Shape()
    {
        var ctx = Context();
        var seen = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        var queue = new Queue<Type>(Roots);
        while (queue.Count > 0)
        {
            var t = queue.Dequeue();
            t = Nullable.GetUnderlyingType(t) ?? t;
            var name = Name(t);
            if (seen.ContainsKey(name)) continue;
            var lines = new List<string>();
            seen[name] = lines;
            if (t.IsEnum)
            {
                lines.Add($"type {name} enum {string.Join(",", Enum.GetNames(t))}");
                continue;
            }
            var info = ctx.GetTypeInfo(t) ?? throw new InvalidOperationException($"{name} reaches the JSON but the context has no metadata for it");
            switch (info.Kind)
            {
                case JsonTypeInfoKind.None:
                    lines.Add($"type {name} value");
                    break;
                case JsonTypeInfoKind.Enumerable:
                {
                    var el = t.IsArray ? t.GetElementType()! : GenericOf(t, typeof(IEnumerable<>))[0];
                    lines.Add($"type {name} list of {Name(el)}");
                    queue.Enqueue(el);
                    break;
                }
                case JsonTypeInfoKind.Dictionary:
                {
                    var kv = GenericOf(t, typeof(IDictionary<,>));
                    lines.Add($"type {name} map {Name(kv[0])} to {Name(kv[1])}");
                    queue.Enqueue(kv[0]);
                    queue.Enqueue(kv[1]);
                    break;
                }
                case JsonTypeInfoKind.Object:
                    lines.Add($"type {name} {(t.IsAbstract ? "abstract" : "object")}");
                    if (info.PolymorphismOptions is { } poly)
                    {
                        lines.Add($"  discriminator {poly.TypeDiscriminatorPropertyName} unknown={poly.UnknownDerivedTypeHandling}");
                        foreach (var d in poly.DerivedTypes.OrderBy(d => d.TypeDiscriminator?.ToString(), StringComparer.Ordinal))
                        {
                            lines.Add($"  derived {d.TypeDiscriminator} = {Name(d.DerivedType)}");
                            queue.Enqueue(d.DerivedType);
                        }
                    }
                    foreach (var p in info.Properties.OrderBy(p => p.Name, StringComparer.Ordinal))
                    {
                        if (p.Get == null && p.Set == null) continue;
                        var flags = (p.Set == null ? " read-only" : "") + (p.IsExtensionData ? " extension-data" : "") + (p.IsRequired ? " required" : "");
                        lines.Add($"  {p.Name}: {Name(p.PropertyType)}{flags}");
                        queue.Enqueue(p.PropertyType);
                    }
                    break;
                default:
                    lines.Add($"type {name} {info.Kind}");
                    break;
            }
        }
        return seen.Values.SelectMany(l => l).ToList();
    }

    static Type[] GenericOf(Type t, Type open) =>
        t.GetInterfaces().Append(t).First(i => i.IsGenericType && i.GetGenericTypeDefinition() == open).GetGenericArguments();

    static string Name(Type t)
    {
        if (Nullable.GetUnderlyingType(t) is { } u) return Name(u) + "?";
        if (t.IsArray) return Name(t.GetElementType()!) + "[]";
        if (t == typeof(string)) return "string";
        if (t == typeof(int)) return "int";
        if (t == typeof(long)) return "long";
        if (t == typeof(double)) return "double";
        if (t == typeof(bool)) return "bool";
        if (t == typeof(byte)) return "byte";
        if (t.IsGenericType)
        {
            var bare = t.Name[..t.Name.IndexOf('`')];
            return $"{bare}<{string.Join(",", t.GetGenericArguments().Select(Name))}>";
        }
        return t.IsNested ? $"{t.DeclaringType!.Name}.{t.Name}" : t.Name;
    }
}
