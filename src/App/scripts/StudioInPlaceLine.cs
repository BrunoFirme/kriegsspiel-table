using System.Collections.Generic;
using System.Linq;

namespace KTable.App;

sealed partial class StudioInPlace
{
    readonly Dictionary<string, string>? _restoredLine;

    internal KindLine Line { get; }

    internal KindLine.Item Current => Line.Current;

    internal bool IsLine => Line.IsLine;

    internal int Sharing(string kind) =>
        _live.View.Layers.Sum(l => l.Value.Count(p => p.Def == kind)) - Line.Items.Count(i => i.Was == kind && i.Kind != kind);

    internal void MoveTo(int index, KTable.Core.Model.PackManifest pack)
    {
        if (Instance is { } node) node.Visible = true;
        _aimed = false;
        Line.MoveTo(index, pack);
    }
}
