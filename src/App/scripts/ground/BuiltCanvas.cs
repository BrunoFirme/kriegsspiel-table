using System.Collections.Generic;
using System.Linq;
using Godot;

using static KTable.App.BuiltShapes;

namespace KTable.App;

abstract partial class BuiltCanvas : Node2D
{
    protected const float LobedFromPx = 7, KeyScale = 4096;
    const float CrownReach = 1.25f, FoliageCell = 8, StripInsetPx = 0.5f, SeedStepM = 4, StuffFromPx = 7;
    const int Seeds = 16, FoliageCells = 12, DiscSides = 24;

    protected Surface _roof;
    protected float _m;
    protected Vector2 _key;

    protected float Chance(float salt) => Mathf.PosMod(Mathf.Sin(_key.X * 12.9898f + _key.Y * 78.233f + salt) * 43758.547f, 1);

    const float LeastFilledPx = 0.02f;

    protected void Filled(Vector2[] px, Color fill)
    {
        if (Mathf.Abs(BuiltShapes.AreaOf(px)) > LeastFilledPx) DrawColoredPolygon(px, fill);
        DrawPolyline(px.Append(px[0]).ToArray(), fill, 1, true);
    }

    float Seed => Mathf.Floor(Chance(77.7f) * Seeds) * SeedStepM;

    protected void Covered(Vector2[] px, Color fill, Surface kind, Vector2 from, Vector2 along, bool wash = false)
    {
        var across = along.Orthogonal();
        float seed = SurfaceBook.Mark + Seed - px.Min(p => (p - from).Dot(along)) / _m;
        DrawColoredPolygon(px, SurfaceBook.Coded(fill, kind, wash), px.Select(p => new Vector2(seed + (p - from).Dot(along) / _m, (p - from).Dot(across) / _m)).ToArray());
        if (!wash) DrawPolyline(px.Append(px[0]).ToArray(), fill, 1, true);
    }

    protected void Opened(IReadOnlyList<RoadMouths.Mouth> mouths, float fine)
    {
        foreach (var mouth in mouths)
        {
            _m = mouth.M;
            _key = mouth.Seed * KeyScale;
            Covered(mouth.Open.Ring, mouth.Fill, mouth.Kind, mouth.Origin, mouth.Into);
        }
        foreach (var mouth in mouths)
        {
            foreach (var corner in mouth.Open.Corners) DrawPolyline(corner, mouth.Ink, fine, true);
            foreach (var track in mouth.Tracks) DrawPolyline(track, mouth.Groove, Mathf.Max(BuiltHand.LeastPx, RoadMouths.RutM * mouth.M), true);
        }
    }

    protected void Surfaced(Vector2[] px, Color fill, Surface kind, bool wash)
    {
        var first = px[1] - px[0];
        var along = px.Length == 4 && first.Length() < (px[2] - px[1]).Length() ? px[2] - px[1] : first;
        if (along.Length() < 0.001f) Filled(px, wash ? fill with { A = 0.5f } : fill);
        else Covered(px, fill, kind, px[0], along.Normalized(), wash);
    }

    protected void Sloped(Vector2[] facet, Color fill, Vector2 centre, bool inward)
    {
        int eave = 0;
        float best = float.MinValue;
        for (int i = 0; i < facet.Length; i++)
        {
            float off = ((facet[i] + facet[(i + 1) % facet.Length]) / 2 - centre).Length() * (inward ? -1 : 1);
            if (off <= best || (facet[(i + 1) % facet.Length] - facet[i]).Length() < 0.001f) continue;
            best = off;
            eave = i;
        }
        var along = facet[(eave + 1) % facet.Length] - facet[eave];
        if (along.Length() < 0.001f) Filled(facet, fill);
        else Covered(facet, fill, _roof, facet[eave], along.Normalized());
    }

    protected void Strip(Vector2[] px, float wide, Color fill, Surface kind) => SurfaceBook.Strip(Meshed, px, wide / 2 - StripInsetPx, _m, Seed, fill, kind);

    protected void Stuffed(Vector2[] ring, Color fill, Surface stuff)
    {
        var first = ring[1] - ring[0];
        if (stuff == Surface.None || fill.A < 1 || first.Length() < 0.001f || ring.Max(p => (p - ring[0]).Length()) < StuffFromPx) DrawColoredPolygon(ring, fill);
        else if (stuff == Surface.Rounded) Lump(ring, fill);
        else Covered(ring, fill, stuff, ring[0], first.Normalized());
    }

    protected void Lump(Vector2[] ring, Color fill)
    {
        var middle = Mid(ring);
        float reach = Mathf.Max(ring.Max(p => (p - middle).Length()), 0.001f);
        var cell = new Vector2(Mathf.Floor(Chance(middle.X) * FoliageCells), Mathf.Floor(Chance(middle.Y) * FoliageCells)) * FoliageCell + new Vector2(SurfaceBook.Mark, 0);
        DrawColoredPolygon(ring, SurfaceBook.Coded(fill, Surface.Rounded), ring.Select(p => cell + (p - middle) / reach).ToArray());
    }

    protected void Disc(Vector2 at, float r, Color fill, Surface stuff)
    {
        if (stuff == Surface.None || r * 2 < StuffFromPx) DrawCircle(at, r, fill);
        else Stuffed(Enumerable.Range(0, DiscSides).Select(i => at + Vector2.Right.Rotated(i * Mathf.Tau / DiscSides) * r).ToArray(), fill, stuff);
    }

    protected void Leafed(Vector2 at, float r, Color leaf, int which = 0)
    {
        if (r < LobedFromPx)
        {
            DrawCircle(at, r, leaf);
            return;
        }
        float reach = r * CrownReach;
        var cell = new Vector2(Mathf.Floor(Chance(0.7f + which) * FoliageCells), Mathf.Floor(Chance(2.9f + which) * FoliageCells)) * FoliageCell + new Vector2(SurfaceBook.Mark, 0);
        Vector2[] corners = [new(-1, -1), new(1, -1), new(1, 1), new(-1, 1)];
        DrawColoredPolygon(corners.Select(c => at + c * reach).ToArray(), SurfaceBook.Coded(leaf, Surface.Foliage), corners.Select(c => cell + c * CrownReach).ToArray());
    }
}
