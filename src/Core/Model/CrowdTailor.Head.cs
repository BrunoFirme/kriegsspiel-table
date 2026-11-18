using System.Numerics;
using S = KTable.Core.Model.CrowdSlots;

namespace KTable.Core.Model;

sealed partial class Tailor
{
    int Hs => Math.Max(_es1, 3);
    int Hws => _near ? _es0 + 2 : 5;

    void CapHair(float sc = 1.06f) => Add(Loft.Shell(0, Cy + Hh * 0.02f, -Hh * 0.02f, Hw * sc, Hh * 0.52f * sc, Hd * sc, 0.6f * MathF.PI, -0.45f, Hws, Hs), S.Hair);

    void HairAndHat()
    {
        Hair();
        Beard();
        if (_o.Has(CrowdDetails.Curls) && _near)
            foreach (float s in new[] { 1f, -1f })
                foreach (int k in new[] { 0, 1 }) Add(Loft.Ellipsoid(s * Hw * 0.98f, Cy - Hh * (0.04f + 0.13f * k), -Hh * 0.08f, Hh * 0.075f, Hh * 0.06f, Hh * 0.15f, _es0, _es1), S.Hair);
        if (_o.Has(CrowdDetails.Ribbon) && _near) Add(Loft.Ellipsoid(0, Chin - 0.005f, -Hd * 0.95f, Hh * 0.09f, Hh * 0.04f, Hh * 0.03f, 6, 4), S.Accent);
        Hat();
    }

    void Hair()
    {
        switch (_o.Hair)
        {
            case "short":
                CapHair();
                break;
            case "queue":
                CapHair();
                Add(Loft.Rings([new(Chin - 0.03f, 0.012f, 0.012f, 0, -Hd * 0.85f), new(Cy, 0.015f, 0.015f, 0, -Hd * 0.98f)], _lseg, bottom: true), S.Hair);
                break;
            case "long":
                CapHair();
                Add(Loft.Rings([new(Shoulder - 0.02f, Hw * 0.9f, Hd * 0.32f, 0, -Hd * 0.6f), new(Cy, Hw * 1.02f, Hd * 0.5f, 0, -Hd * 0.45f)], _seg, bottom: true), S.Hair);
                break;
            case "bun":
                CapHair();
                Add(Loft.Ellipsoid(0, Cy + Hh * 0.16f, -Hd, Hh * 0.16f, Hh * 0.15f, Hh * 0.15f, _es0, _es1), S.Hair);
                break;
            case "topknot":
                CapHair(1.04f);
                Add(Loft.Rings([new(Cy + Hh * 0.44f, Hh * 0.05f, Hh * 0.05f, 0, -Hh * 0.05f), new(Cy + Hh * 0.62f, Hh * 0.045f, Hh * 0.05f, 0, Hh * 0.06f)], _lseg, top: true), S.Hair);
                break;
            case "cropped":
                Add(Loft.Shell(0, Cy + Hh * 0.03f, -Hh * 0.04f, Hw * 1.02f, Hh * 0.5f, Hd * 1.02f, 0.55f * MathF.PI, -0.5f, Hws, Hs), S.Hair);
                break;
            case "curly":
                CapHair(1.12f);
                if (_near)
                    for (int i = 0; i < 7; i++)
                    {
                        float t = i / 7f * MathF.PI * 1.6f - MathF.PI * 0.3f;
                        Add(Loft.Ellipsoid(MathF.Cos(t) * Hw, Cy + Hh * (0.2f + 0.12f * MathF.Sin(i * 2.1f)), -MathF.Sin(t) * Hd * 0.85f - Hh * 0.05f, Hh * 0.1f, Hh * 0.09f, Hh * 0.1f, 6, 4), S.Hair);
                    }
                break;
            case "bob":
                CapHair(1.08f);
                float th0 = MathF.PI / 2 + 0.75f, th1 = MathF.PI / 2 - 0.75f + MathF.Tau;
                Add(Loft.Rings([new Ring(Chin + 0.006f, Hw * 1.08f, Hd, 0, -Hd * 0.18f).Open(th0, th1), new Ring(Cy + Hh * 0.1f, Hw * 1.1f, Hd * 1.05f, 0, -Hd * 0.08f).Open(th0, th1)], _near ? 14
                    : 5),
                    S.Hair);
                break;
            case "braid":
                CapHair();
                var bp = Loft.Bez(new Vector3(0, Cy, -Hd), new Vector3(0, Chin - 0.04f, -Hd * 1.25f), new Vector3(0, Shoulder - 0.12f, -BodyAt(Shoulder - 0.12f).B - 0.012f), _near ? 7 : 3);
                Add(Loft.Sweep(bp, bp.Select((_, i) => (Hh * (0.08f - 0.035f * i / (bp.Count - 1)), Hh * (0.07f - 0.03f * i / (bp.Count - 1)))).ToList(), _near ? 6 : 4, caps: true), S.Hair);
                break;
            case "balding":
                Add(Loft.Shell(0, Cy + Hh * 0.02f, -Hh * 0.03f, Hw * 1.05f, Hh * 0.53f, Hd * 1.05f, 0.3f * MathF.PI, -0.9f, Hws, Hs, 1, 0.32f * MathF.PI), S.Hair);
                break;
            case "tonsure":
                Add(Loft.Shell(0, Cy + Hh * 0.02f, -Hh * 0.02f, Hw * 1.06f, Hh * 0.55f, Hd * 1.06f, 0.34f * MathF.PI, -0.25f, Hws, Hs, 1, 0.28f * MathF.PI), S.Hair);
                break;
            case "chonmage":
                Add(Loft.Shell(0, Cy + Hh * 0.02f, -Hh * 0.02f, Hw * 1.05f, Hh * 0.53f, Hd * 1.05f, 0.38f * MathF.PI, -0.55f, Hws, Hs, 1, 0.24f * MathF.PI), S.Hair);
                Add(Loft.Ellipsoid(0, Ht + Hh * 0.01f, -Hh * 0.06f, Hh * 0.035f, Hh * 0.03f, Hh * 0.17f, _es0, 3), S.Hair);
                break;
        }
    }

    void Beard()
    {
        if (_o.Beard is not { } beard) return;
        bool full = beard == "full";
        if (beard != "moustache") Add(Loft.Ellipsoid(0, Cy - Hh * (full ? 0.3f : 0.26f), Hd * 0.5f, Hw * 0.82f, Hh * (full ? 0.24f : 0.16f), Hd * 0.52f, _es0 + 2, _es1), S.Hair);
        if (_near || beard == "moustache") Add(Loft.Ellipsoid(0, Cy - Hh * 0.16f, Hd * 0.93f, Hw * 0.5f, Hh * 0.045f, Hh * 0.06f, _es0, 3), S.Hair);
    }

    Piece Brim(float y, float a, float b, float thick = 0.004f) => Loft.Rings([new(y - thick, a, b), new(y + thick, a, b)], _gseg, top: true, bottom: true);

    Piece Crown(float y0, float y1, float a1, float b1, float a0 = 1.05f, float b0 = 1.03f) => Loft.Rings([new(y0, Hw * a0, Hd * b0), new(y1, Hw * a1, Hd * b1)], _seg, top: true);

    string HatSlot => _o.HatSlot;

    void Hat()
    {
        switch (_o.Hat)
        {
            case "brim" or "fedora":
            {
                bool fed = _o.Hat == "fedora";
                float yb = Cy + Hh * (fed ? 0.24f : 0.28f), r = fed ? 1.75f : 2.15f;
                Add(Brim(yb, Hw * r, Hd * r * 0.9f), HatSlot);
                Add(Crown(yb, Ht + Hh * (fed ? 0.18f : 0.1f), fed ? 0.9f : 0.98f, fed ? 0.8f : 0.96f), HatSlot);
                if (_o.Has(CrowdDetails.HatBand)) Add(Loft.Rings([new(yb + 0.002f, Hw * 1.07f, Hd * 1.05f), new(yb + 0.016f, Hw * 1.05f, Hd * 1.02f)], _seg), S.Trim);
                break;
            }
            case "tricorne":
                Tricorne();
                break;
            case "cap" or "coif":
                Add(Loft.Shell(0, Cy + Hh * 0.04f, -Hh * 0.03f, Hw * 1.1f, Hh * 0.56f, Hd * 1.08f, 0.58f * MathF.PI, -0.5f, Hws, Hs), HatSlot);
                break;
            case "mobcap":
                Add(Loft.Shell(0, Cy + Hh * 0.06f, -Hh * 0.05f, Hw * 1.28f, Hh * 0.62f, Hd * 1.2f, 0.6f * MathF.PI, -0.55f, Hws, Hs), HatSlot);
                break;
            case "hood" or "wimple" or "veil" or "palla" or "headscarf":
                Hood();
                break;
            case "turban" or "bigturban":
            {
                float s = _o.Hat == "bigturban" ? 1.35f : 1.12f;
                int n = _near ? 6 : 3;
                var rs = Enumerable.Range(0, n).Select(i =>
                {
                    float u = (float)i / (n - 1), swell = 1 - 0.35f * u * u;
                    return new Ring(Cy + Hh * (0.12f + 0.5f * s * u), Hw * s * (1 + 0.25f * MathF.Sin(MathF.PI * u)) * swell, Hd * s * (1 + 0.22f * MathF.Sin(MathF.PI * u)) * swell, 0, -Hh * 0.03f);
                }).ToList();
                Add(Loft.Rings(rs, _near ? 22 : _seg, top: true, fold: _near ? _ => 0.045f : null, k: 7, phase: 0.4f), HatSlot);
                break;
            }
            case "chaperon":
                Add(Loft.Rings([new(Cy + Hh * 0.25f, Hw * 1.15f, Hd * 1.12f, 0, -Hh * 0.02f), new(Cy + Hh * 0.42f, Hw * 1.35f, Hd * 1.3f, 0, -Hh * 0.04f),
                    new(Ht + Hh * 0.08f, Hw * 0.9f, Hd * 0.85f, 0, -Hh * 0.06f)],
                    _seg, top: true), HatSlot);
                Add(Loft.Rings([new(Chest, Hh * 0.08f, Hh * 0.05f, 0, -BodyAt(Chest).B - 0.02f), new(Cy + Hh * 0.2f, Hh * 0.1f, Hh * 0.06f, 0, -Hd * 1.05f)], _lseg, bottom: true), HatSlot);
                break;
            case "bork":
                if (_o.Has(CrowdDetails.BorkFlap))
                {
                    var fl = Loft.Bez(new Vector3(0, Cy + Hh * 0.7f, -Hd), new Vector3(0, Cy - Hh * 0.2f, -Hd * 1.6f), new Vector3(0, Shoulder - 0.08f, -BodyAt(Shoulder).B - 0.035f), _near ? 6 : 3);
                    Add(Loft.Sweep(fl, fl.Select(_ => (0.04f, 0.003f)).ToList(), 4, up: -Vector3.UnitZ, rot: MathF.PI / 4), HatSlot);
                }
                Add(Loft.Rings([new(Cy + Hh * 0.25f, Hw * 1.05f, Hd * 1.03f), new(Cy + Hh * 1.35f, Hw * 0.95f, Hd * 0.55f, 0, -Hh * 0.55f)], _seg, top: true), HatSlot);
                Add(Loft.Rings([new(Cy + Hh * 0.2f, Hw * 1.1f, Hd * 1.08f), new(Cy + Hh * 0.36f, Hw * 1.1f, Hd * 1.08f)], _seg), S.Metal);
                break;
            case "kasa":
                Add(Loft.Rings([new(Cy + Hh * 0.3f, Hw * 2.9f, Hw * 2.9f), new(Ht + Hh * 0.35f, Hw * 0.15f, Hw * 0.15f)], _gseg, top: true, bottom: true), HatSlot);
                break;
            case "jingasa":
                Add(Loft.Rings([new(Cy + Hh * 0.38f, Hw * 2.2f, Hw * 2.2f), new(Ht + Hh * 0.18f, Hw * 0.4f, Hw * 0.4f)], _gseg, top: true, bottom: true), HatSlot);
                break;
            case "kettle":
                Add(Loft.Shell(0, Cy + Hh * 0.08f, 0, Hw * 1.12f, Hh * 0.55f, Hd * 1.1f, 0.5f * MathF.PI, 0, Hws, Hs), HatSlot);
                Add(Loft.Rings([new(Cy + Hh * 0.02f, Hw * 2, Hd * 1.7f), new(Cy + Hh * 0.1f, Hw * 1.12f, Hd * 1.1f)], _gseg), HatSlot);
                break;
            case "galea":
                Add(Loft.Shell(0, Cy + Hh * 0.06f, -Hh * 0.02f, Hw * 1.12f, Hh * 0.56f, Hd * 1.1f, 0.55f * MathF.PI, -0.35f, Hws, Hs), HatSlot);
                Add(Loft.Ellipsoid(0, Ht + Hh * 0.12f, 0, Hh * 0.04f, Hh * 0.12f, Hd * 0.9f, 4, 3), HatSlot);
                break;
            case "helmet":
                Add(Loft.Shell(0, Cy + Hh * 0.04f, 0, Hw * 1.22f, Hh * 0.6f, Hd * 1.18f, 0.5f * MathF.PI, 0, Hws, Hs), HatSlot);
                Add(Loft.Rings([new(Cy, Hw * 1.42f, Hd * 1.36f), new(Cy + Hh * 0.06f, Hw * 1.22f, Hd * 1.18f)], _gseg), HatSlot);
                break;
            case "beret":
                Add(Loft.Ellipsoid(Hd * 0.05f, Cy + Hh * 0.42f, -Hh * 0.02f, Hw * 1.25f, Hh * 0.13f, Hd * 1.15f, Hws, _es1), HatSlot);
                break;
            case "kalpak":
                Add(Crown(Cy + Hh * 0.22f, Ht + Hh * 0.42f, 1, 0.98f, 1.1f, 1.08f), HatSlot);
                break;
            case "headband":
                Add(Loft.Rings([new(Cy + Hh * 0.16f, Hw * 1.04f, Hd * 1.03f), new(Cy + Hh * 0.26f, Hw * 1.02f, Hd * 1.01f)], _seg), HatSlot);
                break;
            case "petasos":
                Add(Brim(Cy + Hh * 0.3f, Hw * 2.5f, Hd * 2.3f), HatSlot);
                Add(Crown(Cy + Hh * 0.3f, Ht + Hh * 0.08f, 0.7f, 0.7f), HatSlot);
                break;
            case "homburg":
                Add(Loft.Rings([new(Cy + Hh * 0.24f - 0.004f, Hw * 1.6f, Hd * 1.45f), new(Cy + Hh * 0.24f + 0.006f, Hw * 1.62f, Hd * 1.47f)], _gseg, top: true, bottom: true), HatSlot);
                Add(Crown(Cy + Hh * 0.24f, Ht + Hh * 0.24f, 0.95f, 0.85f), HatSlot);
                if (_o.Has(CrowdDetails.HatBand)) Add(Loft.Rings([new(Cy + Hh * 0.25f, Hw * 1.07f, Hd * 1.05f), new(Cy + Hh * 0.32f, Hw * 1.06f, Hd * 1.04f)], _seg), S.Trim);
                break;
            case "bonnet":
                Add(Loft.Shell(0, Cy + Hh * 0.06f, -Hh * 0.05f, Hw * 1.28f, Hh * 0.62f, Hd * 1.2f, 0.6f * MathF.PI, -0.55f, Hws, Hs), S.Linen);
                Add(Brim(Cy + Hh * 0.36f, Hw * 2.3f, Hd * 2.1f, 0.003f), HatSlot);
                Add(Crown(Cy + Hh * 0.36f, Ht + Hh * 0.06f, 0.9f, 0.85f, 1.1f, 1.05f), HatSlot);
                break;
            case "flatcap":
                Add(Loft.Ellipsoid(0, Cy + Hh * 0.32f, Hh * 0.04f, Hw * 1.15f, Hh * 0.18f, Hd * 1.18f, Hws, _es1), HatSlot);
                Add(Loft.Ellipsoid(0, Cy + Hh * 0.26f, Hd, Hw * 0.75f, Hh * 0.03f, Hd * 0.45f, _es0, 3), HatSlot);
                break;
        }
    }

    void Tricorne()
    {
        float yb = Cy + Hh * 0.27f;
        static float Lobe(float th) => 0.5f + 0.5f * MathF.Cos(3 * (th - MathF.PI / 2));
        var brim = new Ring(yb + 0.001f, Hw * 2.05f, Hw * 2.05f) { RAt = th => 0.76f + 0.24f * Lobe(th), YAt = th => yb + Hh * (0.06f + 0.3f * Lobe(th)) };
        Add(Loft.Rings([new Ring(yb, Hw * 1.05f, Hd * 1.03f), brim], _near ? 36 : 9), HatSlot);
        Add(Loft.Rings([new(yb - 0.01f, Hw * 1.05f, Hd * 1.03f), new(Ht + Hh * 0.06f, Hw * 0.95f, Hd * 0.92f)], _seg, top: true), HatSlot);
        if (!_o.Has(CrowdDetails.HatTrim) || !_near) return;
        var edge = new List<Vector3>();
        for (int i = 0; i <= 36; i++)
        {
            float th = i / 36f * MathF.Tau, rr = Hw * 2.05f * (0.76f + 0.24f * Lobe(th));
            edge.Add(new Vector3(rr * MathF.Cos(th), yb + Hh * (0.06f + 0.3f * Lobe(th)), rr * MathF.Sin(th)));
        }
        Add(Loft.Sweep(edge, edge.Select(_ => (0.004f, 0.005f)).ToList(), 5), S.Linen);
        float c = MathF.PI / 2 - 0.55f, r = Hw * 2.05f * (0.76f + 0.24f * Lobe(c)) * 0.86f;
        Add(Loft.Ellipsoid(r * MathF.Cos(c), yb + Hh * (0.06f + 0.3f * Lobe(c)) - 0.01f, r * MathF.Sin(c), 0.016f, 0.016f, 0.006f, 8, 4), S.Accent);
    }

    void Hood()
    {
        string hat = _o.Hat!;
        float big = hat == "hood" ? 1.16f : 1.12f;
        bool scarf = hat == "headscarf", palla = hat == "palla", closed = hat is "hood" or "wimple";
        Add(Loft.Shell(0, Cy + Hh * 0.02f, -Hh * 0.02f, Hw * big, Hh * 0.56f * big, Hd * big, 0.62f * MathF.PI, -MathF.PI / 2 + 0.25f, Hws, Math.Max(Hs, 4), 0.9f), HatSlot);
        float capeBot = palla ? Chest : scarf ? NeckBase : Shoulder - 0.02f;
        var bb = BodyAt(capeBot);
        Add(Loft.Rings([new(capeBot, (bb.A + _ease * 1.6f) * (scarf ? 0.7f : palla ? 1.1f : 1.02f), (bb.B + _ease * 1.6f) * 1.02f, 0, bb.Z - (scarf || palla ? Hd * 0.3f : 0)),
            new(Cy - Hh * 0.2f, Hw * big, Hd * big * (closed ? 0.9f : 0.62f), 0, closed ? -Hh * 0.06f : -Hh * 0.2f)], _gseg), HatSlot);
        if (hat == "veil")
            Add(Loft.Rings([new Ring(Chin - 0.01f, Hw * 1.02f, Hd).Open(MathF.PI / 2 - 0.9f, MathF.PI / 2 + 0.9f),
                new Ring(Cy - Hh * 0.08f, Hw * 1.04f, Hd * 1.04f).Open(MathF.PI / 2 - 0.9f, MathF.PI / 2 + 0.9f)], 8),
                HatSlot);
        if (!_o.Has(CrowdDetails.HoodTail)) return;
        var tail = Loft.Bez(new Vector3(0, Cy + Hh * 0.35f, -Hd * 1.05f), new Vector3(0, Cy - Hh * 0.2f, -Hd * 1.7f), new Vector3(0, Chest, -BodyAt(Chest).B - 0.05f), _near ? 7 : 3);
        Add(Loft.Sweep(tail, tail.Select((_, i) => (0.02f * (1 - 0.5f * i / (tail.Count - 1)), 0.02f * (1 - 0.5f * i / (tail.Count - 1)))).ToList(), _near ? 8 : 4, caps: true), HatSlot);
    }
}
