using System.Collections.Generic;
using Godot;

namespace KTable.App;

static class OverflowCheck
{
    static readonly HashSet<string> Reported = new();

    public static void Report(Node root)
    {
        void Walk(Node n, Rect2? bounds, string path)
        {
            if (n is Window w && w != root)
            {
                if (!w.Visible) return;
                bounds = new Rect2(Vector2.Zero, w.Size);
                path += "/" + w.GetType().Name;
            }
            if (n is Control c)
            {
                if (!c.IsVisibleInTree()) return;
                var r = c.GetGlobalRect();
                if (bounds is { } b && c.Size.X > 0 && c.Size.Y > 0)
                {
                    float over = Mathf.Max(r.End.X - b.End.X, b.Position.X - r.Position.X);
                    if (over > 1.5f && (c is not Label || ((Label)c).AutowrapMode == TextServer.AutowrapMode.Off))
                    {
                        var key = path + "/" + c.GetType().Name + ":" + c.Name;
                        if (Reported.Add(key)) GD.Print($"[overflow] {over:0}px  {key}  text='{(c is Button bt ? bt.Text : c is Label lb ? lb.Text : c is LineEdit le ? le.Text : "")}'  rect={r}  bounds={b}");
                    }
                }
                if (c is PanelContainer or ScrollContainer or TabContainer || c.ClipContents) bounds = r;
                path += "/" + c.GetType().Name;
            }
            foreach (var ch in n.GetChildren()) Walk(ch, bounds, path);
        }
        Walk(root, null, "");
    }
}
