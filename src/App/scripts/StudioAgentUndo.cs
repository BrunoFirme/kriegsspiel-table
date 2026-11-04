using System.Collections.Generic;
using System.Linq;
using KTable.Core;
using KTable.Core.Model;

namespace KTable.App;

sealed class StudioAgentUndo
{
    const int Keep = 60;

    enum Was { Edited, Made, Deleted }

    sealed record Entry(string What, Was How, string DefId, string? Def, Dictionary<string, AssetEntry> Assets, int Index);

    readonly List<Entry> _done = new();

    public int Count => _done.Count;

    public void Clear() => _done.Clear();

    public void Edited(PackManifest pack, PieceDef d, string what) => Push(new Entry(what, Was.Edited, d.Id, Json.Serialize(d), AssetsOf(pack, d), pack.Pieces.IndexOf(d)));

    public void Made(PieceDef d, string what) => Push(new Entry(what, Was.Made, d.Id, null, new(), -1));

    public void Deleted(PackManifest pack, PieceDef d, string what) => Push(new Entry(what, Was.Deleted, d.Id, Json.Serialize(d), AssetsOf(pack, d), pack.Pieces.IndexOf(d)));

    public bool DropIfSame(PieceDef d)
    {
        if (_done.Count == 0 || _done[^1] is not { How: Was.Edited } e || e.DefId != d.Id || e.Def != Json.Serialize(d)) return false;
        _done.RemoveAt(_done.Count - 1);
        return true;
    }

    void Push(Entry e)
    {
        _done.Add(e);
        if (_done.Count > Keep) _done.RemoveAt(0);
    }

    static Dictionary<string, AssetEntry> AssetsOf(PackManifest pack, PieceDef d) =>
        PackEdit.AssetsOf(d).Distinct().Where(pack.Assets.ContainsKey).ToDictionary(h => h, h => pack.Assets[h]);

    public string? Undo(PieceStudio s)
    {
        if (_done.Count == 0) return null;
        var e = _done[^1];
        _done.RemoveAt(_done.Count - 1);
        var pack = s.Pack;
        foreach (var (hash, entry) in e.Assets) pack.Assets.TryAdd(hash, entry);
        switch (e.How)
        {
            case Was.Made:
                if (pack.Pieces.FirstOrDefault(p => p.Id == e.DefId) is { } made) s.DeleteNow(made);
                break;
            case Was.Deleted:
            {
                var back = Json.Deserialize<PieceDef>(e.Def!);
                pack.Pieces.Insert(System.Math.Clamp(e.Index, 0, pack.Pieces.Count), back);
                s.Store.RegisterDef(pack, back);
                s.SavePack();
                s.RebuildList();
                s.Paper.LayOutAll();
                break;
            }
            case Was.Edited:
            {
                if (pack.Pieces.FirstOrDefault(p => p.Id == e.DefId) is not { } now) return $"{e.What}: that kind is gone, nothing to put back";
                PieceHistory.Wear(s, now, Json.Deserialize<PieceDef>(e.Def!));
                break;
            }
        }
        return e.What;
    }
}
