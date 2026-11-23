using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Client;
using KTable.Core.Model;
using KTable.Core.Protocol;
using Json = KTable.Core.Json;

namespace KTable.App;

sealed class GroundLayerOps
{
    readonly Session _s;
    readonly Net _n;
    readonly Action<string> _say;

    public GroundLayerOps(Session s, Net n, Action<string> say)
    {
        _s = s;
        _n = n;
        _say = say;
    }

    public void Move(string id, int to)
    {
        var order = _s.View.GroundLayers.Select(l => l.Id).Where(x => x != id).ToList();
        order.Insert(Math.Clamp(to, 0, order.Count), id);
        Restack(new MoveGroundLayer { Id = id, Index = to }, order);
    }

    public void Remove(string id) => Restack(new RemoveGroundLayer { Id = id }, _s.View.GroundLayers.Select(l => l.Id).Where(x => x != id).ToList());

    public void Clear(string id) => Restack(null, _s.Relief.Order, id);

    public int Painted(string id) => _s.View.Sheets.Count(sheet => sheet.Relief is { Length: > 0 } && _s.Relief.Under(sheet)?.PaintedOn(id) == true);

    void Restack(Command? edit, IReadOnlyList<string> order, string? clear = null)
    {
        var sheets = _s.View.Sheets.Where(sheet => sheet.Relief is { Length: > 0 }).Select(sheet => (Def: Json.Clone(sheet), Grid: _s.Relief.Under(sheet))).ToList();
        if (sheets.Any(x => x.Grid == null))
        {
            _say("The relief is still loading.");
            return;
        }
        var root = _s.Store.Root;
        string fold = _s.Relief.Layer;
        Task.Run(() =>
        {
            try
            {
                var changed = new List<(SheetDef Def, string Sha, ReliefGrid Grid)>();
                foreach (var (def, grid) in sheets)
                {
                    var g = grid!.Copy();
                    if (clear != null) Bare(g.Cells(clear));
                    g.Settle(order, fold);
                    var (sha, _) = ReliefImport.Save(g, root);
                    if (sha != def.Relief) changed.Add((def, sha, g));
                }
                Callable.From(() => Send(edit, changed)).CallDeferred();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                var why = e.Message;
                Callable.From(() => _say("The ground could not be saved: " + why)).CallDeferred();
            }
        });
    }

    static void Bare(GroundCells cells)
    {
        Array.Clear(cells.Kind);
        Array.Clear(cells.Salt);
        Array.Clear(cells.Detail);
    }

    void Send(Command? edit, List<(SheetDef Def, string Sha, ReliefGrid Grid)> changed)
    {
        if (changed.Any(x => _s.View.Sheets.FirstOrDefault(sheet => sheet.Id == x.Def.Id)?.Relief != x.Def.Relief))
        {
            _say("The ground changed meanwhile; try again.");
            return;
        }
        var batch = new Batch();
        if (edit != null) batch.Items.Add(edit);
        foreach (var (def, sha, grid) in changed)
        {
            _s.Relief.Remember(sha, grid);
            batch.Items.Add(new SetSheetRelief { Id = def.Id, Relief = sha, Height = ReliefTool.HeightUnit(def), Base = def.Relief });
        }
        if (batch.Items.Count == 0) return;
        if (changed.Count == 0)
        {
            _s.Do(edit!);
            return;
        }
        var dir = Path.Combine(_s.Store.Root, UploadAsset.ReliefPackage);
        var files = new List<string>();
        try
        {
            foreach (var (_, sha, _) in changed) files.AddRange(ReliefFiles.Files(sha, ReliefFiles.LoadIndex(Path.Combine(dir, ReliefFiles.HeaderRel(sha)))));
        }
        catch (Exception e) when (e is IOException or InvalidDataException or System.Text.Json.JsonException)
        {
            _say("The ground could not be read back: " + e.Message);
            return;
        }
        _n.ShareThen(files.Distinct(), batch);
    }
}
