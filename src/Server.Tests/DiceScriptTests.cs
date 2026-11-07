using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;
using KTable.Server.Scripting.Translate;

namespace KTable.Server.Tests;

public class DiceScriptTests
{
    const string U = Layers.Umpire;
    const string Die = "p-000009";

    static TranslateContext Ctx() => new() { ResolveImage = (url, type, scale) => "marker" };

    [Fact]
    public void A_module_script_rolls_a_die_reads_its_value_and_sets_a_face()
    {
        using var t = new Table();
        var res = t.Server.Handle(Table.Host, new SpawnPiece { Layer = U, Def = "die6", Id = Die, Pos = new(3, 3) });
        Assert.DoesNotContain(res, o => o.Message is ErrorMessage);
        const string lua = "function onLoad()\n  self.setRotationValue(5)\n  local before = self.getValue()\n  local faces = #self.getRotationValues()\n"
                           + "  local rolled = {}\n  for i = 1, 40 do self.roll() rolled[self.getValue()] = true end\n  local seen = 0\n  for _ in pairs(rolled) do seen = seen + 1 end\n"
                           + "  self.setName(before .. '/' .. faces .. '/' .. (seen > 1 and 'many' or 'one'))\nend\n";
        var r = ModuleTranslator.Translate(lua, Ctx());
        res = t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = Die, Text = r.Script });
        Assert.DoesNotContain(res, o => o.Message is ErrorMessage);
        Assert.Equal("5/6/many", t.Server.State.Find(U, Die)!.Label);
    }

    [Fact]
    public void A_module_script_gives_a_die_sides_of_its_own_and_reads_their_values_and_turns()
    {
        using var t = new Table();
        t.Server.Handle(Table.Host, new SpawnPiece { Layer = U, Def = "die6", Id = Die, Pos = new(3, 3) });
        const string lua = """
            function onLoad()
              self.setRotationValues({ { value = "Hit", rotation = { x = -90, y = 0, z = 0 } }, { value = "Miss", rotation = { x = 90, y = 0, z = 0 } } })
              self.setRotationValue("Miss")
              local after = self.getRotationValue() .. ":" .. self.getValue() .. ":" .. #self.getRotationValues()
              local back = self.getRotationValues()[1].rotation
              self.setName(after .. ":" .. math.floor(back.x + 0.5) .. ":" .. math.floor(back.z + 0.5))
            end
            """;
        var r = ModuleTranslator.Translate(lua, Ctx());
        var res = t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = Die, Text = r.Script });
        Assert.DoesNotContain(res, o => o.Message is ErrorMessage);
        var die = t.Server.State.Find(U, Die)!;
        Assert.Equal("Miss:2:2:-90:0", die.Label);
        Assert.Equal(new[] { "Hit", "Miss" }, die.Faces!.Select(f => f.Value));
        Assert.Equal(1, die.ShownFace);
        Assert.Equal(Json.Serialize(t.Server.State), Json.Serialize(SaveLoader.Replay(t.Scenario, t.Server.Log)));
        t.Server.Handle(Table.Ump3, new SetDieFaces { Layer = U, Id = Die, Faces = null });
        Assert.Null(t.Server.State.Find(U, Die)!.Faces);
        Assert.Equal(6, PieceInstance.FacesOf(t.Store.Def("die6"), t.Server.State.Find(U, Die)!).Count);
    }

    [Fact]
    public void A_roll_an_umpire_makes_reaches_the_die_s_script_and_the_global_one()
    {
        using var t = new Table();
        t.Server.Handle(Table.Host, new SpawnPiece { Layer = U, Def = "die6", Id = Die, Pos = new(3, 3) });
        var own = ModuleTranslator.Translate("function onRandomize(color) self.setDescription('rolled by ' .. color) end", Ctx());
        t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = Die, Text = own.Script });
        var global = ModuleTranslator.Translate("function onObjectRandomize(obj, color) obj.setName('seen ' .. obj.getValue()) end", new TranslateContext { IsGlobal = true });
        t.Server.Handle(Table.Lead, new SetGlobalScript { Text = global.Script });
        t.Server.Handle(Table.Ump3, new RollDice { Layer = U, PieceIds = [Die], Count = 1, Sides = 6 });
        var die = t.Server.State.Find(U, Die)!;
        Assert.StartsWith("rolled by ", die.Note);
        Assert.Equal($"seen {die.ShownFace + 1}", die.Label);
    }

    [Fact]
    public void Randomize_turns_a_piece_of_several_looks_to_another_and_rolls_a_die()
    {
        using var t = new Table();
        t.Store.RegisterDef(new PackManifest { Id = "testpack" }, new PieceDef { Id = "flag", States = { new() { Name = "Up" }, new() { Name = "Down" } } });
        t.Server.Handle(Table.Host, new SpawnPiece { Layer = U, Def = "flag", Id = "p-000020", Pos = new(1, 1) });
        var r = ModuleTranslator.Translate("function onLoad() self.shuffleStates() end", Ctx());
        t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = "p-000020", Text = r.Script });
        Assert.Equal(1, t.Server.State.Find(U, "p-000020")!.State);
        t.Server.Handle(Table.Host, new SpawnPiece { Layer = U, Def = "die6", Id = Die, Pos = new(3, 3) });
        r = ModuleTranslator.Translate("function onLoad() self.randomize() end", Ctx());
        t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = Die, Text = r.Script });
        var roll = Assert.Single(t.Server.Log.OfType<RollDice>());
        Assert.Equal(new[] { Die }, roll.PieceIds);
        Assert.Equal(roll.Results[0] - 1, t.Server.State.Find(U, Die)!.ShownFace);
    }

    [Fact]
    public void A_module_script_reads_and_sets_worth_held_turns_the_pick_up_place_and_flips_a_die_to_its_other_side()
    {
        using var t = new Table();
        const string P = "p-000002";
        t.Server.Handle(Table.Host, new SpawnPiece { Layer = U, Def = "die6", Id = Die, Pos = new(3, 3) });
        t.Server.Handle(Table.Ump3, new TurnDie { Layer = U, Id = Die, Face = 1 });
        t.Server.Handle(Table.Ump3, new HoldPiece { Layer = U, Id = P, Held = true });
        t.Server.Handle(Table.Ump3, new MovePiece { Layer = U, Id = P, Pos = new(4, 2), RotDeg = 0 });
        const string lua = """
            function onLoad()
              self.value = 25
              self.value_flags = 3
              self.held_spin_index = 6
              local die = getObjectFromGUID("p-000009")
              die.flip()
              local from = self.pick_up_position
              self.setName(self.value .. ":" .. self.value_flags .. ":" .. self.held_spin_index .. ":" .. from.x .. "," .. from.z .. ":" .. die.getValue())
            end
            """;
        var r = ModuleTranslator.Translate(lua, Ctx());
        var res = t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = P, Text = r.Script });
        Assert.DoesNotContain(res, o => o.Message is ErrorMessage);
        var p = t.Server.State.Find(U, P)!;
        Assert.Equal((25, 3, 90.0), (p.Worth, p.WorthGroups, p.RotDeg));
        Assert.Equal("25:3:6:0,0:5", p.Label);
        Assert.Equal(4, t.Server.State.Find(U, Die)!.ShownFace);
    }
}
