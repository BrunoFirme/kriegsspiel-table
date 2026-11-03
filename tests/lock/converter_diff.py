import argparse, hashlib, http.server, json, os, re, shutil, socketserver, stat, subprocess, sys, threading, time

TTS = os.environ.get("KTABLE_TTS_DIR") or next((p for p in (
    os.path.join(os.path.expanduser("~"), d, "My Games", "Tabletop Simulator")
    for d in ("Documents", os.path.join("OneDrive", "Documents"), os.path.join("OneDrive", "Documentos")))
    if os.path.isdir(p)), "")
MODS = os.path.join(TTS, "Mods")
WORKSHOP = os.path.join(MODS, "Workshop")
SAVES = os.path.join(TTS, "Saves")
REAL_CACHE = os.path.join(os.environ.get("LOCALAPPDATA", ""), "KriegsspielTable", "cache", "tts")
TEMP_ROOT = os.path.join(os.environ.get("TEMP") or os.path.expanduser("~"), "ktable-lock")
FINDINGS = os.path.join(TEMP_ROOT, "findings")

REPEATABLE = "seed=1805,clock=2026-01-01T09:00:00Z"
TIMEOUT_S = 45 * 60

TEXT_MASKS = [
    (r"\.md$", r"^Generated \d{4}-\d{2}-\d{2} \d{2}:\d{2}\.$", "Generated <stamp>."),
    (r"\.stdout$", r"^  \d+/\d+ \(\d+ failed\)$", "  <n>/<total> (<failed so far>)"),
]

TEXT_UNORDERED = [
    (r"\.stdout$", r"^PROGRESS \d+/\d+$"),
]

JSON_MASKS = [
    (r"\.journal\.json$", "pid"),
    (r"\.journal\.json$", "pidStarted"),
]

JSON_DROP = [
    (r"(^|/)cache/index\.json$", lambda k, v: isinstance(v, dict) and v.get("status") == "missing"),
]

TEXT_EXT = {".md", ".txt", ".lua", ".stdout", ".stderr", ".sha256", ".obj", ".mtl", ".csv", ".xml", ".log", ".html"}
IMAGE_EXT = {".png", ".webp", ".jpg", ".jpeg"}

try:
    from PIL import Image
    HAVE_PIL = True
except Exception:
    HAVE_PIL = False


def inputs(quick):
    mods = [os.path.join(WORKSHOP, f) for f in os.listdir(WORKSHOP)
            if f.lower().endswith(".json") and not f.startswith("WorkshopFileInfos")]
    saves = []
    for d, dirs, files in os.walk(SAVES):
        dirs[:] = sorted(x for x in dirs if not (d == SAVES and x == "Saved Objects"))
        saves += [os.path.join(d, f) for f in files if f.lower().endswith(".json") and not f.startswith("SaveFileInfos")]
    if quick:
        mods = sorted(mods, key=os.path.getsize)[:6]
        saves = sorted(saves, key=os.path.getsize)[:2]
    items = [("module", p) for p in sorted(mods)] + [("save", p) for p in sorted(saves)]
    return items


def label(kind, path):
    base = WORKSHOP if kind == "module" else SAVES
    return f"{kind} {os.path.relpath(path, base)}"


class Stub(http.server.BaseHTTPRequestHandler):
    def _no(self):
        self.send_response(404)
        self.send_header("Content-Length", "0")
        self.send_header("Connection", "close")
        self.end_headers()
        self.close_connection = True

    do_GET = do_HEAD = do_POST = do_CONNECT = _no

    def log_message(self, *a):
        pass


class StubServer(socketserver.ThreadingMixIn, http.server.HTTPServer):
    daemon_threads = True
    allow_reuse_address = False


def converter_cmd(build):
    exe = os.path.join(build, "Converter.exe")
    if os.path.isfile(exe):
        return [exe]
    dll = os.path.join(build, "Converter.dll")
    if os.path.isfile(dll):
        return ["dotnet", dll]
    raise SystemExit(f"no Converter.exe or Converter.dll in {build}")


def environment(work, port, invariant):
    env = {k: v for k, v in os.environ.items()
           if not k.upper().startswith("KTABLE_") and k.upper() not in
           {"HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "NO_PROXY", "DOTNET_SYSTEM_GLOBALIZATION_INVARIANT"}}
    proxy = f"http://127.0.0.1:{port}"
    env.update({
        "KTABLE_REPEATABLE": REPEATABLE,
        "KTABLE_PACKAGES": os.path.join(work, "packages"),
        "HTTP_PROXY": proxy, "HTTPS_PROXY": proxy, "ALL_PROXY": proxy,
        "TEMP": os.path.join(work, "tmp"), "TMP": os.path.join(work, "tmp"),
        "DOTNET_CLI_TELEMETRY_OPTOUT": "1",
    })
    if invariant:
        env["DOTNET_SYSTEM_GLOBALIZATION_INVARIANT"] = "1"
    return env


def prepare(work, build, port, cache_src):
    if os.path.exists(work):
        rmtree(work)
    for d in ("packages", "cache", "out", "tmp", os.path.join("out", "docs")):
        os.makedirs(os.path.join(work, d), exist_ok=True)
    if cache_src and os.path.isdir(cache_src):
        shutil.copytree(cache_src, os.path.join(work, "cache"), dirs_exist_ok=True)
    with open(os.path.join(build, "converter.json"), encoding="utf-8-sig") as f:
        cfg = json.load(f)
    cfg["ttsModsPath"] = MODS
    cfg["cachePath"] = os.path.join(work, "cache")
    cfg["outputPath"] = os.path.join(work, "packages")
    cfg["steamHost"] = f"http://127.0.0.1:{port}/"
    with open(os.path.join(work, "converter.json"), "w", encoding="utf-8") as f:
        json.dump(cfg, f, indent=2)


class Runner:
    def __init__(self, build, work, env):
        self.cmd = converter_cmd(build)
        self.work, self.env, self.n, self.codes = work, env, 0, {}

    def run(self, command, *args, tag=None):
        self.n += 1
        tag = f"{self.n:02d}-{tag or command}"
        cmd = self.cmd + ["--config", os.path.join(self.work, "converter.json"),
                          "--out", os.path.join(self.work, "packages"), command] + list(args)
        try:
            p = subprocess.run(cmd, cwd=self.work, env=self.env, capture_output=True, timeout=TIMEOUT_S)
            out, err, code = p.stdout, p.stderr, p.returncode
        except subprocess.TimeoutExpired as e:
            out, err, code = e.stdout or b"", e.stderr or b"", "timeout"
        with open(os.path.join(self.work, "out", tag + ".stdout"), "wb") as f:
            f.write(out)
        with open(os.path.join(self.work, "out", tag + ".stderr"), "wb") as f:
            f.write(err)
        self.codes[tag] = code
        return out.decode("utf-8", "replace"), code

    def finish(self):
        with open(os.path.join(self.work, "out", "exit-codes.json"), "w", encoding="utf-8") as f:
            json.dump(self.codes, f, indent=1, sort_keys=True)


def run_input(build, work, env, path):
    r = Runner(build, work, env)
    plan, code = r.run("plan", path)
    packs = [l[len("PLAN pack "):].strip() for l in plan.splitlines() if l.startswith("PLAN pack ")]
    slug = next((l[len("PLAN scenario "):].strip() for l in plan.splitlines() if l.startswith("PLAN scenario ")), None)
    if code == 0:
        r.run("import", path)
        root = os.path.join(work, "packages")
        scenario = os.path.join(root, "scenarios", slug, "scenario.json") if slug else None
        if scenario and os.path.isfile(scenario):
            r.run("export-tts", scenario, os.path.join(work, "out", "export-tts.json"))
        for p in packs:
            if os.path.isfile(os.path.join(root, p, "pack.json")):
                r.run("export-pack-tts", p, os.path.join(work, "out", f"export-pack-{p}.json"))
        if scenario and os.path.isfile(scenario):
            r.run("check-scripts", root, scenario, tag="check-scripts-scenario")
        r.run("check-scripts", root, tag="check-scripts-packs")
    r.finish()
    return r.codes


def run_api_reference(build, work, env):
    r = Runner(build, work, env)
    r.run("api-reference", os.path.join(work, "out", "docs"))
    r.finish()
    return r.codes


def files(root):
    out = []
    for d, dirs, fs in os.walk(root):
        dirs.sort()
        for f in sorted(fs):
            out.append(os.path.relpath(os.path.join(d, f), root).replace("\\", "/"))
    return out


def sha(p):
    h = hashlib.sha256()
    with open(p, "rb") as f:
        for b in iter(lambda: f.read(1 << 20), b""):
            h.update(b)
    return h.hexdigest()


def short(v, n=80):
    s = json.dumps(v, ensure_ascii=False) if not isinstance(v, str) else repr(v)
    return s if len(s) <= n else s[:n] + "..."


def mask_json(rel, v):
    drops = [test for pat, test in JSON_DROP if re.search(pat, rel)]
    if drops and isinstance(v, dict):
        v = {k: x for k, x in v.items() if not any(t(k, x) for t in drops)}
    keys = {k for pat, k in JSON_MASKS if re.search(pat, rel)}
    if not keys:
        return v

    def walk(x):
        if isinstance(x, dict):
            return {k: ("<masked>" if k in keys else walk(val)) for k, val in x.items()}
        if isinstance(x, list):
            return [walk(i) for i in x]
        return x
    return walk(v)


def json_diff(a, b, where="$"):
    if type(a) is not type(b):
        return f"{where}: old={short(a)} new={short(b)}"
    if isinstance(a, dict):
        if list(a.keys()) != list(b.keys()):
            ka, kb = set(a), set(b)
            gone, new = sorted(ka - kb), sorted(kb - ka)
            if gone or new:
                return f"{where}: keys only in old {gone[:5]}, only in new {new[:5]}"
            return f"{where}: same keys in another order"
        for k in a:
            d = json_diff(a[k], b[k], f"{where}.{k}")
            if d:
                return d
        return None
    if isinstance(a, list):
        for i, (x, y) in enumerate(zip(a, b)):
            d = json_diff(x, y, f"{where}[{i}]")
            if d:
                return d
        if len(a) != len(b):
            return f"{where}: {len(a)} items in old, {len(b)} in new"
        return None
    return None if a == b else f"{where}: old={short(a)} new={short(b)}"


def text_lines(rel, p):
    with open(p, "rb") as f:
        t = f.read().decode("utf-8", "replace")
    lines = t.splitlines()
    for pat, line, rep in TEXT_MASKS:
        if re.search(pat, rel):
            rx = re.compile(line)
            lines = [rep if rx.match(l) else l for l in lines]
    for pat, line in TEXT_UNORDERED:
        if re.search(pat, rel):
            rx = re.compile(line)
            loose = sorted(l for l in lines if rx.match(l))
            if loose:
                lines = [l for l in lines if not rx.match(l)] + [f"<{len(loose)} lines compared in any order>"] + loose
    return lines


TEXT_PICTURE_PIXELS, TEXT_PICTURE_LEVELS = 4, 32


def text_picture(rel, pa):
    root = pa[: len(pa) - len(rel)]
    return rel.startswith("cache/text/") or (
        rel.endswith(".png") and os.path.isfile(os.path.join(root, "cache", "text", os.path.basename(rel))))


def near_pixels(pa, pb):
    with Image.open(pa) as a, Image.open(pb) as b:
        if a.size != b.size or a.mode != b.mode:
            return f"pixels: {a.size} {a.mode} vs {b.size} {b.mode}"
        a, b = a.convert("RGBA"), b.convert("RGBA")
        off = [(x, y) for x, y in zip(a.getdata(), b.getdata()) if x != y]
        worst = max((max(abs(i - j) for i, j in zip(x, y)) for x, y in off), default=0)
        if len(off) > TEXT_PICTURE_PIXELS or worst > TEXT_PICTURE_LEVELS:
            return f"text picture: {len(off)} pixels differ, by up to {worst} levels"
        return None


def same_pixels(pa, pb):
    with Image.open(pa) as a, Image.open(pb) as b:
        if a.size != b.size:
            return f"pixels: size {a.size} vs {b.size}"
        if a.mode != b.mode:
            return f"pixels: mode {a.mode} vs {b.mode}"
        return None if a.tobytes() == b.tobytes() else "pixels differ"


def compare_file(rel, pa, pb):
    ext = os.path.splitext(rel)[1].lower()
    if ext == ".json":
        try:
            with open(pa, encoding="utf-8-sig") as f:
                ja = json.load(f)
            with open(pb, encoding="utf-8-sig") as f:
                jb = json.load(f)
        except Exception:
            ja = jb = None
        if ja is not None or jb is not None:
            d = json_diff(mask_json(rel, ja), mask_json(rel, jb))
            return f"{rel} {d}" if d else None
    if ext in TEXT_EXT or ext == ".json":
        la, lb = text_lines(rel, pa), text_lines(rel, pb)
        for i, (x, y) in enumerate(zip(la, lb)):
            if x != y:
                return f"{rel} line {i + 1}: old={short(x)} new={short(y)}"
        if len(la) != len(lb):
            return f"{rel}: {len(la)} lines in old, {len(lb)} in new"
        return None
    if sha(pa) == sha(pb):
        return None
    if ext == ".png" and HAVE_PIL and text_picture(rel, pa):
        try:
            d = near_pixels(pa, pb)
        except Exception as e:
            d = f"pixels could not be read ({type(e).__name__})"
        return f"{rel} {d}" if d else None
    if ext in IMAGE_EXT and "/tiles/" in "/" + rel and HAVE_PIL:
        try:
            d = same_pixels(pa, pb)
        except Exception as e:
            d = f"pixels could not be read ({type(e).__name__})"
        return f"{rel} {d}" if d else None
    if ext in IMAGE_EXT and HAVE_PIL:
        try:
            d = same_pixels(pa, pb)
        except Exception as e:
            d = f"pixels could not be read ({type(e).__name__})"
        return f"{rel}: bytes differ ({d or 'decoded pixels the same'})"
    return f"{rel}: bytes differ"


def compare(a, b):
    fa, fb = files(a), files(b)
    if fa != fb:
        sa, sb = set(fa), set(fb)
        only_a, only_b = sorted(sa - sb), sorted(sb - sa)
        return f"files only in old {only_a[:3]}{'...' if len(only_a) > 3 else ''}, only in new {only_b[:3]}{'...' if len(only_b) > 3 else ''}"
    for rel in fa:
        d = compare_file(rel, os.path.join(a, rel), os.path.join(b, rel))
        if d:
            compare.rel = rel
            return d
    return None


def rmtree(p):
    def again(func, path, _):
        try:
            os.chmod(path, stat.S_IWRITE)
            func(path)
        except Exception:
            pass
    for _ in range(3):
        shutil.rmtree(p, onerror=again)
        if not os.path.exists(p):
            return
        time.sleep(0.5)


def culture_name():
    try:
        import winreg
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, r"Control Panel\International") as k:
            return winreg.QueryValueEx(k, "LocaleName")[0]
    except Exception:
        return "unknown"


def main():
    for s in (sys.stdout, sys.stderr): s.reconfigure(encoding="utf-8", errors="replace")
    ap = argparse.ArgumentParser(description="Runs two builds of the converter on the same TTS inputs and says, per input, whether they wrote the same things (docs/tools.md).")
    ap.add_argument("--old", required=True)
    ap.add_argument("--new", required=True)
    ap.add_argument("--quick", action="store_true")
    ap.add_argument("--only", help="only inputs whose path contains this text")
    ap.add_argument("--invariant", action="store_true", help="run both builds in the invariant culture")
    ap.add_argument("--cache", default=REAL_CACHE, help="the download cache each run starts from (copied, never changed)")
    ap.add_argument("--skip", type=int, default=0, help="leave out the first N jobs (api-reference is job 0), to run the corpus in parts")
    ap.add_argument("--take", type=int, default=0, help="run at most N jobs")
    ap.add_argument("--keep", action="store_true", help="keep the run folder, every input's two output trees in it")
    a = ap.parse_args()
    builds = {"old": os.path.abspath(a.old), "new": os.path.abspath(a.new)}
    for b in builds.values():
        converter_cmd(b)

    run = os.path.join(TEMP_ROOT, f"run-{os.getpid()}")
    work = os.path.join(run, "work")
    os.makedirs(run, exist_ok=True)
    srv = StubServer(("127.0.0.1", 0), Stub)
    port = srv.server_address[1]
    threading.Thread(target=srv.serve_forever, daemon=True).start()
    cache = os.path.join(run, "cache")
    if a.cache and os.path.isdir(a.cache):
        shutil.copytree(a.cache, cache)
    env = environment(work, port, a.invariant)

    items = inputs(a.quick)
    if a.only:
        items = [(k, p) for k, p in items if a.only.lower() in p.lower()]
    print(f"# old {builds['old']}")
    print(f"# new {builds['new']}")
    print(f"# culture {'invariant (DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1)' if a.invariant else culture_name() + ' (the machine culture, inherited by both)'}"
          f"; {REPEATABLE}; images in tiles/ and text pictures {'by decoded pixels when bytes differ' if HAVE_PIL else 'by bytes only (no Pillow)'}")
    print(f"# {len(items)} inputs + api-reference", flush=True)
    started = time.time()
    diffs = 0
    try:
        jobs = [("api-reference", None)] + [(label(k, p), p) for k, p in items]
        jobs = jobs[a.skip:][:a.take] if a.take else jobs[a.skip:]
        for name, path in jobs:
            t0 = time.time()
            codes = {}
            for side in ("old", "new"):
                prepare(work, builds[side], port, cache)
                if path is None:
                    codes[side] = run_api_reference(builds[side], work, env)
                else:
                    codes[side] = run_input(builds[side], work, env, path)
                os.replace(work, os.path.join(run, side))
            failed = " ".join(f"{t[3:]}={c}" for t, c in sorted(codes["old"].items()) if c != 0)
            note = f"  (exit {failed})" if failed else ""
            compare.rel = None
            try:
                d = compare(os.path.join(run, "old"), os.path.join(run, "new"))
            except Exception as e:
                d = f"comparison failed: {type(e).__name__}: {e}"
            took = time.time() - t0
            if d:
                diffs += 1
                print(f"DIFF  {name}  {d}  [{took:.0f} s]{note}", flush=True)
            else:
                print(f"same  {name}  [{took:.0f} s]{note}", flush=True)
            if d and compare.rel:
                found = os.path.join(FINDINGS, time.strftime("%Y%m%d-%H%M%S") + "-" + re.sub(r"[^A-Za-z0-9._-]+", "_", name))
                for side in ("old", "new"):
                    dst = os.path.join(found, side, compare.rel)
                    os.makedirs(os.path.dirname(dst), exist_ok=True)
                    shutil.copy2(os.path.join(run, side, compare.rel), dst)
            if a.keep:
                keep = os.path.join(run, ("diff-" if d else "same-") + re.sub(r"[^A-Za-z0-9._-]+", "_", name))
                os.makedirs(keep, exist_ok=True)
                for side in ("old", "new"):
                    os.replace(os.path.join(run, side), os.path.join(keep, side))
            for side in ("old", "new"):
                if os.path.exists(os.path.join(run, side)):
                    rmtree(os.path.join(run, side))
    finally:
        srv.shutdown()
        if not a.keep:
            rmtree(run)
    print(f"# {len(jobs)} compared, {len(jobs) - diffs} same, {diffs} DIFF, {time.time() - started:.0f} s")
    return 1 if diffs else 0


if __name__ == "__main__":
    sys.exit(main())
