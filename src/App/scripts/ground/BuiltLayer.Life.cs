using System.Linq;
using Godot;

namespace KTable.App;

sealed partial class BuiltLayer
{
    const float BeastFromPx = 1.3f, SlabM = 2.6f, SlabFromPx = 5, VineRowM = 2.3f, VineApartM = 1.5f, VineM = 0.45f, MostVines = 2400, TierShare = 0.17f;
    const int Tiers = 3, BodySides = 8;

    void Beast(BuiltHand hand, Vector2 at, Vector2 heading, float longM, float wideM, Color coat, Color head)
    {
        Vector2 run = heading * (longM * _m / 2), side = heading.Orthogonal() * (wideM * _m / 2);
        var body = Enumerable.Range(0, BodySides).Select(i => at + run * Mathf.Cos(i * Mathf.Tau / BodySides) + side * Mathf.Sin(i * Mathf.Tau / BodySides)).ToArray();
        if (hand.Shaded) DrawColoredPolygon(body.Select(p => p + Vector2.One * Mathf.Min(0.25f * _m, 2)).ToArray(), hand.Ink with { A = 0.3f });
        DrawColoredPolygon(body, coat);
        DrawPolyline(body.Append(body[0]).ToArray(), hand.Ink with { A = 0.7f }, BuiltHand.LeastPx, true);
        DrawCircle(at + run * 1.05f, wideM * _m * 0.36f, head);
    }

    void Herd(BuiltHand hand, Vector2 at, Vector2 across, int count, float spreadM, float longM, float wideM, Color[] coats, Color head)
    {
        if (longM * _m < BeastFromPx * 2) return;
        for (int i = 0; i < count; i++)
        {
            var off = new Vector2(Chance(i * 2.3f + 1) - 0.5f, Chance(i * 4.1f + 2) - 0.5f) * 2 * spreadM * _m;
            Beast(hand, at + off, across.Rotated(Chance(i * 3.7f) * Mathf.Tau), longM, wideM, coats[i % coats.Length], head);
        }
    }

    void Slabs(BuiltHand hand, Vector2[] px)
    {
        float pitch = SlabM * _m;
        Vector2 along = px[1] - px[0], across = px[3] - px[0];
        if (pitch >= SlabFromPx)
        {
            for (int i = 1; i < (int)(along.Length() / pitch); i++) Edge(px[0] + along * (i * pitch / along.Length()), px[3] + along * (i * pitch / along.Length()), hand, 0.14f);
            for (int i = 1; i < (int)(across.Length() / pitch); i++) Edge(px[0] + across * (i * pitch / across.Length()), px[1] + across * (i * pitch / across.Length()), hand, 0.14f);
        }
        Columns(hand, px, 1.6f * _m);
    }

    void Arena(BuiltHand hand, Vector2[] px)
    {
        var centre = Mid(px);
        if (hand.Shaded) Plain(px.Select(p => p + Vector2.One * Mathf.Min(ShadowM * 1.6f * _m, ShadowMostPx * 1.6f)).ToArray(), hand.Ink with { A = 0.7f });
        for (int tier = 0; tier <= Tiers; tier++)
        {
            float reach = 1 - tier * TierShare;
            var ring = px.Select(p => centre + (p - centre) * reach).ToArray();
            Plain(ring, tier == Tiers ? hand.Mud.Lightened(0.12f) : tier % 2 == 0 ? hand.Stone : hand.Stone.Lightened(0.14f));
            Ringed(ring, hand, tier == 0 ? 0.9f : 0.45f);
        }
        for (int i = 0; i < px.Length; i += px.Length / 4) Edge(px[i], centre + (px[i] - centre) * (1 - Tiers * TierShare), hand, 0.5f);
    }

    void Vines(BuiltHand hand, Vector2[] px)
    {
        float row = VineRowM * _m, apart = VineApartM * _m;
        Vector2 along = px[1] - px[0], across = px[3] - px[0];
        int rows = (int)(across.Length() / row), plants = (int)(along.Length() / apart);
        if (VineM * _m < 0.8f || rows * plants > MostVines) return;
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < plants; c++)
                DrawCircle(px[0] + across * ((r + 0.5f) / rows) + along * ((c + 0.5f) / plants), VineM * _m, r % 2 == 0 ? hand.Tree : hand.Tree.Lightened(0.1f));
    }

    void Threshed(BuiltHand hand, Vector2[] px)
    {
        var centre = Mid(px);
        Ringed(px.Select(p => centre + (p - centre) * 0.62f).ToArray(), hand, 0.18f);
        Ringed(px.Select(p => centre + (p - centre) * 0.3f).ToArray(), hand, 0.18f);
        DrawCircle(centre, Mathf.Max(0.2f * _m, 1), hand.Ink with { A = 0.6f });
    }

    bool Life(BuiltHand hand, string cls, Vector2 at, Vector2 across, Vector2 along)
    {
        switch (cls)
        {
            case "hens":
                Herd(hand, at, across, 5, 0.9f, 0.38f, 0.24f, [hand.Lime, hand.Brick, hand.Lime.Darkened(0.3f)], hand.Signal);
                return true;
            case "geese":
                Herd(hand, at, across, 4, 1, 0.6f, 0.3f, [hand.Lime], hand.Straw);
                return true;
            case "pigs":
                Herd(hand, at, across, 3, 0.9f, 1.2f, 0.52f, [hand.Lime.Lerp(hand.Brick, 0.35f), hand.Lime.Lerp(hand.Brick, 0.5f)], hand.Lime.Lerp(hand.Brick, 0.5f));
                return true;
            case "sheep":
                Herd(hand, at, across, 6, 3.4f, 1.15f, 0.62f, [hand.Lime.Darkened(0.06f), hand.Lime.Darkened(0.14f)], hand.Ink);
                return true;
            case "cattle":
                Herd(hand, at, across, 4, 3.4f, 2.2f, 0.82f, [hand.Brick, hand.Wood, hand.Lime.Darkened(0.1f), hand.Brick.Darkened(0.2f)], hand.Wood);
                return true;
            case "horses":
                Herd(hand, at, across, 3, 3.2f, 2.3f, 0.66f, [hand.Wood, hand.Brick.Darkened(0.25f), hand.Stone], hand.Ink);
                return true;
            case "dog":
                Herd(hand, at, across, 1, 0, 0.85f, 0.3f, [hand.Wood.Lightened(0.15f)], hand.Wood);
                return true;
            case "dungheap" when Seen(2.4f):
                var heap = Enumerable.Range(0, 8).Select(i => at + across.Rotated(i * Mathf.Tau / 8) * ((0.9f + 0.5f * Chance(i * 1.9f)) * _m)).ToArray();
                DrawColoredPolygon(heap, hand.Wood.Lerp(hand.Straw, 0.25f));
                for (int i = 0; i < 5; i++) DrawCircle(at + new Vector2(Chance(i + 5) - 0.5f, Chance(i + 9) - 0.5f) * 1.4f * _m, Mathf.Max(0.12f * _m, 0.7f), hand.Straw);
                return true;
            case "jars" when Seen(1.2f):
                for (int i = 0; i < 4; i++)
                {
                    var jar = at + across * ((i - 1.5f) * 0.55f * _m) + along * ((i % 2) * 0.4f * _m);
                    Tub(hand, jar, 0.26f + 0.1f * Chance(i * 1.3f), hand.Tile);
                    DrawCircle(jar, Mathf.Max(0.1f * _m, 0.6f), hand.Ink with { A = 0.7f });
                }
                return true;
            case "sacks" when Seen(1.2f):
                for (int i = 0; i < 3; i++) Boxed(hand, Box(at + across * ((i - 1) * 0.75f * _m) + along * ((i % 2) * 0.3f * _m), across.Rotated(i * 0.5f), along.Rotated(i * 0.5f), 0.9f, 0.55f),
                    hand.Straw.Lightened(0.2f));
                return true;
            case "bench" when Seen(1.8f):
                Boxed(hand, Box(at, across, along, 1.8f, 0.42f), hand.Wood.Lightened(0.1f));
                return true;
            case "washing" when Seen(3):
                Vector2 one = at - across * 2 * _m, two = at + across * 2 * _m;
                DrawLine(one, two, hand.Ink with { A = 0.6f }, BuiltHand.LeastPx, true);
                foreach (var post in new[] { one, two }) DrawCircle(post, Mathf.Max(0.12f * _m, 0.9f), hand.Ink);
                for (int i = 0; i < 4; i++) Boxed(hand, Box(one.Lerp(two, 0.16f + i * 0.22f), across, along, 0.7f, 0.5f), i % 2 == 0 ? hand.Lime : hand.Cloth.Lerp(hand.Signal, 0.4f), false);
                return true;
        }
        return Gear(hand, cls, at, across, along);
    }

    bool Gear(BuiltHand hand, string cls, Vector2 at, Vector2 across, Vector2 along)
    {
        switch (cls)
        {
            case "wheels" when Seen(1.2f):
                foreach (float off in new[] { -0.7f, 0.7f })
                {
                    var hub = at + across * off * _m;
                    DrawArc(hub, 0.6f * _m, 0, Mathf.Tau, 14, hand.Wood, Mathf.Max(0.12f * _m, 1), true);
                    for (int i = 0; i < 4; i++) DrawLine(hub - Vector2.Right.Rotated(i * Mathf.Pi / 4) * 0.6f * _m, hub
                        + Vector2.Right.Rotated(i * Mathf.Pi / 4) * 0.6f * _m, hand.Wood, BuiltHand.LeastPx, true);
                }
                return true;
            case "logs" when Seen(3):
                for (int i = -2; i <= 2; i++)
                {
                    Vector2 a = at - across * 1.6f * _m + along * (i * 0.36f * _m), b = at + across * 1.6f * _m + along * (i * 0.36f * _m);
                    DrawLine(a, b, i % 2 == 0 ? hand.Wood : hand.Wood.Lightened(0.14f), Mathf.Max(0.3f * _m, 1), true);
                    DrawCircle(b, Mathf.Max(0.15f * _m, 0.7f), hand.Straw);
                }
                return true;
            case "vats" when Seen(1.2f):
                Color[] dyes = [hand.Signal, hand.Wet, hand.Copper];
                for (int i = 0; i < 3; i++)
                {
                    Tub(hand, at + across * ((i - 1) * 1.5f * _m), 0.65f, hand.Wood);
                    DrawCircle(at + across * ((i - 1) * 1.5f * _m), 0.48f * _m, dyes[i]);
                }
                return true;
            case "racks" when Seen(2.4f):
                foreach (float off in new[] { -0.8f, 0.8f })
                {
                    var frame = Box(at + along * off * _m, across, along, 2.6f, 1.1f);
                    Plain(frame, off < 0 ? hand.Lime : hand.Cloth.Lerp(hand.Copper, 0.4f));
                    Ringed(frame, hand, 0.9f);
                }
                return true;
            case "stooks" when Seen(1):
                for (int i = 0; i < 6; i++)
                {
                    var stook = at + new Vector2(i % 3 - 1, i / 3 - 0.5f) * 1.9f * _m;
                    Tub(hand, stook, 0.5f, hand.Straw);
                    DrawCircle(stook, Mathf.Max(0.12f * _m, 0.6f), hand.Straw.Darkened(0.35f));
                }
                return true;
            case "plough" when Seen(2.4f):
                DrawLine(at - across * 1.3f * _m, at + across * 1.3f * _m, hand.Wood, Mathf.Max(0.16f * _m, 1), true);
                DrawColoredPolygon([at + across * 0.2f * _m, at + across * 1.0f * _m + along * 0.45f * _m, at + across * 1.0f * _m], hand.Ink);
                foreach (int sign in new[] { 1, -1 }) DrawLine(at - across * 1.3f * _m, at - across * 1.9f * _m + along * (sign * 0.4f * _m), hand.Wood, BuiltHand.LeastPx, true);
                return true;
            case "oven" when Seen(1.4f):
                Dome(hand, at, 0.8f * _m, hand.Mud.Lerp(hand.Brick, 0.4f));
                DrawCircle(at + across * 0.6f * _m, Mathf.Max(0.22f * _m, 1), hand.Ink with { A = 0.8f });
                return true;
        }
        return Monument(hand, cls, at, across, along);
    }

    bool Monument(BuiltHand hand, string cls, Vector2 at, Vector2 across, Vector2 along)
    {
        float shade = Mathf.Min(ShadowM * _m, ShadowMostPx);
        switch (cls)
        {
            case "marketcross" when Seen(2.4f):
                for (int i = 0; i < 3; i++) Boxed(hand, Box(at, Vector2.Right, Vector2.Down, 2.6f - i * 0.8f, 2.6f - i * 0.8f), hand.Stone.Lightened(i * 0.07f), i == 0);
                if (hand.Crosses) Cross(hand, at + Vector2.Down * 0.3f * _m, 0.8f * _m, Fine);
                return true;
            case "pump" when Seen(1.4f):
                Boxed(hand, Box(at + across * 0.8f * _m, across, along, 1.2f, 0.6f), hand.Stone);
                DrawCircle(at, Mathf.Max(0.22f * _m, 1.2f), hand.Ink);
                DrawLine(at, at - across * 0.9f * _m, hand.Ink, Mathf.Max(0.1f * _m, 1), true);
                return true;
            case "sweep" when Seen(1.4f):
                Tub(hand, at, 0.9f, hand.Stone);
                DrawCircle(at, 0.6f * _m, hand.Wet);
                DrawLine(at - across * 1.2f * _m, at + across * 4.2f * _m + along * 0.6f * _m, hand.Wood, Mathf.Max(0.14f * _m, 1), true);
                DrawCircle(at + across * 4.2f * _m + along * 0.6f * _m, Mathf.Max(0.3f * _m, 1.2f), hand.Stone);
                DrawCircle(at + across * 2.2f * _m + along * 0.3f * _m, Mathf.Max(0.16f * _m, 1), hand.Ink);
                return true;
            case "statue" or "column" when Seen(1.2f):
                if (hand.Shaded) DrawLine(at, at + Vector2.One * shade * 2.2f, hand.Ink with { A = 0.45f }, Mathf.Max(0.5f * _m, 1.2f), true);
                Boxed(hand, Box(at, Vector2.Right, Vector2.Down, 1.3f, 1.3f), hand.Stone.Lightened(0.1f));
                Tub(hand, at, 0.36f, cls == "statue" ? hand.Copper : hand.Lime);
                return true;
            case "altar" when Seen(1.4f):
                Boxed(hand, Box(at, across, along, 1.7f, 1.1f), hand.Stone.Lightened(0.1f));
                DrawCircle(at, Mathf.Max(0.26f * _m, 1), hand.Signal);
                return true;
            case "obelisk" when Seen(1.2f):
                if (hand.Shaded) DrawLine(at, at + Vector2.One * shade * 3, hand.Ink with { A = 0.5f }, Mathf.Max(0.7f * _m, 1.4f), true);
                var needle = Box(at, Vector2.Right, Vector2.Down, 1.3f, 1.3f);
                Boxed(hand, needle, hand.Lime, false);
                Edge(needle[0], needle[2], hand, 0.6f);
                Edge(needle[1], needle[3], hand, 0.6f);
                return true;
            case "menhir" when Seen(1.2f):
                if (hand.Shaded) DrawLine(at, at + Vector2.One * shade * 2.4f, hand.Ink with { A = 0.45f }, Mathf.Max(0.6f * _m, 1.3f), true);
                Boxed(hand, Enumerable.Range(0, 6).Select(i => at + across.Rotated(i * Mathf.Tau / 6) * ((i % 3 == 0 ? 0.75f : 0.45f) * _m)).ToArray(), hand.Stone);
                return true;
            case "tent" when Seen(2.4f):
                var tent = Frame(Box(at, across, along, 3.2f, 2.6f));
                if (hand.Shaded) Plain(tent.A.Select(p => p + Vector2.One * shade * 0.6f).ToArray(), hand.Ink with { A = 0.5f });
                Slopes(hand, tent, Form.Gable, hand.Cloth);
                Ringed(tent.A, hand, 0.8f);
                return true;
            case "shot" when Seen(0.8f):
                for (int i = 0; i < 6; i++) DrawCircle(at + new Vector2(i < 3 ? i - 1 : i < 5 ? i - 3.5f : 0, i < 3 ? 0 : i < 5 ? -0.85f : -1.7f) * 0.36f * _m, Mathf.Max(0.17f * _m, 0.8f), hand.Ink);
                return true;
            case "sentrybox" when Seen(1.2f):
                Boxed(hand, Box(at, across, along, 1.1f, 1.1f), hand.Lime);
                DrawLine(at - across * 0.5f * _m, at + across * 0.5f * _m, hand.Signal, Mathf.Max(0.2f * _m, 1), true);
                return true;
            case "cistern" when Seen(2):
                Boxed(hand, Box(at, across, along, 3.4f, 2.2f), hand.Stone);
                Plain(Box(at, across, along, 2.8f, 1.6f), hand.Wet);
                return true;
            case "hens" or "geese" or "pigs" or "sheep" or "cattle" or "horses" or "dog" or "dungheap" or "jars" or "sacks" or "bench" or "washing" or "wheels" or "logs" or "vats" or "racks" or "stooks"
                or "plough" or "oven" or "marketcross" or "pump" or "sweep" or "statue" or "column" or "altar" or "obelisk" or "menhir" or "tent" or "shot" or "sentrybox" or "cistern":
                return true;
        }
        return false;
    }
}
