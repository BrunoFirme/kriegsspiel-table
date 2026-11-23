using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;

namespace KTable.App;

public static class FileDrops
{
    public const string SharedKind = "Shared pack", ModuleKind = "Module", SheetKind = "Map sheet", ReliefKind = "Relief",
        PictureKind = "Picture", ModelKind = "Model", FileKind = "File";
    const int LongestName = 48;

    static readonly List<(Node Owner, Func<string[], bool> Take)> Takers = new();
    static Window? _window;
    static DropFrame? _frame;

    public sealed class Hold
    {
        readonly DropFrame? _frame;
        bool _done;

        internal Hold(Node owner, DropFrame? frame)
        {
            _frame = frame;
            owner.TreeExiting += Done;
        }

        public void Done()
        {
            if (_done) return;
            _done = true;
            if (_frame != null && GodotObject.IsInstanceValid(_frame)) _frame.Release();
        }
    }

    public static void Install(Window root)
    {
        if (_window == root) return;
        if (_window != null && GodotObject.IsInstanceValid(_window)) _window.FilesDropped -= Dropped;
        _window = root;
        root.FilesDropped += Dropped;
    }

    internal static DropFrame? Shown => _frame != null && GodotObject.IsInstanceValid(_frame) && _frame.IsInsideTree() ? _frame : null;

    static DropFrame? Frame()
    {
        if (Shown is { } shown) return shown;
        var host = _window != null && GodotObject.IsInstanceValid(_window) ? _window : (Engine.GetMainLoop() as SceneTree)?.Root;
        if (host == null) return null;
        _frame = DropFrame.On(host);
        return _frame;
    }

    static void Dropped(string[] files) => Arrive(files);

    internal static void Arrive(string[] files)
    {
        if (Frame() is not { } frame) { Route(files); return; }
        frame.Arrive(Label(Guess(files), files), () => frame.Taken(Route(files)));
    }

    public static Hold Taking(Node owner, string kind, IReadOnlyList<string> files)
    {
        var frame = Frame();
        frame?.Keep(Label(kind, files));
        return new Hold(owner, frame);
    }

    public static string Guess(IReadOnlyList<string> files)
    {
        if (files.Any(ImportDoor.IsShared)) return SharedKind;
        if (files.Any(ImportDoor.IsModule)) return ModuleKind;
        if (files.Any(ImageFiles.IsImage)) return PictureKind;
        if (files.Any(ModelImport.IsModel)) return ModelKind;
        if (files.Any(ReliefImport.IsRelief)) return ReliefKind;
        return FileKind;
    }

    public static string Label(string kind, IReadOnlyList<string> files)
    {
        if (files.Count != 1) return $"{files.Count} {char.ToLowerInvariant(kind[0])}{kind[1..]}s";
        var name = Path.GetFileName(files[0].Trim());
        if (name.Length > LongestName) name = name[..(LongestName - 1)] + "…";
        return $"{kind} · {name}";
    }

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
