import argparse
import hashlib
import io
import json
import os
import sys


def main():
    ap = argparse.ArgumentParser()
    local = os.environ.get("LOCALAPPDATA", "")
    ap.add_argument("--packages", default=os.environ.get("KTABLE_PACKAGES") or os.path.join(local, "KriegsspielTable", "packages"))
    ap.add_argument("--cache", default=os.path.join(local, "KriegsspielTable", "cache", "tts"))
    ap.add_argument("--pack", default="iks.james-effects")
    a = ap.parse_args()

    import site
    user_site = site.getusersitepackages()
    if user_site not in sys.path:
        sys.path.append(user_site)
    try:
        import UnityPy
    except ImportError:
        sys.exit("UnityPy missing: pip install --user UnityPy")

    pack_dir = os.path.join(a.packages, a.pack)
    manifest_path = os.path.join(pack_dir, "pack.json")
    with open(manifest_path, encoding="utf-8") as f:
        pack = json.load(f)

    files = {}
    idx_path = os.path.join(a.cache, "index.json")
    if os.path.exists(idx_path):
        with open(idx_path, encoding="utf-8") as f:
            for e in json.load(f).values():
                if e.get("sha") and e.get("ext") == "unity3d":
                    files[e["sha"]] = e.get("src") or os.path.join(a.cache, f"{e['sha']}.unity3d")

    os.makedirs(os.path.join(pack_dir, "assets"), exist_ok=True)
    effects = []
    for d in pack["pieces"]:
        tags = d.get("tags") or []
        if "effect" not in tags:
            continue
        bundle = next((t[7:] for t in tags if t.startswith("bundle:")), None)
        path = files.get(bundle)
        entry = {"def": d["id"], "name": d["name"], "bundle": bundle, "sprites": [], "particleSystems": 0, "error": None}
        effects.append(entry)
        if not path or not os.path.exists(path):
            entry["error"] = "bundle not available"
            continue
        try:
            env = UnityPy.load(path)
        except Exception as ex:
            entry["error"] = f"load failed: {ex}"
            continue
        sprites = []
        seen = set()
        for obj in env.objects:
            tname = obj.type.name
            if tname == "ParticleSystem":
                entry["particleSystems"] += 1
            if tname not in ("Texture2D", "Sprite"):
                continue
            try:
                data = obj.read()
                img = data.image
                name = getattr(data, "m_Name", None) or getattr(data, "name", "") or tname
            except Exception:
                continue
            if img is None or img.width < 2 or img.height < 2:
                continue
            buf = io.BytesIO()
            img.save(buf, format="PNG")
            raw = buf.getvalue()
            sha = hashlib.sha256(raw).hexdigest()
            if sha in seen:
                continue
            seen.add(sha)
            out = os.path.join(pack_dir, "assets", f"{sha}.png")
            if not os.path.exists(out):
                with open(out, "wb") as f:
                    f.write(raw)
            pack["assets"][sha] = {"file": f"assets/{sha}.png", "kind": "sprite", "w": img.width, "h": img.height}
            sprites.append((img.width * img.height, sha, name))
        sprites.sort(reverse=True)
        entry["sprites"] = [{"sha": s, "name": n} for _, s, n in sprites]
        if sprites:
            d["image"] = sprites[0][1]
            d["states"] = [{"name": n or f"Sprite {i + 1}", "image": s} for i, (_, s, n) in enumerate(sprites)]

    used = set()
    for d in pack["pieces"]:
        for k in ("image", "back", "mesh", "texture"):
            if d.get(k):
                used.add(d[k])
        for s in d.get("states") or []:
            for k in ("image", "mesh", "texture"):
                if s.get(k):
                    used.add(s[k])
    for sha in [k for k in pack["assets"] if k not in used]:
        f = os.path.join(pack_dir, pack["assets"][sha]["file"])
        del pack["assets"][sha]
        if os.path.exists(f):
            os.remove(f)

    with open(manifest_path, "w", encoding="utf-8") as f:
        json.dump(pack, f, indent=2, ensure_ascii=False)
    with open(os.path.join(pack_dir, "effects.json"), "w", encoding="utf-8") as f:
        json.dump(effects, f, indent=2, ensure_ascii=False)
    ok = sum(1 for e in effects if e["sprites"])
    print(f"{len(effects)} effects, {ok} with sprites, {sum(len(e['sprites']) for e in effects)} sprites")
    for e in effects:
        if not e["sprites"]:
            print(f"  no sprites: {e['name']} ({e['error'] or 'bundle has no textures'})")


if __name__ == "__main__":
    main()
