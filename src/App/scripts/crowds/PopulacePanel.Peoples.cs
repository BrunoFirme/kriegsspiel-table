using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;
using Json = KTable.Core.Json;

namespace KTable.App;

sealed partial class PopulacePanel
{
    const int PreviewSide = 150, TreeHigh = 300;

    string? _peopleId, _packId;
    Nationality? _draft;
    int _sample = 1;
    RecordTree? _tree;

    List<PackManifest> TablePacks => S.View.Packs.Select(p => S.Store.Pack(p)).OfType<PackManifest>().ToList();

    List<PackManifest> OwnPacks => TablePacks.Where(p => !StandardPacks.IsStandard(p.Id)).ToList();

    void Peoples()
    {
        var known = Nationalities.Offered(TablePacks).ToList();
        if (known.Count == 0)
        {
            _page.AddChild(Hint("No nationalities"));
            return;
        }
        if (_peopleId == null || known.All(n => n.Id != _peopleId)) Choose(known.FirstOrDefault(n => S.View.Crowds?.Peoples.ContainsKey(n.Id) == true)?.Id ?? known[0].Id);
        var own = OwnPacks;
        _packId = own.Any(p => p.Id == _packId) ? _packId : own.FirstOrDefault(p => p.Nationalities?.Any(n => n.Id == _peopleId) == true)?.Id ?? own.FirstOrDefault()?.Id;
        var form = Ui.Form(Ui.PanelLabels);
        form.AddChild(new Label { Text = "Nationality" });
        form.AddChild(Ui.Picker(known.Select(n => (n.Id, n.Name ?? n.Id, n.Id, (Texture2D?)null)).ToList(), _peopleId!, Pick));
        form.AddChild(new Label { Text = "Pack" });
        var packs = Ui.Picker(own.Select(p => (p.Id, p.Name, p.Id, (Texture2D?)null)).ToList(), _packId ?? "", id => _packId = id);
        packs.Disabled = !S.IsHost || own.Count == 0;
        packs.TooltipText = !S.IsHost ? "Only the host keeps it in a pack; Keep sets it on this table" : own.Count == 0 ? "The table has no pack of its own" : "Where Keep writes it";
        form.AddChild(packs);
        Ui.AlignForm(form);
        _page.AddChild(form);
        _page.AddChild(Preview());
        _tree = new RecordTree { Hidden = { nameof(Nationality.Id) } };
        _tree.View.CustomMinimumSize = Ui.Px(0, TreeHigh);
        _tree.Show(_draft!);
        _page.AddChild(_tree.View);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Tight));
        row.AddChild(Ui.Primary("Keep", Keep, "Sets it on this table, and the host writes it into the pack"));
        row.AddChild(Btn("Revert", () => Pick(_peopleId!), "Back to the record as kept"));
        _page.AddChild(row);
    }

    void Pick(string id)
    {
        Choose(id);
        Rebuild();
    }

    void Choose(string id)
    {
        _peopleId = id;
        var flat = S.View.Crowds?.Peoples.GetValueOrDefault(id);
        var source = OwnPacks.SelectMany(p => p.Nationalities ?? []).FirstOrDefault(n => n.Id == id) ?? Nationalities.Offered(TablePacks).FirstOrDefault(n => n.Id == id);
        _draft = Json.Clone(source ?? flat ?? new Nationality { Id = id });
    }

    Control Preview()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Group));
        var turntable = new FigureTurntable(Ui.Px(PreviewSide, PreviewSide));
        row.AddChild(turntable);
        if (Sample() is { } look) turntable.Show(look);
        var another = Btn("Another", Another, "Another of its people");
        another.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        row.AddChild(another);
        return row;
    }

    void Another()
    {
        _sample++;
        Rebuild();
    }

    CrowdLook? Sample()
    {
        if (_draft == null) return null;
        try
        {
            var flat = CrowdDress.Flat(_draft, Nationalities.Of(TablePacks).ToList());
            var callings = flat.Callings?.Keys.ToList() ?? [];
            var entry = new CrowdEntry
            {
                Seed = _sample * 7919, Nationality = flat.Id, Sex = _sample % 2 == 0 ? CrowdEntry.Woman : CrowdEntry.Man, Band = CrowdEntry.Adult,
                Calling = callings.Count > 0 ? callings[_sample % callings.Count] : "", Wealth = CrowdEntry.Middle,
            };
            return CrowdDress.Dress(entry, flat, CrowdDress.ArmyCoat(flat, _sample));
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or KeyNotFoundException)
        {
            Say("It does not dress yet: " + e.Message);
            return null;
        }
    }

    void Keep()
    {
        if (_draft == null || _peopleId == null) return;
        var n = Json.Clone(_draft);
        n.Id = _peopleId;
        if (CrowdDress.Problems(n) is { Count: > 0 } problems)
        {
            Say("It does not hold together: " + string.Join("; ", problems));
            return;
        }
        if (S.IsHost && OwnPacks.FirstOrDefault(p => p.Id == _packId) is { } pack && !InPack(pack, n)) return;
        S.Do(new SetPeoples { Peoples = { [n.Id] = CrowdDress.Flat(n, Nationalities.Of(TablePacks).ToList()) } });
        Say($"{n.Name ?? n.Id} kept");
    }

    bool InPack(PackManifest pack, Nationality n)
    {
        StudioNationalities.Put(pack, Json.Clone(n));
        try
        {
            Json.SaveBig(PackEdit.ManifestPath(S.Store, pack.Id), pack);
            return true;
        }
        catch (System.IO.IOException e)
        {
            Say("The pack could not be written: " + e.Message);
            return false;
        }
    }
}
