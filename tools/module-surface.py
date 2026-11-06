import argparse
import json
import os
import re
import sys
from collections import defaultdict

USAGE = "python -I tools/module-surface.py [--workshop <dir>] [--saves <file>,...] [--gate] [--quiet]"
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
SURFACE = os.path.join(HERE, "surface")
API = os.path.join(SURFACE, "module-api.json")
DECISIONS = os.path.join(SURFACE, "decisions.json")
LEDGER_MD = os.path.join(SURFACE, "ledger.md")
LEDGER_JSON = os.path.join(SURFACE, "ledger.json")
TRANSLATOR = os.path.join(ROOT, "src", "Server", "Scripting", "Translate", "ModuleTranslator.cs")
COMPAT = os.path.join(ROOT, "src", "Server", "Scripting", "compat.lua")
CONVERTER_DIR = os.path.join(ROOT, "tools", "Converter")
CONVERTER_JSON = os.path.join(CONVERTER_DIR, "converter.json")
AREAS = ("types", "save", "object", "global", "namespaces", "events", "engine")
STATUSES = ("native", "compat", "partial", "stub", "dropped", "planned", "unknown")
UNDECIDED = ("planned", "unknown")
STDLIB = {"string", "math", "table", "os", "coroutine", "bit32", "io", "debug", "utf8", "json", "compat", "board", "game", "wait", "self"}
SUB_STATES = {
    "Transform": "TransformState", "ColorDiffuse": "ColourState", "CustomImage": "CustomImageState", "CustomMesh": "CustomMeshState",
    "CustomAssetbundle": "CustomAssetbundleState", "CustomPDF": "CustomPDFState", "CustomDeck": "CustomDeckState", "Bag": "BagState",
    "Text": "TextState", "FogOfWar": "FogOfWarState", "FogOfWarRevealer": "FogOfWarRevealerState", "LayoutZone": "LayoutZoneState",
    "Clock": "ClockState", "Counter": "CounterState", "Tablet": "TabletState", "Mp3Player": "Mp3PlayerState", "Calculator": "CalculatorState",
    "PhysicsMaterial": "PhysicsMaterialState", "Rigidbody": "RigidbodyState", "JointFixed": "JointFixedState", "JointHinge": "JointHingeState",
    "JointSpring": "JointSpringState", "Grid": "GridState", "Lighting": "LightingState", "Hands": "HandsState",
    "Turns": "TurnsState", "ComponentTags": "TagsState", "MusicPlayer": "MusicPlayerState",
}
CALL_RX = re.compile(r"\b([A-Za-z_][A-Za-z0-9_]*)\s*[.:]\s*([A-Za-z_][A-Za-z0-9_]*)\s*\(")
BARE_RX = re.compile(r"(?<![.:\w])([A-Za-z_][A-Za-z0-9_]*)\s*\(")
DEF_RX = re.compile(r"\bfunction\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(")
PROP_RX = re.compile(r"\b([A-Za-z_][A-Za-z0-9_]*)\s*\.\s*([a-z_][A-Za-z0-9_]*)\b(?!\s*[(.:])")
COMMENT_RX = re.compile(r"--\[(=*)\[.*?\]\1\]|--[^\n]*", re.S)
CASE_RX = re.compile(r'case\s+((?:"[^"]+"\s*(?:or\s*)?)+):\s*return\s+(Direct|Compat|Todo)\b', re.S)
ARROW_RX = re.compile(r'((?:"[^"]+"\s*(?:or\s*)?)+)=>\s*(Direct|Compat|Todo)\b')
QUOTED_RX = re.compile(r'"([A-Za-z0-9_]+)"')
KEY_RX = re.compile(r'\["([A-Za-z0-9_]+)"\]')
SET_RX = re.compile(r"static readonly HashSet<string> (\w+) = new\(StringComparer\.Ordinal\)\s*\{(.*?)\};", re.S)
MAP_RX = re.compile(r"static readonly Dictionary<string, string> EventRenames = .*?\{(.*?)\};", re.S)
FUNC_RX = re.compile(r"^\s*(?:static\s+)?[\w?<>]+\s+(ObjectCall|GlobalFunctionCall|GlobalObjectCall|MemberCall)\(", re.M)


def shell_quiet(message):
    sys.stderr.write(message + "\n")


def load_json(path, default):
    if not os.path.exists(path):
        return default
    return json.load(open(path, encoding="utf-8-sig"))


def upper_first(s):
    return s[:1].upper() + s[1:]


def camelify(s):
    parts = s.split("_")
    return parts[0] + "".join(upper_first(p) for p in parts[1:]) if len(parts) > 1 else s


def folds(name):
    return {name, upper_first(name), camelify(name), upper_first(camelify(name))}


class Api:
    def __init__(self, api):
        self.api = api
        self.object_by_cs = {}
        for lua, m in api["object"].items():
            self.object_by_cs.setdefault(m["cs"] if "." not in lua else lua.split(".")[0] + "." + m["cs"], lua)
        self.ns_by_cs = {ns: {m["cs"]: lua for lua, m in members.items()} for ns, members in api["namespaces"].items()}
        self.global_by_cs = {m["cs"]: lua for lua, m in api["global"].items()}
        self.events = set(api["events"])
        self.props = {lua for lua, m in api["object"].items() if m["kind"] == "property" and "." not in lua}

    def object_member(self, name, part=""):
        key = (part + "." if part else "")
        for f in folds(name):
            hit = self.object_by_cs.get(key + f)
            if hit:
                return hit
        return None

    def ns_member(self, ns, name):
        table = self.ns_by_cs.get(ns)
        if not table:
            return None
        for f in folds(name):
            if f in table:
                return table[f]
        return None

    def global_member(self, name):
        for f in folds(name):
            if f in self.global_by_cs:
                return self.global_by_cs[f]
        return None


class Usage:
    def __init__(self):
        self.count = defaultdict(int)
        self.modules = defaultdict(set)
        self.example = {}
        self.values = defaultdict(lambda: defaultdict(lambda: defaultdict(int)))
        self.defaults = {}

    def add(self, row, module, example=None):
        self.count[row] += 1
        self.modules[row].add(module)
        if example and row not in self.example:
            self.example[row] = example

    def value(self, row, module, v):
        self.values[row][canonical(v)][module] += 1

    def settle(self):
        for row, seen in self.values.items():
            mode = max(seen.items(), key=lambda kv: sum(kv[1].values()))[0]
            self.defaults[row] = mode
            for text, by_module in seen.items():
                if text == mode:
                    continue
                for module, n in by_module.items():
                    self.count[row] += n
                    self.modules[row].add(module)


def canonical(v):
    if isinstance(v, bool) or v is None or isinstance(v, str):
        return json.dumps(v, separators=(",", ":"))
    if isinstance(v, float):
        return json.dumps(int(v) if v.is_integer() else v, separators=(",", ":"))
    if isinstance(v, int):
        return json.dumps(v, separators=(",", ":"))
    if isinstance(v, dict):
        return "{" + ",".join(json.dumps(k) + ":" + canonical(v[k]) for k in sorted(v)) + "}"
    if isinstance(v, list):
        return "[" + ",".join(canonical(x) for x in v) + "]"
    return json.dumps(str(v))


def field(use, row, module, v):
    if isinstance(v, (list, dict)) and len(v) == 0:
        return
    if isinstance(v, list) or (isinstance(v, dict) and any(isinstance(x, (dict, list)) for x in v.values())):
        use.add(row, module, module)
        return
    use.value(row, module, v)


def scan_lua(lua, module, api, use):
    text = COMMENT_RX.sub(" ", lua)
    for m in CALL_RX.finditer(text):
        recv, name = m.group(1), m.group(2)
        if recv in api.api["namespaces"]:
            hit = api.ns_member(recv, name)
            use.add(("namespaces", recv + "." + hit if hit else recv + "." + name), module, module)
        elif recv == "Global":
            use.add(("namespaces", "Global." + name), module, module)
        elif recv not in STDLIB or recv == "self":
            hit = api.object_member(name)
            if hit:
                use.add(("object", hit), module, module)
    for m in BARE_RX.finditer(text):
        name = m.group(1)
        hit = api.global_member(name)
        if hit:
            use.add(("global", hit), module, module)
    for m in DEF_RX.finditer(text):
        name = m.group(1)
        if name in api.events:
            use.add(("events", name), module, module)
    for m in PROP_RX.finditer(text):
        recv, name = m.group(1), m.group(2)
        if recv == "self" and name in api.props:
            use.add(("object", name), module, module)


def scan_object(o, module, use, api):
    name = o.get("Name")
    if isinstance(name, str):
        use.add(("types", name), module, module)
    for k, v in o.items():
        if k in ("ContainedObjects", "ChildObjects", "States", "CustomDeck"):
            field(use, ("save", "ObjectState." + k), module, v)
            continue
        sub = SUB_STATES.get(k)
        if not (sub and isinstance(v, dict)):
            field(use, ("save", "ObjectState." + k), module, v)
            continue
        use.add(("save", "ObjectState." + k), module, module)
        for k2, v2 in v.items():
            if k == "CustomImage" and isinstance(v2, dict):
                use.add(("save", sub + "." + k2), module, module)
                for k3, v3 in v2.items():
                    field(use, ("save", k2 + "State." + k3), module, v3)
            elif k == "CustomMesh" and isinstance(v2, dict):
                use.add(("save", sub + "." + k2), module, module)
                for k3, v3 in v2.items():
                    field(use, ("save", "CustomShaderState." + k3), module, v3)
            else:
                field(use, ("save", sub + "." + k2), module, v2)
    for k in ("ContainedObjects", "ChildObjects"):
        for c in o.get(k) or []:
            scan_object(c, module, use, api)
    states = o.get("States")
    if isinstance(states, dict):
        for c in states.values():
            scan_object(c, module, use, api)
    scan_lua(o.get("LuaScript") or "", module, api, use)


def scan_save(path, use, api):
    try:
        save = json.load(open(path, encoding="utf-8-sig"))
    except (OSError, ValueError):
        return False
    if not isinstance(save, dict) or "ObjectStates" not in save:
        return False
    module = os.path.splitext(os.path.basename(path))[0]
    for k, v in save.items():
        sub = SUB_STATES.get(k)
        if sub and isinstance(v, dict):
            use.add(("save", "SaveState." + k), module, module)
            for k2, v2 in v.items():
                field(use, ("save", sub + "." + k2), module, v2)
        elif k != "ObjectStates":
            field(use, ("save", "SaveState." + k), module, v)
    for o in save.get("ObjectStates") or []:
        if isinstance(o, dict):
            scan_object(o, module, use, api)
    scan_lua(save.get("LuaScript") or "", module, api, use)
    return True


def translator_rules():
    text = open(TRANSLATOR, encoding="utf-8-sig").read()
    rules = {"object": {}, "global": {}, "namespaces": {}, "events": {}, "props": set()}
    sets = {m.group(1): set(QUOTED_RX.findall(m.group(2))) for m in SET_RX.finditer(text)}
    rules["props"] = sets.get("ObjectProps", set())
    renames = MAP_RX.search(text)
    renamed = set(re.findall(r'\["(\w+)"\]\s*=', renames.group(1))) if renames else set()
    for ev in sets.get("EventNames", set()):
        rules["events"][ev] = ("native", "EventRenames") if ev in renamed else ("stub", "EventNames (kept as a function nothing raises)")
    kinds = {"Direct": "native", "Compat": "compat", "Todo": "stub"}
    sections = split_sections(text)
    for section, body in sections.items():
        target = rules["object"] if section == "ObjectCall" else rules["global"] if section == "GlobalFunctionCall" else None
        if target is None:
            continue
        for m in CASE_RX.finditer(body):
            for name in QUOTED_RX.findall(m.group(1)):
                target[name] = (kinds[m.group(2)], section)
    for name in rules["props"]:
        rules["object"].setdefault(name, ("compat", "ObjectProps (get_prop/set_prop)"))
    ns_body = sections.get("GlobalObjectCall", "")
    for block in re.split(r'\n\s*case\s+"', ns_body)[1:]:
        names = QUOTED_RX.findall('"' + block.split(":", 1)[0] + '"')
        rest = block.split(":", 1)[1] if ":" in block else ""
        for m in ARROW_RX.finditer(rest):
            for ns in names:
                for name in QUOTED_RX.findall(m.group(1)):
                    rules["namespaces"][ns + "." + name] = (kinds[m.group(2)], "namespace " + ns)
        for m in re.finditer(r'name == "(\w+)" \? (Direct|Compat|Todo)\b', rest):
            for ns in names:
                rules["namespaces"][ns + "." + m.group(1)] = (kinds[m.group(2)], "namespace " + ns)
        default = re.search(r"_\s*=>\s*(Direct|Compat|Todo)\b", rest) or re.search(r"\? \w+\([^)]*\) : (Direct|Compat|Todo)\b", rest)
        whole = re.search(r"return\s+(Direct|Compat|Todo)\b", rest)
        verdict = default or whole
        if verdict:
            for ns in names:
                rules["namespaces"][ns + ".*"] = (kinds[verdict.group(1)], "namespace " + ns + " default")
    return rules


def split_sections(text):
    starts = [(m.start(), m.group(1)) for m in FUNC_RX.finditer(text)]
    sections = {}
    for i, (at, name) in enumerate(starts):
        end = starts[i + 1][0] if i + 1 < len(starts) else len(text)
        sections[name] = text[at:end]
    return sections


def converter_reads():
    keys, types = set(), set()
    for f in os.listdir(CONVERTER_DIR):
        if not f.endswith(".cs"):
            continue
        text = open(os.path.join(CONVERTER_DIR, f), encoding="utf-8-sig").read()
        keys.update(KEY_RX.findall(text))
        types.update(QUOTED_RX.findall(text))
    cfg = load_json(CONVERTER_JSON, {})
    return keys, types, set(cfg.get("ignoreTypes", [])), set(cfg.get("containerTypes", []))


def compat_functions():
    if not os.path.exists(COMPAT):
        return set()
    return set(re.findall(r"^function compat\.([A-Za-z_][A-Za-z0-9_.]*)", open(COMPAT, encoding="utf-8-sig").read(), re.M))


def decision(row, decided):
    d = decided.get(row)
    if d is None:
        head = row.split(".")[0] + ".*" if "." in row else None
        d = decided.get(head) if head else None
    if d is None:
        return None
    if isinstance(d, list):
        d = dict(zip(("status", "where", "instead", "held_by"), d))
    return d


def classify(area, row, rules, reads, decided):
    d = decision(row, decided)
    if d is not None:
        return d.get("status", "unknown"), d.get("where", ""), d.get("held_by", "")
    keys, types, ignored, containers = reads
    if area == "types":
        if row in ignored:
            return "dropped", "converter.json ignoreTypes (no reason recorded)", ""
        if row.startswith("Die_"):
            return "dropped", "ScenarioBuilder (Die_* skipped, no reason recorded)", ""
        if row in types:
            return "native", "converter", ""
        return "unknown", "", ""
    if area == "save":
        field = row.split(".", 1)[1]
        return ("native", "converter reads it", "") if field in keys else ("unknown", "", "")
    if area == "object":
        name = row.split(".")[-1]
        if row in rules["object"]:
            return rules["object"][row][0], rules["object"][row][1], ""
        if name in rules["object"]:
            return rules["object"][name][0], rules["object"][name][1], ""
        return "unknown", "", ""
    if area == "global":
        if row in rules["global"]:
            return rules["global"][row][0], rules["global"][row][1], ""
        return "unknown", "", ""
    if area == "namespaces":
        if row in rules["namespaces"]:
            return rules["namespaces"][row][0], rules["namespaces"][row][1], ""
        star = row.split(".")[0] + ".*"
        if star in rules["namespaces"]:
            return rules["namespaces"][star][0], rules["namespaces"][star][1], ""
        return "unknown", "", ""
    if area == "events":
        if row in rules["events"]:
            return rules["events"][row][0], rules["events"][row][1], ""
        return "unknown", "", ""
    return "unknown", "", ""


def all_rows(api, use, decided):
    rows = defaultdict(set)
    for t in use.count:
        rows[t[0]].add(t[1])
    rows["object"].update(api.api["object"])
    rows["global"].update(api.api["global"])
    for ns, members in api.api["namespaces"].items():
        rows["namespaces"].update(ns + "." + m for m in members)
    rows["events"].update(api.api["events"])
    for cls, fields in api.api["save"].items():
        rows["save"].update(cls + "." + f for f in fields)
    for area, table in decided.items():
        rows[area].update(r for r in table if not r.endswith(".*"))
    return rows


def build(api, use, decided, rules, reads, scanned):
    ledger = {}
    for area in AREAS:
        entries = []
        for row in sorted(all_rows(api, use, decided)[area]):
            status, where, held = classify(area, row, rules, reads, decided.get(area, {}))
            count, modules = use.count.get((area, row), 0), len(use.modules.get((area, row), ()))
            if area == "engine":
                count, modules = scanned, scanned
            d = decision(row, decided.get(area, {})) or {}
            entries.append({"row": row, "status": status, "where": where, "instead": d.get("instead", ""), "held_by": held, "count": count, "modules": modules})
        entries.sort(key=lambda e: (-e["modules"], -e["count"], e["row"]))
        ledger[area] = entries
    return ledger


def coverage(ledger):
    lines = []
    for area in AREAS:
        used = [e for e in ledger[area] if e["modules"] > 0]
        by = defaultdict(int)
        for e in used:
            by[e["status"]] += 1
        decided = sum(v for k, v in by.items() if k not in UNDECIDED)
        total_rows = len(ledger[area])
        lines.append((area, len(used), decided, by["unknown"] + by["planned"], total_rows))
    return lines


def write_markdown(ledger, modules_scanned, workshop):
    out = ["# Module surface ledger", "", f"Generated by `tools/module-surface.py` over {modules_scanned} saves in the Workshop folder.",
           "A save field counts as used only where its value is not the one most saves carry, so a flag every object",
           "writes at its default is not a use of it.",
           "Status: native (ours, checked), compat (through compat.lua, checked), partial (works for the cases met; gap named),",
           "stub (accepted and ignored), dropped (left out on purpose, named in the import report), planned (left out today,",
           "the plan says translate it), unknown (nobody has decided).", "",
           "| area | rows used by the corpus | decided | planned or unknown | rows in all |", "|---|---|---|---|---|"]
    for area, used, decided, unknown, total in coverage(ledger):
        out.append(f"| {area} | {used} | {decided} | {unknown} | {total} |")
    for area in AREAS:
        out += ["", f"## {area}", "", "| row | used (calls / modules) | status | where | held by |", "|---|---|---|---|---|"]
        for e in ledger[area]:
            used = f"{e['count']} / {e['modules']}" if e["modules"] else "-"
            out.append(f"| `{e['row']}` | {used} | {e['status']} | {e['where']} | {e['held_by']} |")
    with open(LEDGER_MD, "w", encoding="utf-8", newline="\n") as w:
        w.write("\n".join(out) + "\n")


def undecided_in_use(ledger):
    return {(area, e["row"]) for area in AREAS for e in ledger.get(area, []) if e["status"] in UNDECIDED and e["modules"] > 0}


def gate(ledger, previous):
    before = undecided_in_use(previous)
    now = undecided_in_use(ledger)
    new = sorted(now - before)
    if new:
        print("surface gate: rows the corpus uses that nobody has decided, new since the committed ledger:")
        for area, row in new:
            print(f"  {area}: {row}")
    if len(now) > len(before):
        print(f"surface gate: {len(now)} undecided rows in use, was {len(before)}")
    return not new and len(now) <= len(before)


def main():
    p = argparse.ArgumentParser(usage=USAGE)
    p.add_argument("--workshop")
    p.add_argument("--saves")
    p.add_argument("--gate", action="store_true")
    p.add_argument("--quiet", action="store_true")
    a = p.parse_args()
    api = Api(load_json(API, {"object": {}, "global": {}, "namespaces": {}, "events": [], "save": {}}))
    decided = load_json(DECISIONS, {})
    workshop = a.workshop or os.path.join(os.path.expanduser("~"), "OneDrive", "Documentos", "My Games", "Tabletop Simulator", "Mods", "Workshop")
    files = [os.path.join(workshop, f) for f in sorted(os.listdir(workshop))] if os.path.isdir(workshop) else []
    files += [s for s in (a.saves or "").split(",") if s]
    use = Usage()
    scanned = sum(1 for f in files if f.endswith(".json") and scan_save(f, use, api))
    if scanned == 0 and a.gate:
        print(f"surface gate: no module saves to scan under {workshop}; the committed ledger stands")
        sys.exit(0)
    if scanned == 0:
        sys.exit(f"no module saves found under {workshop}\n{USAGE}")
    use.settle()
    ledger = build(api, use, decided, translator_rules(), converter_reads(), scanned)
    ledger["defaults"] = {row[1]: text for row, text in sorted(use.defaults.items())}
    previous = load_json(LEDGER_JSON, {})
    ok = gate(ledger, previous) if a.gate else True
    write_markdown(ledger, scanned, workshop)
    with open(LEDGER_JSON, "w", encoding="utf-8", newline="\n") as w:
        json.dump(ledger, w, indent=1)
        w.write("\n")
    if not a.quiet:
        for area, used, decided_n, unknown, total in coverage(ledger):
            print(f"{area:11s} used {used:4d}  decided {decided_n:4d}  unknown {unknown:4d}  of {total}")
    sys.exit(0 if ok else 1)


main()
