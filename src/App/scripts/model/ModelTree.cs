using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Client.Studio;
using KTable.Core.Model;

namespace KTable.App;

internal static class ModelTree
{
    public const string GroupKind = "group";
    public const string Add = "add", Cut = "cut", Overlap = "overlap";
    public static readonly string[] Joins = { Add, Cut, Overlap };
    public static readonly Transform3D Mirror = new(Basis.FromScale(new Vector3(-1, 1, 1)), Vector3.Zero);
    const int MaxDepth = 32;
    const float SquareTolerance = 1e-4f, LeastSize = 1e-5f;

    public sealed record Chain(string? Parent, List<ModelPart> Members)
    {
        public ModelPart Head => Members[0];
        public string Key => SolidKey(Head.Id);
    }

    public static string SolidKey(string headId) => "s:" + headId;
    public static bool IsSolidKey(string id) => id.StartsWith("s:", StringComparison.Ordinal);
    public static string HeadOf(string solidKey) => solidKey[2..];

    public static ModelPart? ParentOf(ModelDoc doc, ModelPart p) => p.Parent == null ? null : doc.Parts.FirstOrDefault(x => x.Id == p.Parent && x.IsGroup && x != p);

    public static IEnumerable<ModelPart> Ancestors(ModelDoc doc, ModelPart p)
    {
        var at = ParentOf(doc, p);
        for (int i = 0; at != null && i < MaxDepth; i++)
        {
            yield return at;
            at = ParentOf(doc, at);
        }
    }

    public static Transform3D ParentXf(ModelDoc doc, ModelPart p) => Ancestors(doc, p).Aggregate(Transform3D.Identity, (xf, g) => g.Xf() * xf);

    public static Transform3D WorldXf(ModelDoc doc, ModelPart p) => ParentXf(doc, p) * p.Xf();

    public static Transform3D TwinXf(ModelDoc doc, ModelPart p) => ParentXf(doc, p) * Mirror * p.Xf();

    public static bool Shown(ModelDoc doc, ModelPart p) => p.Visible && Ancestors(doc, p).All(g => g.Visible);

    public static int Depth(ModelDoc doc, ModelPart p) => Ancestors(doc, p).Count();

    public static IEnumerable<ModelPart> Children(ModelDoc doc, ModelPart? group) =>
        doc.Parts.Where(p => p != group && (group == null ? ParentOf(doc, p) == null : ParentOf(doc, p) == group));

    public static List<ModelPart> Descendants(ModelDoc doc, ModelPart group)
    {
        var all = new List<ModelPart>();
        void Walk(ModelPart g, int depth)
        {
            if (depth > MaxDepth) return;
            foreach (var c in Children(doc, g)) { all.Add(c); if (c.IsGroup) Walk(c, depth + 1); }
        }
        Walk(group, 0);
        return all;
    }

    public static bool IsInside(ModelDoc doc, ModelPart p, ModelPart group) => Ancestors(doc, p).Contains(group);

    public static List<ModelPart> Ordered(ModelDoc doc)
    {
        var all = new List<ModelPart>();
        void Walk(ModelPart? g, int depth)
        {
            if (depth > MaxDepth) return;
            foreach (var c in Children(doc, g)) { all.Add(c); if (c.IsGroup) Walk(c, depth + 1); }
        }
        Walk(null, 0);
        all.AddRange(doc.Parts.Where(p => !all.Contains(p)));
        return all;
    }

    public static ModelPart? Above(ModelDoc doc, ModelPart p) => Children(doc, ParentOf(doc, p)).Where(x => Shown(doc, x)).TakeWhile(x => x != p).LastOrDefault();

    public static List<Chain> ChainsIn(ModelDoc doc, ModelPart? group)
    {
        var chains = new List<Chain>();
        List<ModelPart>? run = null;
        foreach (var p in Children(doc, group).Where(x => Shown(doc, x)))
        {
            if (run != null && p.Join != null && Joins.Contains(p.Join)) { run.Add(p); continue; }
            run = new List<ModelPart> { p };
            chains.Add(new Chain(group?.Id, run));
        }
        return chains;
    }

    public static List<Chain> Solids(ModelDoc doc)
    {
        var found = new List<Chain>();
        void Walk(ModelPart? group, int depth)
        {
            if (depth > MaxDepth) return;
            foreach (var c in ChainsIn(doc, group))
            {
                if (c.Members.Count > 1) found.Add(c);
                else if (c.Head.IsGroup) Walk(c.Head, depth + 1);
            }
        }
        Walk(null, 0);
        return found;
    }

    public static HashSet<string> Consumed(ModelDoc doc, IEnumerable<Chain> solids)
    {
        var ids = new HashSet<string>();
        foreach (var m in solids.SelectMany(c => c.Members))
        {
            ids.Add(m.Id);
            if (m.IsGroup) foreach (var d in Descendants(doc, m)) ids.Add(d.Id);
        }
        return ids;
    }

    public static Chain? ChainOf(ModelDoc doc, ModelPart p)
    {
        var top = Ancestors(doc, p).Prepend(p).ToList();
        return Solids(doc).FirstOrDefault(c => c.Members.Any(top.Contains));
    }

    public static bool IsSquare(Basis b)
    {
        var c = new[] { b.Column0, b.Column1, b.Column2 };
        for (int i = 0; i < 3; i++)
            for (int j = i + 1; j < 3; j++)
                if (Mathf.Abs(c[i].Dot(c[j])) > SquareTolerance * Mathf.Max(LeastSize, c[i].Length() * c[j].Length())) return false;
        return true;
    }

    public static void SetXf(ModelPart p, Transform3D local)
    {
        var b = local.Basis;
        var s = new Vector3(Mathf.Max(LeastSize, b.Column0.Length()), Mathf.Max(LeastSize, b.Column1.Length()), Mathf.Max(LeastSize, b.Column2.Length()));
        if (b.Determinant() < 0) s.X = -s.X;
        var turn = new Basis(b.Column0 / s.X, b.Column1 / s.Y, b.Column2 / s.Z).Orthonormalized();
        var e = turn.GetEuler();
        var pivot = p.Pivot is { Length: 3 } v ? new Vector3(v[0], v[1], v[2]) : Vector3.Zero;
        var at = local * pivot;
        p.WorldScale = false;
        p.Pos = new[] { at.X, at.Y, at.Z };
        p.Rot = new[] { Mathf.RadToDeg(e.X), Mathf.RadToDeg(e.Y), Mathf.RadToDeg(e.Z) };
        p.Scl = new[] { s.X, s.Y, s.Z };
    }

    public static void Upgrade(ModelDoc doc, Func<ModelPart, Aabb?> boundsOf)
    {
        if (doc.Version >= ModelDoc.CurrentVersion) return;
        foreach (var p in doc.Parts)
        {
            p.WorldScale = true;
            var old = p.Xf();
            if (!IsSquare(old.Basis) || boundsOf(p) is not { } b) continue;
            var c = b.GetCenter();
            p.Pivot = new[] { c.X, c.Y, c.Z };
            SetXf(p, old);
        }
        doc.Version = ModelDoc.CurrentVersion;
    }

    public static void MoveInto(ModelDoc doc, ModelPart p, ModelPart? group)
    {
        if (group == p || (group != null && IsInside(doc, group, p))) return;
        var world = WorldXf(doc, p);
        p.Parent = group?.Id;
        SetXf(p, (group == null ? Transform3D.Identity : WorldXf(doc, group)).AffineInverse() * world);
    }

    public static ModelPart GroupAround(ModelDoc doc, ModelPart p, string name)
    {
        var g = new ModelPart { Name = name, Kind = GroupKind, Parent = p.Parent, Join = p.Join, Blend = p.Blend, Pos = p.Pos.ToArray() };
        doc.Parts.Insert(doc.Parts.IndexOf(p), g);
        p.Join = null;
        p.Blend = 0;
        MoveInto(doc, p, g);
        return g;
    }

    public static void Ungroup(ModelDoc doc, ModelPart g)
    {
        var up = ParentOf(doc, g);
        int at = doc.Parts.IndexOf(g);
        var kids = Children(doc, g).ToList();
        foreach (var k in kids) MoveInto(doc, k, up);
        if (kids.Count > 0 && kids[0].Join == null) { kids[0].Join = g.Join; kids[0].Blend = g.Blend; }
        doc.Parts.Remove(g);
        foreach (var k in kids) { doc.Parts.Remove(k); doc.Parts.Insert(Math.Min(at++, doc.Parts.Count), k); }
    }

    public static bool Shift(ModelDoc doc, ModelPart p, int step)
    {
        var sibs = Children(doc, ParentOf(doc, p)).ToList();
        int i = sibs.IndexOf(p), j = i + step;
        if (i < 0 || j < 0 || j >= sibs.Count) return false;
        var other = sibs[j];
        int a = doc.Parts.IndexOf(p), b = doc.Parts.IndexOf(other);
        doc.Parts[a] = other;
        doc.Parts[b] = p;
        return true;
    }

    public static ModelPart CopyOf(ModelPart p) => System.Text.Json.JsonSerializer.Deserialize<ModelPart>(System.Text.Json.JsonSerializer.Serialize(p, ModelStudio.Opts), ModelStudio.Opts)!;

    public static string NewId() => ArtDoc.NewId();
}
