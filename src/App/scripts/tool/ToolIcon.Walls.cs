using Godot;
using KTable.Core.Model;

namespace KTable.App;

partial class ToolIcon
{
    void WallRun()
    {
        Trace(_c, Bold * 1.4f, 2.5f, 13.5f, 8.5f, 7.5f, 17.5f, 10.5f);
        Square(6.9f, 5.9f, 3.2f);
        Square(1.2f, 12.2f, 2.6f);
        Square(16.2f, 9.2f, 2.6f);
    }

    void Stakes()
    {
        Line(2, 13.5f, 18, 13.5f, Fine);
        foreach (float x in new[] { 3f, 5.8f, 8.6f, 11.4f, 14.2f, 17f }) Solid(x - 0.9f, 15, x - 0.9f, 7.4f, x, 6, x + 0.9f, 7.4f, x + 0.9f, 15);
    }

    void Bank()
    {
        DrawColoredPolygon(Pts(1.5f, 15, 5, 8.5f, 15, 8.5f, 18.5f, 15), Soft);
        Trace(_c, Bold, 1.5f, 15, 5, 8.5f, 15, 8.5f, 18.5f, 15);
    }

    void Dug()
    {
        Trace(Soft, Bold, 2, 6.5f, 18, 6.5f);
        Trace(Soft, Bold, 2, 13.5f, 18, 13.5f);
        foreach (float x in new[] { 2f, 7.5f, 13f }) Line(x, 10, x + 3.5f, 10, Bold);
    }

    void FieldWall()
    {
        foreach (var (x, y, w) in new[] { (1.5f, 9f, 3.4f), (5.4f, 8.4f, 3.8f), (9.7f, 9f, 3.2f), (13.4f, 8.4f, 3.6f), (17.2f, 9f, 1.6f) }) Solid(x, y, x + w, y, x + w, y + 3, x, y + 3);
    }

    void Hedgerow()
    {
        foreach (float x in new[] { 3.5f, 7f, 10.5f, 14f, 17f }) DrawCircle(P(x, 10.5f), 2.1f * _k, _c);
    }

    void Paling()
    {
        foreach (float x in new[] { 3f, 7.7f, 12.3f, 17f }) Line(x, 6, x, 15, Bold);
        Trace(Soft, Fine, 2, 8.5f, 18, 8.5f);
        Trace(Soft, Fine, 2, 12.5f, 18, 12.5f);
    }

    void Towering(int towers)
    {
        Trace(_c, Bold * 1.3f, 2, 10, 18, 10);
        if (towers <= Works.NoTowers) return;
        Square(1, 8.4f, 3.2f);
        Square(15.8f, 8.4f, 3.2f);
        if (towers < Works.ManyTowers) return;
        Square(6.2f, 8.7f, 2.6f);
        Square(11.2f, 8.7f, 2.6f);
    }

    void Rolled()
    {
        Trace(Soft, Bold, 3.5f, 3.5f, 16.5f, 3.5f, 16.5f, 16.5f, 3.5f, 16.5f, 3.5f, 3.5f);
        foreach (var (x, y) in new[] { (7f, 7f), (10f, 10f), (13f, 13f) }) DrawCircle(P(x, y), 1.4f * _k, _c);
    }

    void Mound()
    {
        DrawColoredPolygon(Pts(1.5f, 17, 5, 11, 15, 11, 18.5f, 17), Soft);
        Solid(8, 11, 8, 4.6f, 9.2f, 4.6f, 9.2f, 6, 10.8f, 6, 10.8f, 4.6f, 12, 4.6f, 12, 11);
    }

    void Bailey()
    {
        Trace(_c, Bold, 4, 7.5f, 10, 3, 16, 6.5f, 17, 13.5f, 11.5f, 17.5f, 4, 15, 4, 7.5f);
        foreach (var (x, y) in new[] { (4f, 7.5f), (10f, 3f), (16f, 6.5f), (17f, 13.5f), (11.5f, 17.5f), (4f, 15f) }) DrawCircle(P(x, y), 1.5f * _k, _c);
    }

    void Quad()
    {
        DrawColoredPolygon(Pts(4, 4, 16, 4, 16, 16, 4, 16), Soft);
        Trace(_c, Bold, 4, 4, 16, 4, 16, 16, 4, 16, 4, 4);
        foreach (var (x, y) in new[] { (4f, 4f), (16f, 4f), (16f, 16f), (4f, 16f) }) Square(x - 1.7f, y - 1.7f, 3.4f);
    }

    void Rings()
    {
        Trace(Soft, Bold, 2.5f, 2.5f, 17.5f, 2.5f, 17.5f, 17.5f, 2.5f, 17.5f, 2.5f, 2.5f);
        Trace(_c, Bold, 6, 6, 14, 6, 14, 14, 6, 14, 6, 6);
        foreach (var (x, y) in new[] { (6f, 6f), (14f, 6f), (14f, 14f), (6f, 14f) }) Square(x - 1.3f, y - 1.3f, 2.6f);
    }

    void Spurred()
    {
        Trace(Soft, Fine, 1.5f, 17.5f, 10, 3, 18.5f, 17.5f);
        Trace(_c, Bold, 5, 15, 10, 6.5f, 15, 15, 5, 15);
        Square(8.6f, 10, 2.8f);
    }

    void Nested()
    {
        Trace(Soft, Bold, 2.5f, 4, 17.5f, 4, 17.5f, 17, 2.5f, 17, 2.5f, 4);
        Trace(_c, Fine, 5.5f, 6.5f, 14.5f, 6.5f, 14.5f, 14.5f, 5.5f, 14.5f, 5.5f, 6.5f);
        Square(8.4f, 8.4f, 3.2f);
    }

    void Drums()
    {
        DrawColoredPolygon(Pts(4.5f, 4.5f, 15.5f, 4.5f, 15.5f, 15.5f, 4.5f, 15.5f), Soft);
        Trace(_c, Bold, 4.5f, 4.5f, 15.5f, 4.5f, 15.5f, 15.5f, 4.5f, 15.5f, 4.5f, 4.5f);
        foreach (var (x, y) in new[] { (4.5f, 4.5f), (15.5f, 4.5f), (15.5f, 15.5f), (4.5f, 15.5f) }) DrawCircle(P(x, y), 2.2f * _k, _c);
    }

    void Stockade()
    {
        for (int i = 0; i < 14; i++)
        {
            float a = i * Mathf.Tau / 14;
            Line(10 + Mathf.Cos(a) * 6.6f, 10 + Mathf.Sin(a) * 6.6f, 10 + Mathf.Cos(a) * 8.6f, 10 + Mathf.Sin(a) * 8.6f, Bold);
        }
        Square(8.3f, 8.3f, 3.4f);
    }
}
