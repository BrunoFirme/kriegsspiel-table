using KTable.Core.Model;
using KTable.Core.Panels;
using KTable.Core.Protocol;

namespace KTable.Core.Tests;

public class NestingLimitTests
{
    const string U = Layers.Umpire;

    static string Id(int n) => $"p-{n:D6}";

    static string NestedPanels(int levels) =>
        string.Concat(Enumerable.Repeat("<Panel>", levels - 1)) + "<Text>deep</Text>" + string.Concat(Enumerable.Repeat("</Panel>", levels - 1));

    static UiNode NestedNodes(int levels)
    {
        var node = new UiNode { Tag = "Text", Value = "deep" };
        for (int i = 1; i < levels; i++) node = new UiNode { Tag = "Panel", Children = [node] };
        return node;
    }

    static PieceInstance HeldAsDeepAsItGoes(PieceInstance innermost)
    {
        var piece = innermost;
        for (int i = 1; i <= PieceInstance.MostHolderLevels; i++) piece = new PieceInstance { Id = Id(i), Stowed = [piece] };
        return piece;
    }

    [Fact]
    public void A_holder_goes_inside_a_holder_to_the_limit_and_one_more_is_refused_with_nothing_moved()
    {
        int outermost = PieceInstance.MostHolderLevels + 1;
        var g = new GameState();
        g.Layers[U] = Enumerable.Range(0, outermost + 1).Select(i => new PieceInstance { Id = Id(i) }).ToList();
        for (int i = 0; i < PieceInstance.MostHolderLevels; i++)
            Assert.Null(StateMachine.Apply(g, new Stow { Layer = U, Holder = Id(i + 1), Id = Id(i) }, out _));
        var deepest = g.Find(U, Id(PieceInstance.MostHolderLevels))!;
        Assert.Equal(PieceInstance.MostHolderLevels, deepest.HolderLevels());

        var refused = StateMachine.Apply(g, new Stow { Layer = U, Holder = Id(outermost), Id = deepest.Id }, out _);

        Assert.Contains("no deeper", refused);
        Assert.Same(deepest, g.Find(U, deepest.Id));
        Assert.Null(g.Find(U, Id(outermost))!.Stowed);
    }

    [Fact]
    public void A_piece_riding_on_the_one_stowed_counts_what_it_holds_too()
    {
        var g = new GameState();
        var rider = HeldAsDeepAsItGoes(new PieceInstance { Id = Id(0) });
        g.Layers[U] = [new PieceInstance { Id = "carrier", Attached = [rider.Id] }, rider, new PieceInstance { Id = "box" }];

        Assert.Contains("no deeper", StateMachine.Apply(g, new Stow { Layer = U, Holder = "box", Id = "carrier" }, out _));
    }

    [Fact]
    public void A_unit_panel_as_deep_as_the_modules_build_them_is_within_the_limit()
    {
        string[] tags = ["Panel", "VerticalLayout", "HorizontalLayout", "TableLayout", "Row", "Cell", "HorizontalLayout", "TableLayout", "Row", "Cell"];
        var xml = string.Concat(tags.Select(t => $"<{t}>")) + "<Text>1st Cav</Text>" + string.Concat(tags.Reverse().Select(t => $"</{t}>"));

        Assert.Null(PanelBuild.Of(xml).Layout.Broken);
    }

    [Fact]
    public void The_deepest_table_the_limits_allow_is_still_saved_copied_and_sent()
    {
        var layout = PanelBuild.Of(NestedPanels(PanelBuild.MaxDepth)).Layout;
        Assert.Null(layout.Broken);
        var nodes = NestedNodes(PanelBuild.MaxDepth);
        var innermost = new PieceInstance
        {
            Id = Id(0), Ui = [nodes], UiLayout = layout,
            Looks = [new PieceLook { Ui = [nodes.Clone()], UiLayout = layout.Clone() }],
        };
        var holder = HeldAsDeepAsItGoes(innermost);
        var g = new GameState { ScenarioId = "local.deep", Ui = [nodes.Clone()], UiLayout = layout.Clone() };
        g.Layers[U] = [holder];

        var saved = Json.SerializeBytes(new SaveFile { ScenarioId = g.ScenarioId, Snapshot = g });
        var back = Json.Deserialize<SaveFile>(saved).Snapshot.Find(U, holder.Id)!;
        Assert.Equal(PieceInstance.MostHolderLevels, back.HolderLevels());
        Assert.Equal(Id(0), back.AllStowed().Last().Id);
        Assert.Equal(PieceInstance.MostHolderLevels, g.DeepClone().Find(U, holder.Id)!.HolderLevels());
        Assert.Equal(PieceInstance.MostHolderLevels, new TurnRecord { Packed = TurnRecord.Pack(g) }.Unpack().Find(U, holder.Id)!.HolderLevels());
        Message[] sent =
        [
            new JoinResult { Ok = true, View = g },
            new TurnState { State = g },
            new CommandMessage { Command = new LoadSave { Name = "deep", State = g } },
            new CommandMessage { Command = new Batch { Items = [new RestorePiece { Layer = U, Piece = holder }] } },
        ];
        foreach (var m in sent) Assert.IsType(m.GetType(), Json.Deserialize<Message>(Json.SerializeBytes(m)));
    }
}
