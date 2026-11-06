using System.Text.Json.Nodes;
using KTable.Core.Model;

namespace KTable.Converter.Tests;

public sealed partial class ConverterTests
{
    static ModuleObj BagWithOrder(int? order)
    {
        var bag = Obj("Bag", "Chits", 1, new JsonObject());
        if (order is { } o) bag["Bag"] = new JsonObject { ["Order"] = o };
        return new ModuleObj(bag);
    }

    [Fact]
    public void A_bags_order_comes_over_as_its_take_order_last_in_first_out_when_the_module_names_none()
    {
        Assert.Equal(new string?[] { TakeOrders.Last, TakeOrders.Last, null, TakeOrders.Random },
            new[] { BagWithOrder(null), BagWithOrder(0), BagWithOrder(1), BagWithOrder(2) }.Select(b => b.TakeOrder));
    }

    [Fact]
    public void The_next_piece_out_follows_the_take_order_and_never_a_piece_riding_on_another()
    {
        PieceInstance Held(string id, params string[] riders) => new() { Id = id, Attached = riders.ToList() };
        var holder = new PieceInstance { Id = "box", Stowed = new() { Held("a"), Held("b", "c"), Held("c") } };
        var rng = new Random(7);

        Assert.Equal("a", holder.NextOut(rng)!.Id);
        holder.TakeOrder = TakeOrders.Last;
        Assert.Equal("b", holder.NextOut(rng)!.Id);
        holder.TakeOrder = TakeOrders.Random;
        var drawn = Enumerable.Range(0, 200).Select(_ => holder.NextOut(rng)!.Id).ToHashSet();
        Assert.Equal(new HashSet<string> { "a", "b" }, drawn);
        Assert.Null(new PieceInstance { Id = "empty", TakeOrder = TakeOrders.Random }.NextOut(rng));
    }

    [Fact]
    public void A_piece_the_module_made_not_sticky_comes_over_not_sticky()
    {
        var loose = Obj("Custom_Tile", "Map", 1, new JsonObject { ["Sticky"] = false });
        Assert.Equal((false, true), (new ModuleObj(loose).Sticky, new ModuleObj(Obj("Custom_Tile", "Unit", 1, new JsonObject())).Sticky));
    }
}
