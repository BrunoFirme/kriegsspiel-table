using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KTable.Client;

public static class InUse
{
    static readonly object Gate = new();
    static readonly Dictionary<string, (string doing, int count)> Opened = new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, string> Replacing = new(StringComparer.OrdinalIgnoreCase);

    public static string Scenario(string packagesRoot, string dir) => Norm(Path.Combine(packagesRoot, "scenarios", dir));
    public static string Pack(string packagesRoot, string id) => Norm(Path.Combine(packagesRoot, id));
    static string Norm(string p) => Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    public static string? TryOpen(string folder, string doing)
    {
        folder = Norm(folder);
        lock (Gate)
        {
            if (Replacing.TryGetValue(folder, out var what)) return what;
            Opened[folder] = (doing, Opened.TryGetValue(folder, out var o) ? o.count + 1 : 1);
            return null;
        }
    }

    public static void Close(string folder)
    {
        folder = Norm(folder);
        lock (Gate)
            if (Opened.TryGetValue(folder, out var o)) { if (o.count <= 1) Opened.Remove(folder); else Opened[folder] = (o.doing, o.count - 1); }
    }

    public static string? BeingReplaced(string folder) { lock (Gate) return Replacing.TryGetValue(Norm(folder), out var what) ? what : null; }

    public static Hold Claim(IEnumerable<(string folder, string name)> folders, string what)
    {
        var list = folders.Select(f => (folder: Norm(f.folder), f.name)).DistinctBy(f => f.folder, StringComparer.OrdinalIgnoreCase).ToList();
        lock (Gate)
        {
            foreach (var (folder, name) in list)
            {
                var named = char.ToUpperInvariant(name[0]) + name[1..];
                if (Opened.TryGetValue(folder, out var o)) throw new InUseException($"{named} {o.doing}.");
                if (Replacing.TryGetValue(folder, out var other)) throw new InUseException($"{named} is {other} right now: try again once that has finished.");
            }
            foreach (var (folder, _) in list) Replacing[folder] = what;
        }
        return new Hold(list.Select(x => x.folder).ToList());
    }

    public sealed class Hold : IDisposable
    {
        List<string>? _folders;
        internal Hold(List<string> folders) { _folders = folders; }

        public void Dispose()
        {
            var folders = System.Threading.Interlocked.Exchange(ref _folders, null);
            if (folders == null) return;
            lock (Gate) foreach (var f in folders) Replacing.Remove(f);
        }
    }
}

public sealed class InUseException : IOException
{
    public InUseException(string message) : base(message) { }
}
