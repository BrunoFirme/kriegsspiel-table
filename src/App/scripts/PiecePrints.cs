using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed class PiecePrints
{
    readonly PieceNode _piece;
    readonly List<Node3D> _nodes = new();
    readonly List<(Rect2 rect, string name, int value)> _front = new(), _back = new();
    readonly FaceInk _ink;
    string? _key;

    public PiecePrints(PieceNode piece) { _piece = piece; _ink = new FaceInk(piece, _nodes); }

    public void Sync()
    {
        var key = Key();
        if (key == _key) return;
        Clear();
        _key = key;
        if (key.Length > 0) Build();
    }

    public void Forget()
    {
        _nodes.Clear();
        _front.Clear();
        _back.Clear();
        _key = null;
    }

    public void ForPicture(bool on)
    {
        foreach (var n in _nodes)
            if (n is GeometryInstance3D gi && GodotObject.IsInstanceValid(gi)) gi.VisibilityRangeEnd = on ? 0 : _piece.LetteringRange();
    }

    public (string name, int value)? ButtonAt(Vector2 local, bool back)
    {
        foreach (var (rect, name, value) in back ? _back : _front)
            if (rect.HasPoint(local)) return (name, value);
        return null;
    }

    void Clear()
    {
        foreach (var n in _nodes) if (GodotObject.IsInstanceValid(n)) n.QueueFree();
        _nodes.Clear();
        _front.Clear();
        _back.Clear();
    }

    bool Shows => _piece.Def is { Kind: not "model" } && !_piece.IsDie && !_piece.IsEffect && _piece.Lettered && _piece.Look.ShowFaceText;

    string Key()
    {
        if (!Shows) return "";
        var d = _piece.Def!; var p = _piece.Piece;
        var parts = new List<string>();
        foreach (var c in d.Counters.Where(c => c.Place != null))
            parts.Add($"c{Place(c.Place!)}|{c.Name}|{p.Counters.GetValueOrDefault(c.Name, c.Default)}|{c.Min}|{c.Max}|{c.ShowName}|{c.ShowButtons}");
        foreach (var t in d.TextLines.Where(t => t.Place != null))
            parts.Add($"t{Place(t.Place!)}|{PieceNode.TextOf(p, t)}");
        if (parts.Count == 0) return "";
        return $"{_piece.Footprint}|{_piece.Thickness}|{_piece.Look.FaceTextScale}\u001e" + string.Join("\u001f", parts);
    }

    static string Place(PrintPlace pl) => $"{pl.Side}:{pl.X:0.####},{pl.Y:0.####},{pl.W:0.####},{pl.H:0.####}";

    Rect2 Box(PrintPlace pl)
    {
        float w = _piece.Footprint.X, h = _piece.Footprint.Y;
        return new Rect2(-w / 2 + (float)pl.X * w, -h / 2 + (float)pl.Y * h, Mathf.Max(0.001f, (float)pl.W * w), Mathf.Max(0.001f, (float)pl.H * h));
    }

    void Build()
    {
        var d = _piece.Def!; var p = _piece.Piece;
        foreach (var c in d.Counters.Where(c => c.Place != null))
        {
            bool back = c.Place!.Side == PrintPlace.Back;
            _ink.CounterRow(back, Box(c.Place), new FaceInk.Counter(c.Name, p.Counters.GetValueOrDefault(c.Name, c.Default), c.Min, c.Max, c.ShowName, c.ShowButtons), back ? _back : _front);
        }
        float scale = Mathf.Max(0.01f, _piece.Look.FaceTextScale);
        foreach (var t in d.TextLines.Where(t => t.Place != null))
        {
            var text = PieceNode.TextOf(p, t);
            if (text.Length == 0) continue;
            var r = Box(t.Place!);
            _ink.Text(t.Place!.Side == PrintPlace.Back, text, r.GetCenter().X, r.GetCenter().Y, r.Size.X * 0.94f, r.Size.Y * 0.9f * scale, FaceInk.TextColor, HorizontalAlignment.Center);
        }
    }
}
