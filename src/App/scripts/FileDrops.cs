using System;
using System.Collections.Generic;
using Godot;

namespace KTable.App;

public static class FileDrops
{
    static readonly List<(Node Owner, Func<string[], bool> Take)> Takers = new();
    static Window? _window;

    public static void Install(Window root)
    {
        if (_window == root) return;
        if (_window != null && GodotObject.IsInstanceValid(_window)) _window.FilesDropped -= Dropped;
        _window = root;
        root.FilesDropped += Dropped;
    }

    static void Dropped(string[] files) => Route(files);

    public static void Listen(Node owner, Func<string[], bool> take)
    {
        Takers.Add((owner, take));
        owner.TreeExiting += () => Takers.RemoveAll(t => t.Owner == owner);
    }

    public static bool Route(string[] files)
    {
        Takers.RemoveAll(t => !GodotObject.IsInstanceValid(t.Owner));
        for (var i = Takers.Count - 1; i >= 0; i--)
        {
            var (owner, take) = Takers[i];
            if (!owner.IsInsideTree() || owner is Window { Visible: false }) continue;
            if (take(files)) return true;
        }
        return false;
    }
}
