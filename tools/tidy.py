import os, re, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import codecheck

USAGE = "python -I tools/tidy.py [--check] [--except <file listing paths to leave alone>]"
INDENT = "    "
MODIFIER = r"(?:public|internal|private|protected|static|readonly|const|event|override|sealed|abstract|virtual|required|volatile|unsafe|extern|partial)\b"
MEMBER_START = re.compile(r"^\s*(?:\[[^\]]*\]\s*)*" + MODIFIER)
AFTER_MODIFIER = re.compile(r"\s+" + MODIFIER)
BEFORE_OPS = (" && ", " || ", " ?? ", " + ")
TERNARY = (" ? ", " : ")
ORDER = {"before": 0, "ternary": 1, "chain": 2, "lambda": 3, "after": 4, "assign": 5, "pattern": 6}


def blank(raw):
    return codecheck.split_code_and_comments(raw)[0]


def indent_of(raw):
    return raw[:len(raw) - len(raw.lstrip(" \t"))]


def member_cuts(bl):
    if not MEMBER_START.match(bl):
        return []
    return [i + 1 for i, ch, p, b in codecheck.depths(bl) if ch in ";}" and p == 0 and b == 0 and AFTER_MODIFIER.match(bl, i + 1)]


def top_semis(bl, start=0):
    out, p, b = [], 0, 0
    for i in range(start, len(bl)):
        ch = bl[i]
        if ch == "(": p += 1
        elif ch == ")": p -= 1
        elif ch == "{": b += 1
        elif ch == "}": b -= 1
        elif ch == ";" and p == 0 and b == 0: out.append(i)
    return out


def one_label_end(bl, start):
    if not bl[start:].lstrip().startswith(("case ", "default:")):
        return -1
    p = b = 0
    for i in range(start, len(bl)):
        ch = bl[i]
        if ch == "(": p += 1
        elif ch == ")": p -= 1
        elif ch == "{": b += 1
        elif ch == "}": b -= 1
        elif ch == ":" and p == 0 and b == 0 and bl[i - 1:i] != ":" and bl[i + 1:i + 2] != ":":
            return i
    return -1


def label_end(bl):
    end = one_label_end(bl, 0)
    while end >= 0 and (more := one_label_end(bl, end + 1)) >= 0:
        end = more
    return end


def outer_dense_block(bl):
    i = 0
    while i < len(bl):
        if bl[i] != "{":
            i += 1
            continue
        depth, semis, j = 1, 0, i + 1
        while j < len(bl) and depth > 0:
            if bl[j] == "{": depth += 1
            elif bl[j] == "}": depth -= 1
            elif bl[j] == ";": semis += 1
            j += 1
        if depth > 0:
            return None
        if semis > 2:
            return i, j - 1
        i = j
    return None


def statements_of(inner_raw, inner_bl):
    out, at = [], 0
    for c in top_semis(inner_bl):
        out.append(inner_raw[at:c + 1].strip())
        at = c + 1
    rest = inner_raw[at:].strip()
    if rest:
        out.append(rest)
    return [s for s in out if s]


def expand_block(raw, bl, i, j):
    ind = indent_of(raw)
    prefix, suffix = raw[:i].strip(), raw[j + 1:].strip()
    lines = [ind + prefix] if prefix else []
    lines.append(ind + "{")
    lines.extend(ind + INDENT + s for s in statements_of(raw[i + 1:j], bl[i + 1:j]))
    close = ind + "}"
    if not suffix:
        lines.append(close)
    elif suffix[0] in ",);" or prefix.startswith("do") and suffix.startswith("while"):
        lines.append(close + ("" if suffix[0] in ",);" else " ") + suffix)
    else:
        lines.append(close)
        lines.append(ind + suffix)
    return lines


def split_line(raw, bl):
    ind = indent_of(raw)
    cuts = member_cuts(bl)
    if cuts:
        out, at = [], 0
        for c in cuts:
            out.append(ind + raw[at:c].strip())
            at = c
        return out + [ind + raw[at:].strip()]
    end = label_end(bl)
    if end >= 0:
        semis = top_semis(bl, end + 1)
        if len(semis) >= 2:
            out, at = [ind + raw[:end + 1].strip()], end + 1
            for c in semis:
                out.append(ind + INDENT + raw[at:c + 1].strip())
                at = c + 1
            rest = raw[at:].strip()
            return out + ([ind + INDENT + rest] if rest else [])
    else:
        semis = top_semis(bl)
        if len(semis) >= 2:
            out, at = [], 0
            for c in semis[:-1]:
                out.append(ind + raw[at:c + 1].strip())
                at = c + 1
            rest = raw[at:].strip()
            return out + ([ind + rest] if rest else [])
    block = outer_dense_block(bl)
    return expand_block(raw, bl, *block) if block else None


def split_text(text):
    out = []
    for raw, bl in zip(text.split("\n"), blank(text).split("\n")):
        work = [(raw, bl)]
        while work:
            r, b = work.pop(0)
            parts = split_line(r, b)
            if parts is None or parts == [r]:
                out.append(r)
            else:
                work = [(p, blank(p)) for p in parts] + work
    return "\n".join(out)


def length(raw):
    return codecheck.code_length(blank(raw), raw)


def depth_map(bl):
    out, p, b, a = [], 0, 0, 0
    for i, ch in enumerate(bl):
        prev, nxt = bl[i - 1:i] or " ", bl[i + 1:i + 2] or " "
        if ch == "(": p += 1
        elif ch == ")": p -= 1
        elif ch == "{": b += 1
        elif ch == "}": b -= 1
        elif ch == "<" and re.match(r"\w", prev) and (re.match(r"\w", nxt) or nxt == "("): a += 1
        elif ch == ">" and a > 0 and re.match(r"[\w>\]?)]", prev) and nxt in " ,)(>[?.;=": a -= 1
        out.append((p, b, a))
    return out


def candidates(bl):
    dm, found, asked = depth_map(bl), [], set()
    label = bl.lstrip().startswith(("case ", "default"))
    for i in range(len(bl)):
        p, b, a = dm[i]
        if a > 0:
            continue
        depth = p + b
        if any(bl.startswith(op, i) for op in BEFORE_OPS): found.append(("before", i + 1, depth))
        if bl.startswith(" ? ", i): asked.add(depth)
        if any(bl.startswith(op, i) for op in TERNARY) and depth in asked and not (label and depth == 0): found.append(("ternary", i + 1, depth))
        if bl.startswith(" or ", i): found.append(("pattern", i + 1, depth))
        if bl.startswith(", ", i) and depth >= 1: found.append(("after", i + 1, depth))
        if bl.startswith(" => ", i) and depth == 0: found.append(("lambda", i + 3, 0))
        if bl[i] == "." and bl[i - 1:i] == ")" and depth == 0: found.append(("chain", i, 0))
        if bl.startswith(" = ", i) and depth == 0: found.append(("assign", i + 2, 0))
    return [c for c in found if c[1] > 0]


def choices(found):
    kinds = {}
    for kind, pos, depth in found:
        kinds.setdefault((depth, kind), []).append(pos)
    return [(kind, sorted(kinds[(depth, kind)])) for depth, kind in sorted(kinds, key=lambda k: (k[0], ORDER[k[1]]))]


def wrap_with(raw, kind, positions):
    ind = indent_of(raw)
    pieces, at = [], 0
    for pos in positions:
        pieces.append(raw[at:pos])
        at = pos
    pieces.append(raw[at:])
    pieces = [x for x in pieces if x.strip()]
    if len(pieces) < 2:
        return None
    leading = kind in ("before", "ternary", "pattern")
    lines, cur = [], pieces[0].rstrip()
    for piece in pieces[1:]:
        joined = cur + piece if kind == "chain" else cur + (" " + piece.lstrip(" ") if leading else piece)
        if length(joined) <= codecheck.MAX_LINE and length(cur) <= codecheck.MAX_LINE:
            cur = joined.rstrip()
        else:
            lines.append(cur)
            cur = ind + INDENT + piece.strip()
    lines.append(cur)
    return lines if len(lines) >= 2 else None


def wrap_line(raw, budget=4):
    if length(raw) <= codecheck.MAX_LINE or budget == 0:
        return [raw]
    for kind, positions in choices(candidates(blank(raw))):
        parts = wrap_with(raw, kind, positions)
        if parts:
            return [q for p in parts for q in wrap_line(p, budget - 1)]
    return [raw]


def wrap_text(text):
    return "\n".join(line for raw in text.split("\n") for line in wrap_line(raw))


def tidy(text):
    return wrap_text(split_text(text))


def main():
    args = sys.argv[1:]
    check = "--check" in args
    leave = set()
    if "--except" in args:
        listing = args[args.index("--except") + 1]
        leave = {l.strip().replace("\\", "/") for l in open(listing, encoding="utf-8") if l.strip()}
    changed = 0
    for path in codecheck.walk_files():
        if not path.endswith(".cs") or codecheck.relative(path) in leave:
            continue
        data = open(path, "rb").read()
        bom = data.startswith(b"\xef\xbb\xbf")
        text = data.decode("utf-8-sig")
        eol = "\r\n" if "\r\n" in text else "\n"
        before = text.replace("\r\n", "\n")
        after = tidy(before)
        if after == before:
            continue
        changed += 1
        if check:
            print(codecheck.relative(path))
        else:
            open(path, "wb").write((b"\xef\xbb\xbf" if bom else b"") + after.replace("\n", eol).encode("utf-8"))
    print(f"tidy: {'would change' if check else 'changed'} {changed} file(s)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
