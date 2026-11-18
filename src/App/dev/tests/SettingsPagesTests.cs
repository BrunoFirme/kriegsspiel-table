using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace KTable.App;

static class SettingsPagesTests
{
    static readonly string[] Everywhere = { "Table", "Keys", "Interface", "Display", "Graphics", "Sound", "Captures", "Network" };

    static string Pages(DialogPages pages) =>
        string.Join(",", WindowTests.Descendants(pages.Root).Where(n => n.HasMeta(DialogPages.PageMeta)).Select(n => n.GetMeta(DialogPages.PageMeta).AsString()));

    static string Shown(DialogPages pages) =>
        WindowTests.Descendants(pages.Root).OfType<Control>().FirstOrDefault(n => n.HasMeta(DialogPages.PageMeta) && n.Visible)?.GetMeta(DialogPages.PageMeta).AsString() ?? "";

    static bool Has(DialogPages pages, string text) => WindowTests.Descendants(pages.Root).OfType<Button>().Any(b => b.Text == text);

    static string Expected(params string[] extra) =>
        string.Join(",", Everywhere.Concat(extra).Concat(AgentEndpoint.Current != null ? new[] { "AI agent" } : Array.Empty<string>()));

    [NodeTest]
    static async Task Settings_has_the_same_pages_on_the_title_at_the_table_and_in_the_studio(TestRunner r)
    {
        var h = await HudTests.Host(r, sheets: 1);
        var cfg = h.Hud.Ctx.Cfg;
        var away = SettingsPages.Build(new SettingsPages.Place(h.Hud, cfg) { Table = h.Table });
        var title = SettingsPages.Build(new SettingsPages.Place(h.Hud, cfg) { Storage = box => box.AddChild(new Label { Text = "shelf" }) });
        var table = SettingsPages.Build(new SettingsPages.Place(h.Hud, cfg) { Hud = h.Hud });
        var all = new[] { away, title, table };
        foreach (var p in all) h.Hud.AddChild(p.Root);
        try
        {
            await r.Frames(1);
            Check.Equal(Expected(), Pages(away), "away from a table, the pages");
            Check.Equal(Expected("Storage"), Pages(title), "on the title, the same pages and Storage");
            Check.Equal(Expected(), Pages(table), "at the table, the same pages");
            Check.That(Has(away, "Smooth fine lines too") && Has(away, "Copy to the clipboard") && Has(away, "Reset to defaults"), "graphics, captures and keys away from a table");
            Check.That(Has(table, "Put the panels back"), "the table puts its panels back");
            Check.That(!Has(away, "Put the panels back") && !Has(title, "Put the panels back"), "away from a table there are no panels to put back");

            away.Show("Graphics");
            var again = SettingsPages.Build(new SettingsPages.Place(h.Hud, cfg));
            h.Hud.AddChild(again.Root);
            again.ShowFirst();
            await r.Frames(1);
            Check.Equal("Graphics", Shown(again), "Settings opens where it was left, wherever it is opened");
            again.Root.QueueFree();
        }
        finally
        {
            DialogPages.LastPage.Remove(SettingsPages.Id);
            foreach (var p in all) p.Root.QueueFree();
        }
    }
}
