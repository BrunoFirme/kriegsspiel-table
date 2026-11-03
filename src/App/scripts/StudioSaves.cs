using System;
using Godot;
using KTable.Client.Studio;
using KTable.Core;
using KTable.Core.Model;
using Json = KTable.Core.Json;

namespace KTable.App;

internal sealed class PackSaver
{
    readonly PackageStore _store;
    readonly PackManifest _pack;
    readonly Action<string> _failed;
    readonly Debounce _due = new();

    public string? Problem { get; private set; }

    public PackSaver(PackageStore store, PackManifest pack, Action<string> failed)
    {
        _store = store; _pack = pack; _failed = failed;
    }

    public bool Pending => _due.Pending;

    public void Soon() => _due.Arm(0.5, 3);

    public void Tick(double delta)
    {
        if (_due.Tick(delta)) Flush();
    }

    public bool Flush()
    {
        _due.Clear();
        try { Json.SaveBig(PackEdit.ManifestPath(_store, _pack.Id), _pack); Problem = null; return true; }
        catch (Exception e)
        {
            Problem = e.Message;
            GD.PrintErr("[studio] could not save pack.json: " + e.Message);
            _failed(e.Message);
            _due.In = 3;
            return false;
        }
    }
}
