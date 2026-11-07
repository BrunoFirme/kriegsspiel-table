using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Client.Panels;

namespace KTable.App;

sealed partial class PanelView
{
    readonly List<(Control Holder, Control Source, Control? From)> _lifted = new();

    void Lift(PanelBox root)
    {
        _lifted.Clear();
        if (_top == null) return;
        var sorted = Nodes.Keys.Where(b => b.Tag == "Canvas" && b != root && b.Extra.TryGetValue("sortingorder", out var o) && PanelValues.Int(o) != 0)
            .Select(b => (Box: b, Order: PanelValues.Int(b.Extra["sortingorder"]))).OrderBy(p => p.Order).ToList();
        int below = 0;
        foreach (var (box, order) in sorted)
        {
            if (!Nodes.TryGetValue(box, out var node) || node.GetParentControl() is not { } parent) continue;
            var holder = Holder(box, parent);
            parent.RemoveChild(node);
            holder.Inner.AddChild(node);
            _top.AddChild(holder.Outer);
            if (order < 0) _top.MoveChild(holder.Outer, below++);
        }
    }

    (Control Outer, Control Inner) Holder(PanelBox box, Control parent)
    {
        var clips = new List<(PanelBox Box, Control Node)>();
        for (var at = box.Parent; at != null; at = at.Parent)
            if (at.RectMask && Nodes.TryGetValue(at, out var n) && n != _top) clips.Insert(0, (at, n));
        Control outer = new() { MouseFilter = Control.MouseFilterEnum.Ignore }, inner = outer;
        Control? from = null;
        foreach (var (clipBox, clipNode) in clips)
        {
            var clip = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, ClipContents = true, Size = clipNode.Size };
            Place(clip, Relative(clipNode, from));
            _lifted.Add((clip, clipNode, from));
            inner.AddChild(clip);
            inner = clip;
            from = clipNode;
        }
        var last = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Size = parent.Size };
        Place(last, Relative(parent, from));
        _lifted.Add((last, parent, from));
        inner.AddChild(last);
        return (outer, last);
    }

    void Follow()
    {
        foreach (var (holder, source, from) in _lifted)
        {
            if (!GodotObject.IsInstanceValid(holder) || !GodotObject.IsInstanceValid(source)) continue;
            Place(holder, Relative(source, from));
            holder.Size = source.Size;
        }
    }

    static void Place(Control c, Transform2D t)
    {
        c.Position = t.Origin;
        c.Rotation = t.Rotation;
        c.Scale = t.Scale;
    }
}
