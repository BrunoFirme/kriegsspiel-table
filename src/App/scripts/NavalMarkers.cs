using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace KTable.App;

public static class NavalMarkers
{
    public sealed record Look(string Name, Image Face);
    public sealed record Marker(string Name, double W, double H, Look[] Looks);

    const int Px = 160, Sub = 4;
    const float Body = 0.085f, NoseFrom = 0.8f, TailTo = 0.16f, FinFrom = 0.035f, FinTo = 0.085f, FinSpan = 0.12f, PropSpan = 0.1f, Border = 0.045f;

    static readonly Color Sea = new("#1d3346"), Edge = new("#9fb4c4"), Hull = new("#eef2f4"), Wake = new("#9fc3d9");
    enum Keep { Plate, Frame, Marks }

    static readonly (string Name, Keep Keep)[] Cuts = { ("Solid", Keep.Plate), ("Frame", Keep.Frame), ("Open", Keep.Marks) };

    static readonly Color SpentSea = new("#3e4449"), SpentEdge = new("#6d757b"), SpentHull = new("#8b9298"), Cross = new("#c4503f");

    public static IEnumerable<Marker> All()
    {
        var one = new[] { (0f, 0f) };
        yield return Make("Torpedo marker", 0.5, 1.5, 1, one);
        var fan = new[] { (-16f, 0f), (0f, 0f), (16f, 0f) };
        yield return Make("Torpedo spread marker", 1.2, 1.5, 2.4f, fan);
    }

    static Marker Make(string name, double w, double h, float wide, (float deg, float x)[] torpedoes)
    {
        var running = Paint(wide, 3, torpedoes, false);
        var spent = Paint(wide, 3, torpedoes, true);
        return new Marker(name, w, h, Cuts.Select(c => new Look(c.Name, Cut(running, c.Keep))).Append(new Look("Spent", spent.img)).ToArray());
    }

    static Image Cut((Image img, float[] marks, float[] rim) painted, Keep keep)
    {
        var (src, marks, rim) = painted;
        if (keep == Keep.Plate) return src;
        var img = (Image)src.Duplicate();
        int w = img.GetWidth();
        for (int i = 0; i < marks.Length; i++)
        {
            float a = keep == Keep.Frame ? Mathf.Max(marks[i], rim[i]) : marks[i];
            img.SetPixel(i % w, i / w, img.GetPixel(i % w, i / w) with { A = a });
        }
        return img;
    }

    static (Image img, float[] marks, float[] rim) Paint(float wide, float tall, (float deg, float x)[] torpedoes, bool spent)
    {
        int w = (int)(Px * wide), h = (int)(Px * tall);
        var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        var marks = new float[w * h];
        var rims = new float[w * h];
        var sea = spent ? SpentSea : Sea;
        var edge = spent ? SpentEdge : Edge;
        var hull = spent ? SpentHull : Hull;
        float len = tall * 0.62f, launchY = tall * 0.9f;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float onHull = 0, onWake = 0, onArrow = 0, onRim = 0, onCross = 0;
                for (int sy = 0; sy < Sub; sy++)
                    for (int sx = 0; sx < Sub; sx++)
                    {
                        float px = (x + (sx + 0.5f) / Sub) / Px, py = (y + (sy + 0.5f) / Sub) / Px;
                        if (px < Border || py < Border || px > wide - Border || py > tall - Border) onRim++;
                        foreach (var (deg, x0) in torpedoes)
                        {
                            float a = Mathf.DegToRad(deg), ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                            float dx = px - (wide / 2 + x0), dy = launchY - py;
                            float u = dx * ca - dy * sa, v = dx * sa + dy * ca;
                            float t = v / len - 0.18f;
                            if (InTorpedo(u / len, t)) onHull++;
                            else if (t < 0 && t > -0.2f && Mathf.Abs(u / len) < 0.02f + 0.03f * -t && Mathf.PosMod(t * 30, 2) < 1.1f) onWake++;
                            else if (InArrow(u / len, t)) onArrow++;
                        }
                        if (spent && Mathf.Min(Mathf.Abs(px / wide - py / tall), Mathf.Abs(px / wide - (1 - py / tall))) * Mathf.Min(wide, tall) < 0.035f
                            && px > Border * 2 && px < wide - Border * 2 && py > Border * 2 && py < tall - Border * 2) onCross++;
                    }
                float n = Sub * Sub;
                var c = sea.Lerp(edge, onRim / n);
                c = c.Lerp(spent ? SpentEdge : Wake, Mathf.Min(1, onWake / n));
                c = c.Lerp(hull, Mathf.Min(1, (onHull + onArrow) / n));
                c = c.Lerp(Cross, onCross / n);
                img.SetPixel(x, y, c);
                marks[y * w + x] = Mathf.Min(1, (onHull + onArrow + onWake + onCross) / n);
                rims[y * w + x] = onRim / n;
            }
        return (img, marks, rims);
    }

    static bool InTorpedo(float u, float t)
    {
        float r = Body, au = Mathf.Abs(u);
        if (t >= TailTo && t <= NoseFrom) return au <= r;
        if (t > NoseFrom && t <= 1) { float k = (t - NoseFrom) / (1 - NoseFrom); return au <= r * Mathf.Sqrt(1 - k * k); }
        if (t >= 0.04f && t < TailTo)
        {
            float k = (t - 0.04f) / (TailTo - 0.04f);
            if (au <= r * (0.4f + 0.6f * k)) return true;
        }
        if (t >= FinFrom && t <= FinTo) return au <= FinSpan - (FinSpan - Body * 0.5f) * (t - FinFrom) / (FinTo - FinFrom);
        return t >= 0 && t < 0.04f && (au <= PropSpan && t > 0.012f && t < 0.028f || au <= r * 0.3f);
    }

    static bool InArrow(float u, float t)
    {
        const float From = 1.06f, To = 1.2f, Half = 0.12f, Thick = 0.035f;
        if (t < From || t > To) return false;
        float along = To - t, edge = Mathf.Abs(u) * (To - From) / Half;
        return Mathf.Abs(along - edge) < Thick && Mathf.Abs(u) <= Half;
    }
}
