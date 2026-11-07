using KTable.Core.Panels;

namespace KTable.Client.Panels;

public static class PanelClasses
{
    public const string Prefix = "#class:", Hover = "hoverclass", Press = "pressclass", Select = "selectclass";
    const char Add = '+', Remove = '-';

    public static List<string> Of(PanelBox b, string kind) => (b.Extra.GetValueOrDefault(kind) ?? "").Split(',', ' ')
        .Select(s => s.Trim().ToLowerInvariant()).Where(c => c.Length > 0).ToList();

    public static bool Change(Dictionary<string, string> local, PanelBox b, string kind, bool on)
    {
        var classes = Of(b, kind);
        if (b.Path == null || classes.Count == 0) return false;
        var key = Prefix + PanelScene.Key(b.Path);
        var ops = local.GetValueOrDefault(key) is { Length: > 0 } s ? s.Split(' ').ToList() : new List<string>();
        foreach (var c in classes) ops.Add((on ? Add : Remove) + c);
        local[key] = string.Join(" ", ops);
        return true;
    }

    public static UiLayout Apply(UiLayout layout, Dictionary<string, string> local)
    {
        var keys = local.Keys.Where(k => k.StartsWith(Prefix, StringComparison.Ordinal)).ToList();
        if (keys.Count == 0) return layout;
        var copy = layout.Clone();
        foreach (var key in keys)
        {
            var path = key[Prefix.Length..].Split('.').Select(p => int.TryParse(p, out var i) ? i : -1).ToList();
            if (path.Any(i => i < 0) || copy.At(path) is not { } e)
            {
                local.Remove(key);
                continue;
            }
            var before = e.Classes.ToList();
            foreach (var op in local[key].Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (op[0] == Add) PanelOps.AddClass(copy, path, op[1..]);
                else PanelOps.RemoveClass(copy, path, op[1..]);
            }
            var after = copy.At(path)!.Classes;
            var canon = after.Where(c => !before.Contains(c)).Select(c => Add + c).Concat(before.Where(c => !after.Contains(c)).Select(c => Remove + c)).ToList();
            if (canon.Count == 0) local.Remove(key);
            else local[key] = string.Join(" ", canon);
        }
        return copy;
    }
}
