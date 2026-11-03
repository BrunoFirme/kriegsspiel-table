import hashlib, json, os, shutil, struct, sys, zlib
from pathlib import Path
from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
SEED = HERE / "seed"
FIX = HERE / "fixtures"
PACKAGES = Path(os.environ["LOCALAPPDATA"]) / "KriegsspielTable" / "packages"
TTS = "tts.scripted-war-game-kriegsspiel-pieces"
TTS_DEFS = ["scripted-war.black-infantry-97f7aaf3", "scripted-war.1861-0e136b6a", "scripted-war.1st-division-01985e3b", "scripted-war.blue-model-ee2f255c"]


def save_json(p: Path, v):
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_text(json.dumps(v, indent=2, ensure_ascii=False), encoding="utf-8", newline="\n")


def png(path: Path, w, h, draw):
    im = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    draw(ImageDraw.Draw(im), im)
    path.parent.mkdir(parents=True, exist_ok=True)
    im.save(path, format="PNG", optimize=False)
    return path


def asset(pack_dir: Path, assets: dict, w, h, draw):
    tmp = pack_dir / "assets" / "_tmp.png"
    png(tmp, w, h, draw)
    data = tmp.read_bytes()
    hsh = hashlib.sha256(data).hexdigest()
    final = pack_dir / "assets" / f"{hsh}.png"
    tmp.replace(final)
    assets[hsh] = {"file": f"assets/{hsh}.png", "kind": "image", "w": w, "h": h}
    return hsh


def tts_subset():
    src = PACKAGES / TTS
    p = json.loads((src / "pack.json").read_text(encoding="utf-8"))
    defs = [d for d in p["pieces"] if d["id"] in TTS_DEFS]
    used = set()
    def walk(v):
        if isinstance(v, str) and len(v) == 64 and all(c in "0123456789abcdef" for c in v): used.add(v)
        elif isinstance(v, dict): [walk(x) for k, x in v.items() if k != "script"]
        elif isinstance(v, list): [walk(x) for x in v]
    walk(defs)
    assets = {k: v for k, v in p["assets"].items() if k in used}
    dst = SEED / TTS
    for a in assets.values():
        (dst / a["file"]).parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(src / a["file"], dst / a["file"])
    if (src / "art").exists(): shutil.copytree(src / "art", dst / "art")
    p["pieces"], p["assets"] = defs, assets
    save_json(dst / "pack.json", p)


LUA = r'''
-- The lock's scripted unit: a button, an input, a menu row, a hotkey, a table-menu row and a dialog.
function on_load(saved)
  self.button{ label = "Hit", x = 0, z = 0.36, w = 0.7, h = 0.2, tooltip = "Lose one strength", on_click = "hit" }
  self.button{ label = "Ask", x = 0.55, z = -0.36, w = 0.35, h = 0.2, tooltip = "Name it", on_click = "ask" }
  self.input{ x = -0.2, z = -0.36, w = 0.6, h = 0.2, value = "", placeholder = "orders", on_change = "orders" }
  self.menu_add("Rally", "rally")
  game.hotkey("Lock hotkey 7", hk)
  game.menu_add("Mark here", mark)
end
function on_save() return json.encode({ hits = hits or 0 }) end
function hit(who) hits = (hits or 0) + 1; self.set_counter("Strength", (self.counters["Strength"] or 0) - 1); print("hit " .. hits) end
function orders(who, value) self.set_text("Orders", value); print("orders: " .. value) end
function rally(who) self.set_state(2); game.say("rallied") end
function hk(who, piece, pos, up) game.say("hotkey over " .. (piece and piece.name or "nothing")) end
function mark(who, pos) board.spawn{ def = "lock.pieces.marker", x = pos.x, z = pos.z } end
function ask(who) game.dialog(who, { kind = "input", title = "Name it", text = "A new name" }, function(a) if a then self.set_name(a) end end) end
function on_turn(turn) print("turn " .. turn) end
'''


GLOBAL = r'''
-- The lock table's global script: a screen panel in the TTS XML dialect, a table-menu row, chat and number events.
function on_load(saved)
  ui.set([[<Panel id="lockpanel" width="260" height="150" rectAlignment="UpperRight" offsetXY="-20 -140" color="#202020E0">
    <VerticalLayout padding="6">
      <Text id="caption" fontSize="14" color="white">Lock panel</Text>
      <Button id="signal" onClick="signal">Signal</Button>
      <Toggle id="ready" onValueChanged="ready">Ready</Toggle>
      <InputField id="note" onEndEdit="noted" placeholder="note"></InputField>
    </VerticalLayout>
  </Panel>]])
  game.menu_add("Signal here", signal_here)
end
function signal(who) ui.set_value("caption", "Signalled"); game.say("signal") end
function ready(who, value) game.say("ready " .. tostring(value)) end
function noted(who, value) game.say("note " .. tostring(value)) end
function signal_here(who, pos) game.say("signal at " .. math.floor(pos.x) .. "," .. math.floor(pos.z)) end
function on_chat(text, who) print("chat: " .. text) end
function on_scripting_button_down(index, who) game.say("button " .. index) end
'''


API = r'''
-- The API tester: its "Run" button calls every part of the scripting API once, each in a pcall, and prints what came back.
function on_load(saved)
  self.button{ label = "Run", x = 0, z = 0, w = 0.8, h = 0.4, tooltip = "Run every API call", on_click = "run" }
end
local function t(name, fn) local ok, r = pcall(fn); print(name .. ": " .. (ok and json.encode(r == nil and "nil" or r) or ("error " .. tostring(r)))) end
function run(who)
  local me = self
  t("fields", function() return { me.id, me.def, me.pack, me.name, me.state, me.states, me.pos, me.rot, me.y, me.scale, me.size, me.face_down, me.locked, me.tint, me.counters, me.text, me.tags, me.memo, me.exists } end)
  t("set_name", function() me.set_name("Tester") end)
  t("set_note", function() me.set_note("tests the API") end)
  t("set_pos", function() me.set_pos(1, 9) end)
  t("set_rot", function() me.set_rot(45) end)
  t("move", function() me.move(1.5, 9.5, 30) end)
  t("set_scale", function() me.set_scale(1.2) end)
  t("flip", function() me.flip(false) end)
  t("set_tint", function() me.set_tint("#CCDDEE") end)
  t("set_locked", function() me.set_locked(false) end)
  t("set_tags", function() me.set_tags({ "api", "test" }) end)
  t("add_tag", function() me.add_tag("extra") end)
  t("remove_tag", function() me.remove_tag("test") end)
  t("set_memo", function() me.set_memo("memo") end)
  t("to_world", function() return me.to_world(0.5, 0.5) end)
  t("to_local", function() return me.to_local(1, 9) end)
  t("set_lines", function() me.set_lines({ { points = { { x = -0.5, z = -0.5 }, { x = 0.5, z = 0.5 } }, color = "#FF0000", width = 0.05 } }) end)
  t("widgets", function() return #me.widgets() end)
  t("edit", function() me.edit(1, { label = "Ran" }) end)
  t("menu_add", function() me.menu_add("Again", "run") end)
  t("board.pieces", function() return #board.pieces() end)
  t("board.pieces filter", function() return #board.pieces({ tag = "infantry" }) end)
  t("board.find", function() return board.find("p-000003").name end)
  t("board.distance", function() return board.distance(board.find("p-000001"), board.find("p-000003")) end)
  t("board.zones", function() return #board.zones() end)
  t("board.spawn", function() local p = board.spawn{ def = "lock.pieces.marker", x = 3, z = 9, label = "spawned" }; return p.id end)
  t("game", function() return { game.turn, game.time, game.calibration } end)
  t("game.umpires", function() return #game.umpires() end)
  t("game.say", function() game.say("api says", { color = "#88CCFF" }) end)
  t("game.roll", function() return #game.roll(6, 3, "api") end)
  t("game.selected", function() return #game.selected(who) end)
  t("game.hovered", function() return game.hovered(who) ~= nil end)
  t("game.get_var", function() return game.get_var("nothing") end)
  t("call", function() return board.find("p-000001").get_var("hits") end)
  t("ui.xml", function() return #ui.xml() > 0 end)
  t("ui.tree", function() return #ui.tree() end)
  t("ui.get_attribute", function() return ui.get_attribute("caption", "fontSize") end)
  t("ui.set_attribute", function() return ui.set_attribute("caption", "color", "yellow") end)
  t("ui.hide", function() return ui.hide("ready") end)
  t("ui.show", function() return ui.show("ready") end)
  t("ui.get_value", function() return ui.get_value("caption") end)
  t("json", function() return json.decode(json.encode({ a = 1, b = { 2, 3 } })).b[2] end)
  t("wait.frames", function() return wait.frames(2, function() print("two frames later") end) ~= nil end)
  t("wait.condition", function() return wait.condition(function() return true end, function() print("condition met") end, 5) ~= nil end)
  t("string/math/table", function() return { string.format("%05.1f", math.pi), table.concat({ "a", "b" }, "-"), math.floor(7.9), select("#", 1, 2, 3) } end)
  t("refused io", function() return io == nil and load == nil and require == nil end)
end
function on_pick_up(who) print("picked up") end
function on_drop(who) print("dropped") end
function on_rotate(rot, old, who) print("turned " .. math.floor(rot)) end
function on_flip(down) print("flipped " .. tostring(down)) end
function on_state(o, n) print("state " .. o .. " -> " .. n) end
function on_destroy() print("going") end
'''


def lock_pack():
    dst = SEED / "lock.pieces"
    assets = {}
    def face(color, label, w=128, h=80):
        def d(dr, im):
            dr.rectangle([0, 0, w - 1, h - 1], fill=color, outline=(20, 20, 20, 255), width=4)
            dr.line([8, h // 2, w - 8, h // 2], fill=(255, 255, 255, 255), width=3)
            dr.text((10, 8), label, fill=(0, 0, 0, 255))
        return d
    red = asset(dst, assets, 128, 80, face((200, 50, 40, 255), "RED"))
    red2 = asset(dst, assets, 128, 80, face((120, 30, 25, 255), "RED 2"))
    blue = asset(dst, assets, 128, 80, face((40, 80, 200, 255), "BLUE"))
    back = asset(dst, assets, 128, 80, face((90, 90, 90, 255), "BACK"))
    flag = asset(dst, assets, 64, 64, lambda dr, im: dr.ellipse([4, 4, 59, 59], fill=(240, 200, 30, 255), outline=(0, 0, 0, 255), width=3))
    marker = asset(dst, assets, 48, 48, lambda dr, im: dr.polygon([(24, 2), (46, 46), (2, 46)], fill=(30, 160, 60, 255)))
    pieces = [
        {"id": "lock.pieces.red-battalion", "name": "Red battalion", "kind": "flat", "image": red, "back": back, "size": {"w": 1.6, "h": 1.0},
         "shape": "box", "thickness": 0.2, "tint": "#FFFFFF", "tags": ["infantry", "red"],
         "states": [{"name": "Formed", "image": red}, {"name": "Shaken", "image": red2}],
         "counters": [{"name": "Strength", "min": 0, "max": 9, "default": 6}], "textLines": [{"name": "Orders", "default": ""}],
         "attachments": [], "script": LUA.strip() + "\n"},
        {"id": "lock.pieces.blue-battalion", "name": "Blue battalion", "kind": "flat", "image": blue, "back": back, "size": {"w": 1.6, "h": 1.0},
         "shape": "box", "thickness": 0.2, "tint": "#FFFFFF", "tags": ["infantry", "blue"], "states": [],
         "counters": [{"name": "Strength", "min": 0, "max": 9, "default": 5}, {"name": "Morale", "min": 0, "max": 3, "default": 3}],
         "textLines": [{"name": "Commander", "default": "Boggs"}], "attachments": []},
        {"id": "lock.pieces.flag", "name": "Flag", "kind": "flat", "image": flag, "size": {"w": 0.6, "h": 0.6}, "shape": "alpha", "thickness": 0.05,
         "tint": "#FFFFFF", "tags": ["marker"], "states": [], "counters": [], "textLines": [], "attachments": []},
        {"id": "lock.pieces.colour-party", "name": "Colour party", "kind": "flat", "image": blue, "size": {"w": 1.0, "h": 0.6}, "shape": "box",
         "thickness": 0.15, "tint": "#D0D0FF", "tags": [], "states": [], "counters": [], "textLines": [],
         "attachments": [{"piece": "lock.pieces.flag", "offset": {"x": 0, "z": -0.5}}]},
        {"id": "lock.pieces.api-tester", "name": "API tester", "kind": "flat", "image": flag, "size": {"w": 1.2, "h": 1.2}, "shape": "box",
         "thickness": 0.1, "tint": "#FFFFFF", "tags": ["test"], "states": [], "counters": [{"name": "Count", "min": 0, "max": 5, "default": 2}],
         "textLines": [{"name": "Name", "default": ""}], "attachments": [], "script": API.strip() + "\n"},
        {"id": "lock.pieces.marker", "name": "Marker", "kind": "flat", "image": marker, "size": {"w": 0.5, "h": 0.5}, "shape": "alpha",
         "thickness": 0.05, "tint": "#FFFFFF", "tags": ["marker"], "states": [], "counters": [], "textLines": [], "attachments": []},
    ]
    save_json(dst / "pack.json", {"format": 1, "id": "lock.pieces", "name": "Lock pieces", "source": {"authors": ["Lock"], "url": "", "license": "test pieces"},
                                  "pieces": pieces, "assets": assets})


def piece(n, d, x, z, rot=0, **kw):
    p = {"id": f"p-{n:06d}", "def": d, "pos": {"x": x, "z": z}, "rotDeg": rot, "stack": 0, "state": 0, "counters": {}, "text": {},
         "attached": [], "scale": 1, "y": 0, "faceDown": False}
    p.update(kw)
    return p


def scenarios():
    board = [
        piece(1, "lock.pieces.red-battalion", -4, 0),
        piece(2, "lock.pieces.red-battalion", -4, 3, 30, name="2nd Red"),
        piece(3, "lock.pieces.blue-battalion", 4, 0, 180),
        piece(4, "lock.pieces.blue-battalion", 4, 3, 180, faceDown=True),
        piece(5, "lock.pieces.colour-party", 0, -4),
        piece(6, f"scripted-war.black-infantry-97f7aaf3", 0, 4, 0, scriptState="{\"name\":\"Infantry\",\"ctr1\":9,\"ctr2\":9,\"ctr3\":9,\"currentForm\":1}"),
        piece(7, "scripted-war.1861-0e136b6a", -2, -6),
        piece(8, "std.dice.d6", 2, -6),
        piece(9, "lock.pieces.api-tester", -6, 6),
    ]
    sc = {"format": 1, "id": "local.lock-table", "name": "Lock table",
          "packs": [TTS, "std.dice", "std.effects", "lock.pieces"], "sheets": [],
          "grid": {"kind": "none", "pitch": 10, "subdivisions": 1, "color": "#1A1A2080"},
          "clock": {"turn": 0, "gameTime": "1812-07-22T06:00", "incrementMinutes": 15},
          "wind": {"from": 0, "strength": 0}, "visionRadius": 40,
          "boards": [{"id": "b-000020", "name": "Layer 1"}], "umpireBoard": board, "drawings": {"umpire": []}, "nextId": 30, "script": GLOBAL.strip() + "\n"}
    save_json(SEED / "scenarios" / "lock-table" / "scenario.json", sc)
    small = {"format": 1, "id": "local.lock-small", "name": "Lock small", "packs": ["lock.pieces"], "sheets": [],
             "clock": {"turn": 3, "gameTime": "1813-05-02T14:30", "incrementMinutes": 30},
             "umpireBoard": [piece(1, "lock.pieces.blue-battalion", 0, 0)], "drawings": {"umpire": []}, "nextId": 10}
    save_json(SEED / "scenarios" / "lock-small" / "scenario.json", small)


def fixtures():
    FIX.mkdir(parents=True, exist_ok=True)
    def mapdraw(dr, im):
        dr.rectangle([0, 0, 1023, 767], fill=(214, 204, 170, 255))
        for i in range(0, 1024, 64): dr.line([i, 0, i, 767], fill=(150, 140, 110, 255), width=1)
        for j in range(0, 768, 64): dr.line([0, j, 1023, j], fill=(150, 140, 110, 255), width=1)
        dr.ellipse([300, 200, 600, 420], fill=(120, 160, 90, 255))
        dr.line([0, 600, 1023, 500], fill=(60, 90, 200, 255), width=12)
        dr.rectangle([700, 100, 760, 160], fill=(90, 60, 40, 255))
    png(FIX / "map.png", 1024, 768, mapdraw)
    png(FIX / "picture.png", 200, 120, lambda dr, im: (dr.rectangle([0, 0, 199, 119], fill=(250, 240, 220, 255)), dr.ellipse([50, 20, 150, 100], fill=(180, 30, 30, 255))))
    V = [(-0.5, 0, -0.5), (0.5, 0, -0.5), (0.5, 0, 0.5), (-0.5, 0, 0.5), (-0.5, 1, -0.5), (0.5, 1, -0.5), (0.5, 1, 0.5), (-0.5, 1, 0.5)]
    F = [(0, 2, 1), (0, 3, 2), (4, 5, 6), (4, 6, 7), (0, 1, 5), (0, 5, 4), (1, 2, 6), (1, 6, 5), (2, 3, 7), (2, 7, 6), (3, 0, 4), (3, 4, 7)]
    obj = "# lock cube\n" + "".join(f"v {x} {y} {z}\n" for x, y, z in V) + "".join(f"f {a+1} {b+1} {c+1}\n" for a, b, c in F)
    (FIX / "cube.obj").write_text(obj, encoding="ascii", newline="\n")
    stl = bytearray(b"lock cube".ljust(80, b" ")) + struct.pack("<I", len(F))
    for a, b, c in F:
        stl += struct.pack("<3f", 0, 0, 0) + b"".join(struct.pack("<3f", *V[i]) for i in (a, b, c)) + b"\0\0"
    (FIX / "cube.stl").write_bytes(bytes(stl))
    ply = "ply\nformat ascii 1.0\nelement vertex 8\nproperty float x\nproperty float y\nproperty float z\nelement face 12\nproperty list uchar int vertex_indices\nend_header\n"
    ply += "".join(f"{x} {y} {z}\n" for x, y, z in V) + "".join(f"3 {a} {b} {c}\n" for a, b, c in F)
    (FIX / "cube.ply").write_text(ply, encoding="ascii", newline="\n")
    pos = b"".join(struct.pack("<3f", *v) for v in V)
    idx = b"".join(struct.pack("<3H", *f) for f in F)
    binbuf = pos + idx + b"\0" * ((4 - (len(pos) + len(idx)) % 4) % 4)
    gltf = {"asset": {"version": "2.0"}, "scene": 0, "scenes": [{"nodes": [0]}], "nodes": [{"mesh": 0}],
            "meshes": [{"primitives": [{"attributes": {"POSITION": 0}, "indices": 1}]}],
            "buffers": [{"byteLength": len(binbuf)}],
            "bufferViews": [{"buffer": 0, "byteOffset": 0, "byteLength": len(pos), "target": 34962},
                            {"buffer": 0, "byteOffset": len(pos), "byteLength": len(idx), "target": 34963}],
            "accessors": [{"bufferView": 0, "componentType": 5126, "count": 8, "type": "VEC3", "min": [-0.5, 0, -0.5], "max": [0.5, 1, 0.5]},
                          {"bufferView": 1, "componentType": 5123, "count": 36, "type": "SCALAR"}]}
    js = json.dumps(gltf).encode()
    js += b" " * ((4 - len(js) % 4) % 4)
    glb = struct.pack("<III", 0x46546C67, 2, 12 + 8 + len(js) + 8 + len(binbuf)) + struct.pack("<II", len(js), 0x4E4F534A) + js + struct.pack("<II", len(binbuf), 0x004E4942) + binbuf
    (FIX / "cube.glb").write_bytes(glb)


def join_seed():
    dst = HERE / "seed-join" / "lock.joiner"
    assets = {}
    img = asset(dst, assets, 96, 96, lambda dr, im: dr.rectangle([4, 4, 91, 91], fill=(130, 60, 160, 255), outline=(0, 0, 0, 255), width=4))
    save_json(dst / "pack.json", {"format": 1, "id": "lock.joiner", "name": "Joiner's pieces", "source": {"authors": ["Boggs"], "url": "", "license": "test pieces"},
                                  "pieces": [{"id": "lock.joiner.scout", "name": "Scout", "kind": "flat", "image": img, "size": {"w": 0.8, "h": 0.8},
                                              "shape": "box", "thickness": 0.1, "tint": "#FFFFFF", "tags": [], "states": [], "counters": [],
                                              "textLines": [], "attachments": []}], "assets": assets})


def main():
    for d in (SEED, HERE / "seed-join"):
        if d.exists(): shutil.rmtree(d)
    SEED.mkdir(parents=True)
    join_seed()
    tts_subset()
    lock_pack()
    scenarios()
    fixtures()
    n = sum(1 for _ in SEED.rglob("*") if _.is_file())
    print(f"seed: {n} files in {SEED}; fixtures in {FIX}")


if __name__ == "__main__":
    main()
