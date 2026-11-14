using System;
using System.Collections.Generic;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class Writing
{
    const float GlyphWide = 0.5f;

    public static float Span(string text, float em)
    {
        int longest = 0, lines = 1, run = 0;
        foreach (char c in text)
        {
            if (c == '\n') { lines++; run = 0; }
            else longest = Math.Max(longest, ++run);
        }
        return longest == 0 ? 0 : Math.Max(longest * em * GlyphWide, lines * em);
    }
}

sealed class PieceReach
{
    const float Unmeasured = -1, Nearest = 12f, Farthest = 3000f, PieceCountsUpTo = 12f;

    readonly PieceNode _piece;
    float _span = Unmeasured;
    List<Widget>? _widgets;
    KTable.Core.Model.Lettering? _lettering;

    public PieceReach(PieceNode piece) => _piece = piece;

    public void Forget() => _span = Unmeasured;

    public float WritingSpan
    {
        get
        {
            var p = _piece.Piece;
            if (_span >= 0 && ReferenceEquals(_widgets, p.Widgets) && ReferenceEquals(_lettering, p.Lettering)) return _span;
            _widgets = p.Widgets;
            _lettering = p.Lettering;
            float unboxed = _piece.IsDie || _piece.IsEffect ? 0 : PieceWidgets.UnboxedSpan(p, _piece.Look.FaceTextScale);
            return _span = Mathf.Max(unboxed, _piece.Spoken && p.Lettering is { } lt ? ModuleText.Span(lt, p.Scale) : 0);
        }
    }

    public float Range()
    {
        float reach = _piece.Look.LetteringReach;
        float byPiece = Mathf.Clamp(Mathf.Max(_piece.Footprint.X, _piece.Footprint.Y) * reach, Nearest, Mathf.Min(Farthest, reach * PieceCountsUpTo));
        return Mathf.Max(byPiece, Mathf.Min(Farthest, WritingSpan * reach));
    }
}
