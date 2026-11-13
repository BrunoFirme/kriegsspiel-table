using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace KTable.App;

public partial class GroundTiles : Node
{
    public const int TilePx = 256, GutterPx = 2, Slots = 16;
    const int AtlasPx = TilePx * Slots, BakesPerFrame = 8, UrgentBakesPerFrame = 24, MostLevel = 9, RetireFrames = 180, Budget = Slots * Slots - UrgentBakesPerFrame - BakesPerFrame, Relaxings = 4;
    const int CardQuietFrames = 120, CoveredWaitFrames = 12, BlendedLevels = 2, OutOfSight = int.MaxValue, MostKeptParts = 30000, FadeFrames = 10;
    const double QuickFrame = 0.018, FairFrame = 0.035;
    const int QuickBakes = 24, FairBakes = 12, QuickFresh = 8, FairFresh = 4, SlowFresh = 2;
    static readonly Vector2 Aside = new(-8 * AtlasPx, -8 * AtlasPx);
    const float FinestTexelWorld = 0.002f, Sharpness = 0.9f, MostStretch = 4f;
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
    readonly List<(ColorRect Quad, Sheet Sheet, Tile Tile, int Slot, bool Urgent)> _baking = new();
    readonly List<(ColorRect Quad, Sheet Sheet, Tile Tile, int Slot)> _drawn = new();
    readonly Node2D _grounds = new(), _builts = new();
    long _frame;
    int _keptParts, _freshBuilt, _bakes = BakesPerFrame, _fresh = SlowFresh;
    readonly List<(Sheet Sheet, Tile Tile)> _leaves = new(), _ahead = new();
    bool _carding;

    internal sealed class KeptBuilt
    {
        public required Node2D Mover { get; init; }
        public required int Parts { get; init; }
        public required long Of { get; init; }
        public required int Style { get; init; }
        public long Used;
    }

    public static bool Enabled => DisplayServer.GetName() != "headless";

    public static int Style { get; set; }

    public static int BuiltLayersDrawn { get; private set; }

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
        public required Texture2D Atlas { get; init; }
        public required Func<Vector2, float, Vector3> World { get; init; }
        public required float Low { get; init; }
        public required float High { get; init; }
        public GroundCard.Spec? Card { get; init; }
        public BuiltPainter? Built { get; init; }
        public readonly Dictionary<Tile, int> Baked = new();
        internal readonly Dictionary<Tile, KeptBuilt> Kept = new();
        internal readonly Dictionary<Tile, long> Born = new();
        public readonly HashSet<Tile> Stale = new();
        public readonly HashSet<Tile> Urgent = new();
        internal readonly HashSet<Tile> Underway = new();
        public bool PagesDirty;
        internal int Changes = 1, ChangesSeen, ChangesNoted, CardOf;
        internal long QuietSince, StaledAt;

        public void Lay(ShaderMaterial onto)
        {
            onto.SetShaderParameter("atlas", Atlas);
            onto.SetShaderParameter("pages", PagesTex);
            onto.SetShaderParameter("levels", (float)Levels);
            onto.SetShaderParameter("atlas_slots", (float)Slots);
            onto.SetShaderParameter("tile_px", (float)TilePx);
            onto.SetShaderParameter("gutter_px", (float)GutterPx);
        }

        public void StaleAll()
        {
            foreach (var t in Baked.Keys) Stale.Add(t);
            foreach (var t in Underway) Stale.Add(t);
            Changes++;
        }

        public void StaleUv(Rect2 uv, bool urgent = false)
        {
            foreach (var t in Baked.Keys.Concat(Underway))
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

    public override void _Ready()
    {
        AddChild(_atlas);
        _atlas.AddChild(_grounds);
        _atlas.AddChild(_builts);
        var warm = new Node2D { Material = new ShaderMaterial { Shader = SurfaceBook.Shader } };
        _builts.AddChild(warm);
        RenderingServer.CanvasItemAddTriangleArray(warm.GetCanvasItem(), [0, 1, 2], [Vector2.Zero, Vector2.Right, Vector2.Down], [Colors.Transparent, Colors.Transparent, Colors.Transparent],
            [Vector2.Zero, Vector2.Zero, Vector2.Zero]);
        _atlas.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        RenderingServer.FramePostDraw += Drawn;
    }

    public override void _ExitTree() => RenderingServer.FramePostDraw -= Drawn;

    void Drawn()
    {
        foreach (var (quad, sheet, tile, slot, _) in _baking) _drawn.Add((quad, sheet, tile, slot));
        _baking.Clear();
    }

    public Sheet Track(string key, Node3D owner, ShaderMaterial bake, Func<Vector2, float, Vector3> world, float sheetWidth, float low, float high, bool cut, bool redrawn,
        BuiltPainter? built = null, GroundCard.Spec? card = null)
    {
        int levels = Math.Clamp((int)Math.Ceiling(Math.Log2(Math.Max(1, sheetWidth / FinestTexelWorld / (TilePx - 2 * GutterPx)))), 0, MostLevel);
        int side = 1 << levels;
        var pages = Image.CreateEmpty(side * 2, side, false, Image.Format.Rgba8);
        var view = new ShaderMaterial { Shader = ViewShader, RenderPriority = cut ? 1 : 0 };
        view.SetShaderParameter("cut", cut);
        var sheet = new Sheet
        {
            Key = key, Owner = owner, Bake = bake, View = view, Levels = levels, Pages = pages, PagesTex = ImageTexture.CreateFromImage(pages), Atlas = _atlas.GetTexture(),
            World = world, Low = low, High = high, Card = card, Built = built,
        };
        sheet.Lay(view);
        if (_retired.Remove(key, out var old) && old.Sheet.Levels == levels)
        {
            foreach (var (t, slot) in old.Sheet.Baked)
            {
                sheet.Baked[t] = slot;
                _slots[slot].Owner = sheet;
            }
            foreach (var (t, kept) in old.Sheet.Kept) sheet.Kept[t] = kept;
            old.Sheet.Kept.Clear();
            if (redrawn) sheet.StaleAll();
            Repage(sheet, _frame);
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
        for (int i = _drawn.Count - 1; i >= 0; i--)
            if (_drawn[i].Sheet == sheet)
            {
                _drawn[i].Quad.QueueFree();
                _drawn.RemoveAt(i);
            }
        sheet.Underway.Clear();
        foreach (var t in sheet.Kept.Keys.ToList()) Forget(sheet, t);
    }

    void Forget(Sheet sheet, Tile t)
    {
        if (!sheet.Kept.Remove(t, out var kept)) return;
        _keptParts -= kept.Parts;
        kept.Mover.QueueFree();
    }

    bool Kept(Sheet s, Tile t) => s.Built is { } painter && s.Kept.TryGetValue(t, out var kept) && kept.Of == painter.Key && kept.Style == Style;

    void LayBuilt(Sheet s, Tile t, Rect2 drawn, Vector2 at)
    {
        if (s.Built is not { } painter) return;
        if (!Kept(s, t))
        {
            Forget(s, t);
            if (painter.Layer(drawn, TilePx, Style) is not { } layer) return;
            var clip = new Control { Size = new Vector2(TilePx, TilePx), ClipContents = true, MouseFilter = Control.MouseFilterEnum.Ignore };
            clip.AddChild(layer);
            var mover = new Node2D();
            mover.AddChild(clip);
            _builts.AddChild(mover);
            s.Kept[t] = new KeptBuilt { Mover = mover, Parts = painter.Parts(drawn), Of = painter.Key, Style = Style };
            _keptParts += s.Kept[t].Parts;
            _freshBuilt++;
            BuiltLayersDrawn++;
        }
        s.Kept[t].Mover.Position = at;
        s.Kept[t].Used = _frame;
    }

    void PutAside(Sheet s, Tile t)
    {
        if (s.Kept.TryGetValue(t, out var kept)) kept.Mover.Position = Aside;
    }

    void ForgetTheOldest()
    {
        while (_keptParts > MostKeptParts)
        {
            var oldest = _sheets.Concat(_retired.Values.Select(r => r.Sheet)).SelectMany(s => s.Kept.Select(k => (Sheet: s, Tile: k.Key, k.Value.Used)))
                .Where(k => k.Used < _frame - 1).OrderBy(k => k.Used).Take(1).ToList();
            if (oldest.Count == 0) return;
            Forget(oldest[0].Sheet, oldest[0].Tile);
        }
    }

    public override void _Process(double delta)
    {
        _frame++;
        _bakes = delta <= QuickFrame ? QuickBakes : delta <= FairFrame ? FairBakes : BakesPerFrame;
        _fresh = delta <= QuickFrame ? QuickFresh : delta <= FairFrame ? FairFresh : SlowFresh;
        foreach (var key in _retired.Where(r => r.Value.Frame < _frame - RetireFrames).Select(r => r.Key).ToList())
        {
            Untrack(_retired[key].Sheet);
            _retired.Remove(key);
        }
        _freshBuilt = 0;
        foreach (var (quad, sheet, tile, slot) in _drawn)
        {
            quad.QueueFree();
            PutAside(sheet, tile);
            sheet.Underway.Remove(tile);
            if (_slots[slot].Owner != sheet) continue;
            if (!sheet.Baked.ContainsKey(tile)) sheet.Born[tile] = _frame;
            sheet.Baked[tile] = slot;
            sheet.PagesDirty = true;
        }
        _drawn.Clear();
        foreach (var s in _sheets)
        {
            if (s.Changes == s.ChangesNoted) continue;
            s.ChangesNoted = s.Changes;
            s.StaledAt = _frame;
        }
        if (Wanted() is { } wanted) Bake(wanted, urgentOnly: false);
        foreach (var s in _sheets)
        {
            if (s.PagesDirty || s.Born.Count > 0) Repage(s, _frame);
            CardWhenQuiet(s);
        }
    }

    List<(Sheet Sheet, Tile Tile, float Size, bool Shown)>? Wanted()
    {
        if (GetViewport()?.GetCamera3D() is not { } cam) return null;
        var frustum = cam.GetFrustum();
        var wanted = new List<(Sheet Sheet, Tile Tile, float Size, bool Shown)>();
        float sharpness = Sharpness;
        for (int relaxed = 0; ; relaxed++)
        {
            wanted.Clear();
            _leaves.Clear();
            foreach (var s in _sheets)
                if (IsInstanceValid(s.Owner) && s.Owner.IsVisibleInTree()) Need(s, cam, frustum, new Tile(0, 0, 0), sharpness, wanted);
            if (wanted.Count <= Budget || relaxed >= Relaxings) break;
            sharpness *= 2;
        }
        _ahead.Clear();
        foreach (var (s, t) in _leaves)
        {
            if (t.Level >= s.Levels || wanted.Count + _ahead.Count + 4 > Budget) continue;
            for (int j = 0; j < 2; j++)
                for (int i = 0; i < 2; i++) _ahead.Add((s, new Tile(t.Level + 1, t.X * 2 + i, t.Y * 2 + j)));
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
        DrawCard(s.Bake, card, s.Built);
    }

    async void DrawCard(ShaderMaterial bake, GroundCard.Spec card, BuiltPainter? built)
    {
        _carding = true;
        try { await GroundCard.Draw(this, bake, card, built); }
        catch (Exception e) { GD.PushWarning("the ground's card picture could not be drawn: " + e.Message); }
        finally { _carding = false; }
    }

    int Need(Sheet s, Camera3D cam, Godot.Collections.Array<Plane> frustum, Tile t, float sharpness, List<(Sheet Sheet, Tile Tile, float Size, bool Shown)> wanted)
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
            if (allOut) return OutOfSight;
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
        int mine = wanted.Count;
        wanted.Add((s, t, size, true));
        if (t.Level >= s.Levels || (!behind && size <= (TilePx - 2 * GutterPx) * sharpness))
        {
            _leaves.Add((s, t));
            return 0;
        }
        int finest = OutOfSight;
        for (int j = 0; j < 2; j++)
            for (int i = 0; i < 2; i++) finest = Math.Min(finest, Need(s, cam, frustum, new Tile(t.Level + 1, t.X * 2 + i, t.Y * 2 + j), sharpness, wanted));
        if (finest == OutOfSight) return 0;
        wanted[mine] = (s, t, size, finest < BlendedLevels);
        return finest + 1;
    }

    static float ShownSize(Span<Vector2> corners)
    {
        float along = ((corners[1] - corners[0]).Length() + (corners[2] - corners[3]).Length()) / 2;
        float across = ((corners[3] - corners[0]).Length() + (corners[2] - corners[1]).Length()) / 2;
        return Math.Min(Math.Max(along, across), Math.Min(along, across) * MostStretch);
    }

    void Bake(List<(Sheet Sheet, Tile Tile, float Size, bool Shown)> wanted, bool urgentOnly)
    {
        foreach (var (s, t, _, _) in wanted)
            if (s.Baked.TryGetValue(t, out int slot)) _slots[slot].Used = _frame;
        foreach (var (s, t) in _ahead)
            if (s.Baked.TryGetValue(t, out int slot)) _slots[slot].Used = _frame;
        bool Underway((Sheet Sheet, Tile Tile, float Size, bool Shown) w) => w.Sheet.Underway.Contains(w.Tile);
        bool Hurried((Sheet Sheet, Tile Tile, float Size, bool Shown) w) => w.Shown && w.Sheet.Urgent.Contains(w.Tile);
        bool Due((Sheet Sheet, Tile Tile, float Size, bool Shown) w) =>
            !w.Sheet.Baked.ContainsKey(w.Tile) || (w.Sheet.Stale.Contains(w.Tile) && (w.Shown || _frame - w.Sheet.StaledAt >= CoveredWaitFrames));
        int urgentLeft = UrgentBakesPerFrame - _baking.Count(b => b.Urgent);
        var todo = wanted.Where(w => Hurried(w) && !Underway(w)).OrderByDescending(w => w.Tile.Level).Take(Math.Max(0, urgentLeft)).ToList();
        if (!urgentOnly)
            todo.AddRange(wanted.Where(w => !Hurried(w) && !Underway(w) && Due(w))
                .OrderBy(w => w.Sheet.Baked.ContainsKey(w.Tile) ? 1 : 0).ThenBy(w => w.Tile.Level).ThenByDescending(w => w.Size).Take(_bakes));
        if (!urgentOnly && todo.Count < _bakes)
            todo.AddRange(_ahead.Select(a => (a.Sheet, a.Tile, Size: 0f, Shown: false)).Where(w => !Underway(w) && Due(w)).Take(_bakes - todo.Count));
        foreach (var w in todo)
        {
            var (s, t, _, _) = w;
            var uv = t.Uv;
            float g = uv.Size.X * GutterPx / (TilePx - 2 * GutterPx);
            var drawn = new Rect2(uv.Position.X - g, uv.Position.Y - g, uv.Size.X + 2 * g, uv.Size.Y + 2 * g);
            bool fresh = s.Built is { } painter && !Kept(s, t) && painter.Parts(drawn) > 0;
            if (fresh && !Hurried(w) && _freshBuilt >= _fresh) continue;
            int slot = s.Baked.TryGetValue(t, out int had) ? had : Allocate();
            if (slot < 0) break;
            _slots[slot] = (s, t, _frame);
            var mat = (ShaderMaterial)s.Bake.Duplicate();
            mat.SetShaderParameter("uv_rect", new Vector4(drawn.Position.X, drawn.Position.Y, drawn.Size.X, drawn.Size.Y));
            var quad = new ColorRect { Position = new Vector2(slot % Slots * TilePx, slot / Slots * TilePx), Size = new Vector2(TilePx, TilePx), Material = mat };
            _grounds.AddChild(quad);
            LayBuilt(s, t, drawn, quad.Position);
            s.Underway.Add(t);
            s.Stale.Remove(t);
            _baking.Add((quad, s, t, slot, s.Urgent.Remove(t)));
        }
        ForgetTheOldest();
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
            owner.Born.Remove(tile);
            owner.Stale.Remove(tile);
            owner.Urgent.Remove(tile);
            owner.PagesDirty = true;
        }
        _slots[best] = (null, default, 0);
        return best;
    }

    static void Repage(Sheet s, long frame)
    {
        s.Pages.Fill(new Color(0, 0, 0, 0));
        foreach (var t in s.Born.Where(b => frame - b.Value >= FadeFrames).Select(b => b.Key).ToList()) s.Born.Remove(t);
        foreach (var (t, slot) in s.Baked.OrderBy(b => b.Key.Level))
        {
            byte shown = s.Born.TryGetValue(t, out long born) ? (byte)Math.Clamp((frame - born + 1) * 255 / FadeFrames, 1, 254) : (byte)255;
            var entry = Color.Color8((byte)(slot % Slots), (byte)(slot / Slots), (byte)t.Level, shown);
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
