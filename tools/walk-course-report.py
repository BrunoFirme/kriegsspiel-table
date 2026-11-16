import csv
import glob
import math
import os
import sys

if len(sys.argv) < 2:
    sys.exit("usage: python tools/walk-course-report.py <folder the --walk-course run wrote>")
folder = sys.argv[1]
SKIP = 0.4
JOLT = 15.0


def load(path):
    rows = list(csv.DictReader(open(path, encoding="utf-8")))
    return [{k: float(v) for k, v in r.items()} for r in rows]


def physics(rows):
    out = []
    for r in rows:
        if not out or (r["x"], r["y"], r["z"]) != (out[-1]["x"], out[-1]["y"], out[-1]["z"]):
            out.append(r)
    return out


def summary(path):
    name = os.path.basename(path)[:-4]
    rows = [r for r in load(path) if r["t"] >= SKIP]
    if len(rows) < 10:
        return name, None
    steps = physics(rows)
    speeds, slopes = [], []
    for a, b in zip(steps, steps[1:]):
        dt = b["t"] - a["t"]
        flat = math.hypot(b["x"] - a["x"], b["z"] - a["z"])
        if dt <= 0:
            continue
        speeds.append((b["t"], flat / dt))
        slopes.append(math.degrees(math.atan2(b["y"] - a["y"], flat)) if flat > 1e-4 else 0.0)
    window = []
    smooth = []
    for t, v in speeds:
        window.append((t, v))
        while window and t - window[0][0] > 0.25:
            window.pop(0)
        smooth.append(sum(x[1] for x in window) / len(window))
    run = "-run-" in name
    want = 5.0 if run else 1.5
    stall, longest, last = 0.0, 0.0, None
    for (t, _), v in zip(speeds, smooth):
        if last is not None and v < 0.2 * want:
            stall += t - last
            longest = max(longest, stall)
        else:
            stall = 0.0
        last = t
    air = sum(1 for r in rows if r["grounded"] < 0.5) / len(rows)
    jolts, worst = 0, 0.0
    step = 1 / 60
    times = [r["t"] for r in rows]
    eyes = [r["eye"] for r in rows]
    even, k, t = [], 0, times[0]
    while t <= times[-1]:
        while k + 1 < len(times) and times[k + 1] < t:
            k += 1
        if k + 1 < len(times) and times[k + 1] > times[k]:
            f = (t - times[k]) / (times[k + 1] - times[k])
            even.append(eyes[k] + (eyes[k + 1] - eyes[k]) * f)
        t += step
    for a, b, c in zip(even, even[1:], even[2:]):
        acc = (c - 2 * b + a) / (step * step)
        worst = max(worst, abs(acc))
        jolts += abs(acc) > JOLT
    up = [v for (t, v), s in zip(speeds, slopes) if s > 5]
    down = [v for (t, v), s in zip(speeds, slopes) if s < -5]
    mean = sum(v for _, v in speeds) / max(1, len(speeds))
    lifts = [r.get("shown", r["y"]) - r["ground"] for r in rows if "ground" in r and r["grounded"] > 0.5]
    lift = max((abs(x) for x in lifts), default=float("nan")) if name.startswith("terrain") else float("nan")
    return name, dict(time=rows[-1]["t"], mean=mean, up=sum(up) / len(up) if up else float("nan"), down=sum(down) / len(down) if down else float("nan"),
                      steep=max((abs(s) for s in slopes), default=0), lift=lift, air=air * 100, stall=longest, jolts=jolts, worst=worst)


print(f"{'run':30} {'time':>6} {'mean':>6} {'up':>6} {'down':>6} {'slope':>6} {'air%':>6} {'stall':>6} {'jolts':>6} {'worst':>7} {'lift':>6}")
for path in sorted(glob.glob(os.path.join(folder, "*.csv"))):
    name, m = summary(path)
    if m is None:
        print(f"{name:30} too short")
        continue
    print(f"{name:30} {m['time']:6.1f} {m['mean']:6.2f} {m['up']:6.2f} {m['down']:6.2f} {m['steep']:6.1f} {m['air']:6.1f} {m['stall']:6.2f} {m['jolts']:6d} {m['worst']:7.1f} {m['lift']:6.3f}")
