using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Client.Studio;
using KTable.Core.Model;

namespace KTable.App;

sealed class PieceBack
{
    readonly PieceNode _piece;
    readonly List<Node3D> _card = new();
    readonly List<(Rect2 rect, string name, int value)> _buttons = new();
    readonly FaceInk _ink;
    Rect2 _titleRect;
    Label3D? _titleLabel;
    (float x, float z, float maxW, float lineH) _titleLayout;
    bool _built;
    string? _key;

    public PieceBack(PieceNode piece) { _piece = piece; _ink = new FaceInk(piece, _card); }

    public const float Below = 0.004f;
    public static Vector3 Under(float x, float z, float below) => new(-x, -below, z);

    public bool Stale => !_built || Key() != _key;

    public void SetTitleText(string text)
    {
        _key = null;
        if (_titleLabel == null || !GodotObject.IsInstanceValid(_titleLabel)) return;
        _card.Remove(_titleLabel);
        _titleLabel.QueueFree();
        _titleLabel = _ink.Text(true, text, _titleLayout.x, _titleLayout.z, _titleLayout.maxW, _titleLayout.lineH, FaceInk.TextColor, HorizontalAlignment.Center);
    }

    public void Clear()
    {
        foreach (var n in _card) if (GodotObject.IsInstanceValid(n)) n.QueueFree();
        _card.Clear();
        _buttons.Clear();
        _titleRect = new Rect2();
        _titleLabel = null;
        _built = false;
    }

    public void Forget()
    {
        _card.Clear();
        _buttons.Clear();
        _built = false;
    }

    public bool TitleAt(Vector2 local) => _titleRect.Size.X > 0 && _titleRect.HasPoint(local);

    public (string name, int value)? ButtonAt(Vector2 local)
    {
        foreach (var (rect, name, value) in _buttons)
            if (rect.HasPoint(local)) return (name, value);
        return null;
    }

    public bool HasButtons => _buttons.Count > 0;

    List<FaceInk.Counter> Rows()
    {
        var def = _piece.Def; var p = _piece.Piece;
        return def != null && def.Counters.Count > 0
            ? def.Counters.Where(c => c.Place == null).Select(c => new FaceInk.Counter(c.Name, p.Counters.GetValueOrDefault(c.Name, c.Default), c.Min, c.Max, c.ShowName, c.ShowButtons)).ToList()
            : p.Counters.Select(kv => new FaceInk.Counter(kv.Key, kv.Value, -999, 999, true, true)).ToList();
    }

    bool Titled => _piece.Def == null || PiecePrinting.CardTitle(_piece.Def);

    string Key() => string.Join("\u001f", Rows().Select(r => r.ToString())
        .Prepend($"{_piece.CardTitle}\u001e{Titled}\u001e{_piece.Look.ShowFaceText}\u001e{_piece.Def?.Kind}\u001e{_piece.Piece.Script != null || _piece.Def?.Script != null}\u001e{_piece.Footprint}\u001e{_piece.Boxy}"));

    public void Build()
    {
        Clear();
        _built = true;
        _key = Key();
        var def = _piece.Def;
        if (def?.Kind == "model" || !_piece.Look.ShowFaceText) return;
        if (_piece.Piece.Script != null || def?.Script != null) return;
        var rows = Rows();
        bool titled = Titled;
        if (rows.Count == 0 && !titled) return;
        float w = _piece.Footprint.X, h = _piece.Footprint.Y;
        Rect2 R(FaceBox b) => new(-w / 2 + (float)b.X * w, -h / 2 + (float)b.Y * h, (float)b.W * w, (float)b.H * h);
        var (title, boxes) = PiecePrinting.Card(_piece.Boxy, titled, rows.Count);
        if (title != null)
        {
            var t = R(title);
            float zc = t.GetCenter().Y, cw = t.Size.X, titleH = t.Size.Y;
            _ink.Plane(true, 0, zc, cw * 1.04f, titleH * 0.82f, FaceInk.BandMat, FaceInk.BandLift);
            _titleLabel = _ink.Text(true, _piece.CardTitle, 0, zc, cw, titleH * 0.55f, FaceInk.TextColor, HorizontalAlignment.Center);
            _titleLayout = (0, zc, cw, titleH * 0.55f);
            _titleRect = t;
        }
        for (int i = 0; i < rows.Count; i++)
        {
            var r = R(boxes[i]);
            _ink.Plane(true, 0, r.GetCenter().Y, r.Size.X * 1.04f, r.Size.Y * 0.86f, FaceInk.BandMat, FaceInk.BandLift);
            _ink.CounterRow(true, r, rows[i], _buttons);
        }
    }
}
