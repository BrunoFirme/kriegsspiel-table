import math
import os

import numpy as np
from PIL import Image, ImageDraw, ImageEnhance, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "src", "App")
S = 1024
K = S / 512
SS = 4
MS = 2

WASH = (240, 228, 200)
WATER = (34, 84, 142)
VIGNETTE = (58, 42, 18)
RIM = (74, 56, 24)
MARKER = (228, 42, 50)
TAIL, TIP = (172.0, 340.0), (278.0, 234.0)
BARB = 40
NIB = 17


def the_map():
    src = Image.open(os.path.join(HERE, "icon-map.jpg")).convert("RGB")
    a = np.asarray(src).astype(int)
    blue = (a[..., 2] - a[..., 0] > 10) & (a[..., 2] > 70)
    img = src.resize((S, S), Image.LANCZOS)
    img = ImageEnhance.Contrast(img).enhance(1.15)
    img = Image.blend(img, Image.new("RGB", (S, S), WASH), 0.18)
    lines = Image.fromarray((blue * 255).astype(np.uint8)).resize((S, S), Image.LANCZOS).filter(ImageFilter.MaxFilter(3))
    img.paste(Image.new("RGB", (S, S), WATER), (0, 0), lines.point(lambda v: int(v * 0.85)))
    yy, xx = np.mgrid[0:S, 0:S]
    d = np.hypot(xx - S / 2, yy - S / 2) / (0.72 * S)
    alpha = np.clip((d - 0.6) / 0.4, 0, 1) * 0.45
    shade = Image.new("RGBA", (S, S), VIGNETTE + (0,))
    shade.putalpha(Image.fromarray((alpha * 255).astype(np.uint8)))
    out = img.convert("RGBA")
    out.alpha_composite(shade)
    return out


def bezier(p0, c, p1, n=32):
    t = np.linspace(0, 1, n)[:, None]
    return (1 - t) ** 2 * np.array(p0) + 2 * (1 - t) * t * np.array(c) + t ** 2 * np.array(p1)


def marker_stroke(pts, scale, size, rng, dry):
    pts = np.asarray(pts, float) * scale
    r = NIB * scale / 2
    x0, y0 = (int(max(v - 1.2 * r - 4, 0)) for v in pts.min(axis=0))
    x1, y1 = (int(min(v + 1.2 * r + 4, size)) for v in pts.max(axis=0))
    yy, xx = np.mgrid[y0:y1, x0:x1].astype(float) + 0.5
    dist = np.full(xx.shape, np.inf)
    along = np.zeros(xx.shape)
    side = np.zeros(xx.shape)
    seg = np.diff(pts, axis=0)
    seglen = np.hypot(seg[:, 0], seg[:, 1])
    cum = np.concatenate([[0], np.cumsum(seglen)])
    for i, (a, d, ln) in enumerate(zip(pts[:-1], seg, seglen)):
        t = np.clip(((xx - a[0]) * d[0] + (yy - a[1]) * d[1]) / (ln * ln), 0, 1)
        px, py = xx - (a[0] + t * d[0]), yy - (a[1] + t * d[1])
        di = np.hypot(px, py)
        m = di < dist
        dist[m] = di[m]
        along[m] = ((cum[i] + t * ln) / cum[-1])[m]
        side[m] = ((d[0] * py - d[1] * px) / ln / r)[m]
    reach = r * (1 + 0.07 * np.exp(-along * cum[-1] / (1.5 * r)) - 0.05 * along)
    cover = np.clip(reach - dist + 0.5, 0, 1)
    knots = rng.random(40)
    streak = np.interp((side + 1.3) * 14, np.arange(40), knots)
    streak = np.clip((streak - 0.5) * 2.5, 0, 1)
    ink = 1 - (dry[0] + (dry[1] - dry[0]) * along) * streak
    ink *= 1 + 0.12 * np.clip((dist / reach - 0.7) / 0.3, 0, 1)
    return x0, y0, cover * np.clip(ink * 0.95, 0, 1)


def marker_arrow():
    size = S * MS
    scale = K * MS
    rng = np.random.default_rng(1889)
    dx, dy = TIP[0] - TAIL[0], TIP[1] - TAIL[1]
    ln = math.hypot(dx, dy)
    u = (dx / ln, dy / ln)
    n = (-u[1], u[0])
    mid = ((TAIL[0] + TIP[0]) / 2 + n[0] * ln * 0.025, (TAIL[1] + TIP[1]) / 2 + n[1] * ln * 0.025)
    shaft = bezier(TAIL, mid, TIP)

    def barb(deg, length):
        c, s = math.cos(math.radians(deg)), math.sin(math.radians(deg))
        bx, by = -u[0] * c - -u[1] * s, -u[0] * s + -u[1] * c
        return (TIP[0] + bx * length, TIP[1] + by * length)

    tip = (TIP[0] + u[0] * 3, TIP[1] + u[1] * 3)
    head = [barb(30, BARB), tip, barb(-35, BARB * 0.9)]

    factor = np.ones((size, size, 3))
    dye = 1 - np.array(MARKER) / 255.0
    for pts, dry in ((shaft, (0.08, 0.32)), (head, (0.1, 0.25))):
        x0, y0, a = marker_stroke(pts, scale, size, rng, dry)
        h, w = a.shape
        factor[y0:y0 + h, x0:x0 + w] *= 1 - a[..., None] * dye
    img = Image.fromarray((factor * 255).round().astype(np.uint8)).resize((S, S), Image.LANCZOS)
    return np.asarray(img).astype(float) / 255


def main():
    big = S * SS
    r0, r1, rad = 12 * K * SS, (512 - 12) * K * SS - 1, 92 * K * SS

    mask = Image.new("L", (big, big), 0)
    ImageDraw.Draw(mask).rounded_rectangle([r0, r0, r1, r1], radius=rad, fill=255)
    mask = mask.resize((S, S), Image.LANCZOS)

    top = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    ImageDraw.Draw(top).rounded_rectangle([r0, r0, r1, r1], radius=rad, outline=RIM + (178,), width=int(6 * K * SS))
    top = top.resize((S, S), Image.LANCZOS)

    drawn = np.asarray(the_map().convert("RGB")).astype(float) * marker_arrow()
    out = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    out.paste(Image.fromarray(drawn.round().astype(np.uint8)), (0, 0), mask)
    out.alpha_composite(top)

    out.resize((256, 256), Image.LANCZOS).save(os.path.join(OUT, "icon.png"))
    out.save(os.path.join(OUT, "icon.ico"), sizes=[(256, 256), (128, 128), (64, 64), (48, 48), (32, 32), (16, 16)])
    print("wrote icon.png and icon.ico")


main()
