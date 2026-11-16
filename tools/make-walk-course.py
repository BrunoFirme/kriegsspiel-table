import hashlib
import json
import math
import os
import sys

if len(sys.argv) < 2:
    sys.exit("usage: python tools/make-walk-course.py <scratch packages folder>")
packages = sys.argv[1]
pack_dir = os.path.join(packages, "test.course")
assets_dir = os.path.join(pack_dir, "assets")
os.makedirs(assets_dir, exist_ok=True)
for old in os.listdir(assets_dir):
    os.remove(os.path.join(assets_dir, old))

APRON = 5.0
STRIPS = 2


def hill(angle, rise=2.0, plateau=2.0):
    run = rise / math.tan(math.radians(angle))
    length = 2 * run + plateau
    half = length / 2

    def h(x):
        x = x + half
        if x <= 0 or x >= length:
            return 0.0
        if x < run:
            return x / run * rise
        if x <= run + plateau:
            return rise
        return (length - x) / run * rise

    return length, h, [-half, -half + run, -half + run + plateau, half]


def stairs(riser, tread=0.3, steps=8, landing=1.2):
    length = 2 * steps * tread + landing
    half = length / 2
    up = steps * tread

    def h(x):
        x = x + half
        if x <= 0 or x >= length:
            return 0.0
        if x < up:
            return (int(x / tread) + 1) * riser
        if x <= up + landing:
            return steps * riser
        down = x - up - landing
        return (steps - 1 - int(down / tread)) * riser if down < up else 0.0

    xs = [-half, half]
    for i in range(steps + 1):
        for edge in (-half + i * tread, -half + up + landing + i * tread):
            xs += [edge - 0.001, edge + 0.001]
    return length, h, sorted(set(round(v, 4) for v in xs if -half <= v <= half))


def bumps(length=12.0, amplitude=0.08, wave=1.5):
    half = length / 2

    def h(x):
        return amplitude * (1 + math.sin((x + half) / wave * 2 * math.pi)) / 2 if -half < x < half else 0.0

    return length, h, [-half + i * length / 240 for i in range(241)]


def write_obj(path, length, h, xs):
    core = length / 2
    half = core + APRON
    xs = sorted(set([round(x, 4) for x in xs] + [-half, -core, core, half]))

    def height(x):
        return h(x) if -core < x < core else 0.0

    verts, faces = [], []
    for x in xs:
        for j in range(STRIPS + 1):
            verts.append((x, height(x), -half + j * 2 * half / STRIPS))
    for x in xs:
        for z in (-half, half):
            verts.append((x, 0.0, z))
    row = STRIPS + 1
    low = len(xs) * row
    for i in range(len(xs) - 1):
        for j in range(STRIPS):
            a, b = i * row + j + 1, (i + 1) * row + j + 1
            faces += [(a, b + 1, b), (a, a + 1, b + 1)]
        for side, j in ((0, 0), (1, STRIPS)):
            a, b = i * row + j + 1, (i + 1) * row + j + 1
            c, d = low + 2 * i + side + 1, low + 2 * (i + 1) + side + 1
            faces += [(a, b, d), (a, d, c)]
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        for x, y, z in verts:
            f.write(f"v {x:.4f} {y:.4f} {z:.4f}\n")
        for a, b, c in faces:
            f.write(f"f {a} {b} {c}\n")
    return max(height(x) for x in xs)


course = [(f"hill-{a}", *hill(a)) for a in (10, 20, 30, 40, 45, 50, 60)]
course += [("crest-25", *hill(25, plateau=0.0))]
course += [(f"stairs-{int(r * 100)}", *stairs(r)) for r in (0.15, 0.18, 0.25)]
course += [("bumps", *bumps())]
pieces, assets = [], {}
for name, length, h, xs in course:
    scratch = os.path.join(assets_dir, "scratch.obj")
    tall = write_obj(scratch, length, h, xs)
    key = hashlib.sha256(open(scratch, "rb").read()).hexdigest()
    os.replace(scratch, os.path.join(assets_dir, key + ".obj"))
    assets[key] = {"file": f"assets/{key}.obj", "kind": "mesh", "w": 0, "h": 0}
    side = round(length + 2 * APRON, 4)
    pieces.append({
        "id": f"test.course.{name}", "name": name, "kind": "model", "mesh": key,
        "size": {"w": side, "h": side}, "thickness": round(max(tall, 0.05), 4),
        "tags": ["prop", "life-size"], "shape": "box", "tint": "#B8A98A",
        "faces": [], "counters": [], "textLines": [], "attachments": [], "states": [],
    })
json.dump({"format": 2, "apiVersion": 1, "id": "test.course", "name": "Walk course",
           "source": {"authors": ["dev"], "url": "", "license": "dev test"}, "partial": False,
           "pieces": pieces, "assets": assets}, open(os.path.join(pack_dir, "pack.json"), "w", encoding="utf-8"), indent=1)

table_dir = os.path.join(packages, "scenarios", "walk-course")
os.makedirs(table_dir, exist_ok=True)
json.dump({
    "format": 1, "apiVersion": 1, "id": "walk-course", "name": "Walk course", "packs": [], "sheets": [],
    "grid": {"kind": "none", "pitch": 10, "subdivisions": 1, "color": "#1A1A2080"},
    "calibration": {"unitsPerMeter": 0.02, "units": "m", "perMetre": 1, "unitName": "m"},
    "clock": {"turn": 0, "gameTime": "1812-07-22T06:00", "incrementMinutes": 15},
    "lighting": {"sunEnergy": 0.6, "ambient": 0.85, "warmth": 0.7, "shadows": True, "fromClock": False, "tableShade": 1.05,
                 "backdrop": "open-sky-day", "mapStyle": "ancient", "contours": True, "snowLine": 0, "streams": True},
    "wind": {"from": 270, "strength": 1}, "visionRadius": 40, "pointersAlwaysShown": False,
    "boards": [{"id": "b-000001", "name": "Layer 1"}], "umpireBoard": [], "drawings": {}, "decals": {"umpire": []},
    "decalPalette": [], "scriptState": "", "ui": [], "uiPictures": {},
}, open(os.path.join(table_dir, "scenario.json"), "w", encoding="utf-8"), indent=1)
print(f"{len(pieces)} course pieces and the flat walk-course table written under {packages}")
