using KTable.Core.Model;
using KTable.Core.Protocol;
using KTable.Server.Scripting.Translate;

namespace KTable.Server.Tests;

public class ScriptObjectLifeTests
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
    public void A_piece_given_a_new_look_and_reloaded_becomes_that_kind_keeps_its_own_script_and_is_destroyed_and_spawned_again()
    {
        using var t = new Table();
        t.Store.RegisterDef(new PackManifest { Id = "testpack" }, new PieceDef { Id = "column", Name = "Column", ModuleType = "Custom_Token" });
        var r = ModuleTranslator.Translate("""
            function onLoad()
              print('loaded ' .. self.getDescription())
              if self.getDescription() ~= '' then return end
              self.setDescription('swapped')
              self.setCustomObject({ image = 'https://example.test/column.png' })
              self.reload()
            end
            function onDestroy() print('destroyed') end
            function onObjectSpawn(o) print('spawned') end
            """, new TranslateContext { ResolveLook = _ => "column" });
        Assert.DoesNotContain(t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = P, Text = r.Script }), o => o.Message is ErrorMessage);
        var p = t.Server.State.Find(U, P)!;
        Assert.Equal("column", p.Def);
        Assert.NotNull(p.Script);
        Assert.Equal(["loaded ", "destroyed", "loaded swapped", "spawned"], t.Server.Scripts.Console.Where(l => l is "destroyed" or "spawned" || l.StartsWith("loaded", StringComparison.Ordinal)));
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

    [Fact]
    public void A_bag_lists_and_counts_only_what_lies_loose_in_it_each_with_its_own_notes_memo_and_tags()
    {
        using var t = new Table();
        var state = KTable.Core.Json.Clone(t.Server.State);
        var holder = state.Find(U, "p-000003")!;
        holder.Holds = true;
        holder.Stowed =
        [
            new PieceInstance { Id = "p-000091", Def = "marker", Guid = "bat001", Label = "Battalion", Note = "1st", Memo = "m", Tags = ["foot"], Attached = ["p-000092"] },
            new PieceInstance { Id = "p-000092", Def = "marker", Guid = "flg001", Label = "Flag" },
            new PieceInstance { Id = "p-000093", Def = "marker", Guid = "gun001", Label = "Gun" },
        ];
        t.Server.LoadSave(state, new List<TurnRecord>(), "Loaded");
        var r = ModuleTranslator.Translate("""
            function onLoad()
              local bag = getObjectFromGUID('p-000003')
              local o = bag.getObjects()
              self.setName(#o .. ' ' .. bag.getQuantity() .. ' ' .. o[1].description .. o[1].memo .. o[1].tags[1] .. ' ' .. o[2].name)
            end
            """, new TranslateContext());
        Assert.DoesNotContain(t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = P, Text = r.Script }), o => o.Message is ErrorMessage);
        Assert.Equal("2 2 1stmfoot Gun", t.Server.State.Find(U, P)!.Label);
    }

    [Fact]
    public void A_spawned_object_s_own_on_spawn_runs_once_for_itself_and_not_for_other_spawns()
    {
        using var t = new Table();
        t.Store.RegisterDef(new PackManifest { Id = "testpack" }, new PieceDef { Id = "foot", Name = "Foot", ModuleType = "Custom_Token", Sources = new() { ["ImageURL"] = "https://example.test/foot.png" } });
        var own = ModuleTranslator.Translate("function onSpawn() print('own spawn ' .. self.getName()) end", new TranslateContext()).Script;
        var r = ModuleTranslator.Translate($$"""
            function onLoad()
              if self.getDescription() ~= '' then return end
              self.setDescription('spawning')
              local a = spawnObjectData({ data = { Name = 'Custom_Token', Nickname = 'first', CustomImage = { ImageURL = 'https://example.test/foot.png' }, LuaScript = [[{{own}}]] } })
              local b = spawnObjectData({ data = { Name = 'Custom_Token', Nickname = 'second', CustomImage = { ImageURL = 'https://example.test/foot.png' } } })
            end
            """, new TranslateContext());
        Assert.DoesNotContain(t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = P, Text = r.Script }), o => o.Message is ErrorMessage);
        Assert.Equal(["own spawn first"], t.Server.Scripts.Console.Where(l => l.StartsWith("own spawn", StringComparison.Ordinal)));
    }

    [Fact]
    public void The_object_a_spawn_returns_is_the_very_object_later_events_pass()
    {
        using var t = new Table();
        t.Store.RegisterDef(new PackManifest { Id = "testpack" }, new PieceDef { Id = "foot", Name = "Foot", ModuleType = "Custom_Token", Sources = new() { ["ImageURL"] = "https://example.test/foot.png" } });
        var r = ModuleTranslator.Translate("""
            spawned = nil
            function onLoad()
              if self.getDescription() ~= '' then return end
              self.setDescription('spawning')
              spawned = spawnObjectData({ data = { Name = 'Custom_Token', Nickname = 'copy', CustomImage = { ImageURL = 'https://example.test/foot.png' } } })
            end
            function onObjectSpawn(o)
              if o.getName() == 'copy' then print('same ' .. tostring(o == spawned)) end
            end
            """, new TranslateContext());
        Assert.DoesNotContain(t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = P, Text = r.Script }), o => o.Message is ErrorMessage);
        Assert.Contains("same true", t.Server.Scripts.Console);
    }
}
