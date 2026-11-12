using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Client;
using KTable.Core.Model;
using KTable.Core.Protocol;
using Json = KTable.Core.Json;

namespace KTable.App;

sealed class PaintConverter
{
    readonly TableContext _c;
    readonly ReliefTool _relief;
    readonly HashSet<string> _looked = new();

    public PaintConverter(TableContext c, ReliefTool relief)
    {
        _c = c;
        _relief = relief;
    }

    public void Look()
    {
        var s = _c.S;
        if (!s.IsHost || s.Relief.Drafting || !s.CanEdit(Layers.Umpire) || !ReliefTool.Scaled(s)) return;
        foreach (var sheet in s.View.Sheets.ToList())
        {
            if (sheet.Relief is not { Length: > 0 } sha || s.Relief.Grid(sheet) is not { } grid || !_looked.Add(sha)) continue;
            if (BuiltFromPaint.Needed(grid)) Convert(s, Json.Clone(sheet), grid);
        }
    }

    void Convert(Session s, SheetDef sheet, ReliefGrid grid)
    {
        var site = BuildTool.Site(s, sheet);
        var root = s.Store.Root;
        Task.Run(() =>
        {
            try
            {
                var (things, ground) = BuiltFromPaint.Convert(grid, site, sheet.Id);
                var (sha, _) = ReliefImport.Save(ground, root);
                Callable.From(() => Lay(s, sheet, things, ground, sha)).CallDeferred();
            }
            catch (Exception e)
            {
                var why = e.Message;
                Callable.From(() => GD.PrintErr($"[built] converting the painted roads and towns of {sheet.Name}: {why}")).CallDeferred();
            }
        });
    }

    void Lay(Session s, SheetDef sheet, List<BuiltThing> things, ReliefGrid ground, string sha)
    {
        if (s != _c.S || s.View.Sheets.FirstOrDefault(x => x.Id == sheet.Id)?.Relief != sheet.Relief) return;
        s.Relief.Remember(sha, ground);
        _looked.Add(sha);
        var batch = new Batch();
        foreach (var thing in things) batch.Items.Add(new LayBuilt { Thing = thing });
        batch.Items.Add(new SetSheetRelief { Id = sheet.Id, Relief = sha, Height = ReliefTool.HeightUnit(sheet) });
        _relief.Share(sha, batch);
        _c.Say($"What was painted as roads, towns and farms on {sheet.Name} is built now");
    }
}
