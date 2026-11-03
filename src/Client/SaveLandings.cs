namespace KTable.Client;

sealed class SaveLandings
{
    readonly Dictionary<string, long> _newest = new(StringComparer.OrdinalIgnoreCase);
    readonly object _gate = new();

    static string KeyOf(string path)
    {
        try { return Path.GetFullPath(path); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return path; }
    }

    public bool TryLand(string path, long number, Action land)
    {
        lock (_gate)
        {
            var key = KeyOf(path);
            if (_newest.TryGetValue(key, out var newest) && newest > number) return false;
            land();
            _newest[key] = number;
            return true;
        }
    }
}
