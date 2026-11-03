using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Protocol;
using KTable.Core.Model;

namespace KTable.App;

sealed class DrawingLayer
{
    readonly TableContext _c;
    readonly Dictionary<string, MeshInstance3D> _drawings = new();
    public Node3D Root = null!;

    public DrawingLayer(TableContext c) { _c = c; }

    public string Board = "";
    public readonly HashSet<string> HiddenBoards = new();

    public string BoardName(string id) => _c.S.View.Boards.FirstOrDefault(b => b.Id == id)?.Name ?? "That layer";

    public string DrawBoard()
    {
        var boards = _c.S.View.Boards;
        if (Board.Length > 0 && boards.Any(b => b.Id == Board)) return Board;
        return boards.Count > 0 ? boards[0].Id : "";
    }

    public bool BoardShown(string board) => board.Length == 0 || !HiddenBoards.Contains(board);

    public bool Locked => !BoardShown(DrawBoard());

    public bool OnDrawBoard(string board) => BoardShown(board) && (board.Length == 0 || board == DrawBoard());

    public void Sync()
    {
        foreach (var d in _drawings.Values) d.QueueFree();
        _drawings.Clear();
        foreach (var (layer, strokes) in _c.S.View.Drawings)
        {
            if (!_c.LayerShown(layer)) continue;
            if (strokes.Count == 0) continue;
            var im = new ImmediateMesh();
            foreach (var st in strokes)
            {
                if (st.Points.Count < 2 || !BoardShown(st.Board)) continue;
                var mat = new StandardMaterial3D { AlbedoColor = Coords.ParseColor(st.Color, Colors.Black), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
                Ribbon.Draw(im, mat, st.Points, st.Width, 0.025f, _c.S.Ground);
            }
            var mi = new MeshInstance3D { Mesh = im, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            Root.AddChild(mi);
            _drawings[layer] = mi;
        }
    }

    public void Reground() => Sync();

    public int DrawnSurfaces() => _drawings.Values.Sum(mi => mi.Mesh is ImmediateMesh im ? im.GetSurfaceCount() : 0);

    public int DrawingCount() => _c.S.View.Drawings.Where(d => _c.S.CanEdit(d.Key)).Sum(d => d.Value.Count(s => OnDrawBoard(s.Board)));

    public void EraseAll()
    {
        var cmds = new List<Command>();
        var board = DrawBoard();
        foreach (var (layer, strokes) in _c.S.View.Drawings)
        {
            if (!_c.S.CanEdit(layer)) continue;
            foreach (var st in strokes.Where(x => OnDrawBoard(x.Board)).ToList()) cmds.Add(new EraseStroke { Layer = layer, StrokeId = st.Id });
        }
        _c.DoAll(cmds);
    }
}
