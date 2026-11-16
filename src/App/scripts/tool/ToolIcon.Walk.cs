using Godot;

namespace KTable.App;

public partial class ToolIcon
{
    bool DrawWalkIcon()
    {
        switch (Kind)
        {
            case "fly":
                Line(10, 17, 10, 4, 1.6f);
                Line(10, 4, 5.5f, 8.5f, 1.6f);
                Line(10, 4, 14.5f, 8.5f, 1.6f);
                Line(3, 17.5f, 17, 17.5f, 1.2f);
                return true;
            case "walls":
                Poly(3, 9, 10, 3.5f, 17, 9, 17, 17, 3, 17);
                DrawColoredPolygon(new[] { P(4.6f, 12.6f), P(15.4f, 12.6f), P(15.4f, 15.6f), P(4.6f, 15.6f) }, _c);
                Line(1.5f, 11, 18.5f, 11, 1.4f);
                return true;
            case "body":
                DrawArc(P(10, 5.2f), 2.6f * _k, 0, Mathf.Tau, 32, _c, _w, antialiased: true);
                DrawPolyline(Bend(4.5f, 17.5f, 4.5f, 9.5f, 15.5f, 9.5f, 15.5f, 17.5f), _c, _w, antialiased: true);
                return true;
            case "page":
                DrawPolyline(new[] { P(4.5f, 2.5f), P(12f, 2.5f), P(15.5f, 6f), P(15.5f, 17.5f), P(4.5f, 17.5f), P(4.5f, 2.5f) }, _c, _w, antialiased: true);
                Line(7.5f, 9f, 12.5f, 9f, 1.2f);
                Line(7.5f, 12f, 12.5f, 12f, 1.2f);
                Line(7.5f, 15f, 11f, 15f, 1.2f);
                return true;
            default:
                return false;
        }
    }
}
