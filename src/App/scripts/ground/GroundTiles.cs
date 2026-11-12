using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace KTable.App;

public partial class GroundTiles : Node
{
    public const int TilePx = 256, GutterPx = 2, Slots = 16;
    const int AtlasPx = TilePx * Slots, BakesPerFrame = 8, UrgentBakesPerFrame = 24, MostLevel = 9, RetireFrames = 180, Budget = Slots * Slots - UrgentBakesPerFrame - BakesPerFrame, Relaxings = 4;
    const int CardQuietFrames = 120;
    const float FinestTexelWorld = 0.004f, Sharpness = 0.9f, MostStretch = 4f;
    static GroundTiles? _shared;
    static Shader? _viewShader, _bakeShader;

    readonly SubViewport _atlas = new()
    {
        Size = new Vector2I(AtlasPx, AtlasPx), TransparentBg = true, Disable3D = true,
        RenderTargetClearMode = SubViewport.ClearMode.Never, RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
    };
    readonly List<Sheet> _sheets = new();
    readonly Dictionary<string, (Sheet Sheet, long Frame)> _retired = new();
    readonly (Sheet? Owner, Tile Tile, long Used)[] _slots = new (Sheet?, Tile, long)[Slots * Slots];
    readonly List<(ColorRect Quad, Sheet Sheet, Tile Tile, int Slot)> _baking = new();
    long _frame;
    bool _carding;

    public static bool Enabled => DisplayServer.GetName() != "headless";

    public static Shader ViewShader => _viewShader ??= new Shader { Code = FileAccess.GetFileAsString("res://shaders/ground_view.gdshader") };

    public static Shader BakeShader => _bakeShader ??= new Shader { Code = FileAccess.GetFileAsString("res://shaders/ground_bake.gdshader") };

    public readonly record struct Tile(int Level, int X, int Y)
    {
        public Rect2 Uv => new(X / (float)(1 << Level), Y / (float)(1 << Level), 1f / (1 << Level), 1f / (1 << Level));
    }

    public sealed class Sheet
    {
        public required string Key { get; init; }
        public required Node3D Owner { get; init; }
        public required ShaderMaterial Bake { get; init; }
        public required ShaderMaterial View { get; init; }
        public required int Levels { get; init; }
        public required Image Pages { get; init; }
        public required ImageTexture PagesTex { get; init; }
        public required Func<Vector2, float, Vector3> World { get; init; }
        public required float Low { get; init; }
        public required float High { get; init; }
        public GroundCard.Spec? Card { get; init; }
        public readonly Dictionary<Tile, int> Baked = new();
        public readonly HashSet<Tile> Stale = new();
        public readonly HashSet<Tile> Urgent = new();
        public bool PagesDirty;
        internal int Changes = 1, ChangesSeen, CardOf;
        internal long QuietSince;

        public void StaleAll()
        {
            foreach (var t in Baked.Keys) Stale.Add(t);
            Changes++;
        }

        public void StaleUv(Rect2 uv, bool urgent = false)
        {
            foreach (var t in Baked.Keys)
            {
                if (!t.Uv.Intersects(uv, true)) continue;
                Stale.Add(t);
                if (urgent) Urgent.Add(t);
            }
            Changes++;
        }
    }

    public static GroundTiles For(Node inTree)
    {
        if (_shared != null && IsInstanceValid(_shared)) return _shared;
        _shared = new GroundTiles { Name = "GroundTiles" };
        inTree.GetTree().Root.CallDeferred(Node.MethodName.AddChild, _shared);
        return _shared;
    }

    public static void StaleEverything()
    {
        if (_shared == null || !IsInstanceValid(_shared)) return;
        foreach (var s in _shared._sheets) s.StaleAll();
    }

    public static void Release(Sheet sheet)
    {
        if (_shared == null || !IsInstanceValid(_shared)) return;
        _shared._sheets.Remove(sheet);
        _shared._retired[sheet.Key] = (sheet, _shared._frame);
    }

    public override void _Ready() => AddChild(_atlas);

    public Sheet Track(string key, Node3D owner, ShaderMaterial bake, Func<Vector2, float, Vector3> world, float sheetWidth, float low, float high, bool cut, bool redrawn,
        GroundCard.Spec? card = null)
    {
        int levels = Math.Clamp((int)Math.Ceiling(Math.Log2(Math.Max(1, sheetWidth / FinestTexelWorld / (TilePx - 2 * GutterPx)))), 0, MostLevel);
        int side = 1 << levels;
        var pages = Image.CreateEmpty(side * 2, side, false, Image.Format.Rgba8);
        var view = new ShaderMaterial { Shader = ViewShader, RenderPriority = cut ? 1 : 0 };
        view.SetShaderParameter("cut", cut);
        var pagesTex = ImageTexture.CreateFromImage(pages);
        view.SetShaderParameter("atlas", _atlas.GetTexture());
        view.SetShaderParameter("pages", pagesTex);
        view.SetShaderParameter("levels", (float)levels);
        view.SetShaderParameter("atlas_slots", (float)Slots);
        view.SetShaderParameter("tile_px", (float)TilePx);
        view.SetShaderParameter("gutter_px", (float)GutterPx);
        var sheet = new Sheet { Key = key, Owner = owner, Bake = bake, View = view, Levels = levels, Pages = pages, PagesTex = pagesTex, World = world, Low = low, High = high, Card = card };
        if (_retired.Remove(key, out var old) && old.Sheet.Levels == levels)
        {
            foreach (var (t, slot) in old.Sheet.Baked)
            {
                sheet.Baked[t] = slot;
                _slots[slot].Owner = sheet;
            }
            if (redrawn) sheet.StaleAll();
            Repage(sheet);
        }
        else if (old.Sheet != null) Untrack(old.Sheet);
        _sheets.Add(sheet);
        return sheet;
    }

    public void Untrack(Sheet sheet)
    {
        _sheets.Remove(sheet);
        for (int i = 0; i < _slots.Length; i++)
            if (_slots[i].Owner == sheet) _slots[i] = (null, default, 0);
        for (int i = _baking.Count - 1; i >= 0; i--)
            if (_baking[i].Sheet == sheet)
            {
                _baking[i].Quad.QueueFree();
                _baking.RemoveAt(i);
            }
    }

    public override void _Process(double delta)
    {
        _frame++;
        foreach (var key in _retired.Where(r => r.Value.Frame < _frame - RetireFrames).Select(r => r.Key).ToList())
        {
            Untrack(_retired[key].Sheet);
            _retired.Remove(key);
        }
        foreach (var (quad, sheet, tile, slot) in _baking)
        {
            quad.QueueFree();
            if (_slots[slot].Owner != sheet) continue;
            sheet.Baked[tile] = slot;
            sheet.Stale.Remove(tile);
            sheet.Urgent.Remove(tile);
            sheet.PagesDirty = true;
        }
        _baking.Clear();
        if (Wanted() is { } wanted) Bake(wanted, urgentOnly: false);
        foreach (var s in _sheets)
        {
            if (s.PagesDirty) Repage(s);
            CardWhenQuiet(s);
        }
    }

    List<(Sheet Sheet, Tile Tile, float Size)>? Wanted()
    {
        if (GetViewport()?.GetCamera3D() is not { } cam) return null;
        var frustum = cam.GetFrustum();
        var wanted = new List<(Sheet Sheet, Tile Tile, float Size)>();
        float sharpness = Sharpness;
        for (int relaxed = 0; ; relaxed++)
        {
            wanted.Clear();
            foreach (var s in _sheets)
                if (IsInstanceValid(s.Owner) && s.Owner.IsVisibleInTree()) Need(s, cam, frustum, new Tile(0, 0, 0), sharpness, wanted);
            if (wanted.Count <= Budget || relaxed >= Relaxings) break;
            sharpness *= 2;
        }
        return wanted;
    }

    public void Hurry()
    {
        if (IsInsideTree() && _sheets.Any(s => s.Urgent.Count > 0) && Wanted() is { } wanted) Bake(wanted, urgentOnly: true);
    }

    void CardWhenQuiet(Sheet s)
    {
        if (s.Card is not { } card || _carding || !IsInstanceValid(s.Owner) || !s.Owner.IsVisibleInTree()) return;
        if (s.Changes != s.ChangesSeen)
        {
            s.ChangesSeen = s.Changes;
            s.QuietSince = _frame;
            return;
        }
        if (s.CardOf == s.Changes || _frame - s.QuietSince < CardQuietFrames) return;
        s.CardOf = s.Changes;
        DrawCard(s.Bake, card);
    }

    async void DrawCard(ShaderMaterial bake, GroundCard.Spec card)
    {
        _carding = true;
        try { await GroundCard.Draw(this, bake, card); }
        catch (Exception e) { GD.PushWarning("the ground's card picture could not be drawn: " + e.Message); }
        finally { _carding = false; }
    }

    void Need(Sheet s, Camera3D cam, Godot.Collections.Array<Plane> frustum, Tile t, float sharpness, List<(Sheet, Tile, float)> wanted)
    {
        var uv = t.Uv;
        Span<Vector3> pts = stackalloc Vector3[8];
        int k = 0;
        foreach (var c in new[] { uv.Position, new Vector2(uv.End.X, uv.Position.Y), uv.End, new Vector2(uv.Position.X, uv.End.Y) })
        {
            pts[k++] = s.World(c, s.Low);
            pts[k++] = s.World(c, s.High);
        }
        foreach (var plane in frustum)
        {
            bool allOut = true;
            foreach (var pt in pts)
                if (!plane.IsPointOver(pt)) { allOut = false; break; }
            if (allOut) return;
        }
        float size = 0;
        bool behind = false;
        Span<Vector2> screen = stackalloc Vector2[4];
        for (int i = 0; i < 4; i++)
        {
            var mid = (pts[2 * i] + pts[2 * i + 1]) * 0.5f;
            if (cam.IsPositionBehind(mid)) { behind = true; break; }
            screen[i] = cam.UnprojectPosition(mid);
        }
        if (!behind) size = ShownSize(screen);
        wanted.Add((s, t, size));
        if (t.Level >= s.Levels || (!behind && size <= (TilePx - 2 * GutterPx) * sharpness)) return;
        for (int j = 0; j < 2; j++)
            for (int i = 0; i < 2; i++) Need(s, cam, frustum, new Tile(t.Level + 1, t.X * 2 + i, t.Y * 2 + j), sharpness, wanted);
    }

    static float ShownSize(Span<Vector2> corners)
    {
        float along = ((corners[1] - corners[0]).Length() + (corners[2] - corners[3]).Length()) / 2;
        float across = ((corners[3] - corners[0]).Length() + (corners[2] - corners[1]).Length()) / 2;
        return Math.Min(Math.Max(along, across), Math.Min(along, across) * MostStretch);
    }

    void Bake(List<(Sheet Sheet, Tile Tile, float Size)> wanted, bool urgentOnly)
    {
        foreach (var (s, t, _) in wanted)
            if (s.Baked.TryGetValue(t, out int slot)) _slots[slot].Used = _frame;
        bool Underway((Sheet Sheet, Tile Tile, float Size) w) => _baking.Any(b => b.Sheet == w.Sheet && b.Tile == w.Tile);
        int urgentLeft = UrgentBakesPerFrame - _baking.Count(b => b.Sheet.Urgent.Contains(b.Tile));
        var todo = wanted.Where(w => w.Sheet.Urgent.Contains(w.Tile) && !Underway(w)).OrderByDescending(w => w.Tile.Level).Take(Math.Max(0, urgentLeft)).ToList();
        if (!urgentOnly)
            todo.AddRange(wanted.Where(w => !w.Sheet.Urgent.Contains(w.Tile) && (!w.Sheet.Baked.ContainsKey(w.Tile) || w.Sheet.Stale.Contains(w.Tile)))
                .OrderBy(w => w.Sheet.Baked.ContainsKey(w.Tile) ? 1 : 0).ThenBy(w => w.Tile.Level).ThenByDescending(w => w.Size).Take(BakesPerFrame));
        foreach (var (s, t, _) in todo)
        {
            int slot = s.Baked.TryGetValue(t, out int kept) ? kept : Allocate();
            if (slot < 0) break;
            _slots[slot] = (s, t, _frame);
            var mat = (ShaderMaterial)s.Bake.Duplicate();
            var uv = t.Uv;
            float g = uv.Size.X * GutterPx / (TilePx - 2 * GutterPx);
            mat.SetShaderParameter("uv_rect", new Vector4(uv.Position.X - g, uv.Position.Y - g, uv.Size.X + 2 * g, uv.Size.Y + 2 * g));
            var quad = new ColorRect { Position = new Vector2(slot % Slots * TilePx, slot / Slots * TilePx), Size = new Vector2(TilePx, TilePx), Material = mat };
            _atlas.AddChild(quad);
            _baking.Add((quad, s, t, slot));
        }
        if (_baking.Count > 0) _atlas.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
    }

    int Allocate()
    {
        int best = -1;
        long oldest = long.MaxValue;
        for (int i = 0; i < _slots.Length; i++)
        {
            if (_slots[i].Owner == null) return i;
            if (_slots[i].Used < _frame - 1 && _slots[i].Used < oldest) { oldest = _slots[i].Used; best = i; }
        }
        if (best < 0) return -1;
        var (owner, tile, _) = _slots[best];
        if (owner != null && owner.Baked.Remove(tile))
        {
            owner.Stale.Remove(tile);
            owner.Urgent.Remove(tile);
            owner.PagesDirty = true;
        }
        _slots[best] = (null, default, 0);
        return best;
    }

    static void Repage(Sheet s)
    {
        s.Pages.Fill(new Color(0, 0, 0, 0));
        foreach (var (t, slot) in s.Baked.OrderBy(b => b.Key.Level))
        {
            var entry = Color.Color8((byte)(slot % Slots), (byte)(slot / Slots), (byte)t.Level, 255);
            for (int level = t.Level; level <= s.Levels; level++)
            {
                int span = 1 << (level - t.Level);
                s.Pages.FillRect(new Rect2I((1 << level) + t.X * span, t.Y * span, span, span), entry);
            }
        }
        s.PagesTex.Update(s.Pages);
        s.PagesDirty = false;
    }
}
