using System;
using System.Collections.Generic;
using System.Linq;
using KTable.Client;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed class BuildZone
{
    internal const string NotLead = "Only the host and the lead umpires set what a place is.";

    readonly TableContext _c;
    readonly BuildTool _tool;

    public BuildZone(TableContext c, BuildTool tool)
    {
        _c = c;
        _tool = tool;
    }

    public bool May => _c.S.IsLead && !_c.S.Reviewing;

    bool Allowed()
    {
        if (May) return true;
        _c.Say(NotLead);
        return false;
    }

    BuiltThing? Town => _tool.Picked is { Kind: BuiltKinds.Settlement } town && !BuiltThings.Fixed(town) ? town : null;

    SheetDef? SheetOf(BuiltThing thing) => _c.S.View.Sheets.FirstOrDefault(s => s.Id == thing.Sheet);

    public IReadOnlyList<int> Buildings => _tool.Picked is { } thing ? _tool.PickedParts.Where(i => Looks.Takes(thing.Parts[i])).ToList() : [];

    void Do(BuiltThing changed) => _c.S.Do(new SetBuilt { Thing = changed });

    public void LivesOn(double share)
    {
        if (Town != null && Allowed()) _tool.Reset(Settlements.Farming, Math.Clamp(share, 0, 1));
    }

    public void War(bool on)
    {
        if (Town is not { } town || !Allowed() || town.Setting(PlaceZones.War, 0) >= 1 == on) return;
        Do(ZoneOverrides.WithWar(town, on));
    }

    public void Holder(int culture, double? coat)
    {
        if (Town is not { } town || !Allowed()) return;
        var held = ZoneOverrides.WithHolder(town, culture, coat);
        if (ZoneOverrides.Introduced(town, held) || ZoneOverrides.Introduced(held, town)) Do(held);
    }

    public void Culture(string? culture)
    {
        if (_tool.Picked is not { } thing || SheetOf(thing) is not { } sheet || Buildings is not { Count: > 0 } parts || !Allowed()) return;
        var set = ZoneOverrides.WithCulture(thing, sheet, parts, culture);
        if (parts.Any(i => set.Parts[i].Culture != thing.Parts[i].Culture || !thing.Parts[i].Kept)) Do(set);
    }

    public void Wealth(WealthLevel level)
    {
        if (_tool.Picked is not { } thing || SheetOf(thing) is not { } sheet || Buildings is not { Count: > 0 } parts || !Allowed()) return;
        var set = ZoneOverrides.WithWealth(thing, parts, level, BuildTool.Site(_c.S, sheet, thing.Id));
        if (parts.Any(i => set.Parts[i].Wealth != thing.Parts[i].Wealth || !thing.Parts[i].Kept)) Do(set);
    }

    public void Brush(List<Vec2>? stroke, double radius, bool richer)
    {
        if (stroke == null || !Allowed()) return;
        var batch = new Batch();
        foreach (var thing in _c.S.View.Built.Where(b => !BuiltThings.Fixed(b)))
        {
            if (SheetOf(thing) is not { } sheet) continue;
            if (ZoneOverrides.Brushed(thing, stroke.Select(p => Spot.Of(BuiltGeometry.ToSheet(sheet, p))).ToList(), radius, richer, BuildTool.Site(_c.S, sheet, thing.Id)) is { } brushed)
                batch.Items.Add(new SetBuilt { Thing = brushed });
        }
        if (batch.Items.Count == 1) _c.S.Do(batch.Items[0]);
        else if (batch.Items.Count > 1) _c.S.Do(batch);
    }
}
