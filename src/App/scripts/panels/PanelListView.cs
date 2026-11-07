using System.Collections.Generic;
using System.Globalization;
using Godot;
using KTable.Client.Panels;

namespace KTable.App;

sealed partial class PanelView
{
    PanelBox? _list, _listOwner;
    Control? _listHolder, _blocker;
    const string ListOpen = "#list";
    const float ListFade = 0.15f;

    void OpenList(PanelBox owner, bool fade = true)
    {
        if (_list != null || _scene == null || _top == null || owner.Path == null || !Interactable(owner) || !Nodes.TryGetValue(owner, out var node)) return;
        if ((owner.Element?.Options?.Count ?? 0) == 0) return;
        int value = PanelScene.DropdownIndex(owner, owner.Element!, _local.GetValueOrDefault(Key(owner)));
        var list = PanelLists.Open(_scene, owner, value);
        _list = list;
        _listOwner = owner;
        _local[ListOpen] = Key(owner);
        _blocker = new Control { MouseFilter = Control.MouseFilterEnum.Stop, Size = _top.Size };
        _blocker.GuiInput += e =>
        {
            if (e is not InputEventMouseButton { Pressed: false }) return;
            _blocker?.AcceptEvent();
            CloseList();
        };
        _top.AddChild(_blocker);
        var holder = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        var rel = _top.GetGlobalTransform().AffineInverse() * node.GetGlobalTransform();
        if (!_top.IsInsideTree()) rel = Relative(node);
        holder.Position = rel.Origin;
        holder.Rotation = rel.Rotation;
        holder.Scale = rel.Scale;
        holder.Size = node.Size;
        holder.AddChild(Node(list, owner));
        _top.AddChild(holder);
        _listHolder = holder;
        if (!fade) return;
        holder.Modulate = Colors.White with { A = 0 };
        holder.CreateTween().TweenProperty(holder, "modulate:a", 1f, ListFade);
    }

    Transform2D Relative(Control node, Control? stop = null)
    {
        var t = Transform2D.Identity;
        for (Control? at = node; at != null && at != _top && at != stop; at = at.GetParentControl()) t = at.GetTransform() * t;
        return t;
    }

    void Choose(PanelBox item, bool down)
    {
        if (down)
        {
            _pressed = item;
            Repaint(item);
            return;
        }
        bool clicked = _pressed == item;
        _pressed = null;
        Repaint(item);
        if (!clicked || _listOwner is not { } owner) return;
        int index = PanelValues.Int(item.Extra.GetValueOrDefault(PanelLists.Index));
        int was = PanelScene.DropdownIndex(owner, owner.Element!, _local.GetValueOrDefault(Key(owner)));
        CloseList();
        if (was == index) return;
        _local[Key(owner)] = index.ToString(CultureInfo.InvariantCulture);
        Fire(owner, "onValueChanged", index.ToString(CultureInfo.InvariantCulture));
        Redraw();
    }

    void CloseList()
    {
        if (_list == null) return;
        var owner = _listOwner;
        var holder = _listHolder;
        if (_blocker != null && GodotObject.IsInstanceValid(_blocker)) _blocker.QueueFree();
        _blocker = null;
        _listHolder = null;
        Forget(_list);
        _list = null;
        _listOwner = null;
        _local.Remove(ListOpen);
        if (holder != null && GodotObject.IsInstanceValid(holder))
        {
            var tween = holder.CreateTween();
            tween.TweenProperty(holder, "modulate:a", 0f, ListFade);
            tween.TweenCallback(Callable.From(() =>
            {
                if (GodotObject.IsInstanceValid(holder)) holder.QueueFree();
            }));
        }
        if (owner != null && Alive(owner)) Select(owner);
    }

    void Forget(PanelBox b)
    {
        Nodes.Remove(b);
        _hovered.Remove(b);
        _motions.Remove(b);
        b.Parent?.Children.Remove(b);
        foreach (var c in new List<PanelBox>(b.Children)) Forget(c);
    }

    void RestoreList(PanelScene scene)
    {
        if (_local.GetValueOrDefault(ListOpen) is not { } key) return;
        var owner = scene.Elements.Find(e => e.Path != null && PanelScene.Key(e.Path) == key && Nodes.ContainsKey(e));
        if (owner == null)
        {
            _local.Remove(ListOpen);
            return;
        }
        Callable.From(() => OpenList(owner, false)).CallDeferred();
    }
}
