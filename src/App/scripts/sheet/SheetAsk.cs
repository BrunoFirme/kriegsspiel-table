using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class SheetAsk
{
    public static string Named(SheetDef sheet) => sheet.Name.Length > 0 ? sheet.Name : sheet.Id;

    public static void Remove(Node parent, Table table, Session session, SheetDef sheet) =>
        Ui.Confirm(parent, $"Remove \"{Named(sheet)}\"?", "Remove", () =>
        {
            if (table.SelectedSheet == sheet.Id) table.SelectSheet(null);
            session.Do(new RemoveSheet { Id = sheet.Id });
        }, "The sheet leaves the table. Ctrl+Z brings it back.");
}
