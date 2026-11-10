using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace KTable.App;

static class InterfaceFontTests
{
    static readonly float[] BetweenWholeSizes = { 0.7f, 1.25f, 1.5f, 2.6f }, WholeSizes = { 1f, 2f, 3f };

    static bool AllDistanceFields(bool on) =>
        new[] { Ui.Sans(), Ui.SansStrong(), Ui.SansItalic(), Ui.Serif() }.All(font => font is FontFile face && face.MultichannelSignedDistanceField == on);

    [NodeTest]
    static Task Interface_fonts_are_drawn_as_distance_fields_between_whole_sizes_and_the_table_s_lettering_never_is(TestRunner r)
    {
        try
        {
            foreach (float size in BetweenWholeSizes)
            {
                Ui.DrawFontsAt(size);
                Check.That(AllDistanceFields(true), $"at {size} the interface fonts are distance fields, sharp wherever a letter lands");
                Check.That(Ui.TableSans() is FontFile { MultichannelSignedDistanceField: false }, $"at {size} the table's lettering keeps its own drawing");
            }
            foreach (float size in WholeSizes)
            {
                Ui.DrawFontsAt(size);
                Check.That(AllDistanceFields(false), $"at {size} the interface fonts are drawn to the pixel grid");
            }
        }
        finally { Ui.DrawFontsAt(r.GetTree().Root.ContentScaleFactor); }
        return Task.CompletedTask;
    }
}
