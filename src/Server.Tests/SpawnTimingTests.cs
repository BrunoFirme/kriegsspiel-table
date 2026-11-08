using KTable.Core.Model;
using KTable.Core.Protocol;
using KTable.Server.Scripting.Translate;

namespace KTable.Server.Tests;

public class SpawnTimingTests
{
    const string U = Layers.Umpire;
    const string P = "p-000002";

    [Fact]
    public void A_spawned_piece_is_announced_after_the_script_that_spawned_it_has_finished_so_what_it_set_on_the_copy_is_already_there()
    {
        using var t = new Table();
        t.Store.RegisterDef(new PackManifest { Id = "testpack" }, new PieceDef { Id = "foot", Name = "Foot", ModuleType = "Custom_Token", Sources = new() { ["ImageURL"] = "https://example.test/foot.png" } });
        var r = ModuleTranslator.Translate("""
            function onLoad()
              if self.getDescription() ~= '' then return end
              self.setDescription('spawning')
              local copy = spawnObjectData({ data = { Name = 'Custom_Token', Nickname = 'copy', CustomImage = { ImageURL = 'https://example.test/foot.png' }, Transform = { posX = 2, posY = 1, posZ = 3 } } })
              copy.memo = '7'
              self.setDescription('spawned')
            end
            function onObjectSpawn(o)
              if o.getName() == 'copy' then self.setName('memo ' .. tostring(o.memo) .. ' after ' .. self.getDescription()) end
            end
            """, new TranslateContext());
        var answers = t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = P, Text = r.Script });
        Assert.DoesNotContain(answers, o => o.Message is ErrorMessage);
        Assert.Equal("memo 7 after spawned", t.Server.State.Find(U, P)!.Label);
    }

    [Fact]
    public void A_clone_made_without_a_position_appears_at_the_middle_of_the_table_and_a_piece_taken_out_by_its_guid_comes_out_unlocked()
    {
        using var t = new Table();
        var state = KTable.Core.Json.Clone(t.Server.State);
        var holder = state.Find(U, "p-000003")!;
        holder.Holds = true;
        holder.Stowed = [new PieceInstance { Id = "p-000090", Def = "marker", Guid = "bag001", Locked = true, Label = "kept" }];
        t.Server.LoadSave(state, new List<TurnRecord>(), "Loaded");
        var r = ModuleTranslator.Translate("""
            function onLoad()
              if self.getName() == 'the clone' or self.getDescription() ~= '' then return end
              self.setDescription('done')
              local c = self.clone()
              c.setName('the clone')
              getObjectFromGUID('p-000003').takeObject({ guid = 'bag001', position = { 6, 1, 6 } })
            end
            """, new TranslateContext());
        Assert.DoesNotContain(t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = P, Text = r.Script }), o => o.Message is ErrorMessage);
        Assert.Equal(new Vec2(0, 0), t.Server.State.Layer(U).Single(p => p.Label == "the clone").Pos);
        Assert.False(t.Server.State.Layer(U).Single(p => p.Label == "kept").Locked);
    }
}
