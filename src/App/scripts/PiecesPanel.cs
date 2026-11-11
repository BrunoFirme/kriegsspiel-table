using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed class PiecesPanel : HudPart
{
    public FloatPanel Panel;
    public OptionButton PackPick;
    public LineEdit Search;
    public Button Hearts;
    public ItemList List;
    List<PackManifest> _packs = new();
    int _listGen;
    readonly HashSet<string> _damagedSaid = new();

    readonly IHudShell Shell;
    public PiecesPanel(HudContext ctx, IHudShell shell) : base(ctx)
    {
        Shell = shell;
        var packs = new VBoxContainer { Name = "Packs" };
        packs.AddThemeConstantOverride("separation", Ui.Gap.Tight);
        PackPick = new OptionButton { ClipText = true, FitToLongestItem = false };
        PackPick.ItemSelected += i =>
        {
            Cfg.PiecesPack = PackPick.GetItemMetadata((int)i).AsString();
            Cfg.Save();
            RebuildList();
        };
        packs.AddChild(PackPick);
        Search = new LineEdit { PlaceholderText = "search name, tag, colour…", ClearButtonEnabled = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        Search.TextChanged += _ => RebuildList();
        var searchRow = new HBoxContainer();
        searchRow.AddThemeConstantOverride("separation", Ui.Gap.Tight);
        searchRow.AddChild(Search);
        var hearts = Hearts = Ui.IconBtn("heart", "Favourites only", "Right-click a piece here, or on the table, to make it a favourite: favourites come first");
        hearts.ToggleMode = true;
        hearts.ButtonPressed = Cfg.FavouritesOnly;
        hearts.Toggled += on =>
        {
            Cfg.FavouritesOnly = on;
            Cfg.Save();
            RebuildList();
        };
        searchRow.AddChild(hearts);
        packs.AddChild(searchRow);
        BuildList();
        packs.AddChild(PieceTiles.Host(List));
        var dragHint = Hint("Drag a piece onto the table, or click it and then the table · Shift keeps placing");
        dragHint.CustomMinimumSize = new Vector2(240, 0);
        packs.AddChild(dragHint);
        Panel = shell.MakePanel("pieces", "Pieces");
        Panel.Resizable = true;
        Panel.CustomMinimumSize = shell.PanelSize("pieces", 320, 420);
        Panel.Body.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        packs.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        Index = new PieceIndex(ctx);
        Tabs = new TabContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        Tabs.AddChild(packs);
        Tabs.AddChild(Index.Root);
        Panel.Body.AddChild(Tabs);
        Tabs.CurrentTab = Math.Clamp(Cfg.PiecesTab, 0, Tabs.GetTabCount() - 1);
        Tabs.TabChanged += tab =>
        {
            Cfg.PiecesTab = (int)tab;
            Cfg.Save();
        };
    }

    public readonly TabContainer Tabs;
    public readonly PieceIndex Index;

    public void ShowIndex() => Tabs.CurrentTab = Index.Root.GetIndex();

    [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(List))]
    void BuildList()
    {
        List = PieceTiles.List();
        List.ItemSelected += i => { if (List.GetItemMetadata((int)i).AsString() is { Length: > 0 } id) { Shell.EnsureInGame(id); T.SetTool(Tool.Place, id); } };
        Vector2 palPress = Vector2.Zero;
        int palItem = -1;
        bool palDragging = false;
        List.GuiInput += e =>
        {
            switch (e)
            {
                case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } mb:
                    palPress = mb.Position;
                    palItem = List.GetItemAtPosition(mb.Position, true);
                    palDragging = false;
                    break;
                case InputEventMouseMotion mm when palItem >= 0 && !palDragging && (mm.ButtonMask & MouseButtonMask.Left) != 0:
                    if ((mm.Position - palPress).Length() > 10 && List.GetItemMetadata(palItem).AsString() is { Length: > 0 } id)
                    {
                        palDragging = true;
                        Shell.EnsureInGame(id);
                        T.BeginPaletteDrag(id, at => Panel.GetGlobalRect().HasPoint(at));
                    }
                    break;
                case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } mb:
                    if (palDragging) T.EndPaletteDrag(drop: !Panel.GetGlobalRect().HasPoint(mb.GlobalPosition));
                    palItem = -1;
                    palDragging = false;
                    break;
                case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } rb:
                    HeartMenu(rb);
                    break;
            }
        };
    }

    void HeartMenu(InputEventMouseButton rb)
    {
        int at = List.GetItemAtPosition(rb.Position, true);
        if (at < 0 || List.GetItemMetadata(at).AsString() is not { Length: > 0 } defId) return;
        List.AcceptEvent();
        var pm = new PopupMenu();
        pm.AddCheckItem("Favourite", 1);
        pm.SetItemChecked(0, IsFavourite(defId));
        pm.IdPressed += _ => ToggleFavourite(defId);
        pm.PopupHide += () => pm.QueueFree();
        Ctx.Root.AddChild(pm);
        pm.Popup(new Rect2I((Vector2I)rb.GlobalPosition.Round(), Vector2I.Zero));
    }

    public void RebuildPacks()
    {
        var installed = S.Store.ListPackIds().OrderBy(i => S.Store.Pack(i)?.Name ?? i, StringComparer.OrdinalIgnoreCase).ToList();
        var order = KTable.Client.Library.Current.Ordered(pack: true, installed.Concat(S.View.Packs).Distinct());
        int Rank(string id) => order.IndexOf(id) is var i and >= 0 ? i : int.MaxValue;
        var ids = S.View.Packs.OrderBy(Rank).ToList();
        if (S.IsUmpire) ids.AddRange(installed.Where(i => !ids.Contains(i)).OrderBy(Rank));
        ids.AddRange(UserPacks.LocalUserPacks(S.Store).Where(i => !ids.Contains(i)).OrderBy(Rank));
        _packs = ids.Select(id => S.Store.Pack(id)).Where(p => p != null).Cast<PackManifest>().ToList();
        foreach (var id in ids)
            if (S.Store.Problem(id) is { } why && _damagedSaid.Add(id))
                Log($"[color=#ff7060]The pack {Fmt.Bb(id)} cannot be read ({Fmt.Bb(why)}); its pieces are not shown.{(S.IsHost ? " Remove it under Piece studio on the title screen, or install it again." : "")}[/color]");
        var prev = PackPick.Selected >= 0 ? PackPick.GetItemMetadata(PackPick.Selected).AsString() : Cfg.PiecesPack;
        PackPick.Clear();
        PackPick.AddItem($"All packs ({_packs.Sum(p => p.Pieces.Count)})");
        PackPick.SetItemMetadata(0, "");
        for (int i = 0; i < _packs.Count; i++)
        {
            PackPick.AddItem($"{_packs[i].Name} ({_packs[i].Pieces.Count}){(_packs[i].Partial ? "  ·  only this scenario's pieces" : "")}{(S.View.Packs.Contains(_packs[i].Id) ? "" : "  ·  not in scenario")}");
            PackPick.SetItemMetadata(i + 1, _packs[i].Id);
            if (_packs[i].Id == prev) PackPick.Selected = i + 1;
        }
        if (prev == null || (prev.Length > 0 && PackPick.Selected <= 0)) PackPick.Selected = _packs.Count > 1 ? 1 : 0;
        RebuildList();
    }

    public void SelectPack(string packId)
    {
        for (int i = 0; i < PackPick.ItemCount; i++)
            if (PackPick.GetItemMetadata(i).AsString() == packId)
            {
                PackPick.Selected = i;
                RebuildList();
                return;
            }
    }

    public bool IsFavourite(string defId) => Cfg.Favourites.Contains(defId);

    public void ToggleFavourite(string defId, bool rebuild = true)
    {
        if (!Cfg.Favourites.Remove(defId)) Cfg.Favourites.Add(defId);
        Cfg.Save();
        if (rebuild) RebuildList();
    }

    public void RebuildList()
    {
        int gen = ++_listGen;
        List.Clear();
        var packId = PackPick.Selected > 0 ? PackPick.GetItemMetadata(PackPick.Selected).AsString() : "";
        var q = Search.Text.Trim();
        var source = _packs.Where(p => packId == "" || p.Id == packId);
        var hearted = Cfg.Favourites.ToHashSet();
        var defs = source.SelectMany(p => p.Pieces.Where(d => !d.Retired).Select(d => (d, p)))
            .Where(x => !Cfg.FavouritesOnly || hearted.Contains(x.d.Id))
            .Where(x => q.Length == 0 || x.d.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || x.d.Tags.Any(t => t.Contains(q, StringComparison.OrdinalIgnoreCase)) || x.p.Name.Contains(q,
                StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => hearted.Contains(x.d.Id) ? 0 : 1).ThenBy(x => x.d.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.d.Size.W * x.d.Size.H)
            .Take(1200).ToList();
        var dupNames = defs.GroupBy(x => x.d.Name).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        foreach (var (d, p) in defs)
        {
            var sub = dupNames.Contains(d.Name) ? $"{d.Size.W:0.#}×{d.Size.H:0.#} {ShapeWord(d)}" : ShapeWord(d);
            int idx = List.AddItem($"{(hearted.Contains(d.Id) ? "♥ " : "")}{d.Name}\n{sub}", Ui.LoadingIcon);
            List.SetItemMetadata(idx, d.Id);
            List.SetItemTooltip(idx,
                $"{d.Name}\n{p.Name}\n{d.Size.W:0.##} × {d.Size.H:0.##} units · {ShapeWord(d)}" +
                (d.States.Count > 1 ? $"\nstates: {string.Join(", ", d.States.Select(st => st.Name))}" : "") +
                (d.Counters.Count > 0 ? $"\ncounters: {string.Join(", ", d.Counters.Select(c => $"{c.Name} {c.Min}–{c.Max}"))}" : "") +
                (d.Tags.Count > 0 ? $"\ntags: {string.Join(", ", d.Tags)}" : ""));
            PieceTiles.Picture(S, List, idx, d, p, () => gen == _listGen);
        }
        if (defs.Count == 1200) List.AddItem("… refine the search to see more");
    }

    public static string ShapeWord(PieceDef d) => d.Kind == "model" ? (d.Tags.Contains("die") ? "die" : "3D model") : d.Tags.Contains("effect") ? "effect" : d.Shape switch
    {
        "alpha" => "token", "circle" => "round tile", "hex" => "hex tile", "rounded" => "tile", _ => d.Tags.Contains("block") ? "block" : "tile",
    } + (d.States.Count > 1 && !d.Tags.Contains("effect") ? $" ⇄{d.States.Count}" : "") + (d.Counters.Count > 0 ? " ⚙" : "");

    public Vector2 ItemCentre(int i) => PieceTiles.Centre(List, i);
}
