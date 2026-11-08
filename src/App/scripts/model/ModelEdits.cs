using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Client.Studio;

namespace KTable.App;

internal sealed class ModelEdits
{
    readonly ModelStudio _m;

    public ModelEdits(ModelStudio m) => _m = m;

    ModelDoc Doc => _m.Doc;

    void Done(bool panels = true)
    {
        _m.RefreshSelection();
        _m.Changed(mesh: true);
        if (!panels) return;
        _m.Studio.RebuildModelPanels();
        _m.Studio.RebuildModelStrip();
    }

    public void Reshape(ModelPart p, ShapeSpec spec, bool panels = true)
    {
        if (!ShapeMaker.Valid(spec) || !_m.Views.TryGetValue(p.Id, out var old)) return;
        _m.Snapshot();
        var mesh = ShapeMaker.Make(spec);
        var made = _m.NewPartFor(mesh, p.Name, p.Kind);
        p.Spec = spec.Copy();
        p.Mesh = made.Mesh;
        p.Tex = made.Tex;
        p.Groups = made.Groups;
        p.OwnLayout = null;
        p.Pivot = null;
        p.Paint = null;
        _m.Views.Remove(p.Id);
        _m.MakeView(p);
        var now = _m.Views[p.Id];
        if (PaintCarry.Reshaped(old.Data, old.Paint, old.W, old.H, now.Data, now.Paint, now.W, now.H)) now.Repaint();
        old.Node.QueueFree();
        old.TwinNode?.QueueFree();
        Done(panels);
    }

    public void Group(ModelPart p)
    {
        _m.Snapshot();
        var g = ModelTree.GroupAround(Doc, p, p.Name + " group");
        _m.SelPartId = g.Id;
        Done();
    }

    public void Ungroup(ModelPart g)
    {
        if (!g.IsGroup) return;
        _m.Snapshot();
        var first = ModelTree.Children(Doc, g).FirstOrDefault();
        ModelTree.Ungroup(Doc, g);
        _m.SelPartId = first?.Id;
        Done();
    }

    public void Into(ModelPart p, ModelPart? group)
    {
        if (ModelTree.ParentOf(Doc, p) == group) return;
        _m.Snapshot();
        ModelTree.MoveInto(Doc, p, group);
        Doc.Parts.Remove(p);
        int at = group == null ? Doc.Parts.Count : LastIndexInside(group) + 1;
        Doc.Parts.Insert(Math.Min(at, Doc.Parts.Count), p);
        Done();
    }

    int LastIndexInside(ModelPart group) => ModelTree.Descendants(Doc, group).Select(d => Doc.Parts.IndexOf(d)).DefaultIfEmpty(Doc.Parts.IndexOf(group)).Max();

    public void Shift(ModelPart p, int step)
    {
        _m.Snapshot();
        if (ModelTree.Shift(Doc, p, step)) Done();
    }

    public void SetJoin(ModelPart p, string? join, float? blend = null)
    {
        _m.Snapshot();
        p.Join = join != null && ModelTree.Joins.Contains(join) ? join : null;
        if (blend is { } b) p.Blend = Mathf.Max(0, b);
        Done();
    }

    public static bool CanBlend(ModelDoc doc, ModelPart p) =>
        ModelTree.ChainOf(doc, p) is { } c ? c.Members.All(ShapeSdf.Fits) : ShapeSdf.Fits(p) && (ModelTree.Above(doc, p) is not { } a || ShapeSdf.Fits(a));

    Vector3 IntoParent(ModelPart p, Vector3 rootDelta) => ModelTree.ParentXf(Doc, p).Basis.Inverse() * rootDelta;

    public void Move(ModelPart p, Vector3 rootDelta)
    {
        var d = IntoParent(p, rootDelta);
        p.Pos = new[] { p.Pos[0] + d.X, p.Pos[1] + d.Y, p.Pos[2] + d.Z };
    }

    public bool SitOn(ModelPart p, ModelPart on, bool panels = true)
    {
        if (p == on || _m.BoundsOf(p) is not { } a || _m.BoundsOf(on) is not { } b) return false;
        _m.Snapshot();
        Move(p, new Vector3(b.GetCenter().X - a.GetCenter().X, b.End.Y - a.Position.Y, b.GetCenter().Z - a.GetCenter().Z));
        Done(panels);
        return true;
    }

    public bool CentreOn(ModelPart p, ModelPart on, string axes, bool panels = true)
    {
        if (p == on || _m.BoundsOf(p) is not { } a || _m.BoundsOf(on) is not { } b) return false;
        var d = b.GetCenter() - a.GetCenter();
        var keep = new Vector3(axes.Contains('x') ? 1 : 0, axes.Contains('y') ? 1 : 0, axes.Contains('z') ? 1 : 0);
        _m.Snapshot();
        Move(p, d * keep);
        Done(panels);
        return true;
    }

    public (float centres, Vector3 gaps)? Measure(ModelPart a, ModelPart b)
    {
        if (_m.BoundsOf(a) is not { } x || _m.BoundsOf(b) is not { } y) return null;
        float k = Doc.Scale;
        var gap = new Vector3(Gap(x.Position.X, x.End.X, y.Position.X, y.End.X), Gap(x.Position.Y, x.End.Y, y.Position.Y, y.End.Y), Gap(x.Position.Z, x.End.Z, y.Position.Z, y.End.Z));
        return (x.GetCenter().DistanceTo(y.GetCenter()) * k, gap * k);
        static float Gap(float a0, float a1, float b0, float b1) => Mathf.Max(b0 - a1, a0 - b1);
    }

    public ModelPart Duplicate(ModelPart s)
    {
        _m.Snapshot();
        var made = new Dictionary<string, string>();
        var all = (s.IsGroup ? ModelTree.Descendants(Doc, s).Prepend(s) : new[] { s }).ToList();
        var copies = new List<ModelPart>();
        foreach (var x in all)
        {
            var c = ModelTree.CopyOf(x);
            made[x.Id] = c.Id = ModelTree.NewId();
            copies.Add(c);
        }
        foreach (var c in copies) if (c.Parent != null && made.TryGetValue(c.Parent, out var np)) c.Parent = np;
        var top = copies[0];
        top.Name = s.Name + " copy";
        top.Pos[0] += 0.3f / Math.Max(1e-4f, Doc.Scale);
        top.SolidMesh = top.SolidPaint = null;
        top.SolidRegion = null;
        int at = (s.IsGroup ? LastIndexInside(s) : Doc.Parts.IndexOf(s)) + 1;
        Doc.Parts.InsertRange(Math.Min(at, Doc.Parts.Count), copies);
        foreach (var (x, c) in all.Zip(copies))
        {
            if (x.IsGroup) continue;
            _m.MakeView(c);
            if (_m.Views.TryGetValue(x.Id, out var src) && _m.Views[c.Id].Paint.Length == src.Paint.Length)
            {
                Array.Copy(src.Paint, _m.Views[c.Id].Paint, src.Paint.Length);
                _m.Views[c.Id].Repaint();
            }
        }
        _m.SelPartId = top.Id;
        Done();
        return top;
    }
}
