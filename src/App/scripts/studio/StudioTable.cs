using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

internal sealed class TablePaper
{
    readonly PieceStudio _s;
    readonly Dictionary<string, string> _inst = new();

    public TablePaper(PieceStudio s) => _s = s;

    Session S => _s.S;
    Table T => _s.T;
    PackManifest Pack => _s.Pack;

    public bool Quiet;

    public bool Has(PieceDef d) => _inst.ContainsKey(d.Id);

    public void Forget(PieceDef d) => _inst.Remove(d.Id);

    public PieceInstance? Instance(PieceDef? d) => d != null && _inst.TryGetValue(d.Id, out var id) ? S.View.Find(Layers.Umpire, id) : null;

    public void LayOutAll(bool force = false)
    {
        if (force)
        {
            var del = S.View.Layer(Layers.Umpire).Select(p => (Command)new DeletePiece { Layer = Layers.Umpire, Id = p.Id }).ToList();
            if (del.Count > 0) S.Do(new Batch { Items = del });
            _inst.Clear();
        }
        var defs = Pack.Pieces.Where(d => !_inst.ContainsKey(d.Id)).OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList();
        if (defs.Count == 0) return;
        double total = Pack.Pieces.Sum(d => (d.Size.W + 0.6) * (d.Size.H + 0.6));
        double rowW = Math.Max(12, Math.Sqrt(total) * 1.35);
        double x = 0, z = 0, rowH = 0;
        if (_inst.Count > 0 && S.View.Layer(Layers.Umpire).Count > 0) z = S.View.Layer(Layers.Umpire).Min(p => p.Pos.Z - (S.Def(p.Def)?.Size.H ?? 1)) - 1;
        var cmds = new List<Command>();
        foreach (var d in defs)
        {
            double w = d.Size.W + 0.6, h = d.Size.H + 0.6;
            if (x + w > rowW && x > 0)
            {
                x = 0;
                z -= rowH;
                rowH = 0;
            }
            var id = S.View.NewId();
            _inst[d.Id] = id;
            cmds.Add(new SpawnPiece { Layer = Layers.Umpire, Def = d.Id, Pos = new Vec2(x + w / 2, z - h / 2), RotDeg = 180, Id = id });
            x += w;
            rowH = Math.Max(rowH, h);
        }
        S.Do(new Batch { Items = cmds });
    }

    public void FrameAll()
    {
        var ps = S.View.Layer(Layers.Umpire);
        if (ps.Count == 0) { T.Rig.Frame(Vector3.Zero, 20); return; }
        double minX = ps.Min(p => p.Pos.X), maxX = ps.Max(p => p.Pos.X), minZ = ps.Min(p => p.Pos.Z), maxZ = ps.Max(p => p.Pos.Z);
        T.Rig.Frame(Coords.ToWorld(new Vec2((minX + maxX) / 2, (minZ + maxZ) / 2)), (float)Math.Max(10, Math.Max(maxX - minX, maxZ - minZ) + 6));
    }

    public void OnTableSelection()
    {
        if (Quiet) return;
        var n = T.SelectedNodes.FirstOrDefault();
        if (n == null) return;
        if (!_inst.ContainsValue(n.Piece.Id)) return;
        var d = Pack.Pieces.FirstOrDefault(p => p.Id == n.Piece.Def);
        if (d != null && d != _s.Sel) _s.Select(d, frame: false);
    }

    public void ShowMenu(Vector2 at)
    {
        var pm = new PopupMenu();
        pm.AddItem("Paint this piece", 8);
        pm.AddItem("Turn over", 9);
        pm.AddSeparator();
        pm.AddItem("Turn around 180°", 1);
        pm.AddSeparator();
        pm.AddItem("Duplicate the piece", 5);
        pm.AddItem("Delete the piece", 6);
        pm.AddSeparator();
        pm.AddItem("Take off the paper (keeps the piece in the pack)", 7);
        pm.IdPressed += id =>
        {
            switch (id)
            {
                case 1:
                    T.RotateSmooth(180);
                    break;
                case 5:
                    _s.DuplicateSel();
                    break;
                case 6:
                    _s.DeleteSel();
                    break;
                case 7: foreach (var n in T.SelectedNodes.ToList()) { if (_inst.FirstOrDefault(kv => kv.Value == n.Piece.Id).Key is { } k) _inst.Remove(k); S.Do(new DeletePiece { Layer = n.Layer,
                    Id = n.Piece.Id }); } break;
                case 8:
                    _s.SetView("paint");
                    break;
                case 9:
                    T.FlipSelection();
                    break;
            }
        };
        pm.PopupHide += () => pm.QueueFree();
        _s.Root.AddChild(pm);
        pm.Popup(new Rect2I((Vector2I)at.Round(), Vector2I.Zero));
    }

    public void AskReference()
    {
        var store = _s.Store;
        var dlg = new ConfirmationDialog { Title = "Place a piece from another pack", OkButtonText = "Place" };
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(420, 360) };
        var packs = new OptionButton();
        var ids = store.ListPackIds().Where(i => i != Pack.Id).OrderBy(i => store.Pack(i)?.Name ?? i).ToList();
        foreach (var id in ids) packs.AddItem(store.Pack(id)?.Name ?? id);
        box.AddChild(packs);
        var list = new ItemList { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        box.AddChild(list);
        void Fill()
        {
            list.Clear();
            if (packs.Selected < 0 || packs.Selected >= ids.Count) return;
            foreach (var d in store.Pack(ids[packs.Selected])!.Pieces.OrderBy(p => p.Name)) { list.AddItem($"{d.Name}   {d.Size.W:0.#}×{d.Size.H:0.#}"); list.SetItemMetadata(list.ItemCount - 1, d.Id); }
        }
        packs.ItemSelected += _ => Fill();
        if (ids.Count > 0) { packs.Selected = 0; Fill(); }
        box.AddChild(Ui.Hint("Placed next to the camera target for size comparison; it is not part of this pack."));
        dlg.AddChild(box);
        dlg.Confirmed += () =>
        {
            var sel = list.GetSelectedItems();
            if (sel.Length == 0) return;
            _s.SetView("table");
            var at = Coords.FromWorld(T.Rig.Target);
            S.Do(new SpawnPiece { Layer = Layers.Umpire, Def = list.GetItemMetadata(sel[0]).AsString(), Pos = at + new Vec2(3, 0), RotDeg = 180 });
        };
        Ui.Open(_s.Root, dlg);
        Ui.FreeOnClose(dlg);
    }
}
