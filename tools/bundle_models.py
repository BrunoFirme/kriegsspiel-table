import argparse
import hashlib
import json
import os
import re
import sys

PACK_FORMAT = 1
PACK_API = 1


def main():
    ap = argparse.ArgumentParser(description="Turn the Unity asset bundles of a module into a pack of 3D pieces.")
    local = os.environ.get("LOCALAPPDATA", "")
    ap.add_argument("--packages", default=os.environ.get("KTABLE_PACKAGES") or os.path.join(local, "KriegsspielTable", "packages"))
    ap.add_argument("--cache", default=os.path.join(local, "KriegsspielTable", "cache", "tts"))
    ap.add_argument("--pack", default="user.bundle-models")
    ap.add_argument("--name", default="Models from module asset bundles")
    ap.add_argument("--module", action="append", default=[], help="a module file to read bundle addresses from")
    ap.add_argument("--sha", action="append", default=[], help="a bundle's Steam address, instead of a module")
    ap.add_argument("--bundles", action="append", default=[], help="another folder of .unity3d files")
    ap.add_argument("--atlas-max", type=int, default=2048)
    a = ap.parse_args()

    import site
    user_site = site.getusersitepackages()
    if user_site not in sys.path:
        sys.path.append(user_site)
    try:
        import numpy as np
        import UnityPy
        from PIL import Image
        from UnityPy.helpers.MeshHelper import MeshHandler
    except ImportError as e:
        sys.exit(f"missing package: {e}. pip install --user UnityPy numpy pillow")

    wanted = [s.upper() for s in a.sha]
    for module in a.module:
        wanted += [s for s in bundle_shas(module) if s not in wanted]
    if not wanted:
        sys.exit("name a module with --module, or bundles with --sha")

    bundles = Bundles(a.cache, a.bundles)
    read, skipped = [], []
    for sha in wanted:
        path = bundles.get(sha)
        if not path:
            skipped.append((sha, "the bundle is not on this computer"))
            continue
        try:
            model = read_bundle(np, UnityPy, Image, MeshHandler, path, sha)
        except Exception as e:
            skipped.append((sha, f"{kindly(e)}"))
            continue
        if model is None:
            skipped.append((sha, "it holds an effect, not a model"))
            continue
        read.append(model)

    if not read:
        for sha, why in skipped:
            print(f"  {sha[:12]} -- {why}")
        sys.exit("no bundle on this computer holds a model")

    pack_dir = os.path.join(a.packages, a.pack)
    os.makedirs(os.path.join(pack_dir, "assets"), exist_ok=True)
    atlas, places = pack_atlas(Image, {t["sha"]: t["image"] for m in read for t in m["textures"]}, a.atlas_max)
    texture_sha = write_png(Image, pack_dir, atlas)

    kept_pieces, kept_assets = already_there(pack_dir, {m["sha"] for m in read})
    pieces = list(kept_pieces)
    assets = dict(kept_assets)
    assets[texture_sha] = {"file": f"assets/{texture_sha}.png", "kind": "image", "w": atlas.width, "h": atlas.height}
    for model in read:
        obj = write_obj(np, model, places, atlas.width, atlas.height)
        mesh_sha = hashlib.sha256(obj.encode("utf-8")).hexdigest()
        path = os.path.join(pack_dir, "assets", f"{mesh_sha}.obj")
        if not os.path.exists(path):
            with open(path, "w", encoding="utf-8", newline="\n") as f:
                f.write(obj)
        assets[mesh_sha] = {"file": f"assets/{mesh_sha}.obj", "kind": "mesh", "w": 0, "h": 0}
        low, high = model["low"], model["high"]
        pieces.append({
            "id": f"{a.pack}.{slug(model['name'])}-{model['sha'][:8].lower()}",
            "name": pretty(model["name"]),
            "kind": "model",
            "mesh": mesh_sha,
            "texture": texture_sha,
            "size": {"w": round(high[0] - low[0], 4), "h": round(high[2] - low[2], 4)},
            "shape": "box",
            "thickness": round(high[1] - low[1], 4),
            "tint": "#FFFFFF",
            "tags": ["model", "bundle:" + model["sha"]],
            "states": [], "counters": [], "textLines": [], "attachments": [],
        })

    pieces.sort(key=lambda d: d["id"])
    manifest = {
        "format": PACK_FORMAT, "apiVersion": PACK_API, "id": a.pack, "name": a.name,
        "source": {"authors": [], "url": "", "license": "models read from the asset bundles of imported modules"},
        "pieces": pieces, "assets": assets, "partial": False,
    }
    keep = {d["mesh"] for d in pieces} | {d["texture"] for d in pieces}
    for name in os.listdir(os.path.join(pack_dir, "assets")):
        if os.path.splitext(name)[0] not in keep:
            os.remove(os.path.join(pack_dir, "assets", name))
    with open(os.path.join(pack_dir, "pack.json"), "w", encoding="utf-8") as f:
        json.dump(manifest, f, indent=2, ensure_ascii=False)

    for piece in pieces:
        model = next((m for m in read if "bundle:" + m["sha"] in piece["tags"]), None)
        tris = f"{model['tris']:6d} tris" if model else "   kept    "
        print(f"  {piece['name'][:30]:32s} {tris}  {piece['size']['w']:6.2f} x {piece['thickness']:6.2f} x {piece['size']['h']:6.2f}")
    for sha, why in skipped:
        print(f"  {sha[:12]} -- {why}")
    print(f"{len(pieces)} model(s) in {pack_dir}, one {atlas.width} x {atlas.height} texture for all of them")


def already_there(pack_dir, read_now):
    path = os.path.join(pack_dir, "pack.json")
    if not os.path.exists(path):
        return [], {}
    with open(path, encoding="utf-8") as f:
        pack = json.load(f)
    tags = {"bundle:" + sha for sha in read_now}
    kept = [d for d in pack.get("pieces") or [] if d.get("kind") == "model" and not (set(d.get("tags") or []) & tags)]
    used = {d["mesh"] for d in kept} | {d["texture"] for d in kept}
    return kept, {sha: e for sha, e in (pack.get("assets") or {}).items() if sha in used}


def kindly(e):
    text = str(e)
    if "unity default resources" in text:
        return "it holds an effect, not a model"
    return f"{type(e).__name__}: {text}"


def slug(name):
    s = re.sub(r"[^a-z0-9]+", "-", name.lower()).strip("-")
    return s or "model"


def pretty(name):
    s = re.sub(r"[_]+", " ", name).strip()
    return s[:1].upper() + s[1:] if s else "Model"


def bundle_shas(path):
    with open(path, encoding="utf-8-sig") as f:
        save = json.load(f)
    found = []

    def walk(objects):
        for o in objects:
            c = o.get("CustomAssetbundle")
            if c:
                for key in ("AssetbundleURL", "AssetbundleSecondaryURL"):
                    m = re.search(r"[0-9A-Fa-f]{40}", c.get(key) or "")
                    if m and m.group(0).upper() not in found:
                        found.append(m.group(0).upper())
            walk(o.get("ChildObjects") or [])
            walk(o.get("ContainedObjects") or [])
            for state in (o.get("States") or {}).values():
                walk([state])

    walk(save.get("ObjectStates") or [])
    return found


class Bundles:
    def __init__(self, cache, extra):
        home = os.path.expanduser("~")
        dirs = [cache] + list(extra) + [
            os.path.join(home, p, "My Games", "Tabletop Simulator", "Mods", "Assetbundles")
            for p in (os.path.join("OneDrive", "Documentos"), os.path.join("OneDrive", "Documents"), "Documents")
        ]
        self.files = []
        for d in dirs:
            if os.path.isdir(d):
                self.files += [(name.upper(), os.path.join(d, name)) for name in os.listdir(d)]
        index = os.path.join(cache, "index.json")
        if os.path.exists(index):
            with open(index, encoding="utf-8") as f:
                for e in json.load(f).values():
                    if e.get("ext") != "unity3d" or e.get("status") != "ok" or not e.get("sha"):
                        continue
                    src = e.get("src") if e.get("src") not in (None, "None") else os.path.join(cache, e["sha"] + ".unity3d")
                    self.files.append(((e.get("url") or "").upper(), src))

    def get(self, sha):
        for name, path in self.files:
            if sha in name and os.path.exists(path):
                return path
        return None


def quat_matrix(np, q):
    x, y, z, w = q.x, q.y, q.z, q.w
    return np.array([
        [1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
        [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
        [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)],
    ], dtype=np.float64)


def trs(np, t):
    m = np.eye(4)
    m[:3, :3] = quat_matrix(np, t.m_LocalRotation) @ np.diag([t.m_LocalScale.x, t.m_LocalScale.y, t.m_LocalScale.z])
    m[:3, 3] = [t.m_LocalPosition.x, t.m_LocalPosition.y, t.m_LocalPosition.z]
    return m


def mat4(np, m):
    return np.array([
        [m.e00, m.e01, m.e02, m.e03],
        [m.e10, m.e11, m.e12, m.e13],
        [m.e20, m.e21, m.e22, m.e23],
        [m.e30, m.e31, m.e32, m.e33],
    ], dtype=np.float64)


def read_bundle(np, UnityPy, Image, MeshHandler, path, sha):
    env = UnityPy.load(path)
    transforms, gameobjects = {}, {}
    for o in env.objects:
        if o.type.name == "Transform":
            transforms[o.path_id] = o.read()
        elif o.type.name == "GameObject":
            gameobjects[o.path_id] = o.read()
    if not transforms:
        return None

    world = {}

    def father(pid):
        t = transforms[pid]
        up = t.m_Father.path_id if t.m_Father else None
        return up if up in transforms else None

    def world_of(pid):
        if pid not in world:
            local = trs(np, transforms[pid])
            up = father(pid)
            world[pid] = local if up is None else world_of(up) @ local
        return world[pid]

    def shown(pid):
        t = transforms[pid]
        go = gameobjects.get(t.m_GameObject.path_id) if t.m_GameObject else None
        if go is not None and not go.m_IsActive:
            return False
        up = father(pid)
        return True if up is None else shown(up)

    roots = [pid for pid in transforms if father(pid) is None]
    origin = world_of(roots[0])[:3, 3] if roots else np.zeros(3)
    name = sha
    for pid in roots:
        go = gameobjects.get(transforms[pid].m_GameObject.path_id) if transforms[pid].m_GameObject else None
        if go is not None and go.m_Name:
            name = go.m_Name
            break

    parts, textures, lost = [], {}, 0
    for o in env.objects:
        kind = o.type.name
        if kind not in ("MeshRenderer", "SkinnedMeshRenderer"):
            continue
        try:
            r = o.read()
            if not getattr(r, "m_Enabled", True):
                continue
            go = r.m_GameObject.read()
            here = go.m_Transform.path_id if go.m_Transform else None
            if here not in transforms or not shown(here):
                continue
            mesh_ptr = r.m_Mesh if kind == "SkinnedMeshRenderer" else mesh_filter(go)
            if mesh_ptr is None or not mesh_ptr.path_id:
                continue
            mesh = mesh_ptr.read()
            handler = MeshHandler(mesh)
            handler.process()
            if not handler.m_Vertices:
                continue
            verts = np.array(handler.m_Vertices, dtype=np.float64)[:, :3]
            uvs = np.array(handler.m_UV0, dtype=np.float64)[:, :2] if handler.m_UV0 else np.zeros((len(verts), 2))
            verts = posed(np, r, kind, mesh, verts, world_of, here, transforms) - origin
            mats = list(getattr(r, "m_Materials", None) or [])
            for index, triangles in enumerate(handler.get_triangles()):
                if not len(triangles):
                    continue
                ptr = mats[index] if index < len(mats) else (mats[0] if mats else None)
                parts.append({"verts": verts, "uvs": uvs, "tris": np.array(triangles, dtype=np.int64),
                              "tex": main_texture(ptr, textures)})
        except Exception:
            lost += 1

    if not parts:
        return None
    if lost:
        print(f"  {sha[:12]} -- {lost} part(s) of it lean on Unity's own resources and are not in the bundle; the rest is read")
    low = [min(float(p["verts"][np.unique(p["tris"])][:, i].min()) for p in parts) for i in range(3)]
    high = [max(float(p["verts"][np.unique(p["tris"])][:, i].max()) for p in parts) for i in range(3)]
    low[0], high[0] = -high[0], -low[0]
    return {"sha": sha, "name": name, "parts": parts, "low": low, "high": high,
            "tris": sum(len(p["tris"]) for p in parts),
            "textures": [{"sha": s, "image": i} for s, i in textures.items()]}


def mesh_filter(go):
    for c in go.m_Components or []:
        comp = c.read() if hasattr(c, "read") else None
        if comp is not None and type(comp).__name__ == "MeshFilter":
            return comp.m_Mesh
    return None


def posed(np, r, kind, mesh, verts, world_of, here, transforms):
    bones = [b.path_id for b in (getattr(r, "m_Bones", None) or [])]
    homo = np.hstack([verts, np.ones((len(verts), 1))])
    if kind != "SkinnedMeshRenderer" or not bones or not mesh.m_Skin or not mesh.m_BindPose:
        return (world_of(here) @ homo.T).T[:, :3]
    binds = [mat4(np, m) for m in mesh.m_BindPose]
    skin = [(world_of(bones[i]) if bones[i] in transforms else np.eye(4)) @ binds[i]
            for i in range(min(len(bones), len(binds)))]
    out = np.zeros_like(verts)
    for i, influence in enumerate(mesh.m_Skin[:len(verts)]):
        acc = np.zeros(3)
        total = 0.0
        for k in range(4):
            w = getattr(influence, f"weight_{k}_")
            b = getattr(influence, f"boneIndex_{k}_")
            if w <= 0 or b >= len(skin):
                continue
            acc += w * (skin[b] @ homo[i])[:3]
            total += w
        out[i] = acc / total if total > 0 else (world_of(here) @ homo[i])[:3]
    return out


def main_texture(material_ptr, textures):
    if material_ptr is None:
        return None
    try:
        material = material_ptr.read()
        for name, env in material.m_SavedProperties.m_TexEnvs:
            if name != "_MainTex" or not env.m_Texture or not env.m_Texture.path_id:
                continue
            image = env.m_Texture.read().image
            if image is None:
                return None
            image = image.convert("RGBA")
            sha = hashlib.sha256(image.tobytes() + f"{image.width}x{image.height}".encode()).hexdigest()
            textures.setdefault(sha, image)
            return sha
    except Exception:
        return None
    return None


def pack_atlas(Image, textures, atlas_max):
    if not textures:
        return Image.new("RGBA", (4, 4), (255, 255, 255, 255)), {}
    items = list(textures.items())
    while sum(i.width * i.height for _, i in items) > atlas_max * atlas_max or max(i.width for _, i in items) > atlas_max:
        items = [(s, i.resize((max(1, i.width // 2), max(1, i.height // 2)), Image.LANCZOS)) for s, i in items]
    items.sort(key=lambda kv: (-kv[1].height, -kv[1].width))
    area = sum(i.width * i.height for _, i in items)
    width = 1
    while width * width < area:
        width *= 2
    width = max(width, max(i.width for _, i in items))
    x = y = shelf = 0
    places = {}
    for sha, image in items:
        if x + image.width > width:
            x, y, shelf = 0, y + shelf, 0
        places[sha] = (x, y, image.width, image.height)
        x += image.width
        shelf = max(shelf, image.height)
    height = y + shelf
    atlas = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    for sha, image in items:
        px, py, w, h = places[sha]
        atlas.paste(image, (px, height - py - h))
    return atlas, places


def write_png(Image, pack_dir, atlas):
    import io
    buffer = io.BytesIO()
    atlas.save(buffer, format="PNG", optimize=True)
    raw = buffer.getvalue()
    sha = hashlib.sha256(raw).hexdigest()
    path = os.path.join(pack_dir, "assets", f"{sha}.png")
    if not os.path.exists(path):
        with open(path, "wb") as f:
            f.write(raw)
    return sha


def write_obj(np, model, places, atlas_w, atlas_h):
    lines = [f"o {model['name']}\n"]
    base = 1
    for part in model["parts"]:
        used = np.unique(part["tris"])
        remap = {int(v): i for i, v in enumerate(used)}
        vs = part["verts"][used]
        us = part["uvs"][used]
        place = places.get(part["tex"])
        if place:
            px, py, pw, ph = place
            us = np.column_stack([(np.mod(us[:, 0], 1.0) * pw + px) / atlas_w,
                                  (np.mod(us[:, 1], 1.0) * ph + py) / atlas_h])
        for v in vs:
            lines.append(f"v {-v[0]:.6g} {v[1]:.6g} {v[2]:.6g}\n")
        for u in us:
            lines.append(f"vt {u[0]:.6g} {u[1]:.6g}\n")
        for a, b, c in part["tris"]:
            x, y, z = base + remap[int(a)], base + remap[int(b)], base + remap[int(c)]
            lines.append(f"f {z}/{z} {y}/{y} {x}/{x}\n")
        base += len(vs)
    return "".join(lines)


if __name__ == "__main__":
    main()
