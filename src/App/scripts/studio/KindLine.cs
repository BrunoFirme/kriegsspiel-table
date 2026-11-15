using System.Collections.Generic;
using System.Linq;
using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;
using Json = KTable.Core.Json;

namespace KTable.App;

sealed record LinePiece(string Layer, string Id, string Kind, string Name);

enum LineAnswer { Own, Share, Undo }

sealed class KindLine
{
    internal sealed class Item
    {
        public Item(LinePiece p)
        {
            Layer = p.Layer;
            Id = p.Id;
            Was = Kind = p.Kind;
            Name = p.Name;
        }

        public string Layer { get; }
        public string Id { get; }
        public string Was { get; }
        public string Name { get; }
        public string Kind { get; set; }
        public bool Answered { get; set; }
        public string Base { get; private set; } = "";
        public Dictionary<string, AssetEntry> BaseAssets { get; private set; } = new();

        public string Key => Layer + "/" + Id;

        public void Mark(PackManifest pack)
        {
            if (pack.Pieces.Find(d => d.Id == Kind) is not { } d) return;
            Base = Json.Serialize(d);
            BaseAssets = PackEdit.AssetsOf(d).Distinct().Where(pack.Assets.ContainsKey).ToDictionary(h => h, h => pack.Assets[h]);
        }
    }

    readonly List<Item> _items;
    int _at;

    internal KindLine(IEnumerable<LinePiece> pieces) => _items = pieces.Select(p => new Item(p)).ToList();

    internal IReadOnlyList<Item> Items => _items;

    internal Item Current => _items[_at];

    internal int At => _at;

    internal bool IsLine => _items.Count > 1;

    internal LineAnswer? Rest { get; set; }

    internal bool Holds(string kind) => _items.Any(i => i.Kind == kind);

    internal int IndexOf(string kind) => _items.FindIndex(i => i.Kind == kind);

    internal string Where => Current.Name.Length > 0 ? $"{_at + 1} of {_items.Count} · {Current.Name}" : $"{_at + 1} of {_items.Count}";

    internal bool Unanswered(Item but) => _items.Any(i => i != but && !i.Answered);

    internal void MoveTo(int index, PackManifest pack)
    {
        _at = (index % _items.Count + _items.Count) % _items.Count;
        Current.Mark(pack);
    }

    internal static void Took(Item item, string copy)
    {
        item.Kind = copy;
        item.Answered = true;
    }

    internal bool FirstEdit(PackManifest pack) =>
        IsLine && !Current.Answered && Current.Base.Length > 0 && pack.Pieces.Find(d => d.Id == Current.Kind) is { } d && Json.Serialize(d) != Current.Base;

    internal Dictionary<string, string> Moved() => _items.Where(i => i.Kind != i.Was).ToDictionary(i => i.Key, i => i.Kind);

    internal List<Command> MovedCommands() =>
        _items.Where(i => i.Kind != i.Was).Select(i => (Command)new SetDef { Layer = i.Layer, Id = i.Id, Def = i.Kind }).ToList();

    internal void Restore(Dictionary<string, string>? kept, PackManifest pack)
    {
        if (kept == null) return;
        foreach (var i in _items)
            if (kept.TryGetValue(i.Key, out var kind) && pack.Pieces.Any(d => d.Id == kind))
            {
                i.Kind = kind;
                i.Answered = true;
            }
    }
}
