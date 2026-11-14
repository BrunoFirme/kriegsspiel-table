using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Godot;

namespace KTable.App;

static class ModelTriangles
{
    public const int PerPiece = 2400;

    static readonly ConcurrentDictionary<string, Task<List<Vector3[]>?>> Read = new(StringComparer.OrdinalIgnoreCase);

    public static Task<List<Vector3[]>?> Of(string file) => Read.GetOrAdd(file, f => Task.Run(() => Load(f)));

    static List<Vector3[]>? Load(string file)
    {
        try
        {
            var inv = CultureInfo.InvariantCulture;
            var points = new List<Vector3>();
            var pieces = new List<Vector3[]>();
            var piece = new List<Vector3>(PerPiece * 3);
            var corners = new List<int>();
            foreach (var raw in KTable.Core.Model.PackedModel.Lines(file))
            {
                var line = raw.Trim();
                if (line.StartsWith("v ", StringComparison.Ordinal))
                {
                    var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    points.Add(new Vector3(float.Parse(p[1], inv), float.Parse(p[2], inv), float.Parse(p[3], inv)));
                }
                else if (line.StartsWith("f ", StringComparison.Ordinal))
                {
                    corners.Clear();
                    var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    for (int i = 1; i < p.Length; i++)
                    {
                        var head = p[i];
                        int slash = head.IndexOf('/');
                        int k = int.Parse(slash < 0 ? head : head[..slash], inv);
                        corners.Add(k < 0 ? points.Count + k : k - 1);
                    }
                    for (int i = 1; i + 1 < corners.Count; i++)
                    {
                        if (!Inside(corners[0], points) || !Inside(corners[i], points) || !Inside(corners[i + 1], points)) continue;
                        piece.Add(points[corners[0]]);
                        piece.Add(points[corners[i + 1]]);
                        piece.Add(points[corners[i]]);
                        if (piece.Count < PerPiece * 3) continue;
                        pieces.Add(piece.ToArray());
                        piece.Clear();
                    }
                }
            }
            if (piece.Count > 0) pieces.Add(piece.ToArray());
            return pieces;
        }
        catch (Exception e) when (e is FormatException or OverflowException or System.IO.IOException or UnauthorizedAccessException or IndexOutOfRangeException)
        {
            GD.PrintErr($"[solids] could not read the model's triangles {file}: {e.Message}");
            return null;
        }
    }

    static bool Inside(int k, List<Vector3> points) => k >= 0 && k < points.Count;
}
