import argparse
import concurrent.futures
import glob
import json
import os
import re
import shutil
import subprocess
import sys
import threading
import time
from collections import defaultdict

USAGE = ("python -I tools/import-sweep.py [--base <rev>] [--base-exe <Converter.exe>] [--modules <id|name>,...] "
         "[--out <dir>] [--standard <packages dir>] [--jobs <n>] [--fresh-base] [--keep] [--diff-only]")
JOBS = 4
IMPORT_LIMIT, TIMED_OUT = 1800, 124
STALE_LOCK, LOCK_WAIT = 6 * 3600, 15
BASES = os.path.join(os.environ.get("TEMP", "/tmp"), "ktable-sweep-bases")
GAMES = [os.path.join(os.path.expanduser("~"), *inside, "My Games") for inside in ((), ("Documents",), ("OneDrive", "Documents"), ("OneDrive", "Documentos"))]
STANDARD = "std.effects"
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
ROSTER = os.path.join(HERE, "import-sweep.json")
ERRORS_RX = re.compile(r"(\d+) errors")
STAMP_RX = re.compile(r"\d{6,}")
EXAMPLES = 4
TOLD_SIZES = ("IMPORTED", "ALSO")
SAME_PIECE = ("name", "kind", "image", "back", "mesh", "texture", "shape")


def shell(args, cwd=None, show=False, limit=None):
    try:
        done = subprocess.run(args, cwd=cwd or ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=limit)
    except subprocess.TimeoutExpired as late:
        said = late.stdout.decode("utf-8", "replace") if isinstance(late.stdout, bytes) else (late.stdout or "")
        return subprocess.CompletedProcess(args, TIMED_OUT, said, f"stopped after {limit} s without finishing")
    if done.returncode and show:
        sys.stderr.write(" ".join(str(a) for a in args) + "\n" + (done.stdout or "")[-3000:] + (done.stderr or "")[-3000:] + "\n")
    return done


def roster(picked):
    modules = json.load(open(ROSTER, encoding="utf-8-sig"))["modules"]
    if not picked:
        return modules
    wanted = [w.strip().lower() for w in picked.split(",") if w.strip()]
    chosen = [m for m in modules if m["id"] in wanted or any(w in m["name"].lower() for w in wanted)]
    unknown = [w for w in wanted if not any(w == m["id"] or w in m["name"].lower() for m in chosen)]
    if unknown:
        sys.exit("not in the roster: " + ", ".join(unknown) + "\n" + USAGE)
    return chosen


def source_at(rev, out):
    source = os.path.join(out, "base-src")
    if os.path.isdir(os.path.join(source, "tools", "Converter")):
        return source
    if shell(["git", "worktree", "add", "--detach", source, rev], show=True).returncode:
        sys.exit(f"no worktree could be made at {rev}")
    return source


def converter(source, binaries):
    project = os.path.join(source, "tools", "Converter", "Converter.csproj")
    exe = os.path.join(binaries, "Converter.exe" if os.name == "nt" else "Converter")
    shell(["dotnet", "build", project, "-c", "Release", "-o", binaries, "--nologo", "-v", "q"], cwd=source, show=True)
    if not os.path.exists(exe):
        sys.exit(f"the converter did not build from {source}")
    return exe


def real_cache():
    local = os.environ.get("LOCALAPPDATA", "")
    return os.path.join(local, "KriegsspielTable", "cache", "tts")


def own_cache(real, mine):
    if not os.path.isdir(real):
        os.makedirs(mine, exist_ok=True)
        return 0
    linked = 0
    for folder, _, names in os.walk(real):
        here = os.path.join(mine, os.path.relpath(folder, real))
        os.makedirs(here, exist_ok=True)
        for name in names:
            theirs, ours = os.path.join(folder, name), os.path.join(here, name)
            if os.path.exists(ours):
                continue
            if name.lower().endswith(".json"):
                shutil.copy2(theirs, ours)
            else:
                try:
                    os.link(theirs, ours)
                except OSError:
                    shutil.copy2(theirs, ours)
            linked += 1
    return linked


def config_at(source, cache, output, path):
    settings = json.load(open(os.path.join(source, "tools", "Converter", "converter.json"), encoding="utf-8-sig"))
    settings["cachePath"], settings["outputPath"] = cache, output
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        json.dump(settings, f, indent=2)
    return path


def installed_packages():
    return os.path.join(os.environ.get("LOCALAPPDATA", ""), "KriegsspielTable", "packages")


def seed(standard, output):
    theirs, ours = os.path.join(standard, STANDARD), os.path.join(output, STANDARD)
    if os.path.isfile(os.path.join(theirs, "pack.json")) and not os.path.exists(ours):
        shutil.copytree(theirs, ours)


def side(name, source, out, exe, standard, output=None):
    cache, written, binaries = (os.path.join(out, f"{name}-{what}") for what in ("cache", "out", "bin"))
    if output is None:
        shutil.rmtree(written, ignore_errors=True)
    output = output or written
    os.makedirs(output, exist_ok=True)
    seed(standard, output)
    linked = own_cache(real_cache(), cache)
    built = exe or converter(source, binaries)
    return {"name": name, "exe": built, "config": config_at(source, cache, output, os.path.join(out, f"{name}.json")),
            "output": output, "linked": linked, "cache": cache}


def told(text, heads):
    return [line.strip() for line in (text or "").splitlines() if line.startswith(heads)]


def bring_in(place, module):
    began = time.time()
    done = shell([place["exe"], "--config", place["config"], "import", module["id"]], limit=IMPORT_LIMIT)
    scenarios = []
    for line in told(done.stdout, TOLD_SIZES):
        found = re.search(r"scenario=(\S+)", line)
        if found:
            scenarios.append(found.group(1))
    return {"code": done.returncode, "seconds": round(time.time() - began, 1), "said": told(done.stdout, TOLD_SIZES),
            "scenarios": scenarios, "tail": ((done.stdout or "")[-1500:] + (done.stderr or "")[-1500:]).strip()}


def scripts_clean(place, slug):
    scenario = os.path.join(place["output"], "scenarios", slug, "scenario.json")
    if not os.path.exists(scenario):
        return None
    done = shell([place["exe"], "--config", place["config"], "check-scripts", place["output"], scenario], limit=IMPORT_LIMIT)
    counted = [int(ERRORS_RX.search(line).group(1)) for line in (done.stdout or "").splitlines() if ERRORS_RX.search(line)]
    return max(counted) if counted else None


SAVE_NAME_RX = re.compile(r'"SaveName"\s*:\s*"([^"]*)"')
WRITING = defaultdict(threading.Lock)


def save_name(place, module):
    fetched = [os.path.join(cache, "workshop", module["id"] + ".json") for cache in (place.get("cache"), real_cache()) if cache]
    path = module_file(module["id"]) or next((f for f in fetched if os.path.exists(f)), None)
    if path is None:
        return module["id"]
    with open(path, encoding="utf-8", errors="replace") as f:
        found = SAVE_NAME_RX.search(f.read(8192))
    return found.group(1) if found else module["id"]


def one_import(place, module):
    with WRITING[(place["name"], save_name(place, module))]:
        brought = bring_in(place, module)
    brought["errors"] = {slug: scripts_clean(place, slug) for slug in brought["scenarios"]}
    print(f"  {place['name']:4} {module['name']} ({brought['seconds']} s)", flush=True)
    return brought


def sweep(modules, sides, jobs, kept_base=None):
    runs = {m["id"]: {"name": m["name"]} for m in modules}
    if kept_base:
        for module in modules:
            runs[module["id"]]["base"] = kept_base[module["id"]]
    work = [(place, module) for module in sorted(modules, key=lambda m: -m.get("seconds", 0)) for place in sides]
    named = [(place, module) for place, module in work if save_name(place, module) != module["id"]]
    unnamed = [(place, module) for place, module in work if (place, module) not in named]
    with concurrent.futures.ThreadPoolExecutor(max_workers=max(1, jobs)) as pool:
        done = {pool.submit(one_import, place, module): (place, module) for place, module in named}
        for future in concurrent.futures.as_completed(done):
            place, module = done[future]
            runs[module["id"]][place["name"]] = future.result()
    for place, module in unnamed:
        runs[module["id"]][place["name"]] = one_import(place, module)
    return runs


def module_file(module_id):
    found = [path for games in GAMES for path in glob.glob(os.path.join(games, "*", "Mods", "Workshop", module_id + ".json"))]
    return found[0] if found else None


def module_mark(module):
    path = module_file(module["id"])
    if path is None:
        return "steam"
    try:
        state = os.stat(path)
        return f"{state.st_size}:{int(state.st_mtime)}"
    except OSError:
        return "steam"


def base_lock(sha):
    os.makedirs(BASES, exist_ok=True)
    path = os.path.join(BASES, sha + ".lock")
    told = False
    while True:
        try:
            os.close(os.open(path, os.O_CREAT | os.O_EXCL | os.O_WRONLY))
            return path
        except FileExistsError:
            pass
        try:
            if time.time() - os.path.getmtime(path) > STALE_LOCK:
                os.remove(path)
                continue
        except OSError:
            continue
        if not told:
            print(f"another sweep is converting base {sha[:8]}: waiting for it", flush=True)
            told = True
        time.sleep(LOCK_WAIT)


def release(path):
    try:
        os.remove(path)
    except OSError:
        pass


def kept_base_for(sha, modules):
    store = os.path.join(BASES, sha)
    runs = read_json(os.path.join(store, "runs.json")) or {}
    if not os.path.isdir(os.path.join(store, "out")):
        return store, None
    if all(m["id"] in runs and runs[m["id"]].get("mark") == module_mark(m) and not runs[m["id"]].get("code") for m in modules):
        return store, runs
    return store, None


def keep_base(store, modules, runs):
    with open(os.path.join(store, "runs.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump({m["id"]: dict(runs[m["id"]]["base"], mark=module_mark(m)) for m in modules}, f, indent=2)


def read_json(path):
    try:
        return json.load(open(path, encoding="utf-8-sig"))
    except (OSError, ValueError):
        return None


def keyed(pieces):
    out, seen = {}, defaultdict(int)
    for piece in pieces:
        name = piece.get("guid") or piece.get("id") or "?"
        seen[name] += 1
        out[name if seen[name] == 1 else f"{name}#{seen[name]}"] = piece
    return out


def files_under(folder):
    found = set()
    for place, _, names in os.walk(folder):
        for name in names:
            if name in ("scenario.json", "pack.json", "import-report.md"):
                continue
            found.add(os.path.relpath(os.path.join(place, name), folder).replace("\\", "/"))
    return found


def stem(path):
    return os.path.splitext(os.path.basename(path))[0]


def read_side(output):
    packs, scenarios = {}, {}
    if not os.path.isdir(output):
        return {"packs": packs, "scenarios": scenarios}
    for name in sorted(os.listdir(output)):
        folder = os.path.join(output, name)
        if name == "scenarios" or not os.path.isdir(folder):
            continue
        manifest = read_json(os.path.join(folder, "pack.json"))
        if manifest is None:
            continue
        defs = {d.get("id", str(i)): d for i, d in enumerate(manifest.pop("pieces", []) or [])}
        assets = manifest.pop("assets", {}) or {}
        packs[name] = {"head": manifest, "defs": defs, "assets": assets, "files": files_under(folder)}
    inside = os.path.join(output, "scenarios")
    for name in sorted(os.listdir(inside) if os.path.isdir(inside) else []):
        folder = os.path.join(inside, name)
        table = read_json(os.path.join(folder, "scenario.json"))
        if table is None:
            continue
        pieces = keyed(table.pop("umpireBoard", []) or [])
        report = os.path.join(folder, "import-report.md")
        scenarios[name] = {"head": table, "pieces": pieces, "files": files_under(folder),
                           "report": open(report, encoding="utf-8-sig").read().splitlines() if os.path.exists(report) else []}
    return {"packs": packs, "scenarios": scenarios}


def fields_apart(mine, theirs):
    return sorted(k for k in set(mine) | set(theirs) if mine.get(k) != theirs.get(k))


def few(names):
    names = sorted(names)
    shown = ", ".join(names[:EXAMPLES])
    return shown + (f", and {len(names) - EXAMPLES} more" if len(names) > EXAMPLES else "")


def sets_apart(what, mine, theirs, lines):
    gone, new = mine - theirs, theirs - mine
    if gone and new and len(gone) == len(new):
        lines.append(f"  {what} replaced: {len(gone)} (base {few(gone)}; new {few(new)})")
        return True
    if gone:
        lines.append(f"  {what} only in base: {len(gone)} ({few(gone)})")
    if new:
        lines.append(f"  {what} only in new: {len(new)} ({few(new)})")
    return bool(gone or new)


def same_piece(piece):
    return json.dumps([piece.get(k) for k in SAME_PIECE], ensure_ascii=False, sort_keys=True)


def rekeyed(what, mine, theirs, lines):
    gone, new = set(mine) - set(theirs), set(theirs) - set(mine)
    if not gone or not new:
        return sets_apart(what, set(mine), set(theirs), lines)
    was = defaultdict(list)
    for name in gone:
        was[same_piece(mine[name])].append(name)
    moved, left = [], set()
    for name in new:
        twin = was.get(same_piece(theirs[name]))
        if twin:
            moved.append((twin.pop(), name))
            if not twin:
                was.pop(same_piece(theirs[name]))
        else:
            left.add(name)
    if moved:
        lines.append(f"  {what} re-keyed: {len(moved)} same name, kind and pictures, new id "
                     f"({few(f'{a} -> {b}' for a, b in moved[:EXAMPLES])})")
        grouped = defaultdict(list)
        for was_named, now_named in moved:
            grouped[tuple(f for f in fields_apart(mine[was_named], theirs[now_named]) if f != "id")].append((was_named, now_named))
        for changed, pairs in sorted(grouped.items(), key=lambda g: -len(g[1])):
            if not changed:
                continue
            was_named, now_named = pairs[0]
            lines.append(f"      of those, {len(pairs)} also changed {', '.join(changed)}, "
                         f"e.g. {now_named} {changed[0]}: {short(mine[was_named].get(changed[0]))} -> {short(theirs[now_named].get(changed[0]))}")
    still = {name for names in was.values() for name in names}
    sets_apart(what, still, left, lines)
    return True


def items_apart(what, mine, theirs, lines, by_shape=False):
    apart = rekeyed(what, mine, theirs, lines) if by_shape else sets_apart(what, set(mine), set(theirs), lines)
    grouped = defaultdict(list)
    for name in sorted(set(mine) & set(theirs)):
        changed = fields_apart(mine[name], theirs[name])
        if changed:
            grouped[tuple(changed)].append(name)
    for changed, names in sorted(grouped.items(), key=lambda g: -len(g[1])):
        apart = True
        lines.append(f"  {what} changed: {len(names)} on {', '.join(changed)} ({few(names)})")
        first = names[0]
        for field in changed[:3]:
            lines.append(f"      {first} {field}: {short(mine[first].get(field))} -> {short(theirs[first].get(field))}")
    return apart


def short(value):
    text = json.dumps(value, ensure_ascii=False) if not isinstance(value, str) else value
    return text if len(text) <= 90 else text[:87] + "..."


def report_apart(said, now, lines):
    mine, theirs = [STAMP_RX.sub("...", l) for l in said], [STAMP_RX.sub("...", l) for l in now]
    gone = [l for l in mine if l not in theirs and l.startswith("- ")]
    new = [l for l in theirs if l not in mine and l.startswith("- ")]
    if gone or new:
        lines.append(f"  import report: {len(gone)} line(s) gone, {len(new)} new")
        for line in (gone[:2] + new[:2]):
            lines.append(f"      {short(line)}")
    return bool(gone or new)


def written_by(runs):
    names = {STANDARD}
    for row in runs.values():
        for which in ("base", "new"):
            for line in (row.get(which) or {}).get("said") or []:
                names.update(re.findall(r"(?:scenario|pack)=(\S+)", line))
    return names | {n + ".extras" for n in names}


def only(side, names):
    return {kind: {k: v for k, v in side[kind].items() if k in names} for kind in ("packs", "scenarios")}


def compare(base, new, names=None):
    one, two = read_side(base), read_side(new)
    if names is not None:
        one, two = only(one, names), only(two, names)
    lines, apart = [], False
    for kind in ("packs", "scenarios"):
        for name in sorted(set(one[kind]) | set(two[kind])):
            mine, theirs = one[kind].get(name), two[kind].get(name)
            said = []
            if mine is None or theirs is None:
                said.append(f"  only in {'new' if mine is None else 'base'}")
            else:
                for field in fields_apart(mine["head"], theirs["head"]):
                    said.append(f"  {field}: {short(mine['head'].get(field))} -> {short(theirs['head'].get(field))}")
                if kind == "packs":
                    items_apart("defs", mine["defs"], theirs["defs"], said, by_shape=True)
                    items_apart("assets", mine["assets"], theirs["assets"], said)
                    named = set(mine["assets"]) | set(theirs["assets"])
                    mine, theirs = dict(mine), dict(theirs)
                    mine["files"] = {f for f in mine["files"] if stem(f) not in named}
                    theirs["files"] = {f for f in theirs["files"] if stem(f) not in named}
                else:
                    items_apart("pieces", mine["pieces"], theirs["pieces"], said)
                    report_apart(mine["report"], theirs["report"], said)
                sets_apart("files", mine["files"], theirs["files"], said)
            if said:
                apart = True
                lines.append(f"{kind[:-1]} {name}:")
                lines += said
    found = sum(len(one[kind]) + len(two[kind]) for kind in ("packs", "scenarios"))
    return lines, apart, found


def summary(runs):
    lines, trouble = [], False
    for module, row in runs.items():
        base, new = row.get("base", {}), row.get("new", {})
        lines.append(f"{row['name']} ({module})")
        for which, place in (("base", base), ("new", new)):
            errors = ", ".join(f"{slug} {count} script errors" for slug, count in (place.get("errors") or {}).items() if count)
            lines.append(f"  {which}: exit {place.get('code')} in {place.get('seconds')} s; " +
                         "; ".join(place.get("said") or ["nothing imported"]) + (f"; {errors}" if errors else ""))
            known = (base.get("errors") or {}) if which == "new" else (place.get("errors") or {})
            worse = any(count and count > (known.get(slug) or 0) for slug, count in (place.get("errors") or {}).items())
            if place.get("code") or not place.get("said") or worse:
                trouble = True
                if place.get("tail"):
                    lines.append(f"      {short(place['tail'].splitlines()[-1] if place['tail'].splitlines() else '')}")
    return lines, trouble


def clear(out, sides, keep):
    if keep:
        return
    shell(["git", "worktree", "remove", "--force", os.path.join(out, "base-src")])
    for place in sides:
        for what in ("cache", "out", "bin"):
            shutil.rmtree(os.path.join(out, f"{place['name']}-{what}"), ignore_errors=True)


def main():
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    ap = argparse.ArgumentParser(usage=USAGE, description="Import the roster of modules at two builds of the converter and diff what they wrote.")
    ap.add_argument("--base", default="HEAD")
    ap.add_argument("--base-exe", default="")
    ap.add_argument("--new-exe", default="")
    ap.add_argument("--modules", default="")
    ap.add_argument("--out", default=os.path.join(os.environ.get("TEMP", "/tmp"), "ktable-sweep"))
    ap.add_argument("--keep", action="store_true")
    ap.add_argument("--jobs", type=int, default=JOBS)
    ap.add_argument("--fresh-base", action="store_true")
    ap.add_argument("--diff-only", action="store_true")
    ap.add_argument("--standard", default=installed_packages())
    a = ap.parse_args()
    modules = roster(a.modules)
    out = os.path.abspath(a.out)
    os.makedirs(out, exist_ok=True)
    kept = os.path.join(out, "runs.json")
    sides = []
    runs = {}
    if a.diff_only:
        runs = read_json(kept) or {}
    else:
        dirty = [l for l in shell(["git", "status", "--porcelain"]).stdout.splitlines() if l.strip()]
        if dirty:
            print(f"the working tree has {len(dirty)} changed file(s): the new side is built from them", flush=True)
        print(f"base {a.base}; {len(modules)} module(s); {out}", flush=True)
        sha = shell(["git", "rev-parse", a.base]).stdout.strip()
        lock = base_lock(sha) if sha and not a.base_exe else None
        try:
            store, kept_base = (None, None) if a.base_exe or not sha else kept_base_for(sha, modules)
            if a.fresh_base:
                kept_base = None
            new = side("new", ROOT, out, a.new_exe, a.standard)
            if kept_base:
                print(f"base {sha[:8]} kept from an earlier sweep: only the new side is converted", flush=True)
                base_out = os.path.join(store, "out")
                sides = [new]
            else:
                if store:
                    shutil.rmtree(store, ignore_errors=True)
                base_out = os.path.join(store, "out") if store else None
                base = side("base", source_at(a.base, out) if not a.base_exe else ROOT, out, a.base_exe, a.standard, base_out)
                base_out = base["output"]
                sides = [base, new]
            print("caches linked: " + ", ".join(f"{s['name']} {s['linked']}" for s in sides) + " file(s)", flush=True)
            runs = sweep(modules, sides, a.jobs, kept_base)
            if store and not kept_base and all(not runs[m["id"]]["base"].get("code") for m in modules):
                keep_base(store, modules, runs)
        finally:
            if lock:
                release(lock)
        with open(kept, "w", encoding="utf-8", newline="\n") as f:
            json.dump(runs, f, indent=2)
    told_lines, trouble = summary(runs)
    if a.diff_only:
        base_out = os.path.join(out, "base-out")
    apart_lines, apart, found = compare(base_out, os.path.join(out, "new-out"), written_by(runs))
    if not found:
        trouble = True
        apart_lines = [f"neither side wrote a pack or a scenario under {out}: nothing was compared"]
    verdict = "differences found" if apart else ("nothing compared" if not found else "no difference")
    text = "\n".join(["# Import sweep", "", f"base {a.base}", f"modules {len(modules)}", f"verdict: {verdict}", "",
                      "## Imports", ""] + told_lines + ["", "## Differences", ""] + (apart_lines or ["none"]))
    with open(os.path.join(out, "sweep-report.md"), "w", encoding="utf-8", newline="\n") as f:
        f.write(text + "\n")
    print("\n".join(told_lines + [""] + (apart_lines or ["no difference in packs, scenarios, reports or files"])))
    print(f"\nreport: {os.path.join(out, 'sweep-report.md')}")
    if not a.diff_only:
        clear(out, sides, a.keep or apart or trouble)
    return 2 if trouble else (1 if apart else 0)


if __name__ == "__main__":
    sys.exit(main())
