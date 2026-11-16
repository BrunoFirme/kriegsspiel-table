using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed class Gait(PieceNode body, TableContext c)
{
    const float WalkStrideHeights = 0.62f, RunStrideHeights = 0.93f, RunFromHeights = 1.6f, StillHeights = 0.05f, IdleSeconds = 4.5f, Ease = 7f, SpeedEvery = 0.05f;

    sealed record Cycles(ArrayMesh Mesh, int[] Walk, int[] Run, int[] Idle);

    static readonly Dictionary<string, Cycles?> Built = new();

    sealed record Frame(string Pack, string Rel, string Abs);

    sealed record Kept(List<Frame> Files, int Walk, int Run, int Idle)
    {
        public string Key { get; } = string.Join("|", Files.Select(f => f.Abs));
    }

    readonly Kept _kept = Files(body.Def, c);
    readonly List<ArrayMesh> _loaded = new();
    readonly HashSet<string> _asked = new();
    List<Frame> Frames => _kept.Files;
    MeshInstance3D? _shown;
    int _onDisk;
    float _walkPhase, _runPhase, _idlePhase, _moving, _running, _travel, _spent;
    Vector3? _last;

    static Kept Files(PieceDef? def, TableContext c)
    {
        var files = new List<Frame>();
        if (def == null || c.S.PackOfDef(def.Id) is not { } pack) return new Kept(files, 0, 0, 0);
        int Add(List<string>? keys)
        {
            int had = files.Count;
            foreach (var key in keys ?? [])
                if (pack.Assets.TryGetValue(key, out var asset)) files.Add(new Frame(pack.Id, asset.File, c.S.Assets.Abs(pack.Id, asset.File)));
            return files.Count - had;
        }
        int walk = Add(def.Walk), run = Add(def.Run), idle = Add(def.Idle);
        return walk > 0 ? new Kept(files, walk, run, idle) : new Kept([], 0, 0, 0);
    }

    public static IEnumerable<bool> Warming(PieceDef def, TableContext c, ISet<string> asked)
    {
        var kept = Files(def, c);
        if (kept.Files.Count == 0 || Built.ContainsKey(kept.Key)) yield break;
        if (OnDisk(kept, c, asked, 0) < kept.Files.Count)
        {
            if (Lost(kept, c)) Built[kept.Key] = null;
            yield break;
        }
        var loaded = new List<ArrayMesh>();
        foreach (var file in kept.Files)
        {
            if (ModelCache.Get(file.Abs) is not { } mesh)
            {
                Built[kept.Key] = null;
                yield break;
            }
            loaded.Add(mesh);
            yield return true;
        }
        if (!Built.ContainsKey(kept.Key)) Built[kept.Key] = Make(loaded, kept.Walk, kept.Run, kept.Idle);
        yield return true;
    }

    public static bool Warmed(PieceDef def, TableContext c) => Built.TryGetValue(Files(def, c).Key, out var cycles) && cycles != null;

    public static bool Settled(PieceDef def, TableContext c) => Files(def, c) is var kept && (kept.Files.Count == 0 || Built.ContainsKey(kept.Key));

    static int OnDisk(Kept kept, TableContext c, ISet<string> asked, int from)
    {
        foreach (var f in kept.Files)
            if (asked.Add(f.Abs) && !File.Exists(f.Abs)) c.S.Assets.EnsureFile(f.Pack, f.Rel, () => { });
        while (from < kept.Files.Count && File.Exists(kept.Files[from].Abs)) from++;
        return from;
    }

    static bool Lost(Kept kept, TableContext c) => kept.Files.Any(f => c.S.Assets.Waits.KnownMissing(f.Abs));

    public void Step(Vector3 feet, float height, float delta)
    {
        if (Frames.Count == 0 || !GodotObject.IsInstanceValid(body) || Ready() is not { } cycles) return;
        float moved = _last is { } was ? new Vector2(feet.X - was.X, feet.Z - was.Z).Length() : 0;
        _last = feet;
        _travel += moved;
        _spent += delta;
        if (_spent >= SpeedEvery)
        {
            float speed = _travel / _spent / Mathf.Max(height, 1e-6f), k = 1 - Mathf.Exp(-_spent * Ease);
            _moving = Mathf.Lerp(_moving, speed > StillHeights ? 1 : 0, k);
            _running = Mathf.Lerp(_running, cycles.Run.Length > 0 && speed > RunFromHeights ? 1 : 0, k);
            (_travel, _spent) = (0, 0);
        }
        _walkPhase = Mathf.PosMod(_walkPhase + moved / Mathf.Max(height * WalkStrideHeights, 1e-6f), 1);
        _runPhase = Mathf.PosMod(_runPhase + moved / Mathf.Max(height * RunStrideHeights, 1e-6f), 1);
        _idlePhase = Mathf.PosMod(_idlePhase + delta / IdleSeconds, 1);
        Show(cycles);
        var weights = new float[cycles.Mesh.GetBlendShapeCount()];
        float idle = cycles.Idle.Length > 0 ? 1 - _moving : 0, run = _moving * _running, walk = 1 - idle - run;
        Spread(weights, cycles.Walk, _walkPhase, walk);
        Spread(weights, cycles.Run, _runPhase, run);
        Spread(weights, cycles.Idle, _idlePhase, idle);
        for (int i = 0; i < weights.Length; i++) _shown!.SetBlendShapeValue(i, weights[i]);
    }

    static void Spread(float[] weights, int[] frames, float phase, float share)
    {
        if (frames.Length == 0 || share <= 0) return;
        float at = phase * frames.Length;
        int a = (int)at % frames.Length, b = (a + 1) % frames.Length;
        float t = at - Mathf.Floor(at);
        weights[frames[a]] += share * (1 - t);
        weights[frames[b]] += share * t;
    }

    Cycles? Ready()
    {
        if (Built.TryGetValue(_kept.Key, out var done)) return done;
        if ((_onDisk = OnDisk(_kept, c, _asked, _onDisk)) < Frames.Count) return Lost(_kept, c) ? Built[_kept.Key] = null : null;
        if (_loaded.Count < Frames.Count)
        {
            if (ModelCache.Get(Frames[_loaded.Count].Abs) is { } mesh) _loaded.Add(mesh);
            else Built[_kept.Key] = null;
            return null;
        }
        return Built[_kept.Key] = Make(_loaded, _kept.Walk, _kept.Run, _kept.Idle);
    }

    static Cycles? Make(List<ArrayMesh> frames, int walk, int run, int idle)
    {
        var first = frames[0].SurfaceGetArrays(0);
        int count = ((Vector3[])first[(int)Mesh.ArrayType.Vertex]).Length;
        if (frames.Any(f => f.GetSurfaceCount() != 1 || ((Vector3[])f.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex]).Length != count)) return null;
        var mesh = new ArrayMesh { BlendShapeMode = Mesh.BlendShapeMode.Normalized };
        var shapes = new Godot.Collections.Array<Godot.Collections.Array>();
        for (int i = 0; i < frames.Count; i++)
        {
            mesh.AddBlendShape($"frame{i}");
            var arrays = frames[i].SurfaceGetArrays(0);
            var shape = new Godot.Collections.Array();
            shape.Resize((int)Mesh.ArrayType.Max);
            shape[(int)Mesh.ArrayType.Vertex] = arrays[(int)Mesh.ArrayType.Vertex];
            shape[(int)Mesh.ArrayType.Normal] = arrays[(int)Mesh.ArrayType.Normal];
            shape[(int)Mesh.ArrayType.Tangent] = arrays[(int)Mesh.ArrayType.Tangent];
            shapes.Add(shape);
        }
        mesh.AddSurface(Mesh.PrimitiveType.Triangles, first, shapes);
        mesh.SurfaceSetName(0, frames[0].SurfaceGetName(0));
        return new Cycles(mesh, Enumerable.Range(0, walk).ToArray(), Enumerable.Range(walk, run).ToArray(), Enumerable.Range(walk + run, idle).ToArray());
    }

    void Show(Cycles cycles)
    {
        if (body.Model is not { } model || !GodotObject.IsInstanceValid(model)) return;
        if (_shown == null || !GodotObject.IsInstanceValid(_shown) || _shown.GetParent() != model.GetParent())
        {
            _shown = new MeshInstance3D { Mesh = cycles.Mesh, CastShadow = model.CastShadow };
            model.AddSibling(_shown);
        }
        _shown.MaterialOverride = model.MaterialOverride;
        if (model.GetSurfaceOverrideMaterialCount() > 0) _shown.SetSurfaceOverrideMaterial(0, model.GetSurfaceOverrideMaterial(0));
        _shown.Transform = model.Transform;
        _shown.Visible = true;
        model.Visible = false;
    }

    public void Stop()
    {
        if (_shown != null && GodotObject.IsInstanceValid(_shown)) _shown.QueueFree();
        _shown = null;
        if (GodotObject.IsInstanceValid(body) && body.Model is { } model && GodotObject.IsInstanceValid(model)) model.Visible = true;
    }
}
