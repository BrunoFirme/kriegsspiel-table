using System;
using System.Collections.Generic;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed class TableContext
{
    readonly Action<Tool, string?> _setTool;

    public TableContext(Node3D scene, Action<Tool, string?> setTool) { Scene = scene; _setTool = setTool; Sound = new TableSounds(this); }

    public Node3D Scene { get; }

    public Session S { get; set; } = null!;
    public Settings Settings { get; set; } = null!;
    public TableSounds Sound { get; }
    public Action<PieceNode> Moving { get; set; } = _ => { };
    public CameraRig Rig { get; set; } = null!;
    public ScreenLabels Labels { get; set; } = null!;

    public Tool Tool { get; set; } = Tool.Select;
    public string? PlaceDef { get; set; }

    public void SetTool(Tool t, string? placeDef = null) => _setTool(t, placeDef);

    public event Action<string>? Hint;
    public event Action? SelectionChanged;
    public event Action<Rect2?>? ScreenRect;

    public void Say(string hint) => Hint?.Invoke(hint);
    public void NotifySelectionChanged() => SelectionChanged?.Invoke();
    public void NotifyScreenRect(Rect2? r) => ScreenRect?.Invoke(r);

    public bool LayerShown(string layer) => layer == Layers.Umpire && S.SeesBoard;

    public bool LayerInteractive(string layer) => S.CanEdit(layer);

    public Vec2? Ground(Vector2 screen) => Rig.GroundAt(screen) is { } w ? Coords.FromWorld(w) : null;

    public void DoAll(List<Command> cmds)
    {
        if (cmds.Count == 1) S.Do(cmds[0]); else if (cmds.Count > 1) S.Do(new Batch { Items = cmds });
    }
}
