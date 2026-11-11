using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Client;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

internal sealed class MarkerPlacer
{
    readonly PieceStudio _s;
    PieceDef? _markerDef;
    readonly Dictionary<int, string> _markerIds = new();
    double _markerTick;

    public MarkerPlacer(PieceStudio s) => _s = s;

    public bool Active { get; private set; }

    Session S => _s.S;
    Table T => _s.T;

    public bool TryMarker(int index, out string id) => _markerIds.TryGetValue(index, out id!);

    public void BuildTab(VBoxContainer p)
    {
        var d = _s.Sel!;
        if (Active) p.AddChild(Ui.Primary("Done placing", () => { End(); _s.RebuildInspector(); }, "Takes the markers off the paper; their places are kept"));
        else if (d.Attachments.Count > 0) p.AddChild(Ui.Btn("Place them by hand on the table…", Begin, "Puts the piece's markers on the paper beside it: drag them where they belong"));
        for (int i = 0; i < d.Attachments.Count; i++)
        {
            var at = d.Attachments[i];
            var ai = i;
            var ar = new HBoxContainer();
            ar.AddChild(new Label { Text = _s.Store.Def(at.Piece)?.Name ?? at.Piece, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ClipText = true, CustomMinimumSize = new Vector2(70, 0) });
            var ox = new SpinBox { MinValue = -50, MaxValue = 50, Step = 0.01, Value = Math.Round(at.Offset.X, 3), Prefix = "x", CustomMinimumSize = new Vector2(84, 0), TooltipText = "To the piece's right (units)" };
            var oz = new SpinBox { MinValue = -50, MaxValue = 50, Step = 0.01, Value = Math.Round(at.Offset.Z, 3), Prefix = "z", CustomMinimumSize = new Vector2(84, 0), TooltipText = "Ahead of the piece (units)" };
            ox.ValueChanged += v => { _s.Apply(x => x.Attachments[ai].Offset = new Vec2(v, x.Attachments[ai].Offset.Z)); Place(ai); };
            oz.ValueChanged += v => { _s.Apply(x => x.Attachments[ai].Offset = new Vec2(x.Attachments[ai].Offset.X, v)); Place(ai); };
            ar.AddChild(ox);
            ar.AddChild(oz);
            ar.AddChild(Ui.Btn("✕", () => { if (Active) End(); _s.Apply(x => x.Attachments.RemoveAt(ai), inspector: true); }, "remove"));
            p.AddChild(ar);
        }
        var addM = new OptionButton { Text = "Add a marker…", FitToLongestItem = false };
        foreach (var o in _s.Pack.Pieces.Where(x => x != d).OrderBy(x => x.Name)) { addM.AddItem(o.Name); addM.SetItemMetadata(addM.ItemCount - 1, o.Id); }
        addM.Selected = -1;
        addM.ItemSelected += i =>
        {
            _s.Apply(x => x.Attachments.Add(new AttachmentDef { Piece = addM.GetItemMetadata((int)i).AsString(), Offset = new Vec2(x.Size.W * 0.8, 0) }), inspector: true);
            if (Active) { End(); Begin(); }
        };
        if (addM.ItemCount > 0) p.AddChild(addM);
        else p.AddChild(Ui.Hint("No other piece in the pack"));
    }

    public void Begin()
    {
        var sel = _s.Sel;
        if (sel == null || _s.Paper.Instance(sel) is not { } basePiece) return;
        _s.SetView("table");
        Active = true;
        _markerDef = sel;
        _markerIds.Clear();
        var cmds = new List<Command>();
        for (int i = 0; i < sel.Attachments.Count; i++)
        {
            var at = sel.Attachments[i];
            if (_s.Store.Def(at.Piece) == null) continue;
            var id = S.View.NewId();
            _markerIds[i] = id;
            cmds.Add(new SpawnPiece { Layer = Layers.Umpire, Def = at.Piece, Pos = MarkerPlace.World(basePiece.Pos, basePiece.RotDeg, at.Offset), RotDeg = basePiece.RotDeg, Id = id });
        }
        if (cmds.Count > 0) S.Do(new Batch { Items = cmds });
        T.Rig.Frame(Coords.ToWorld(basePiece.Pos), (float)Math.Max(8, Math.Max(sel.Size.W, sel.Size.H) * 6));
        _s.Flash("Drag the markers where they belong beside the piece; their places are written as you go. Done placing takes them off.");
        _s.RebuildInspector();
        _s.Inspector.Tabs.CurrentTab = Math.Max(0, _s.Inspector.TabIndex("Piece"));
    }

    void Place(int ai)
    {
        var sel = _s.Sel;
        if (!Active || sel == null || _s.Paper.Instance(sel) is not { } b || !_markerIds.TryGetValue(ai, out var id) || S.View.Find(Layers.Umpire, id) is not { } m) return;
        var to = MarkerPlace.World(b.Pos, b.RotDeg, sel.Attachments[ai].Offset);
        if ((to - m.Pos).Length > 1e-3) S.Do(new MovePiece { Layer = Layers.Umpire, Id = id, Pos = to, RotDeg = b.RotDeg });
    }

    public void Watch(double delta)
    {
        _markerTick -= delta;
        if (_markerTick > 0) return;
        _markerTick = 0.2;
        var d = _markerDef;
        if (d == null || d != _s.Sel || _s.Paper.Instance(d) is not { } b) { End(); return; }
        bool changed = false;
        foreach (var (ai, id) in _markerIds)
        {
            if (ai >= d.Attachments.Count || S.View.Find(Layers.Umpire, id) is not { } m) continue;
            var off = MarkerPlace.Offset(b.Pos, b.RotDeg, m.Pos);
            off = new Vec2(Math.Round(off.X, 3), Math.Round(off.Z, 3));
            if ((off - d.Attachments[ai].Offset).Length > 0.004) { d.Attachments[ai].Offset = off; changed = true; }
        }
        if (!changed) return;
        _s.SavePack();
        if (!_s.Typing()) _s.RebuildInspector();
    }

    public void End()
    {
        if (!Active) return;
        Active = false;
        _markerDef = null;
        var del = _markerIds.Values.Where(id => S.View.Find(Layers.Umpire, id) != null).Select(id => (Command)new DeletePiece { Layer = Layers.Umpire, Id = id }).ToList();
        if (del.Count > 0) S.Do(new Batch { Items = del });
        _markerIds.Clear();
    }
}
