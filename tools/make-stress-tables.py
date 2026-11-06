import io, json, os, sys

packages = os.environ.get("KSTABLE_PACKAGES") or os.path.join(
    os.environ["LOCALAPPDATA"], "KriegsspielTable", "packages")
model_src = sys.argv[1]
pack = "tts.scripted-war-game-kriegsspiel-pieces"
tints = [None, "#9a3b3b", "#3b5d9a"]

base = json.load(io.open(model_src, encoding="utf-8"))
model = base["umpireBoard"][0]
defs = [p["id"] for p in json.load(io.open(os.path.join(packages, pack, "pack.json"), encoding="utf-8"))["pieces"]]
print(f"{len(defs)} defs in {pack}")

sizes = [int(a) for a in sys.argv[2:]] or [10000, 25000, 50000]
total = 0
for n in sizes:
    for kind in ("same", "mixed"):
        cols = int(n ** 0.5) + 1
        board = []
        for i in range(n):
            p = json.loads(json.dumps(model))
            p["id"] = f"p-{i:06d}"
            p["pos"] = {"x": (i % cols) * 2.2, "z": (i // cols) * 1.6}
            p["rotDeg"] = 180.0
            if kind == "mixed":
                p["def"] = defs[i % len(defs)]
                p["scale"] = 1.0 if i % 2 else 1.4
                t = tints[i % len(tints)]
                if t: p["tintOverride"] = t
                else: p.pop("tintOverride", None)
            else:
                p["scale"] = 1.0
                p.pop("tintOverride", None)
            board.append(p)
        sid = f"stress-{n // 1000}k-{kind}"
        d = json.loads(json.dumps(base))
        d["id"] = sid
        d["name"] = (f"Stress test: {n:,} of one block" if kind == "same"
                     else f"Stress test: {n:,} over {len(defs):,} kinds").replace(",", " ")
        d["sheets"] = []
        d["umpireBoard"] = board
        dirn = os.path.join(packages, "scenarios", sid)
        os.makedirs(dirn, exist_ok=True)
        for junk in ("scenario.start.json",):
            j = os.path.join(dirn, junk)
            if os.path.exists(j): os.remove(j)
        out = os.path.join(dirn, "scenario.json")
        with io.open(out, "w", encoding="utf-8") as f:
            json.dump(d, f)
        mb = os.path.getsize(out) / 1e6
        total += mb
        print(f"{sid}: {n} pieces, {mb:.0f} MB  ({d['name']})")
print(f"total {total:.0f} MB in {os.path.join(packages, 'scenarios')}")
