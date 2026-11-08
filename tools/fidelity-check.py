import argparse
import json
import math
import os
import sys
from collections import defaultdict

USAGE = "python -I tools/fidelity-check.py <module .json> <packages dir> <scenario id> [--examples <n>]"
HALF_TURN, TURN, FLIP_WINDOW, NOISE, TEXT_LIES_AT = 180.0, 360.0, 30.0, 0.05, 90.0
NEAR_POS, NEAR_TURN = 0.002, 0.15
EXAMPLES = 5
WHITE = "#FFFFFF"
NOTE_TAG, NOTE_TURN = "note", 180.0
NOT_PIECES = {"HandTrigger", "ScriptingTrigger", "FogOfWarTrigger", "FogOfWar", "RandomizeTrigger", "LayoutZone"}


def load(path):
    with open(path, encoding="utf-8-sig") as f:
        return json.load(f)


def signed(a):
    a %= TURN
    if a > HALF_TURN:
        a -= TURN
    return a


def clean(a):
    a = signed(a)
    return 0.0 if abs(a) < NOISE else round(a, 3)


def near_half(a):
    return abs(signed(a - HALF_TURN)) < FLIP_WINDOW


def expected_pose(o, model):
    t = o.get("Transform") or {}
    rx, ry, rz = t.get("rotX", 0.0), t.get("rotY", 0.0), t.get("rotZ", 0.0)
    if o.get("Name") == "3DText":
        return ry - rz, False, clean(rx - TEXT_LIES_AT), 0.0
    if model:
        return ry, False, clean(rx), clean(rz)
    fx, fz = near_half(rx), near_half(rz)
    return (ry + HALF_TURN if fx else ry), fx != fz, clean(HALF_TURN - rx if fx else rx), clean(rz - HALF_TURN if fz else rz)


def is_die(o):
    name = o.get("Name") or ""
    return name.startswith("Die_") or name.startswith("Custom_Dice")


def apart(a, b):
    d = abs(signed(a - b))
    return min(d, TURN - d)


def hex_of(c):
    if not isinstance(c, dict):
        return None
    parts = [max(0, min(255, round(float(c.get(k, 1)) * 255))) for k in ("r", "g", "b")]
    return "#" + "".join(f"{v:02X}" for v in parts)


def shown_tint(piece, d):
    if piece.get("tintOverride"):
        return piece["tintOverride"].upper()[:7]
    states = d.get("states") or []
    state = piece.get("state", 0)
    if 0 <= state < len(states) and states[state].get("tint"):
        return states[state]["tint"].upper()[:7]
    return (d.get("tint") or WHITE).upper()[:7]


def kinds(packages, scenario):
    out = {}
    for pack in scenario.get("packs") or []:
        path = os.path.join(packages, pack, "pack.json")
        if os.path.isfile(path):
            for d in load(path).get("pieces") or []:
                out[d["id"]] = d
    return out


def pieces_by_guid(scenario):
    out = {}
    for layer in ["umpireBoard"] + [f"boards.{k}" for k in (scenario.get("boards") or {})]:
        board = scenario["umpireBoard"] if layer == "umpireBoard" else scenario["boards"][layer.split(".", 1)[1]]
        for p in board:
            if p.get("guid"):
                out.setdefault(p["guid"], p)
    return out


def check(module, packages, scenario_id):
    scenario = load(os.path.join(packages, "scenarios", scenario_id, "scenario.json"))
    defs = kinds(packages, scenario)
    ours = pieces_by_guid(scenario)
    found = defaultdict(list)
    counted = defaultdict(int)
    for o in module.get("ObjectStates") or []:
        guid = o.get("GUID")
        name = (o.get("Nickname") or o.get("Name") or "").replace("\n", " ")[:40]
        p = ours.get(guid)
        if p is None:
            if o.get("Name") in NOT_PIECES:
                continue
            found["not on the table"].append(f"{guid} {o.get('Name')} '{name}'")
            continue
        d = defs.get(p["def"], {})
        model = d.get("kind") == "model"
        t = o.get("Transform") or {}
        counted["objects"] += 1
        dx, dz = p["pos"]["x"] - t.get("posX", 0.0), p["pos"]["z"] - t.get("posZ", 0.0)
        if math.hypot(dx, dz) > NEAR_POS:
            found["position"].append(f"{guid} '{name}' off by ({dx:.3f}, {dz:.3f})")
        if len(d.get("faces") or []) <= 1 and not is_die(o):
            heading, down, tx, tz = expected_pose(o, model)
            if NOTE_TAG in (d.get("tags") or []):
                heading += NOTE_TURN
            got = (p.get("rotDeg", 0.0), p.get("faceDown", False), p.get("tiltX", 0.0), p.get("tiltZ", 0.0))
            if apart(got[0], heading) > NEAR_TURN or got[1] != down or apart(got[2], tx) > NEAR_TURN or apart(got[3], tz) > NEAR_TURN:
                found["lie"].append(f"{guid} '{name}' game heading {heading:.1f} down {down} tilt ({tx}, {tz}); ours {got[0]:.1f} {got[1]} ({got[2]}, {got[3]})")
        if o.get("Name") == "Custom_Token" and hex_of(o.get("ColorDiffuse")) not in (None, WHITE):
            want, got = hex_of(o.get("ColorDiffuse")), shown_tint(p, d)
            if got != want:
                found["colour"].append(f"{guid} '{name}' game {want}, ours {got}")
        label = o.get("Nickname") or ""
        if label and (p.get("label") or "") != label:
            found["name"].append(f"{guid} game '{name}', ours '{(p.get('label') or '')[:40]}'")
        if bool(o.get("Locked")) != bool(p.get("locked")):
            found["locked"].append(f"{guid} '{name}' game {bool(o.get('Locked'))}, ours {bool(p.get('locked'))}")
        states = d.get("states") or []
        state = p.get("state", 0)
        if o.get("States") and 0 <= state < len(states):
            shown = (states[state].get("module") or {}).get("guid")
            if shown and shown != guid:
                found["state shown"].append(f"{guid} '{name}' ours shows the state saved as {shown}")
        drawn = [c["name"] for c in d.get("counters") or []] + [x["name"] for x in d.get("textLines") or []]
        drawn += [k for k, v in (p.get("text") or {}).items() if (v or "").strip()] + list((p.get("counters") or {}).keys())
        if drawn and NOTE_TAG not in (d.get("tags") or []):
            found["drawn by the app"].append(f"{guid} '{name}' carries the app's own {', '.join(drawn)}")
    return counted, found


def main():
    ap = argparse.ArgumentParser(usage=USAGE, description="Compare what the game shows from a module's save with what the import laid out.")
    ap.add_argument("module")
    ap.add_argument("packages")
    ap.add_argument("scenario")
    ap.add_argument("--examples", type=int, default=EXAMPLES)
    a = ap.parse_args()
    sys.stdout.reconfigure(encoding="utf-8")
    counted, found = check(load(a.module), a.packages, a.scenario)
    print(f"{counted['objects']} objects compared")
    for what, rows in sorted(found.items()):
        print(f"{what}: {len(rows)}")
        for r in rows[:a.examples]:
            print("  " + r)
    sys.exit(1 if any(k != "not on the table" for k in found) else 0)


if __name__ == "__main__":
    main()
