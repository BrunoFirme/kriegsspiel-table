import os, sys, collections
import xml.etree.ElementTree as ET

USAGE = "python -I tools/covcheck.py [--report] <cobertura.xml>..."
FLOOR = 48.0
LEFT_OUT = ("/dev/", "/platform/", "/obj/")
LEFT_OUT_ENDINGS = (".g.cs", ".generated.cs")


def own_code(name):
    name = name.replace("\\", "/")
    return not any(p in name for p in LEFT_OUT) and not name.endswith(LEFT_OUT_ENDINGS)


def read(paths):
    files = collections.defaultdict(dict)
    for path in paths:
        for cls in ET.parse(path).getroot().iter("class"):
            name = cls.get("filename", "").replace("\\", "/")
            if not own_code(name):
                continue
            hits = files[name]
            for line in cls.iter("line"):
                n = int(line.get("number", "0"))
                hits[n] = hits.get(n, 0) + int(line.get("hits", "0"))
    return files


def short(name):
    return name.split("src/App/")[-1] if "src/App/" in name else os.path.basename(name)


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    if not args:
        print(USAGE)
        return 2
    files = read(args)
    total = sum(len(h) for h in files.values())
    hit = sum(1 for h in files.values() for v in h.values() if v > 0)
    pct = 100 * hit / max(total, 1)
    untouched = sorted(short(n) for n, h in files.items() if not any(v > 0 for v in h.values()))
    print(f"app coverage: {pct:.1f}% of {total} lines ({hit} run), floor {FLOOR:.1f}%; {len(untouched)} of {len(files)} files untouched")
    if "--report" in sys.argv:
        print("most uncovered lines first:")
        ranked = sorted(files.items(), key=lambda kv: -sum(1 for v in kv[1].values() if v == 0))
        for name, h in ranked[:40]:
            miss = sum(1 for v in h.values() if v == 0)
            print(f"  {miss:5} uncovered  {100 * (len(h) - miss) / max(len(h), 1):5.1f}%  {short(name)}")
        print("untouched: " + ", ".join(untouched))
        return 0
    if pct + 0.05 < FLOOR:
        print(f"covcheck: app coverage {pct:.1f}% is below the floor of {FLOOR:.1f}%: add node or screen tests for what the change touches")
        return 1
    print("covcheck: clean")
    return 0


if __name__ == "__main__":
    sys.exit(main())
