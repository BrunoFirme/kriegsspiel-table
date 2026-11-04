using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;

namespace KTable.App;

internal sealed class DecalTool
{
    readonly ModelStudio _m;
    Image? _decal;
    byte[]? _decalData;
    ImageTexture? _decalTex;
    string _decalName = "";
    public float Px = 220;
    public float Turn;
    public float Opacity = 1;
    public bool Mirror;

    public DecalTool(ModelStudio m) => _m = m;

    public bool Holds => _decal != null;

    public void Draw(Control o, Vector2 at)
    {
        if (_decalTex == null) return;
        var (hx, hy) = Half();
        o.DrawSetTransform(at, Mathf.DegToRad(Turn), new Vector2(Mirror ? -1 : 1, 1));
        o.DrawTextureRect(_decalTex, new Rect2(-hx, -hy, hx * 2, hy * 2), false, new Color(1, 1, 1, 0.55f * Mathf.Max(0.3f, Opacity)));
        o.DrawRect(new Rect2(-hx, -hy, hx * 2, hy * 2), new Color(0, 0, 0, 0.6f), false, 2.5f);
        o.DrawRect(new Rect2(-hx, -hy, hx * 2, hy * 2), new Color(0.95f, 0.78f, 0.25f, 0.95f), false, 1.2f);
        o.DrawLine(new Vector2(0, -hy), new Vector2(0, -hy - 10), new Color(0.95f, 0.78f, 0.25f, 0.95f), 1.2f);
        o.DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    (float hx, float hy) Half()
    {
        if (_decal == null) return (Px / 2, Px / 2);
        float aspect = _decal.GetWidth() / (float)Math.Max(1, _decal.GetHeight());
        return aspect >= 1 ? (Px / 2, Px / 2 / aspect) : (Px / 2 * aspect, Px / 2);
    }

    public void Set(Image img, string name)
    {
        img = (Image)img.Duplicate();
        if (img.IsCompressed()) img.Decompress();
        img.Convert(Image.Format.Rgba8);
        var used = img.GetUsedRect();
        if (used.Size.X > 0 && used.Size.Y > 0 && (used.Size.X < img.GetWidth() || used.Size.Y < img.GetHeight())) img = img.GetRegion(used);
        int m = Math.Max(img.GetWidth(), img.GetHeight());
        if (m > 2048) img.Resize(Math.Max(1, img.GetWidth() * 2048 / m), Math.Max(1, img.GetHeight() * 2048 / m), Image.Interpolation.Lanczos);
        _decal = img;
        _decalData = img.GetData();
        _decalTex = ImageTexture.CreateFromImage(img);
        _decalName = name;
        if (_m.Tool != "decal") _m.SetTool("decal");
        else _m.Studio.RebuildModelOptions();
        _m.RedrawOverlay();
    }

    public void Clear()
    {
        _decal = null;
        _decalData = null;
        _decalTex = null;
        _decalName = "";
        _m.Studio.RebuildModelOptions();
        _m.RedrawOverlay();
    }

    readonly struct Lens
    {
        readonly Vector2 _size;
        readonly Projection _proj;
        readonly Transform3D _view;
        readonly float _near;

        public Lens(Vector2 size, Camera3D cam)
        {
            _size = size;
            _proj = cam.GetCameraProjection();
            _view = cam.GlobalTransform.AffineInverse();
            _near = cam.Near;
        }

        public bool Project(Vector3 w, out Vector2 s, out float z)
        {
            var v = _view * w;
            z = -v.Z;
            s = default;
            if (z <= _near * 0.5f) return false;
            var c = _proj * new Vector4(v.X, v.Y, v.Z, 1);
            s = new Vector2((c.X / c.W * 0.5f + 0.5f) * _size.X, (1 - (c.Y / c.W * 0.5f + 0.5f)) * _size.Y);
            return true;
        }
    }

    sealed record Seen(Vector2[] S, float[] Z, bool[] Ok, Vector3[] W);

    public int Project(Vector2 at)
    {
        if (_decal == null || _decalData == null) return 0;
        var vpSize = (Vector2)_m.Vp.Size;
        var lens = new Lens(vpSize, _m.Cam);
        var (hx, hy) = Half();
        float reach = Mathf.Sqrt(hx * hx + hy * hy);
        int x0 = Math.Max(0, (int)(at.X - reach)), x1 = Math.Min((int)vpSize.X - 1, (int)(at.X + reach));
        int y0 = Math.Max(0, (int)(at.Y - reach)), y1 = Math.Min((int)vpSize.Y - 1, (int)(at.Y + reach));
        if (x1 < x0 || y1 < y0) return 0;
        var box = new Rect2I(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
        var parts = _m.Views.Values.Where(v => v.Part.Visible).ToList();
        var screen = new Dictionary<string, Seen>();
        var depth = Depth(parts, lens, box, screen);
        int painted = 0;
        foreach (var v in parts)
        {
            if (v.Fixed) continue;
            if (PressOn(v, screen[v.Part.Id], lens, box, depth, at, ref painted)) v.Repaint();
        }
        return painted;
    }

    float[] Depth(List<ModelPartView> parts, Lens lens, Rect2I box, Dictionary<string, Seen> screen)
    {
        int x0 = box.Position.X, y0 = box.Position.Y, x1 = box.End.X - 1, y1 = box.End.Y - 1, rw = box.Size.X;
        var depth = new float[box.Size.X * box.Size.Y];
        Array.Fill(depth, float.MaxValue);
        foreach (var v in parts)
        {
            var xf = _m.Stand.Transform * v.Part.Xf();
            int n = v.Data.V.Count;
            var s = new Vector2[n];
            var z = new float[n];
            var ok = new bool[n];
            var w = new Vector3[n];
            for (int i = 0; i < n; i++) { w[i] = xf * v.Data.V[i]; ok[i] = lens.Project(w[i], out s[i], out z[i]); }
            screen[v.Part.Id] = new Seen(s, z, ok, w);
            for (int t = 0; t < v.Data.Tris; t++)
            {
                int a = t * 3, b = a + 1, c = a + 2;
                if (!ok[a] || !ok[b] || !ok[c]) continue;
                float bx0 = Mathf.Min(s[a].X, Mathf.Min(s[b].X, s[c].X)), bx1 = Mathf.Max(s[a].X, Mathf.Max(s[b].X, s[c].X));
                float by0 = Mathf.Min(s[a].Y, Mathf.Min(s[b].Y, s[c].Y)), by1 = Mathf.Max(s[a].Y, Mathf.Max(s[b].Y, s[c].Y));
                if (bx1 < x0 || bx0 > x1 + 1 || by1 < y0 || by0 > y1 + 1) continue;
                float den = (s[b].Y - s[c].Y) * (s[a].X - s[c].X) + (s[c].X - s[b].X) * (s[a].Y - s[c].Y);
                if (Mathf.Abs(den) < 1e-9f) continue;
                int px0 = Math.Max(x0, (int)bx0), px1 = Math.Min(x1, (int)bx1), py0 = Math.Max(y0, (int)by0), py1 = Math.Min(y1, (int)by1);
                for (int py = py0; py <= py1; py++)
                    for (int px = px0; px <= px1; px++)
                    {
                        float qx = px + 0.5f, qy = py + 0.5f;
                        float w0 = ((s[b].Y - s[c].Y) * (qx - s[c].X) + (s[c].X - s[b].X) * (qy - s[c].Y)) / den;
                        float w1 = ((s[c].Y - s[a].Y) * (qx - s[c].X) + (s[a].X - s[c].X) * (qy - s[c].Y)) / den;
                        float w2 = 1 - w0 - w1;
                        if (w0 < -1e-4f || w1 < -1e-4f || w2 < -1e-4f) continue;
                        float zz = 1 / (w0 / z[a] + w1 / z[b] + w2 / z[c]);
                        int k = (py - y0) * rw + (px - x0);
                        if (zz < depth[k]) depth[k] = zz;
                    }
            }
        }
        return depth;
    }

    bool PressOn(ModelPartView v, Seen seen, Lens lens, Rect2I box, float[] depth, Vector2 at, ref int painted)
    {
        int x0 = box.Position.X, y0 = box.Position.Y, x1 = box.End.X - 1, y1 = box.End.Y - 1, rw = box.Size.X;
        var (hx, hy) = Half();
        float rot = Mathf.DegToRad(Turn);
        float cr = Mathf.Cos(-rot), sr = Mathf.Sin(-rot);
        float near = _m.Cam.Near;
        var camPos = _m.Cam.GlobalPosition;
        int dw = _decal!.GetWidth(), dh = _decal.GetHeight();
        var (s, z, ok, w) = seen;
        bool any = false;
        int count = painted;
        for (int t = 0; t < v.Data.Tris; t++)
        {
            int a = t * 3, b = a + 1, c = a + 2;
            if (!ok[a] || !ok[b] || !ok[c]) continue;
            float bx0 = Mathf.Min(s[a].X, Mathf.Min(s[b].X, s[c].X)), bx1 = Mathf.Max(s[a].X, Mathf.Max(s[b].X, s[c].X));
            float by0 = Mathf.Min(s[a].Y, Mathf.Min(s[b].Y, s[c].Y)), by1 = Mathf.Max(s[a].Y, Mathf.Max(s[b].Y, s[c].Y));
            if (bx1 < x0 || bx0 > x1 + 1 || by1 < y0 || by0 > y1 + 1) continue;
            var nrm = (w[c] - w[a]).Cross(w[b] - w[a]);
            if (nrm.LengthSquared() < 1e-20f) continue;
            nrm = nrm.Normalized();
            float facing = nrm.Dot((camPos - (w[a] + w[b] + w[c]) / 3).Normalized());
            if (facing <= 0.02f) continue;
            float fade = Mathf.SmoothStep(0.02f, 0.35f, facing);
            var ta = v.Data.UV[a] * new Vector2(v.W, v.H);
            var tb = v.Data.UV[b] * new Vector2(v.W, v.H);
            var tc = v.Data.UV[c] * new Vector2(v.W, v.H);
            Vector3 wa = w[a], wb = w[b], wc = w[c];
            ModelImport.Raster(ta, tb, tc, Math.Max(v.W, v.H), (tx, ty, w0, w1, w2) =>
            {
                if (tx >= v.W || ty >= v.H) return;
                var p = wa * w0 + wb * w1 + wc * w2;
                if (!lens.Project(p, out var sp, out var zp)) return;
                var d = sp - at;
                float lx = d.X * cr - d.Y * sr, ly = d.X * sr + d.Y * cr;
                float u = lx / (2 * hx) + 0.5f, vv = ly / (2 * hy) + 0.5f;
                if (u < 0 || u > 1 || vv < 0 || vv > 1) return;
                int px = (int)sp.X, py = (int)sp.Y;
                if (px < x0 || px > x1 || py < y0 || py > y1) return;
                float zb = depth[(py - y0) * rw + (px - x0)];
                if (zp > zb * 1.006f + near) return;
                if (Mirror) u = 1 - u;
                var col = Sample(dw, dh, u, vv);
                float alpha = col.A * Opacity * fade;
                if (alpha <= 0.004f) return;
                int o = (ty * v.W + tx) * 4;
                float k = 1 - alpha;
                v.Paint[o] = (byte)Math.Min(255, col.R * 255 * alpha + v.Paint[o] * k);
                v.Paint[o + 1] = (byte)Math.Min(255, col.G * 255 * alpha + v.Paint[o + 1] * k);
                v.Paint[o + 2] = (byte)Math.Min(255, col.B * 255 * alpha + v.Paint[o + 2] * k);
                v.Paint[o + 3] = (byte)Math.Min(255, 255 * alpha + v.Paint[o + 3] * k);
                any = true;
                count++;
            });
        }
        painted = count;
        return any;
    }

    Color Sample(int w, int h, float u, float v)
    {
        var d = _decalData!;
        float fx = u * w - 0.5f, fy = v * h - 0.5f;
        int xa = Math.Clamp((int)Mathf.Floor(fx), 0, w - 1), ya = Math.Clamp((int)Mathf.Floor(fy), 0, h - 1);
        int xb = Math.Min(w - 1, xa + 1), yb = Math.Min(h - 1, ya + 1);
        float tx = Mathf.Clamp(fx - xa, 0, 1), ty = Mathf.Clamp(fy - ya, 0, 1);
        Color P(int x, int y) { int o = (y * w + x) * 4; return new Color(d[o] / 255f, d[o + 1] / 255f, d[o + 2] / 255f, d[o + 3] / 255f); }
        Color a = P(xa, ya), b = P(xb, ya), c = P(xa, yb), e = P(xb, yb);
        float wa = (1 - tx) * (1 - ty) * a.A, wb = tx * (1 - ty) * b.A, wc = (1 - tx) * ty * c.A, we = tx * ty * e.A;
        float sum = wa + wb + wc + we;
        float alpha = (1 - tx) * (1 - ty) * a.A + tx * (1 - ty) * b.A + (1 - tx) * ty * c.A + tx * ty * e.A;
        if (sum <= 1e-6f) return new Color(0, 0, 0, 0);
        return new Color((a.R * wa + b.R * wb + c.R * wc + e.R * we) / sum, (a.G * wa + b.G * wb + c.G * wc + e.G * we) / sum, (a.B * wa + b.B * wb + c.B * wc + e.B * we) / sum, alpha);
    }

    public void Press(Vector2 at)
    {
        if (_decal == null) { _m.Studio.Say("Choose a picture for the decal first (the tool panel)."); return; }
        _m.BeginStroke();
        int n = Project(at);
        _m.EndStroke();
        _m.Studio.Say(n > 0 ? $"Decal pressed on ({n:#,0} texels). Ctrl+Z takes it back off." : "Nothing of the model under the decal.");
    }

    public void Options(PieceStudio studio, VBoxContainer box)
    {
        var row = new HBoxContainer();
        row.AddChild(Ui.Btn("Picture…", () =>
        {
            var fd =
                new FileDialog { FileMode = FileDialog.FileModeEnum.OpenFile, Access = FileDialog.AccessEnum.Filesystem, Filters = new[] { ImageFiles.Filter }, Title = "A picture for the decal",
                    Size = new Vector2I(900, 600) };
            Ui.RememberFolder(fd);
            FileThumbs.Show(fd);
            _m.AddChild(fd);
            void Use(string f) { if (ImageFiles.Load(f) is { } img) Set(img, Path.GetFileNameWithoutExtension(f)); else studio.Say("Could not read " + Path.GetFileName(f)); }
            fd.FileSelected += Use;
            PictureLinks.Offer(fd, studio.Store.Root, Use);
            fd.PopupCentered();
            Ui.FreeOnClose(fd);
        }, "PNG, JPEG, WebP, SVG, BMP, TGA, TIFF: a see-through PNG or an SVG makes a clean transfer"));
        row.AddChild(Ui.Btn("Paste", () => { if (ImageFiles.FromClipboard() is { } img) Set(img, "pasted"); else studio.Say("No picture on the clipboard."); }, "The picture on the clipboard   (Ctrl+V)"));
        box.AddChild(row);
        var pieces = new OptionButton { FitToLongestItem = false, TooltipText = "The front of a piece of this pack: its emblem onto the model" };
        pieces.AddItem("A piece's picture…");
        var withPics = _m.Pack.Pieces.Where(p => p.Image != null && p != _m.Def).OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Take(400).ToList();
        foreach (var p in withPics) pieces.AddItem(p.Name);
        pieces.Selected = 0;
        pieces.ItemSelected += i =>
        {
            if (i <= 0) return;
            var p = withPics[(int)i - 1];
            if (PackEdit.LoadAsset(_m.Store, _m.Pack, p.Image) is { } img) Set(img, p.Name);
        };
        if (withPics.Count > 0) box.AddChild(pieces);
        if (_decalTex != null)
        {
            var thumb =
                new TextureRect { Texture = _decalTex, CustomMinimumSize = new Vector2(0, 72), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, TooltipText = _decalName };
            box.AddChild(thumb);
            box.AddChild(Ui.Btn("No picture", Clear, "Let go of this picture: nothing is held over the model"));
        }
        else box.AddChild(Ui.Hint("No picture yet."));
        var g = Ui.Form(64);
        HSlider Slider(string label, double min, double max, double step, double value, Action<double> set, string suffix)
        {
            g.AddChild(new Label { Text = label });
            var h = new HBoxContainer();
            var s = new HSlider { MinValue = min, MaxValue = max, Step = step, Value = value, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
            var l = Ui.Readout($"{value:0}{suffix}", 48);
            s.ValueChanged += x =>
            {
                set(x);
                l.Text = $"{x:0}{suffix}";
                _m.RedrawOverlay();
            };
            h.AddChild(s);
            h.AddChild(l);
            g.AddChild(h);
            return s;
        }
        Slider("Size", 16, 1200, 1, Px, x => Px = (float)x, " px");
        Slider("Turn", -180, 180, 1, Turn, x => Turn = (float)x, "°");
        Slider("Strength", 5, 100, 1, Opacity * 100, x => Opacity = (float)x / 100, "%");
        Ui.AlignForm(g);
        box.AddChild(g);
        var mirror = new CheckBox { Text = "Mirrored", ButtonPressed = Mirror, TooltipText = "For the other side of a flag or a symmetrical part" };
        mirror.Toggled += on => { Mirror = on; _m.RedrawOverlay(); };
        box.AddChild(mirror);
    }

    public bool Wheel(InputEventMouseButton w)
    {
        if (_m.Tool != "decal") return false;
        bool up = w.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelLeft;
        if (w.CtrlPressed) Px = Mathf.Clamp(Px * (up ? 1.1f : 1 / 1.1f), 16, 1200);
        else if (w.ShiftPressed || w.ButtonIndex is MouseButton.WheelLeft or MouseButton.WheelRight) Turn = Mathf.Wrap(Turn + (up ? 5 : -5), -180, 180);
        else return false;
        _m.RedrawOverlay();
        _m.Studio.RebuildModelOptions();
        return true;
    }
}
