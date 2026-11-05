using KTable.Core;
using KTable.Core.Model;

namespace KTable.Client;

public static class Clippings
{
    public static Func<string> FileAt { get; set; } = () => Path.Combine(DataPaths.Local, "clipboard.json");

    static Clipping? _held;

    public static Clipping? Held
    {
        get
        {
            var path = BestEffort.Read(FileAt, "");
            if (path.Length == 0 || !File.Exists(path)) return _held;
            return BestEffort.Read(() => Json.Load<Clipping>(path), null) is { Format: Clipping.CurrentFormat } onFile ? onFile : _held;
        }
    }

    public static void Hold(Clipping clip)
    {
        _held = clip;
        BestEffort.Try(() => Json.Save(FileAt(), clip));
    }
}
