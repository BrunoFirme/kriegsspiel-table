using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Client.Studio;

namespace KTable.App;

internal sealed class ModelPartView
{
    public required ModelPart Part;
    public required MeshData Data;
    public MeshInstance3D Node = null!;
    public ShaderMaterial Mat = null!;
    public required Image Base;
    public int W, H;
    public required byte[] Paint;
    public Image PaintImg = null!;
    public ImageTexture PaintTex = null!;
    public bool PaintDirty;
    public Vector3[] Centre = null!;
    public float[] Radius = null!;
    public string? Face;
    public bool Fixed;
    public string? OpenSha;
    public bool FlipFront;
    public string Layer = ArtDoc.NewId();
    public byte[]? Saved;
    public Transform3D Xf = Transform3D.Identity;
    public MeshInstance3D? TwinNode;
    public int Build, SavedBuild, Gen;

    public string StrokeKey => Build > 0 ? Part.Id + "#" + Build : Part.Id;
    public Transform3D[] Places => TwinNode is { } t ? new[] { Xf, t.Transform } : new[] { Xf };

    int[]? _margins;
    object? _marginsOf;

    public int[] Margins()
    {
        if (_margins == null || !ReferenceEquals(_marginsOf, Data.UV) || _margins.Length != W * H)
        {
            _margins = ModelBake.MarginMap(Data.UV, Data.Tris, W, H);
            _marginsOf = Data.UV;
        }
        return _margins;
    }

    public byte[] Shown() => Part.Kind == "block" ? Paint : ModelBake.WithMargins(Paint, Margins());

    public void Repaint()
    {
        PaintDirty = true;
        Gen++;
        Node.SetMeta("upload", true);
    }
}

internal sealed class ModelHistory
{
    readonly List<(string part, byte[] before, byte[]? after)> _stroke = new();
    readonly List<object> _undo = new(), _redo = new();
    readonly List<long> _undoAt = new(), _redoAt = new();

    public long UndoStamp => _undoAt.Count > 0 ? _undoAt[^1] : 0;
    public long RedoStamp => _redoAt.Count > 0 ? _redoAt[^1] : 0;

    public void BeginStroke(IEnumerable<ModelPartView> views) { _stroke.Clear(); foreach (var v in views) _stroke.Add((v.StrokeKey, (byte[])v.Paint.Clone(), null)); }

    public bool EndStroke(IEnumerable<ModelPartView> all)
    {
        var views = all.GroupBy(v => v.StrokeKey).ToDictionary(g => g.Key, g => g.First());
        var changed = _stroke.Where(s => views.TryGetValue(s.part, out var v) && v.Paint.Length == s.before.Length && !s.before.AsSpan().SequenceEqual(v.Paint))
            .Select(s => (s.part, s.before, (byte[]?)(byte[])views[s.part].Paint.Clone())).ToList();
        _stroke.Clear();
        if (changed.Count == 0) return false;
        Add(changed);
        return true;
    }

    public void Add(List<(string part, byte[] before, byte[]? after)> strokes)
    {
        Push(strokes);
    }

    public void Snapshot(string doc)
    {
        Push(doc);
    }

    void Push(object step)
    {
        _undo.Add(step);
        _undoAt.Add(StudioClock.Next());
        _redo.Clear();
        _redoAt.Clear();
        Trim();
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        _undoAt.Clear();
        _redoAt.Clear();
    }

    public void ForgetUndo() { _undo.Clear(); _undoAt.Clear(); }

    public IEnumerable<string> Texts => _undo.Concat(_redo).OfType<string>();

    public object? Take(bool undo)
    {
        var from = undo ? _undo : _redo;
        var at = undo ? _undoAt : _redoAt;
        if (from.Count == 0) return null;
        var e = from[^1];
        from.RemoveAt(from.Count - 1);
        at.RemoveAt(at.Count - 1);
        return e;
    }

    public void Give(bool undo, object step)
    {
        (undo ? _redo : _undo).Add(step);
        (undo ? _redoAt : _undoAt).Add(StudioClock.Next());
    }

    void Trim()
    {
        static long Weight(object e) => e switch
        {
            string s => s.Length * 2L,
            List<(string part, byte[] before, byte[]? after)> l => l.Sum(x => (long)x.before.Length + (x.after?.Length ?? 0)),
            _ => 0,
        };
        long total = _undo.Sum(Weight);
        while (_undo.Count > 1 && (_undo.Count > 40 || total > (320L << 20)))
        {
            total -= Weight(_undo[0]);
            _undo.RemoveAt(0);
            _undoAt.RemoveAt(0);
        }
    }
}
