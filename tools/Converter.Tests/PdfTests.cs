using System.Text.Json.Nodes;
using KTable.Core;
using KTable.Core.Model;

namespace KTable.Converter.Tests;

public sealed partial class ConverterTests
{
    [Fact]
    public void A_PDF_comes_over_as_a_note_carrying_its_link()
    {
        var cfg = TempConfig();
        var rep = new Report();
        var defs = new DefFactory(cfg, new AssetStore(cfg, rep), rep);
        var pack = new PackOut(cfg.Output, "t", "T", "t", null);
        var pdf = Obj("Custom_PDF", "Rules", 1);
        pdf["Description"] = "Read before play";
        pdf["CustomPDF"] = new JsonObject { ["PDFUrl"] = "https://x/rules.pdf", ["PDFPage"] = 0 };

        var def = defs.DefFor(new TtsObj(pdf), pack)!.Def;

        Assert.True(NoteCard.Is(def));
        Assert.Equal("Rules", def.Name);
        var body = def.TextLines.Single(t => t.Name == NoteCard.BodyLine).Default;
        Assert.Contains("Read before play", body);
        Assert.Contains("https://x/rules.pdf", body);
    }
}
