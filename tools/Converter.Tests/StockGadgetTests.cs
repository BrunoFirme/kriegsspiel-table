using KTable.Core;
using KTable.Core.Model;

namespace KTable.Converter.Tests;

public sealed class StockGadgetTests : ConverterHarness
{
    const string Name = "gadgets";

    [Theory]
    [InlineData("Chess_Rook")]
    [InlineData("reversi_chip")]
    [InlineData("Go_Game_Piece_White")]
    public void A_stock_gadget_is_left_out_of_an_import_and_named_in_the_report_as_skipped_by_design(string type)
    {
        WriteConfig();
        var main = typeof(Config).Assembly.EntryPoint!;
        var module = Module(Name, Obj(type, "Gadget", 1), Obj("BlockSquare", "Brigade", 1));
        Assert.Equal(0, (int)main.Invoke(null, new object[] { new[] { "import", module, "--config", Path.Combine(_root, "converter.json"), "--out", Packages } })!);
        var dir = Path.Combine(Packages, "scenarios", Name);
        var table = Json.Load<Scenario>(Path.Combine(dir, "scenario.json"));
        Assert.Single(table.UmpireBoard);
        var report = File.ReadAllText(Path.Combine(dir, "import-report.md"));
        var skipped = report[report.IndexOf("### Skipped object types", StringComparison.Ordinal)..];
        Assert.Contains($"\u00d7 {type} ", skipped[..skipped.IndexOf("### ", 4, StringComparison.Ordinal)]);
    }
}
