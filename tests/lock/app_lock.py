import argparse, fnmatch, gzip, hashlib, json, os, re, shutil, subprocess, sys, time
from pathlib import Path

HERE = Path(__file__).resolve().parent
SCRIPTS = HERE / "app"
SEED = SCRIPTS / "seed"
SEED_JOIN = SCRIPTS / "seed-join"
FIXTURES = SCRIPTS / "fixtures"
GODOT = Path(os.environ["LOCALAPPDATA"]) / "Microsoft/WinGet/Packages/GodotEngine.GodotEngine.Mono_Microsoft.Winget.Source_8wekyb3d8bbwe/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe"
WORK = Path(os.environ.get("KTABLE_LOCK_WORK", Path(os.environ["LOCALAPPDATA"]) / "Temp" / "ktable-lock"))
LIVE = WORK / "live"
SIZE = (1260, 720)
POS = {"solo": (0, 0), "host": (0, 0), "join": (1280, 0)}
PORT = 7811
TRACE = False
REPEATABLE = "seed=1,clock=2030-01-01T00:00:00Z"

OLDER_HARNESSES_LEAVE_THE_POINTER_ALONE = ["--lock-nowarp"]
SW_SHOWNOACTIVATE = 4

SHOT_DELTA = 40
SHOT_FRACTION = 0.002

WALL = re.compile(r"(?<![0-9A-Za-z.])(20\d\d-\d\d-\d\d[T ]\d\d:\d\d(:\d\d(\.\d+)?)?(Z|[+-]\d\d:?\d\d)?|20\d{6}[-_T]?\d{6}(\d{3})?|1[6-9]\d{11}|63\d{16})(?![0-9A-Za-z])")
SKIP_PARTS = ("/logs/", "/app.log", "/app.old.log", "/shader_cache/", "/vulkan/", "/.godot/", "/mono/")


def masks_for(ui_text: str):
    out = []
    for line in ui_text.splitlines():
        m = re.search(r"@(-?\d+),(-?\d+),(\d+)x(\d+)$", line)
        if not m: continue
        x, y, w, h = map(int, m.groups())
        if re.match(r'^Label "\d\d:\d\d(:\d\d)?"', line): out.append((x, y, w, h, "wall clock"))
        elif re.match(r'^RichTextLabel ".*<clock>', line): out.append((x, y, w, h, "wall clock in the log"))
        elif "<clock>" in line: out.append((x, y, w, h, "wall clock text"))
    return out


def dice_masks(steps_text: str):
    out = []
    for m in re.finditer(r"dice-mask (\d+),(\d+),(\d+),(\d+)", steps_text):
        out.append(tuple(map(int, m.groups())) + ("tumbling die",))
    return out


PACKED = re.compile(r'"packed":"([A-Za-z0-9+/=]+)"')
TOKEN = re.compile(r'"(token|tokenHash)":"[^"]*"', re.I)
PASTED = re.compile(r"(?<![0-9A-Za-z])x-c[0-9a-f]{10}(?![0-9A-Za-z])")
CAPTURE_TIME = re.compile(r"(?<=_)\d{6}(?=\.png$)")
BARE_TOKEN = re.compile(r"(?<![A-Z0-9])[A-HJ-NP-Z2-9]{16}(?![A-Z0-9])")


def inner_hash(m):
    import base64
    try:
        text = PASTED.sub("x-c<id>", WALL.sub("<time>", gzip.decompress(base64.b64decode(m.group(1))).decode("utf-8")))
        return '"packed":"<' + hashlib.sha256(text.encode("utf-8")).hexdigest()[:20] + '>"'
    except Exception:
        return m.group(0)


def file_hash(p: Path) -> str:
    try: b = p.read_bytes()
    except OSError as e: return f"<unreadable {type(e).__name__}>"
    if len(b) >= 2 and b[0] == 0x1F and b[1] == 0x8B:
        try: b = gzip.decompress(b)
        except Exception: pass
    if b"\0" not in b[:4096]:
        try:
            t = PASTED.sub("x-c<id>", TOKEN.sub(r'"\1":"<token>"', PACKED.sub(inner_hash, WALL.sub("<time>", b.decode("utf-8")))))
            if p.name == "joins.txt": t = BARE_TOKEN.sub("<token>", t)
            b = t.encode("utf-8")
        except UnicodeDecodeError: pass
    return hashlib.sha256(b).hexdigest()[:20]


def files_list(roots) -> str:
    out = []
    for tag, d in roots:
        if not d.exists(): continue
        rels = sorted((str(f.relative_to(d)).replace("\\", "/") for f in d.rglob("*") if f.is_file()), key=lambda s: s.encode("utf-16-le"))
        rels = sorted(rels, key=lambda s: [ord(c) for c in s])
        for rel in rels:
            if any(p in ("/" + rel).lower() for p in SKIP_PARTS): continue
            out.append(f"{tag}/{CAPTURE_TIME.sub('<hhmmss>', WALL.sub('<time>', rel))} {file_hash(d / rel)}\n")
    return "".join(out)


def header(script: Path):
    h = {"quick": False, "instances": ["solo"], "timeout": 240}
    for line in script.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if not line.startswith("@"): continue
        k, _, v = line[1:].partition(" ")
        if k == "quick": h["quick"] = True
        elif k == "instances": h["instances"] = [x.strip() for x in v.split(",") if x.strip()]
        elif k == "timeout": h["timeout"] = int(v)
    return h


def ensure_built(wt: Path, build: bool):
    app = wt / "src" / "App"
    packages_cs = (wt / "src" / "Core" / "Packages.cs").read_text(encoding="utf-8", errors="replace")
    if "KTABLE_USER_DIR" not in packages_cs or not (app / "dev" / "LockHarness.cs").exists():
        sys.exit(f"{wt} has no lock harness or no KTABLE_USER_DIR override (merge branch lock-app first); not run, to keep the real data safe")
    if build:
        for proj in (app, wt / "tools" / "Converter"):
            r = subprocess.run(["dotnet", "build", str(proj), "-nologo", "-v", "q"], capture_output=True, text=True, encoding="utf-8", errors="replace")
            if r.returncode != 0:
                sys.exit(f"build failed in {proj}:\n{r.stdout[-3000:]}{r.stderr[-2000:]}")
    if not (app / ".godot" / "imported").exists() or not (app / ".godot" / "global_script_class_cache.cfg").exists():
        subprocess.run([str(GODOT), "--headless", "--path", str(app), "--import"], capture_output=True, timeout=600)


def kill_stale():
    ps = f"Get-CimInstance Win32_Process | Where-Object {{ $_.CommandLine -like '*{LIVE}*' -and $_.Name -like 'Godot*' }} | ForEach-Object {{ Stop-Process -Id $_.ProcessId -Force }}"
    subprocess.run(["powershell", "-NoProfile", "-Command", ps], capture_output=True)


def fresh_live(roles):
    if LIVE.exists():
        try: shutil.rmtree(LIVE)
        except OSError:
            kill_stale(); time.sleep(1); shutil.rmtree(LIVE)
    for r in roles:
        base = LIVE / r
        shutil.copytree(SEED_JOIN if r == "join" else SEED, base / "packages")
        for d in ("user/appdata", "user/localappdata", "user/temp"): (base / d).mkdir(parents=True, exist_ok=True)
        ud = base / "user" / "appdata" / "Godot" / "app_userdata" / "Kriegsspiel Table"
        ud.mkdir(parents=True, exist_ok=True)
        (ud / "last-folder.txt").write_text(str(LIVE / "fixtures"), encoding="utf-8")
        (ud / "settings.json").write_text('{"TopLine": false}', encoding="utf-8")
    shutil.copytree(FIXTURES, LIVE / "fixtures")
    (LIVE / "sync").mkdir()


def converter_exe(wt: Path) -> Path:
    found = sorted((wt / "tools" / "Converter" / "bin" / "Debug").glob("net*/win-x64/Converter.exe"), key=lambda p: p.stat().st_mtime)
    return found[-1] if found else wt / "tools" / "Converter" / "bin" / "Debug" / "net10.0" / "win-x64" / "Converter.exe"


def env_for(role, wt: Path):
    base = LIVE / role
    e = dict(os.environ)
    e.update({
        "KTABLE_USER_DIR": str(base / "user"),
        "APPDATA": str(base / "user" / "appdata"),
        "LOCALAPPDATA": str(base / "user" / "localappdata"),
        "TEMP": str(base / "user" / "temp"), "TMP": str(base / "user" / "temp"),
        "KTABLE_REPEATABLE": REPEATABLE,
        "KTABLE_PACKAGES": str(base / "packages"),
    })
    e["KTABLE_CONVERTER"] = str(converter_exe(wt))
    return e


def launch(wt: Path, script: Path, out: Path, role: str, roles, resume: int, their_window: int):
    base = LIVE / role
    x, y = POS[role]
    args = [str(GODOT), "--path", str(wt / "src" / "App"), "--resolution", f"{SIZE[0]}x{SIZE[1]}",
            "--fixed-fps", "60", "--max-fps", "60", "--",
            "--packages", str(base / "packages"), "--port", str(PORT),
            "--lock", str(script), "--lock-out", str(out), "--lock-role", role, "--lock-roles", ",".join(roles),
            "--lock-x", str(x), "--lock-sync", str(LIVE / "sync"), "--lock-live", str(LIVE), "--lock-fixtures", str(LIVE / "fixtures")]
    if resume: args += ["--lock-resume", str(resume)]
    args += OLDER_HARNESSES_LEAVE_THE_POINTER_ALONE + ["--lock-their-window", str(their_window)]
    if TRACE: args += ["--lock-trace"]
    log = open(out / f"_godot.{role}.log", "a", encoding="utf-8", errors="replace")
    return subprocess.Popen(args, env=env_for(role, wt), stdout=log, stderr=subprocess.STDOUT, cwd=str(base), startupinfo=shown_without_focus())


def run_script(wt: Path, script: Path, out: Path, tries: int = 4):
    for t in range(tries):
        problems = play(wt, script, out)
        dist = out / "_disturbed"
        if not dist.exists(): return problems
        why = dist.read_text(encoding="utf-8", errors="replace").strip().splitlines()
        print(f"    ({script.stem}: disturbed from outside, played again: {why[0] if why else ''})", flush=True)
        time.sleep(2)
    problems.append(f"DISTURBED {tries} times: a key or button was pressed in the app's window, or it lost focus mid-drag")
    (out / "_problems.txt").write_text("".join(p + "\n" for p in problems), encoding="utf-8")
    return problems


def play(wt: Path, script: Path, out: Path):
    h = header(script)
    roles = h["instances"]
    if out.exists(): shutil.rmtree(out)
    out.mkdir(parents=True)
    fresh_live(roles)
    problems = []
    deadline = time.time() + h["timeout"]
    their_window = foreground_window()
    procs = {r: launch(wt, script, out, r, roles, 0, their_window) for r in roles}
    restarts = 0
    while procs:
        for r, p in list(procs.items()):
            if p.poll() is None: continue
            del procs[r]
            rf = out / f"_resume.{r}"
            if rf.exists() and restarts < 8:
                n = int(rf.read_text()); rf.unlink(); restarts += 1
                procs[r] = launch(wt, script, out, r, roles, n, their_window)
        if time.time() > deadline:
            for r, p in procs.items():
                p.kill(); p.wait(); problems.append(f"TIMEOUT ({r}, {h['timeout']} s)")
            break
        time.sleep(0.2)
    for r in roles:
        base = LIVE / r
        after = out / (f"{r}._after" if r != "solo" else "_after")
        after.mkdir(exist_ok=True)
        if (base / "packages" / "_saves").exists(): shutil.copytree(base / "packages" / "_saves", after / "saves", dirs_exist_ok=True)
        (after / "files.txt").write_text(clean_live(files_list([("packages", base / "packages"), ("user", base / "user")])), encoding="utf-8", newline=chr(10))
    (out / "_problems.txt").write_text("".join(p + "\n" for p in problems), encoding="utf-8")
    return problems


def clean_live(s: str) -> str:
    for v in {str(LIVE), str(LIVE).replace("\\", "/")}:
        s = re.sub(re.escape(v), "<LIVE>", s, flags=re.I)
    return s


def first_diff(a: str, b: str):
    al, bl = a.splitlines(), b.splitlines()
    for i in range(max(len(al), len(bl))):
        x = al[i] if i < len(al) else "<missing>"
        y = bl[i] if i < len(bl) else "<missing>"
        if x != y: return f"line {i+1}: old {x[:160]!r} / new {y[:160]!r}"
    return None


def compare_shot(a: Path, b: Path, masks, diff_png: Path):
    from PIL import Image, ImageChops
    ia, ib = Image.open(a).convert("RGB"), Image.open(b).convert("RGB")
    if ia.size != ib.size: return f"picture size {ia.size} / {ib.size}"
    d = ImageChops.difference(ia, ib)
    from PIL import ImageDraw
    mask = Image.new("L", ia.size, 255)
    md = ImageDraw.Draw(mask)
    for (x, y, w, h, _why) in masks: md.rectangle([x, y, x + w, y + h], fill=0)
    r, g, bb = d.split()
    mx = ImageChops.lighter(ImageChops.lighter(r, g), bb)
    over = mx.point(lambda v: 255 if v > SHOT_DELTA else 0)
    over = ImageChops.multiply(over, mask)
    n = over.histogram()[255]
    total = mask.histogram()[255] or 1
    if n / total <= SHOT_FRACTION: return None
    diff_png.parent.mkdir(parents=True, exist_ok=True)
    w, h = ia.size
    side = Image.new("RGB", (w * 3, h))
    side.paste(ia, (0, 0)); side.paste(ib, (w, 0))
    red = Image.new("RGB", ia.size, (255, 0, 0))
    hl = Image.composite(red, ImageChops.multiply(ia, Image.new("RGB", ia.size, (90, 90, 90))), over)
    side.paste(hl, (w * 2, 0))
    side.save(diff_png)
    bbox = over.getbbox()
    return f"picture: {n} pixels differ ({100*n/total:.2f}%) in {bbox}"


def top_bar_loose(ui: str) -> str:
    def one(line):
        m = re.search(r"@(-?\d+),(-?\d+),(\d+)x(\d+)$", line)
        if not m or int(m.group(2)) > 34: return line
        return line[:m.start()] + f"@~,{m.group(2)},~x{m.group(4)}"
    return "\n".join(one(l) for l in ui.split("\n"))


def compare_runs(old: Path, new: Path, diffs: Path, two: bool = False):
    po, pn = (old / "_problems.txt").read_text(encoding="utf-8"), (new / "_problems.txt").read_text(encoding="utf-8")
    if po.strip() or pn.strip():
        return f"_run problems: old {po.strip() or 'none'} / new {pn.strip() or 'none'}"
    co = sorted(p.name for p in old.iterdir() if p.is_dir())
    cn = sorted(p.name for p in new.iterdir() if p.is_dir())
    if co != cn: return f"checkpoints: old {co} / new {cn}"
    def written(cp):
        f = old / cp / "steps.txt"
        return f.stat().st_mtime if f.exists() else float("inf")
    for cp in sorted(co, key=written):
        a, b = old / cp, new / cp
        for f in ("steps.txt", "wire.jsonl", "state.json", "ui.txt", "files.txt"):
            fa, fb = a / f, b / f
            if not fa.exists() and not fb.exists(): continue
            ta = fa.read_text(encoding="utf-8") if fa.exists() else "<missing>"
            tb = fb.read_text(encoding="utf-8") if fb.exists() else "<missing>"
            if two and f == "ui.txt": ta, tb = top_bar_loose(ta), top_bar_loose(tb)
            if ta != tb:
                d = diffs / cp
                d.mkdir(parents=True, exist_ok=True)
                (d / ("old." + f)).write_text(ta, encoding="utf-8"); (d / ("new." + f)).write_text(tb, encoding="utf-8")
                return f"{cp} {f} {first_diff(ta, tb)}"
        sa, sb = a / "shot.png", b / "shot.png"
        if sa.exists() and sb.exists():
            ui = (a / "ui.txt").read_text(encoding="utf-8") if (a / "ui.txt").exists() else ""
            st = (a / "steps.txt").read_text(encoding="utf-8") if (a / "steps.txt").exists() else ""
            bar = [(0, 0, 4000, 34, "top bar: the random peer number")] if two else []
            r = compare_shot(sa, sb, masks_for(ui) + dice_masks(st) + bar, diffs / f"{cp}.png")
            if r: return f"{cp} shot.png {r}"
    return None


def scripts(only, quick):
    out = []
    for p in sorted(SCRIPTS.glob("*.lock")):
        if only and not any(fnmatch.fnmatch(p.name, g) or fnmatch.fnmatch(p.stem, g) for g in only): continue
        if quick and not header(p)["quick"]: continue
        out.append(p)
    return out


def foreground_window() -> int:
    if os.name != "nt": return 0
    import ctypes
    return ctypes.windll.user32.GetForegroundWindow() or 0


def shown_without_focus():
    if os.name != "nt": return None
    si = subprocess.STARTUPINFO()
    si.dwFlags |= subprocess.STARTF_USESHOWWINDOW
    si.wShowWindow = SW_SHOWNOACTIVATE
    return si


def uses_os_clipboard(wt: Path) -> bool:
    f = wt / "src" / "App" / "scripts" / "Clipboard.cs"
    return not f.exists() or "AppClipboard" not in f.read_text(encoding="utf-8", errors="replace")


def clipboard(text=None):
    if text is None:
        r = subprocess.run(["powershell", "-NoProfile", "-Command", "Get-Clipboard -Raw"], capture_output=True, text=True)
        return r.stdout
    subprocess.run(["powershell", "-NoProfile", "-Command", "Set-Clipboard -Value $input"], input=text, text=True, capture_output=True)


def main():
    for s in (sys.stdout, sys.stderr): s.reconfigure(encoding="utf-8", errors="replace")
    ap = argparse.ArgumentParser(description="Plays the scripts in tests/lock/app on two builds of the app and compares what comes out (docs/tools.md).")
    ap.add_argument("--old"); ap.add_argument("--new")
    ap.add_argument("--quick", action="store_true"); ap.add_argument("--only", action="append")
    ap.add_argument("--runs", type=int, default=1)
    ap.add_argument("--no-build", action="store_true")
    ap.add_argument("--one"); ap.add_argument("--wt"); ap.add_argument("--out")
    ap.add_argument("--trace", action="store_true", help="with --one: a picture and the controls after every step, in _trace")
    ap.add_argument("--diffs", default=str(WORK / "diffs"))
    a = ap.parse_args()
    global TRACE
    TRACE = a.trace
    if not GODOT.exists(): sys.exit(f"Godot not found at {GODOT}")
    if not SEED.exists(): subprocess.run([sys.executable, str(SCRIPTS / "make_seed.py")], check=True)
    builds = [Path(a.wt).resolve()] if a.one else [Path(x).resolve() for x in (a.old, a.new) if x]
    saved_clip = clipboard() if any(uses_os_clipboard(wt) for wt in builds) else None
    try:
        if a.one:
            wt = Path(a.wt).resolve()
            ensure_built(wt, not a.no_build)
            s = Path(a.one) if Path(a.one).exists() else SCRIPTS / (a.one if a.one.endswith(".lock") else a.one + ".lock")
            out = Path(a.out) if a.out else WORK / "one" / s.stem
            t = time.time()
            problems = run_script(wt, s.resolve(), out)
            steps = "".join((p / "steps.txt").read_text(encoding="utf-8") for p in sorted(out.iterdir()) if (p / "steps.txt").exists())
            bad = [l for l in steps.splitlines() if "NOT FOUND" in l or "failed" in l or "PRESENT" in l or "never came" in l or "still changing" in l]
            print(f"{s.stem}: {time.time()-t:.0f} s, out {out}" + ("; " + "; ".join(problems) if problems else ""))
            for l in bad: print("   ", l)
            return 0
        if not a.old or not a.new: ap.error("--old and --new (or --one) are needed")
        old, new = Path(a.old).resolve(), Path(a.new).resolve()
        for wt in {old, new}: ensure_built(wt, not a.no_build)
        todo = scripts(a.only, a.quick)
        if not todo: sys.exit("no scripts match")
        any_diff = False
        diffs = Path(a.diffs)
        if diffs.exists(): shutil.rmtree(diffs, ignore_errors=True)
        t0 = time.time()
        for run in range(1, a.runs + 1):
            if a.runs > 1: print(f"-- run {run} of {a.runs}")
            for s in todo:
                t = time.time()
                ro, rn = WORK / "out" / "old" / s.stem, WORK / "out" / "new" / s.stem
                if uses_os_clipboard(old): clipboard(" ")
                run_script(old, s, ro)
                if uses_os_clipboard(new): clipboard(" ")
                run_script(new, s, rn)
                d = compare_runs(ro, rn, diffs / f"run{run}" / s.stem, two=len(header(s)["instances"]) > 1)
                print(f"{s.stem}: {'same' if d is None else 'DIFF ' + d}  ({time.time()-t:.0f} s)", flush=True)
                any_diff |= d is not None
        print(f"{len(todo)} script(s) x {a.runs} run(s) in {time.time()-t0:.0f} s; {'DIFFERENCES, see ' + str(diffs) if any_diff else 'all same'}")
        return 1 if any_diff else 0
    finally:
        if saved_clip is not None: clipboard(saved_clip or " ")


if __name__ == "__main__":
    sys.exit(main())
