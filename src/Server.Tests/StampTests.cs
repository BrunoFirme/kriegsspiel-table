using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Server.Tests;

public class StampTests
{
    const string U = Layers.Umpire;

    static DecalDef Blood(double x) => new() { Picture = "blood", Name = "Blood", X = x, Z = 2, W = 1.5, H = 1, Rot = 30 };

    static void Ok(IEnumerable<Outgoing> res) => Assert.DoesNotContain(res, o => o.Message is ErrorMessage);

    [Fact]
    public void A_stamped_decal_lies_on_the_table_until_lifted_and_undo_brings_each_step_back()
    {
        using var t = new Table();
        Ok(t.Server.Handle(Table.Host, new StampDecal { Layer = U, Decal = Blood(1) }));
        Ok(t.Server.Handle(Table.Host, new StampDecal { Layer = U, Decal = Blood(4) }));
        var stamped = t.Server.State.DecalLayer(U);
        Assert.Equal((2, 1.0, 4.0), (stamped.Count, stamped[0].X, stamped[1].X));
        Assert.NotEqual(stamped[0].Id, stamped[1].Id);

        Ok(t.Server.Handle(Table.Host, new RemoveDecal { Layer = U, DecalId = stamped[0].Id! }));
        Assert.Equal(4.0, Assert.Single(t.Server.State.DecalLayer(U)).X);
        Ok(t.Server.Handle(Table.Host, new Undo { TargetSeq = t.Server.State.LastSeq }));
        Assert.Equal(new[] { 1.0, 4.0 }, t.Server.State.DecalLayer(U).Select(d => d.X));
    }

    [Fact]
    public void A_decal_without_a_picture_or_a_size_is_refused_and_the_palette_keeps_its_entries()
    {
        using var t = new Table();
        var bad = t.Server.Handle(Table.Host, new StampDecal { Layer = U, Decal = new DecalDef { Picture = "", W = 1, H = 1 } });
        Assert.Contains(bad, o => o.Message is ErrorMessage);
        var flat = t.Server.Handle(Table.Host, new StampDecal { Layer = U, Decal = new DecalDef { Picture = "blood", W = 0, H = 1 } });
        Assert.Contains(flat, o => o.Message is ErrorMessage);

        var palette = new List<PaletteDecal> { new() { Name = "Guts", Picture = "guts", Size = 0.5 }, new() { Name = "Arrow", Picture = "arrow", Size = 1 } };
        Ok(t.Server.Handle(Table.Host, new SetDecalPalette { Entries = palette }));
        Assert.Equal(new[] { "Guts", "Arrow" }, t.Server.State.DecalPalette.Select(d => d.Name));
        var tiny = t.Server.Handle(Table.Host, new SetDecalPalette { Entries = new() { new() { Name = "Dot", Picture = "dot", Size = 0 } } });
        Assert.Contains(tiny, o => o.Message is ErrorMessage);
        Assert.Equal(2, t.Server.State.DecalPalette.Count);
    }

    [Fact]
    public void Stamped_decals_and_the_palette_go_into_the_scenario_and_come_back_from_it()
    {
        using var t = new Table();
        Ok(t.Server.Handle(Table.Host, new StampDecal { Layer = U, Decal = Blood(3) }));
        Ok(t.Server.Handle(Table.Host, new SetDecalPalette { Entries = new() { new() { Name = "Guts", Picture = "guts", Size = 0.5 } } }));
        var back = GameState.FromScenario(Json.Deserialize<Scenario>(Json.Serialize(t.Server.State.ToScenario("s", "S"))));
        Assert.Equal((3.0, "Guts"), (Assert.Single(back.DecalLayer(U)).X, Assert.Single(back.DecalPalette).Name));
        Assert.True(back.NextId > 1);
    }
}
