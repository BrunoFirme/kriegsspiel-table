using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using KTable.Client;
using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;
using Json = KTable.Core.Json;

namespace KTable.App;

sealed class StudioInPlace
{
    readonly Session _live;
    readonly Table _table;
    readonly Net _net;
    readonly DateTime _sinceUtc;
    PieceStudio? _studio;
    PackManifest _real = new();
    string _was = "";
    Dictionary<string, string> _kindsWere = new(), _kindsShown = new();

    internal StudioInPlace(Session live, Table table, Net net, string kind, string layer, string pieceId, StudioDraft? restored = null)
    {
        _live = live;
        _table = table;
        _net = net;
        Kind = kind;
        _layer = layer;
        _pieceId = pieceId;
        _sinceUtc = restored?.Since ?? DateTime.UtcNow;
    }

    readonly string _layer, _pieceId;
    bool _behind, _aimed;
    CameraWas? _cameraWas;

    sealed record CameraWas(Transform3D At, Camera3D.ProjectionType Projection, float Fov, float Size, float Near, float Far, float HOffset, float VOffset);

    internal bool Ended { get; private set; }

    internal string Kind { get; }

    internal bool Backdrop { get; private set; } = true;

    internal static string? PackToEdit(Session s, PieceDef kind) =>
        s.IsHost && s.Store.PackOfDef(kind.Id) is { } pack && !StandardPacks.IsStandard(pack.Id) ? pack.Id : null;

    internal static void Open(HudContext c, string pack, string kind, string layer, string pieceId) =>
        (c.Node.GetTree().CurrentScene as Main)?.EditInPlace(pack, kind, layer, pieceId);

    internal void Begin(PieceStudio studio)
    {
        _studio = studio;
        _real = BestEffort.Read(() => Json.Load<PackManifest>(PackEdit.ManifestPath(studio.Store, studio.Pack.Id)), Json.Clone(studio.Pack));
        _real.MoveCounterMarkers();
        _was = Json.Serialize(_real);
        _kindsWere = Kinds(_real);
        _kindsShown = new(_kindsWere);
        if (Changed(studio.Pack)) Show(studio.Pack);
        _live.Knocked += Knocked;
        studio.TreeExiting += () =>
        {
            _live.Knocked -= Knocked;
            GiveBack();
        };
    }

    internal void Showing(string view)
    {
        if (_studio is not { } studio) return;
        bool behind = view == "model" && Backdrop, tryIt = view == "table";
        if (!behind) GiveBack();
        if (!behind) _table.Rest(true, pause: true);
        if (!tryIt) studio.T.Rest(true);
        if (behind)
        {
            _table.Rest(false, pause: true);
            _table.SetProcessUnhandledInput(false);
            _table.ProcessMode = Godot.Node.ProcessModeEnum.Disabled;
            _table.Lend(true);
        }
        if (tryIt) studio.T.Rest(false);
        studio.Model?.Backdrop(behind);
        _behind = behind;
    }

    PieceNode? Instance => GodotObject.IsInstanceValid(_table) ? _table.Pieces.NodeOf(_layer, _pieceId) : null;

    internal void Follow()
    {
        if (!_behind || _studio?.Model is not { } m || m.DrawnBounds() is not { } drawn || Instance is not { } node) return;
        if (!_aimed && Aim(m, node)) return;
        var bounds = m.Stand.Transform * drawn;
        if (bounds.Size.X < 1e-4f) return;
        float s = node.Footprint.X / bounds.Size.X;
        var facing = Facing(node);
        var anchor = new Vector3(bounds.GetCenter().X, bounds.Position.Y, bounds.GetCenter().Z);
        var cam = _table.Rig.Cam;
        var eye = m.Cam;
        node.Visible = false;
        cam.GlobalTransform = new Transform3D(facing * eye.GlobalTransform.Basis, node.GlobalPosition + facing * ((eye.GlobalPosition - anchor) * s));
        cam.Near = eye.Near * s;
        cam.Far = Mathf.Max(eye.Far * s, _cameraWas?.Far ?? 0);
        Lens(cam, eye, m.Vc.GetGlobalRect(), s);
    }

    bool Aim(ModelStudio m, PieceNode node)
    {
        _aimed = true;
        var cam = _table.Rig.Cam;
        _cameraWas ??= new CameraWas(cam.GlobalTransform, cam.Projection, cam.Fov, cam.Size, cam.Near, cam.Far, cam.HOffset, cam.VOffset);
        var piece = node.Piece;
        bool looked = false;
        if (m.Flat && piece.State != Math.Max(0, m.FlatState)) { m.ShowState(piece.State); looked = true; }
        if (m.Flat && piece.FaceDown != m.Turned) { m.TurnOver(); looked = true; }
        m.Frame();
        var facing = Facing(node);
        var from = facing.Inverse() * (_cameraWas.At.Origin - node.GlobalPosition);
        if (from.LengthSquared() > 1e-6f)
        {
            var dir = from.Normalized();
            m.Camera.Face(Mathf.Atan2(dir.X, dir.Z), -Mathf.Asin(Mathf.Clamp(dir.Y, -1, 1)));
        }
        return looked;
    }

    void Lens(Camera3D cam, Camera3D eye, Rect2 seen, float s)
    {
        var window = _table.GetViewport().GetVisibleRect().Size;
        if (seen.Size.Y < 1) return;
        float grow = window.Y / seen.Size.Y;
        var shift = window / 2 - seen.GetCenter();
        if (eye.Projection == Camera3D.ProjectionType.Orthogonal)
        {
            float perPixel = eye.Size * s / seen.Size.Y;
            cam.Projection = Camera3D.ProjectionType.Orthogonal;
            cam.Size = eye.Size * s * grow;
            cam.HOffset = shift.X * perPixel;
            cam.VOffset = -shift.Y * perPixel;
            return;
        }
        float nearHeight = 2 * cam.Near * Mathf.Tan(Mathf.DegToRad(eye.Fov) / 2);
        float perNearPixel = nearHeight / seen.Size.Y;
        cam.Projection = Camera3D.ProjectionType.Frustum;
        cam.HOffset = cam.VOffset = 0;
        cam.Size = nearHeight * grow;
        cam.FrustumOffset = new Vector2(shift.X * perNearPixel, -shift.Y * perNearPixel);
    }

    static Basis Facing(PieceNode node)
    {
        var z = node.GlobalTransform.Basis.Z;
        return new Basis(Vector3.Up, Mathf.Atan2(z.X, z.Z));
    }

    void GiveBack()
    {
        _behind = false;
        _aimed = false;
        if (!GodotObject.IsInstanceValid(_table)) return;
        if (Instance is { } node) node.Visible = true;
        if (_cameraWas is { } was)
        {
            var cam = _table.Rig.Cam;
            cam.GlobalTransform = was.At;
            cam.Projection = was.Projection;
            cam.Fov = was.Fov;
            cam.Size = was.Size;
            cam.Near = was.Near;
            cam.Far = was.Far;
            cam.HOffset = was.HOffset;
            cam.VOffset = was.VOffset;
            _cameraWas = null;
        }
        _table.Lend(false);
    }

    internal void ToggleBackdrop()
    {
        Backdrop = !Backdrop;
        if (_studio is { } studio) Showing(studio.View);
    }

    internal void Show(PackManifest draft)
    {
        if (Ended) return;
        var now = Kinds(draft);
        _live.ShowDraft(Json.Clone(draft));
        Redraw(Differing(_kindsShown, now));
        _kindsShown = now;
        bool changed = Changed(draft);
        if (changed) StudioDraft.Keep(draft.Id, _sinceUtc, draft);
        else StudioDraft.Drop(draft.Id);
        _studio?.SetSaved(Status(draft));
    }

    internal string Status(PackManifest draft) => Changed(draft) ? "not confirmed" : "";

    internal bool Changed(PackManifest draft) => !Ended && Json.Serialize(draft) != _was;

    internal string? Confirm(PackageStore store, PackManifest draft)
    {
        if (!Changed(draft))
        {
            End();
            return null;
        }
        var now = Kinds(draft);
        var changed = Differing(_kindsWere, now).ToHashSet();
        var restart = changed.Where(id => ScriptsOf(_real, id) != ScriptsOf(draft, id)).ToHashSet();
        try { Json.SaveBig(PackEdit.ManifestPath(store, draft.Id), draft); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return e.Message; }
        End();
        StudioDraft.Drop(draft.Id);
        _net.SharePack(draft.Id);
        _net.RestartScriptsOf(restart);
        Redraw(changed);
        return null;
    }

    internal void Discard(PackageStore store, PackManifest draft)
    {
        if (Ended) return;
        StudioDraft.DropFiles(store, _real, draft, _sinceUtc);
        StudioDraft.Drop(draft.Id);
        var shown = _kindsShown;
        End();
        Redraw(Differing(_kindsWere, shown));
    }

    void End()
    {
        Ended = true;
        _live.ShowDraft(null);
        GiveBack();
    }

    void Redraw(IEnumerable<string> kinds) => Redraw(_table, _live, kinds);

    internal static void Redraw(Table table, Session s, IEnumerable<string> kinds)
    {
        if (!GodotObject.IsInstanceValid(table)) return;
        var ids = kinds.ToList();
        if (ids.Any(id => s.Def(id) == null)) { table.Rebuild(); return; }
        foreach (var id in ids) table.RebuildPiecesOf(id);
    }

    internal static void RedrawChanged(Table table, Session s, PackManifest? was, PackManifest? now)
    {
        var kinds = now?.Pieces.Select(d => d.Id).ToHashSet() ?? new();
        bool undrawn = s.View.Layers.Any(l => l.Value.Any(p => kinds.Contains(p.Def) && table.Pieces.NodeOf(l.Key, p.Id) == null));
        if (was == null || now == null || undrawn) { table.Rebuild(); return; }
        Redraw(table, s, Differing(Kinds(was), Kinds(now)));
    }

    void Knocked(Knock k)
    {
        if (_studio is { } s && GodotObject.IsInstanceValid(s)) s.Flash($"{k.Name} is at the door: Confirm or Discard to let them in", 10);
    }

    static Dictionary<string, string> Kinds(PackManifest p)
    {
        var kinds = new Dictionary<string, string>();
        foreach (var d in p.Pieces) kinds.TryAdd(d.Id, Json.Serialize(d));
        return kinds;
    }

    static IEnumerable<string> Differing(Dictionary<string, string> a, Dictionary<string, string> b) =>
        a.Keys.Union(b.Keys).Where(k => a.GetValueOrDefault(k) != b.GetValueOrDefault(k));

    static string ScriptsOf(PackManifest p, string id) =>
        p.Pieces.Find(d => d.Id == id) is { } d ? string.Join("\n", d.States.Select(s => s.Script).Prepend(d.Script)) : "";
}
