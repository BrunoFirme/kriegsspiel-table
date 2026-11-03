using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed class MenuCards : HudPart
{
    readonly ToolStrip _strip;
    readonly Func<PiecesPanel> _pieces;
    public MenuCards(HudContext ctx, ToolStrip strip, Func<PiecesPanel> pieces) : base(ctx) { _strip = strip; _pieces = pieces; }

    static string Short(PieceNode n)
    {
        var t = (n.Piece.Label is { Length: > 0 } l ? l : n.Def?.Name ?? "this piece").Trim();
        return t.Length > 22 ? t[..21] + "…" : t;
    }

    public void Show(Vector2 at, PieceNode? hit)
    {
        var menu = new MenuCard();
        var nodes = T.SelectedNodes.ToList();
        if (hit != null && nodes.Count > 0) PieceMenu(menu, hit, nodes);
        else TableMenu(menu, at);
        menu.Show(Ctx.Root, at);
    }

    void PieceMenu(MenuCard menu, PieceNode hit, List<PieceNode> nodes)
    {
        var one = nodes.Count == 1 ? nodes[0] : null;
        var head = one != null ? (one.Piece.Label is { Length: > 0 } lb ? lb : one.Def?.Name ?? one.Piece.Def) : $"{nodes.Count} pieces";
        menu.Head(head);
        menu.Separator();
        if (nodes.Any(Table.IsDie)) menu.Item("Roll", () => T.RollDiceAtHand(), "R");
        menu.Item("Turn left 15°", () => T.RotateSmooth(-15), "Q");
        menu.Item("Turn right 15°", () => T.RotateSmooth(15), "E");
        menu.Item("Turn around 180°", () => T.RotateSmooth(180));
        if (nodes.Any(n => !n.IsDie && !n.IsEffect && n.Def?.Kind != "model")) menu.Item("Turn over (show the back)", T.FlipSelection, Cfg.Keys.Label("flip"));
        menu.Item("Larger", () => T.ScaleSmooth(1.1), "+");
        menu.Item("Smaller", () => T.ScaleSmooth(1 / 1.1), "−");
        if (one != null && !one.IsDie && !one.IsEffect) OnePieceRows(menu, one);
        if (one?.Def is { } def) StateAndCounterRows(menu, one, def);
        menu.Separator();
        SelectionRows(menu, hit, nodes);
        if (one != null && !one.IsDie && !one.IsEffect && S.CanTouch(one.Layer, one.Piece)) { menu.Separator(); menu.Add(PieceNotes(one, menu.Pop)); }
    }

    void OnePieceRows(MenuCard menu, PieceNode one)
    {
        menu.Separator();
        menu.Item("Properties…", () => PropertiesDialog.Open(Ctx, one));
        if (S.IsUmpire) menu.Item("Scripting…", () => Ctx.ShowScriptWindow(one.Piece.Id), Cfg.Keys.Label("scriptWindow"));
        if (Cfg.MakerTools)
        {
            var pid = one.Piece.Id; var pguid = one.Piece.Guid;
            menu.Item("Copy id", () => { AppClipboard.SetText(pid); Say($"Copied {pid}"); }, pid);
            if (pguid != null && pguid != pid) menu.Item("Copy GUID", () => { AppClipboard.SetText(pguid); Say($"Copied {pguid}"); }, pguid);
        }
        if (one.Piece.MenuItems.Count > 0 && one.ScriptDrawingShows && S.CanTouch(one.Layer, one.Piece))
        {
            menu.Separator("Script");
            for (int i = 0; i < one.Piece.MenuItems.Count; i++)
            {
                var mi = i;
                menu.Item(one.Piece.MenuItems[i].Label, () => S.Do(new PickMenuItem { Layer = one.Layer, Id = one.Piece.Id, Index = mi + 1 }));
            }
        }
    }

    void StateAndCounterRows(MenuCard menu, PieceNode one, PieceDef def)
    {
        if (def.States.Count > 1)
        {
            menu.Separator("Look");
            for (int i = 0; i < def.States.Count; i++)
            {
                var si = i;
                menu.Check(def.States[i].Name.Length > 0 ? def.States[i].Name : $"#{i + 1}", one.Piece.State == i,
                    () => S.Do(new SetState { Layer = one.Layer, Id = one.Piece.Id, State = si }), i < 9 ? (i + 1).ToString() : i == 9 ? "0" : "");
            }
        }
        if (def.Counters.Count > 0)
        {
            menu.Separator("Counters");
            foreach (var cd in def.Counters)
            {
                var c = cd; var val = one.Piece.Counters.GetValueOrDefault(c.Name, c.Default);
                if (val < c.Max) menu.Item($"{c.Name} {val} → {val + 1}", () => S.Do(new SetCounter { Layer = one.Layer, Id = one.Piece.Id, Name = c.Name, Value = val + 1 }));
                if (val > c.Min) menu.Item($"{c.Name} {val} → {val - 1}", () => S.Do(new SetCounter { Layer = one.Layer, Id = one.Piece.Id, Name = c.Name, Value = val - 1 }));
            }
        }
        var markers = def.Markers().Where(a => S.Store.Def(a.Piece) != null).ToList();
        if (markers.Count == 0) return;
        menu.Separator("Markers");
        foreach (var at in markers)
        {
            var a = at;
            menu.Item($"Place {S.Store.Def(a.Piece)!.Name}", () => S.Do(new SpawnPiece { Layer = one.Layer, Def = a.Piece, Pos = MarkerPlace.World(one.Piece.Pos, one.Piece.RotDeg, a.Offset), RotDeg = one.Piece.RotDeg }));
        }
    }

    void SelectionRows(MenuCard menu, PieceNode hit, List<PieceNode> nodes)
    {
        if (nodes.Count > 1)
        {
            menu.Item("Form 1 row", () => T.FormRows(1), "Ctrl+1");
            menu.Item("Form 2 rows", () => T.FormRows(2), "Ctrl+2");
            menu.Item($"Attach the others to {Short(hit)}", () =>
            {
                var kids = nodes.Where(n => n != hit && n.Layer == hit.Layer).ToList();
                if (kids.Count == 0) return;
                S.Do(new Batch { Items = kids.Select(n => (Command)new Attach { Layer = hit.Layer, Parent = hit.Piece.Id, Child = n.Piece.Id }).ToList() });
            });
        }
        if (S.IsUmpire && nodes[0].Layer == Layers.Umpire) menu.Item("POV screenshot from here", () => T.SetTool(Tool.Sighting), "V");
        menu.Separator("Options");
        var pieces = _pieces();
        var kinds = nodes.Select(n => n.Piece.Def).Distinct().ToList();
        bool allHearted = kinds.All(pieces.IsFavourite);
        menu.Check("Favourite", allHearted, () => { foreach (var k in kinds) if (pieces.IsFavourite(k) == allHearted) pieces.ToggleFavourite(k, rebuild: false); pieces.RebuildList(); });
        bool allLocked = nodes.All(n => n.Piece.Locked);
        menu.Check("Locked", allLocked, () =>
            S.Do(nodes.Count == 1
                ? new SetLocked { Layer = nodes[0].Layer, Id = nodes[0].Piece.Id, Value = !allLocked }
                : new Batch { Items = nodes.Select(n => (Command)new SetLocked { Layer = n.Layer, Id = n.Piece.Id, Value = !allLocked }).ToList() }));
        bool allCollide = nodes.All(n => !n.Piece.NoCollision);
        menu.Check("Collides with other pieces", allCollide, () => SetOption(nodes, PieceOptions.NoCollision, allCollide));
        var dice = nodes.Where(n => n.IsDie).ToList();
        if (dice.Count > 0)
        {
            bool allReal = dice.All(n => n.Piece.PhysicalRoll);
            menu.Check("Thrown for real (the face it lands on)", allReal, () => SetOption(dice, PieceOptions.PhysicalRoll, !allReal));
        }
        menu.Separator();
        menu.Item("Copy", T.CopySelection, "Ctrl+C");
        menu.Item("Delete", T.DeleteSelection, "Del");
    }

    void SetOption(List<PieceNode> nodes, string name, bool value)
    {
        var cmds = nodes.Select(n => (Command)new SetPieceOption { Layer = n.Layer, Id = n.Piece.Id, Name = name, Value = value }).ToList();
        S.Do(cmds.Count == 1 ? cmds[0] : new Batch { Items = cmds });
    }

    void TableMenu(MenuCard menu, Vector2 at)
    {
        menu.Item("Paste here", () => { if (T.Rig.GroundAt(at) is { } w) T.Paste(Coords.FromWorld(w)); }, "Ctrl+V");
        menu.Item("Select all", () => T.SelectAllEditable(), "Ctrl+A");
        menu.Separator();
        menu.Item("Ruler", () => T.SetTool(Tool.Ruler), "M");
        menu.Item("Capture a region", () => T.SetTool(Tool.Capture), "Ctrl+Shift+C");
        menu.Item(T.Rig.TopDown ? "3D table" : "Map view", () => { T.Rig.ToggleTopDown(); _strip.SyncViewButton(); }, "F2");
        menu.Item("Frame the map", T.FrameAll, "Home");
        if (S.IsUmpire && S.View.MenuItems.Count > 0)
        {
            menu.Separator("Script");
            for (int i = 0; i < S.View.MenuItems.Count; i++)
            {
                var mi = i;
                menu.Item(S.View.MenuItems[i].Label, () => S.Do(new PickGlobalMenuItem { Index = mi + 1, Pos = T.Rig.GroundAt(at) is { } w ? Coords.FromWorld(w) : null }));
            }
        }
    }

    Control PieceNotes(PieceNode node, PopupPanel pop)
    {
        var p = node.Piece; var layer = node.Layer;
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 4);
        var label = new LineEdit { Text = p.Label ?? "", PlaceholderText = "Label (shown on hover)", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var note = new TextEdit
        {
            Text = p.Note ?? "", PlaceholderText = "Description (shown on hover)", CustomMinimumSize = new Vector2(0, 66),
            WrapMode = TextEdit.LineWrappingMode.Boundary, ScrollFitContentHeight = false, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        note.AddThemeFontSizeOverride("font_size", 12);
        var sentLabel = p.Label ?? ""; var sentNote = p.Note ?? "";
        void Commit()
        {
            if (!GodotObject.IsInstanceValid(label) || !GodotObject.IsInstanceValid(note)) return;
            if (label.Text != sentLabel) { sentLabel = label.Text; S.Do(new SetLabel { Layer = layer, Id = p.Id, Value = label.Text.Trim().Length > 0 ? label.Text.Trim() : null }); }
            if (note.Text != sentNote) { sentNote = note.Text; S.Do(new SetNote { Layer = layer, Id = p.Id, Value = note.Text }); }
        }
        label.TextSubmitted += _ => { Commit(); pop.Hide(); };
        label.FocusExited += Commit;
        note.FocusExited += Commit;
        pop.PopupHide += Commit;
        v.AddChild(label);
        v.AddChild(note);
        return v;
    }
}
