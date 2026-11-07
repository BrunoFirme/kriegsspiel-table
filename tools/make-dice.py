import functools
import hashlib
import io
import itertools
import json
import math
import os
import random
import sys

from PIL import Image, ImageDraw, ImageFilter, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "src", "App", "standard", "std.dice")
PHI = (1 + 5 ** 0.5) / 2
ATLAS = 1024
WUP = (0, 1, 0)


def sub(a, b): return tuple(x - y for x, y in zip(a, b))
def add(a, b): return tuple(x + y for x, y in zip(a, b))
def mul(a, k): return tuple(x * k for x in a)
def dot(a, b): return sum(x * y for x, y in zip(a, b))
def cross(a, b): return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])
def norm(a):
    l = math.sqrt(dot(a, a))
    return tuple(x / l for x in a)
def centroid(pts): return mul([sum(p[i] for p in pts) for i in range(3)], 1 / len(pts))


def order_around(verts, idx, normal):
    c = centroid([verts[i] for i in idx])
    u = norm(sub(verts[idx[0]], c))
    v = cross(normal, u)
    return sorted(idx, key=lambda i: math.atan2(dot(sub(verts[i], c), v), dot(sub(verts[i], c), u)))


def tetra():
    v = [(1, 1, 1), (1, -1, -1), (-1, 1, -1), (-1, -1, 1)]
    return v, [list(f) for f in itertools.combinations(range(4), 3)]


def cube():
    v = [(x, y, z) for x in (-1, 1) for y in (-1, 1) for z in (-1, 1)]
    faces = []
    for axis in range(3):
        for s in (-1, 1):
            idx = [i for i, p in enumerate(v) if p[axis] == s]
            faces.append(order_around(v, idx, tuple(s if a == axis else 0 for a in range(3))))
    return v, faces


def octa():
    v = [(1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1)]
    faces = []
    for sx in (0, 1):
        for sy in (2, 3):
            for sz in (4, 5):
                idx = [sx, sy, sz]
                faces.append(order_around(v, idx, norm(centroid([v[i] for i in idx]))))
    return v, faces


def icosa():
    v = []
    for a in (-1, 1):
        for b in (-PHI, PHI):
            v += [(0, a, b), (a, b, 0), (b, 0, a)]
    faces = []
    for tri in itertools.combinations(range(12), 3):
        if all(abs(math.dist(v[i], v[j]) - 2) < 1e-6 for i, j in itertools.combinations(tri, 2)):
            faces.append(order_around(v, list(tri), norm(centroid([v[i] for i in tri]))))
    return v, faces


def dodeca():
    v = [(x, y, z) for x in (-1, 1) for y in (-1, 1) for z in (-1, 1)]
    for a in (-1 / PHI, 1 / PHI):
        for b in (-PHI, PHI):
            v += [(0, a, b), (a, b, 0), (b, 0, a)]
    faces = []
    for a in (-1, 1):
        for b in (-PHI, PHI):
            for n in [(a, 0, b), (0, b, a), (b, a, 0)]:
                n = norm(n)
                best = sorted(range(20), key=lambda i: -dot(v[i], n))[:5]
                faces.append(order_around(v, best, n))
    return v, faces


def trapezohedron():
    r, h = 1.0, 0.13
    ring = [(r * math.cos(math.radians(36 * k)), h if k % 2 == 1 else -h, r * math.sin(math.radians(36 * k))) for k in range(10)]

    def plane_error(H):
        top = (0, H, 0)
        A, B, A2 = ring[9], ring[0], ring[1]
        return dot(cross(sub(A, top), sub(B, top)), sub(A2, top))

    lo, hi = 0.3, 5.0
    assert plane_error(lo) * plane_error(hi) < 0, "d10 apex: no sign change, geometry wrong"
    for _ in range(80):
        mid = (lo + hi) / 2
        if plane_error(lo) * plane_error(mid) <= 0: hi = mid
        else: lo = mid
    H = (lo + hi) / 2
    v = ring + [(0, H, 0), (0, -H, 0)]
    top, bot = 10, 11
    faces = []
    for i in range(5):
        k = 2 * i
        faces.append([top, (k - 1) % 10, k % 10, (k + 1) % 10])
        faces.append([bot, k % 10, (k + 1) % 10, (k + 2) % 10])
    return v, [order_around(v, f, norm(centroid([v[i] for i in f]))) for f in faces]


def face_normal(v, f):
    return norm(cross(sub(v[f[1]], v[f[0]]), sub(v[f[2]], v[f[0]])))


def fix_winding(v, faces):
    out = []
    for f in faces:
        if dot(face_normal(v, f), centroid([v[i] for i in f])) < 0: f = list(reversed(f))
        n = face_normal(v, f)
        ds = [dot(v[i], n) for i in f]
        assert max(ds) - min(ds) < 1e-6, f"face {f} is not planar: {ds}"
        out.append(f)
    return out


def number_faces(v, faces, sides):
    normals = [face_normal(v, f) for f in faces]
    numbers = [0] * len(faces)
    low, high = 1, sides
    used = set()
    for i in range(len(faces)):
        if i in used: continue
        j = next((j for j in range(len(faces)) if j != i and j not in used and dot(normals[i], normals[j]) < -0.999), None)
        if j is None:
            numbers[i] = low; low += 1; used.add(i)
        else:
            numbers[i] = low; numbers[j] = high; low += 1; high -= 1; used.update((i, j))
    return numbers


def is_mirror_axis(pts, c, n, u):
    ax = cross(n, u)
    def refl(p): return sub(p, mul(ax, 2 * dot(sub(p, c), ax)))
    return all(any(math.dist(refl(p), q) < 1e-5 for q in pts) for p in pts)


def rot_apply(R, p): return (dot(R[0], p), dot(R[1], p), dot(R[2], p))


def transpose(R): return tuple(tuple(R[c][r] for c in range(3)) for r in range(3))


def rot_axis(axis, ang):
    x, y, z = axis
    c, s, C = math.cos(ang), math.sin(ang), 1 - math.cos(ang)
    return ((c + x * x * C, x * y * C - z * s, x * z * C + y * s),
            (y * x * C + z * s, c + y * y * C, y * z * C - x * s),
            (z * x * C - y * s, z * y * C + x * s, c + z * z * C))


def stand_up(n):
    d = max(-1.0, min(1.0, dot(n, WUP)))
    if d > 0.9999: return ((1, 0, 0), (0, 1, 0), (0, 0, 1))
    if d < -0.9999: return rot_axis((1, 0, 0), math.pi)
    return rot_axis(norm(cross(n, WUP)), math.acos(d))


def letter_up(v, f, n, mode):
    pts = [v[i] for i in f]
    c = centroid(pts)
    raw = ([sub(p, c) for p in pts] if mode == "vertex"
           else [sub(mul(add(a, b), 0.5), c) for a, b in zip(pts, pts[1:] + pts[:1])])
    cand = [norm(r) for r in raw if dot(r, r) > 1e-12 and is_mirror_axis(pts, c, n, norm(r))]
    if not cand: cand = [norm(r) for r in raw]
    ref = rot_apply(transpose(stand_up(n)), (0, 0, -1))
    return max(cand, key=lambda u: (round(dot(u, ref), 6), u))


def matmul(A, B): return tuple(tuple(sum(A[r][k] * B[k][c] for k in range(3)) for c in range(3)) for r in range(3))


def align(n_from, up_from, n_to, up_to):
    def frame(n, u):
        a = norm(n)
        b = norm(sub(u, mul(a, dot(u, a))))
        return a, b, cross(a, b)
    F, G = frame(n_from, up_from), frame(n_to, up_to)
    return tuple(tuple(sum(G[i][r] * F[i][c] for i in range(3)) for c in range(3)) for r in range(3))


def yaw(deg):
    s, c = math.sin(math.radians(deg)), math.cos(math.radians(deg))
    return ((c, 0, s), (0, 1, 0), (-s, 0, c))


@functools.lru_cache(maxsize=None)
def font(size):
    for name in ("arialbd.ttf", "segoeuib.ttf", "DejaVuSans-Bold.ttf"):
        try: return ImageFont.truetype(name, size)
        except OSError: pass
    return ImageFont.load_default()


def shade(colour, k):
    return tuple(max(0, min(255, int(c * k))) for c in colour)


def fit_font(text, cap, max_w):
    size = max(8, int(cap * 1.35))
    for _ in range(6):
        bb = font(size).getbbox(text)
        k = min(cap / max(1, bb[3] - bb[1]), max_w / max(1, bb[2] - bb[0]))
        if 0.97 <= k <= 1.05: break
        size = max(8, int(size * k))
    return font(size)


def stamp(atlas, cx, cy, box, angle, paint):
    ss, box = 3, max(8, int(box))
    layer = Image.new("RGBA", (box * ss, box * ss), (0, 0, 0, 0))
    paint(layer, box * ss / 2, ss)
    if abs(angle) > 0.01: layer = layer.rotate(angle, Image.BICUBIC)
    atlas.alpha_composite(layer.resize((box, box), Image.LANCZOS), (round(cx) - box // 2, round(cy) - box // 2))


def groove(layer, mask, base, blur, strength=0.32, steps=6):
    soft = mask.filter(ImageFilter.GaussianBlur(blur))
    layer.paste(shade(base, 0.55) + (255,), (0, 0),
                soft.point([int(min(steps - 1, t * steps // 256) * strength * 255 / (steps - 1)) for t in range(256)]))


def numeral_paint(text, cap, ink, base, underline):
    def paint(layer, c, ss):
        f = fit_font(text, cap * ss, cap * ss * 1.85)
        mask = Image.new("L", layer.size, 0)
        md = ImageDraw.Draw(mask)
        bb = md.textbbox((c, c), text, font=f, anchor="mm")
        dx, dy = c - (bb[0] + bb[2]) / 2, c - (bb[1] + bb[3]) / 2
        md.text((c + dx, c + dy), text, fill=255, font=f, anchor="mm")
        if underline:
            t = max(2, int(cap * ss * 0.14))
            md.rectangle([bb[0] + dx + t * 0.5, bb[3] + dy + t, bb[2] + dx - t * 0.5, bb[3] + dy + 2 * t], fill=255)
        groove(layer, mask, base, cap * ss * 0.085)
        layer.paste(ink + (255,), (0, 0), mask)
    return paint


PIPS = {1: [(0, 0)], 2: [(-1, -1), (1, 1)], 3: [(-1, -1), (0, 0), (1, 1)], 4: [(-1, -1), (1, -1), (-1, 1), (1, 1)],
        5: [(-1, -1), (1, -1), (0, 0), (-1, 1), (1, 1)], 6: [(-1, -1), (1, -1), (-1, 0), (1, 0), (-1, 1), (1, 1)]}


def pips_paint(n, pitch, r, ink, base):
    def paint(layer, c, ss):
        d = ImageDraw.Draw(layer)
        spots = [(c + ox * pitch * ss, c + oy * pitch * ss) for ox, oy in PIPS[n]]
        mask = Image.new("L", layer.size, 0)
        md = ImageDraw.Draw(mask)
        for x, y in spots: md.ellipse([x - r * ss, y - r * ss, x + r * ss, y + r * ss], fill=255)
        groove(layer, mask, base, r * ss * 0.38, 0.34)
        for i in range(9):
            f = i / 8
            rr = r * ss * (1 - f * 0.88)
            col = tuple(int(a + (b - a) * f) for a, b in zip(shade(ink, 0.78), shade(ink, 1.85)))
            for x, y in spots: d.ellipse([x - rr, y - rr, x + rr, y + rr], fill=col + (255,))
    return paint


def face_plate(d, poly, base, rim=0.08, steps=14):
    cx = sum(p[0] for p in poly) / len(poly)
    cy = sum(p[1] for p in poly) / len(poly)
    for i in range(steps + 1):
        f = i / steps
        s, k = 1 - rim * f, 0.84 + 0.16 * f
        d.polygon([(cx + (x - cx) * s, cy + (y - cy) * s) for x, y in poly], fill=shade(base, k))


def face_tints(name, colour, n, spread=0.016):
    rnd = random.Random(f"{name} faces")
    return [shade(colour, 1 + rnd.uniform(-spread, spread)) for _ in range(n)]


def inradius(poly, gx, gy):
    best = 1e9
    for (x0, y0), (x1, y1) in zip(poly, poly[1:] + poly[:1]):
        ex, ey = x1 - x0, y1 - y0
        l = math.hypot(ex, ey) or 1
        best = min(best, abs((gx - x0) * ey - (gy - y0) * ex) / l)
    return best


class MeshOut:
    def __init__(self, width, height):
        self.width, self.height = width, height
        self.v, self.vt, self.vn, self.f, self.polys = [], [], [], [], []

    def emit(self, points, uv_px, normals_per_vertex):
        base = len(self.v)
        for p, (uu, vv), n in zip(points, uv_px, normals_per_vertex):
            self.v.append(f"v {p[0]:.6f} {p[1]:.6f} {p[2]:.6f}")
            self.vt.append(f"vt {uu / self.width:.6f} {1 - vv / self.height:.6f}")
            self.vn.append(f"vn {n[0]:.6f} {n[1]:.6f} {n[2]:.6f}")
        self.f.append("f " + " ".join(f"{base + i + 1}/{base + i + 1}/{base + i + 1}" for i in range(len(points))))
        self.polys.append((list(points), list(uv_px), normals_per_vertex[0]))

    def obj(self, name):
        return "\n".join(["# Kriegsspiel Table standard die " + name] + self.v + self.vt + self.vn + self.f) + "\n"


def bevels(v, faces, normals, plate, emit, bevel_px):
    edge_faces = {}
    for fi, f in enumerate(faces):
        for a, b in zip(f, f[1:] + f[:1]):
            edge_faces.setdefault(frozenset((a, b)), []).append(fi)
    for edge, fs in edge_faces.items():
        if len(fs) != 2: continue
        f1, f2 = fs
        a, b = tuple(edge)
        quad = [plate[(f1, a)], plate[(f1, b)], plate[(f2, b)], plate[(f2, a)]]
        nb = norm(add(normals[f1], normals[f2]))
        if dot(cross(sub(quad[1], quad[0]), sub(quad[2], quad[0])), nb) < 0: quad.reverse()
        emit(quad, [bevel_px] * 4, [nb] * 4)
    for vi in range(len(v)):
        around = [(fi, plate[(fi, vi)]) for fi, f in enumerate(faces) if vi in f]
        if len(around) < 3: continue
        nv = norm(v[vi])
        c = centroid([p for _, p in around])
        u = norm(sub(around[0][1], c)); w = cross(nv, u)
        around.sort(key=lambda fp: math.atan2(dot(sub(fp[1], c), w), dot(sub(fp[1], c), u)))
        pts = [p for _, p in around]
        emit(pts, [bevel_px] * len(pts), [nv] * len(pts))


def build(name, solid, sides, colour, ink, chamfer=0.085, up_mode="vertex", vertex_labels=False, pips=False):
    v, faces = solid()
    faces = fix_winding(v, faces)
    normals = [face_normal(v, f) for f in faces]
    cols = math.ceil(math.sqrt(len(faces)))
    rows = math.ceil(len(faces) / cols) + 1
    cell = (ATLAS // max(cols, rows)) & ~3
    atlas = Image.new("RGBA", (cols * cell, rows * cell), shade(colour, 0.84) + (255,))
    d = ImageDraw.Draw(atlas)
    bevel_colour = shade(colour, 0.82)
    d.rectangle([0, (rows - 1) * cell, cols * cell, rows * cell], fill=bevel_colour + (255,))
    bevel_px = (cols * cell / 2, (rows - 0.5) * cell)

    if vertex_labels:
        labels = {i: i + 1 for i in range(len(v))}
        numbers = [None] * len(faces)
    else:
        numbers = number_faces(v, faces, sides)

    plate = {}
    for fi, f in enumerate(faces):
        c = centroid([v[i] for i in f])
        for i in f: plate[(fi, i)] = add(c, mul(sub(v[i], c), 1 - chamfer))

    mesh = MeshOut(atlas.width, atlas.height)
    emit = mesh.emit

    tints = face_tints(name, colour, len(faces))
    states, poses = [], {}
    for fi, f in enumerate(faces):
        pts = [plate[(fi, i)] for i in f]
        c = centroid(pts)
        n = normals[fi]
        up3 = letter_up(v, f, n, up_mode)
        u, w = cross(up3, n), up3
        flat = [(dot(sub(p, c), u), dot(sub(p, c), w)) for p in pts]
        bx = (min(x for x, _ in flat) + max(x for x, _ in flat)) / 2
        by = (min(y for _, y in flat) + max(y for _, y in flat)) / 2
        ext = max(max(abs(x - bx) for x, _ in flat), max(abs(y - by) for _, y in flat))
        scale = cell * 0.46 / ext
        cx, cy = (fi % cols) * cell + cell / 2, (fi // cols) * cell + cell / 2
        poly = [(cx + (x - bx) * scale, cy - (y - by) * scale) for x, y in flat]
        gx, gy = cx - bx * scale, cy + by * scale
        body = tints[fi]
        face_plate(d, poly, body)
        inr = inradius(poly, gx, gy)
        if vertex_labels:
            for (px, py), vi in zip(poly, f):
                ang = math.degrees(math.atan2(-(px - gx), -(py - gy)))
                stamp(atlas, gx + (px - gx) * 0.56, gy + (py - gy) * 0.56, inr * 1.5, ang,
                      numeral_paint(str(labels[vi]), inr * 0.52, ink, body, False))
        elif pips:
            stamp(atlas, gx, gy, cell * 0.76, 0, pips_paint(numbers[fi], cell * 0.23, cell * 0.072, ink, body))
        else:
            k = numbers[fi]
            stamp(atlas, gx, gy, inr * 2.9, 0, numeral_paint(str(k), inr * (0.95 if len(f) == 3 else 0.8), ink, body, sides >= 9 and k in (6, 9)))
        emit(pts, poly, [n] * len(pts))
        if not vertex_labels:
            states.append((numbers[fi], n))
            poses[numbers[fi]] = (n, up3)

    bevels(v, faces, normals, plate, emit, bevel_px)

    if vertex_labels:
        states = [(labels[i], norm(v[i])) for i in range(len(v))]
        for i in range(len(v)):
            nv = norm(v[i])
            nf = next(normals[fi] for fi, f in enumerate(faces) if i in f)
            poses[labels[i]] = (nv, mul(norm(sub(nf, mul(nv, dot(nf, nv)))), -1))
    states.sort(key=lambda s: s[0])
    return mesh.obj(name), atlas, states, mesh.polys, poses


KEY, FILL = (-0.42, 0.78, 0.46), (0.62, 0.18, 0.72)


def affine(scr, uvs):
    i0, i1 = 0, 1
    i2 = max(range(2, len(scr)), key=lambda i: abs((scr[i1][0] - scr[i0][0]) * (scr[i][1] - scr[i0][1])
                                                   - (scr[i][0] - scr[i0][0]) * (scr[i1][1] - scr[i0][1])))
    (x0, y0), (x1, y1), (x2, y2) = scr[i0], scr[i1], scr[i2]
    (u0, v0), (u1, v1), (u2, v2) = uvs[i0], uvs[i1], uvs[i2]
    p = (x1 - x0, y1 - y0); q = (x2 - x0, y2 - y0)
    det = p[0] * q[1] - q[0] * p[1]
    if abs(det) < 1e-9: return None
    du1, du2, dv1, dv2 = u1 - u0, u2 - u0, v1 - v0, v2 - v0
    a = (du1 * q[1] - du2 * p[1]) / det
    b = (du2 * p[0] - du1 * q[0]) / det
    dd = (dv1 * q[1] - dv2 * p[1]) / det
    e = (dv2 * p[0] - dv1 * q[0]) / det
    return a, b, u0 - a * x0 - b * y0, dd, e, v0 - dd * x0 - e * y0


def render(polys, atlas, px, rot, elev, fill=0.93, ss=4,
           ambient=0.50, key=0.54, fil=0.16, ks=0.22, shine=22, rim=0.10):
    W = int(px * ss)
    e = math.radians(elev)
    fwd = (0.0, math.sin(e), math.cos(e))
    right = norm(cross(WUP, fwd))
    up = cross(fwd, right)
    def camvec(k): return norm(add(add(mul(right, k[0]), mul(up, k[1])), mul(fwd, k[2])))
    L1, L2 = camvec(KEY), camvec(FILL)
    H = norm(add(L1, fwd))
    tex = atlas.convert("RGB")

    def view(p):
        q = rot_apply(rot, p)
        return (dot(q, right), -dot(q, up), dot(q, fwd))

    seen = [[view(p) for p in poly[0]] for poly in polys]
    xs = [p[0] for g in seen for p in g]; ys = [p[1] for g in seen for p in g]
    k = W * fill / max(max(xs) - min(xs), max(ys) - min(ys))
    ox = W / 2 - (min(xs) + max(xs)) / 2 * k
    oy = W / 2 - (min(ys) + max(ys)) / 2 * k

    img = Image.new("RGBA", (W, W), (0, 0, 0, 0))
    faces = []
    for (pts3, uvs, n), g in zip(polys, seen):
        nn = rot_apply(rot, n)
        facing = dot(nn, fwd)
        if facing <= 0.012: continue
        faces.append((sum(p[2] for p in g) / len(g), [(p[0] * k + ox, p[1] * k + oy) for p in g], uvs, nn, facing))
    faces.sort(key=lambda t: t[0])
    for _, scr, uvs, nn, facing in faces:
        x0 = max(0, int(min(p[0] for p in scr)) - 1); y0 = max(0, int(min(p[1] for p in scr)) - 1)
        x1 = min(W, int(max(p[0] for p in scr)) + 2); y1 = min(W, int(max(p[1] for p in scr)) + 2)
        if x1 <= x0 or y1 <= y0: continue
        mask = Image.new("L", (x1 - x0, y1 - y0), 0)
        ImageDraw.Draw(mask).polygon([(x - x0, y - y0) for x, y in scr], fill=255)
        A = None if len(set(uvs)) == 1 else affine(scr, uvs)
        if A is None:
            patch = Image.new("RGB", mask.size, tex.getpixel((int(uvs[0][0]), int(uvs[0][1]))))
        else:
            a, b, c, dd, ee, ff = A
            patch = tex.transform(mask.size, Image.AFFINE, (a, b, a * x0 + b * y0 + c, dd, ee, dd * x0 + ee * y0 + ff), Image.BICUBIC)
        lam = ambient + key * max(0.0, dot(nn, L1)) + fil * max(0.0, dot(nn, L2))
        spec = ks * max(0.0, dot(nn, H)) ** shine + rim * (1 - facing) ** 2.2
        patch = patch.point([min(255, int(t * lam + spec * 255)) for t in range(256)] * 3).convert("RGBA")
        img.paste(patch, (x0, y0), mask)
    return img.resize((int(px), int(px)), Image.LANCZOS)


def picture(polys, atlas, poses, number, px, elev, spin=0.0):
    n_from, up_from = poses[number]
    rot = align(n_from, up_from, WUP, (0, 0, -1))
    if spin: rot = matmul(yaw(spin), rot)
    return render(polys, atlas, px, rot, elev)


def sha_bytes(b): return hashlib.sha256(b).hexdigest()


PICTURE_LAYOUT = [
    ((0, 1, 0), (0.022, 0.3035, 0.3593, 0.6407), (-1, 0, 0), (0, 0, 1)),
    ((-1, 0, 0), (0.3593, 0.6407, 0.3593, 0.6407), (0, -1, 0), (0, 0, 1)),
    ((0, -1, 0), (0.6955, 0.977, 0.36, 0.6414), (1, 0, 0), (0, 0, 1)),
    ((0, 0, 1), (0.022, 0.3035, 0.022, 0.3035), (1, 0, 0), (0, 1, 0)),
    ((1, 0, 0), (0.3593, 0.6407, 0.022, 0.3035), (0, 1, 0), (0, 0, 1)),
    ((0, 0, -1), (0.6965, 0.978, 0.022, 0.3035), (1, 0, 0), (0, -1, 0)),
]
PICTURE_BEVEL_UV = (0.5, 0.83)


def quad_curve(a, b, c, steps=24):
    return [tuple((1 - t) ** 2 * a[i] + 2 * (1 - t) * t * b[i] + t * t * c[i] for i in range(2)) for t in (k / steps for k in range(steps + 1))]


def arrows_paint(kinds, ink, base):
    def paint(layer, c, ss):
        size = layer.size[0]
        stroke = int(size * 0.085)
        mask = Image.new("L", layer.size, 0)
        md = ImageDraw.Draw(mask)
        foot, turn = (c, c + 0.36 * size), (c, c - 0.02 * size)
        for k in kinds:
            if k == "S": path = [foot, (c, c - 0.22 * size)]
            else:
                side = -1 if k == "L" else 1
                path = quad_curve(foot, turn, (c + side * 0.30 * size, c - 0.16 * size))
            md.line(path, fill=255, width=stroke, joint="curve")
            (x0, y0), (x1, y1) = path[-2], path[-1]
            dx, dy = x1 - x0, y1 - y0
            ln = math.hypot(dx, dy) or 1
            dx, dy = dx / ln, dy / ln
            head, half = 0.17 * size, 0.12 * size
            tip = (x1 + dx * head, y1 + dy * head)
            md.polygon([tip, (x1 - dy * half, y1 + dx * half), (x1 + dy * half, y1 - dx * half)], fill=255)
        groove(layer, mask, base, size * 0.012)
        layer.paste(ink + (255,), (0, 0), mask)
    return paint


def lost_paint(ink, base):
    faint = tuple(int(a + (b - a) * 0.55) for a, b in zip(ink, base))
    def paint(layer, c, ss):
        size = layer.size[0]
        back = numeral_paint("10", size / ss * 0.42, faint, base, False)
        back(layer, c, ss)
        mask = Image.new("L", layer.size, 0)
        md = ImageDraw.Draw(mask)
        for word, dy in (("ABORTED", -0.13), ("MISSING", 0.13)):
            f = fit_font(word, size * 0.11, size * 0.8)
            md.text((c, c + dy * size), word, fill=255, font=f, anchor="mm")
        groove(layer, mask, base, size * 0.01)
        layer.paste(ink + (255,), (0, 0), mask)
    return paint


def hit_faces(ink, base, cap):
    return [("7", numeral_paint("7", cap, ink, base, False)), ("8", numeral_paint("8", cap, ink, base, False)),
            ("10", numeral_paint("10", cap, ink, base, False)), ("6", numeral_paint("6", cap, ink, base, True)),
            ("9", numeral_paint("9", cap, ink, base, True)), ("Aborted / missing", lost_paint(ink, base))]


def direction_faces(ink, base, cap):
    return [("Straight", arrows_paint("S", ink, base)), ("Straight, left or right", arrows_paint("SLR", ink, base)),
            ("Straight", arrows_paint("S", ink, base)), ("Left", arrows_paint("L", ink, base)),
            ("Left or right", arrows_paint("LR", ink, base)), ("Right", arrows_paint("R", ink, base))]


PICTURE_DICE = [("hit die", hit_faces), ("direction die", direction_faces)]


def build_picture(name, faces_of, colour, ink, chamfer=0.07):
    v, faces = cube()
    faces = fix_winding(v, faces)
    normals = [face_normal(v, f) for f in faces]
    atlas = Image.new("RGBA", (ATLAS, ATLAS), shade(colour, 0.82) + (255,))
    d = ImageDraw.Draw(atlas)
    plate = {}
    for fi, f in enumerate(faces):
        c = centroid([v[i] for i in f])
        for i in f: plate[(fi, i)] = add(c, mul(sub(v[i], c), 1 - chamfer))
    mesh = MeshOut(atlas.width, atlas.height)
    cell = (PICTURE_LAYOUT[0][1][1] - PICTURE_LAYOUT[0][1][0]) * ATLAS
    art = faces_of(ink, colour, cell * 0.42)
    tints = face_tints(name, colour, len(faces))
    states, poses = [], {}
    for k, (normal, (u0, u1, v0, v1), right, up) in enumerate(PICTURE_LAYOUT):
        fi = max(range(len(faces)), key=lambda i: dot(normals[i], normal))
        pts = [plate[(fi, i)] for i in faces[fi]]
        c = centroid(pts)
        half = max(abs(dot(sub(p, c), right)) for p in pts)
        uv = [((u0 + (dot(sub(p, c), right) / half + 1) / 2 * (u1 - u0)) * ATLAS,
               (1 - (v0 + (dot(sub(p, c), up) / half + 1) / 2 * (v1 - v0))) * ATLAS) for p in pts]
        box = [(u0 * ATLAS, (1 - v1) * ATLAS), (u1 * ATLAS, (1 - v1) * ATLAS), (u1 * ATLAS, (1 - v0) * ATLAS), (u0 * ATLAS, (1 - v0) * ATLAS)]
        body = tints[k]
        face_plate(d, box, body)
        label, paint = art[k]
        stamp(atlas, (u0 + u1) / 2 * ATLAS, (1 - (v0 + v1) / 2) * ATLAS, cell * 0.92, 0, paint)
        mesh.emit(pts, uv, [normals[fi]] * len(pts))
        states.append((label, normals[fi]))
        poses[k] = (normals[fi], up)
    bevels(v, faces, normals, plate, mesh.emit, (PICTURE_BEVEL_UV[0] * ATLAS, (1 - PICTURE_BEVEL_UV[1]) * ATLAS))
    return mesh.obj("picture d6"), atlas, states, mesh.polys, poses


def main():
    preview = sys.argv[sys.argv.index("--preview") + 1] if "--preview" in sys.argv else None
    assets_dir = os.path.join(OUT, "assets")
    os.makedirs(assets_dir, exist_ok=True)
    for f in os.listdir(assets_dir): os.remove(os.path.join(assets_dir, f))
    IVORY, DARK = (227, 219, 197), (28, 25, 24)
    dice = [
        ("d4", tetra, 4, IVORY, DARK, 0.095, 22, 44, dict(vertex_labels=True)),
        ("d6", cube, 6, IVORY, DARK, 0.07, 34, 24, dict(pips=True, up_mode="edge")),
        ("d8", octa, 8, IVORY, DARK, 0.085, 38, 0, {}),
        ("d10", trapezohedron, 10, IVORY, DARK, 0.085, 42, 0, {}),
        ("d12", dodeca, 12, IVORY, DARK, 0.08, 44, 0, {}),
        ("d20", icosa, 20, IVORY, DARK, 0.105, 44, 0, {}),
    ]
    pieces, assets = [], {}

    def put(data, ext, kind, w=0, h=0):
        sha = sha_bytes(data)
        rel = f"assets/{sha}.{ext}"
        with open(os.path.join(OUT, rel), "wb") as fh: fh.write(data)
        assets[sha] = {"file": rel, "kind": kind, "w": w, "h": h}
        return sha

    def png(im):
        buf = io.BytesIO(); im.save(buf, "PNG"); return buf.getvalue()

    for name, solid, sides, colour, ink, chamfer, elev, spin, opts in dice:
        obj, atlas, states, polys, poses = build(name, solid, sides, colour, ink, chamfer=chamfer, **opts)
        atlas_sha = put(png(atlas), "png", "image", atlas.width, atlas.height)
        mesh_sha = put(obj.encode("utf-8"), "obj", "mesh")
        shot = picture(polys, atlas, poses, sides, 160, elev, spin)
        thumb_sha = put(png(shot), "png", "image", 160, 160)
        if preview: shot.save(os.path.join(preview, f"thumb-{name.replace(' ', '-')}.png"))
        slug = name.replace(" ", "-")
        pieces.append({
            "id": f"std.dice.{slug}", "name": name, "kind": "model", "mesh": mesh_sha, "texture": atlas_sha, "image": thumb_sha,
            "size": {"w": 0.6, "h": 0.6}, "shape": "box", "thickness": 0.6, "tint": "#FFFFFF",
            "tags": ["die", "dice", name.split()[0]],
            "faces": [{"value": str(k), "up": [round(x, 6) for x in n]} for k, n in states],
            "counters": [], "textLines": [], "attachments": [],
        })
    for name, faces in PICTURE_DICE:
        obj, atlas, states, polys, poses = build_picture(name, faces, IVORY, DARK)
        atlas_sha = put(png(atlas), "png", "image", atlas.width, atlas.height)
        mesh_sha = put(obj.encode("utf-8"), "obj", "mesh")
        shot = picture(polys, atlas, poses, 0, 160, 34, 24)
        thumb_sha = put(png(shot), "png", "image", 160, 160)
        if preview:
            shot.save(os.path.join(preview, f"thumb-{name.replace(' ', '-')}.png"))
            atlas.save(os.path.join(preview, f"faces-{name.replace(' ', '-')}.png"))
        pieces.append({
            "id": f"std.dice.{name.replace(' ', '-')}", "name": name, "kind": "model", "mesh": mesh_sha, "texture": atlas_sha, "image": thumb_sha,
            "size": {"w": 0.6, "h": 0.6}, "shape": "box", "thickness": 0.6, "tint": "#FFFFFF",
            "tags": ["die", "dice", "d6", "picture"],
            "faces": [{"value": k, "up": [round(x, 6) for x in n]} for k, n in states],
            "counters": [], "textLines": [], "attachments": [],
        })
    pack = {
        "format": 1, "id": "std.dice", "name": "Dice",
        "source": {"authors": ["Kriegsspiel Table"], "url": "", "license": "standard pack, included with the app"},
        "pieces": pieces, "assets": assets, "partial": False,
    }
    with open(os.path.join(OUT, "pack.json"), "w", encoding="utf-8") as fh: json.dump(pack, fh, indent=2)
    print(f"wrote {len(pieces)} dice, {len(assets)} assets to {OUT}")


main()
