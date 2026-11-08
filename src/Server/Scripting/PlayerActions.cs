using KTable.Core;
using KTable.Core.Protocol;

namespace KTable.Server.Scripting;

static class PlayerActions
{
    public static (string? Action, List<string> Ids) Of(Command c) => c switch
    {
        DeletePiece d => ("delete", [d.Id]),
        FlipPiece f => ("flip", [f.Id]),
        Batch b when b.Items.Count > 0 && b.Items.All(i => i is DeletePiece) => ("delete", b.Items.Cast<DeletePiece>().Select(d => d.Id).ToList()),
        Batch b when b.Items.Count > 0 && b.Items.All(i => i is FlipPiece) => ("flip", b.Items.Cast<FlipPiece>().Select(f => f.Id).ToList()),
        _ => (null, []),
    };

    public static bool IsAction(Command c) => c is ClickWidget or EditWidget or PickMenuItem or UiEvent or DialogResult or Hotkey or PickGlobalMenuItem || (c is Batch b && b.Items.Any(IsAction));

    public static string FreshGuid(HashSet<string> taken)
    {
        while (true) { var g = Repeatable.Shared.Next(0, 0x1000000).ToString("x6"); if (!taken.Contains(g)) return g; }
    }
}
