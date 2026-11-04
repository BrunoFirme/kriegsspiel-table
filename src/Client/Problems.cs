namespace KTable.Client;

public static class Problems
{
    static readonly List<string> _waiting = new();
    static readonly HashSet<string> _toldOnce = new();
    public static event Action<string>? Told;
    public static Action<string>? Echo { get; set; }

    public static void Tell(string text)
    {
        Echo?.Invoke(text);
        var told = Told;
        if (told != null) { told(text); return; }
        lock (_waiting) _waiting.Add(text);
    }

    public static void TellOnce(string key, string text)
    {
        lock (_toldOnce) if (!_toldOnce.Add(key)) return;
        Tell(text);
    }

    public static List<string> Take()
    {
        lock (_waiting)
        {
            var l = new List<string>(_waiting);
            _waiting.Clear();
            return l;
        }
    }
}
