import ast, io, os, re, sys, tokenize
from collections import defaultdict

USAGE = "python -I tools/codecheck.py [--report]"
MAX_CLASS, MAX_METHOD = 1500, 100
ROOTS = ["src", "tools", "tests"]
ROOT_FILES = ["Directory.Build.props", ".editorconfig"]
SKIPPED_FOLDERS = ("bin", "obj", ".godot")
OTHER_PEOPLES_FILES = ("Fixtures", "fixtures", "seed", "seed-join", "standard")
here = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

TYPE_RX = re.compile(r"^\s*(?:(?:public|internal|private|protected|static|sealed|abstract|partial|readonly|unsafe|file)\s+)*"
                     r"(class|struct|record|interface)\s+(\w+)")
METHOD_RX = re.compile(r"^\s*(?:\[[^\]]*\]\s*)*(?:(?:public|internal|private|protected|static|override|virtual|async|"
                       r"sealed|extern|unsafe|new|partial|readonly)\s+)*[\w<>\[\],.?() ]+?\s+(\w+)\s*(<[^>]*>)?\s*\([^;]*$")
RAW_STRING_RX = re.compile(r'\$*("{3,})')
LUA_LONG_BRACKET_RX = re.compile(r"\[(=*)\[")
NOT_METHOD_NAMES = ("if", "for", "foreach", "while", "switch", "catch", "using", "lock", "return", "new", "nameof", "typeof", "when")


def blank(text):
    return re.sub(r"[^\n]", " ", text)


def line_of(text, i):
    return text.count("\n", 0, i) + 1


def end_of_raw_string(code, i):
    m = RAW_STRING_RX.match(code, i)
    if not m:
        return -1
    quotes = m.group(1)
    j = code.find(quotes, m.end())
    return len(code) if j < 0 else j + len(quotes)


def end_of_string(code, i):
    j = code.index('"', i) + 1
    verbatim = "@" in code[i:j]
    n = len(code)
    while j < n:
        if verbatim and code.startswith('""', j): j += 2; continue
        if not verbatim and code[j] == "\\": j += 2; continue
        if code[j] == '"': return j + 1
        if code[j] == "\n" and not verbatim: return j
        j += 1
    return n


def starts_string(code, i):
    return any(code.startswith(p, i) for p in ('"', '@"', '$"', '$@"', '@$"'))


def split_code_and_comments(code):
    out, comment_lines, i, n = [], set(), 0, len(code)
    while i < n:
        if code.startswith("//", i) or code.startswith("/*", i):
            if code.startswith("//", i):
                j = code.find("\n", i); j = n if j < 0 else j
            else:
                j = code.find("*/", i + 2); j = n if j < 0 else j + 2
            comment_lines.add(line_of(code, i))
            out.append(blank(code[i:j])); i = j
        elif (j := end_of_raw_string(code, i)) >= 0:
            out.append(blank(code[i:j])); i = j
        elif starts_string(code, i):
            j = end_of_string(code, i)
            out.append(blank(code[i:j])); i = j
        elif code[i] == "'":
            j = i + 1
            while j < n and code[j] != "'":
                j += 2 if code[j] == "\\" else 1
            out.append(" " * (j + 1 - i)); i = j + 1
        else:
            out.append(code[i]); i += 1
    return "".join(out), sorted(comment_lines)


def python_comment_lines(code):
    lines = {tok.start[0] for tok in tokenize.generate_tokens(io.StringIO(code).readline) if tok.type == tokenize.COMMENT}
    for node in ast.walk(ast.parse(code)):
        if isinstance(node, (ast.Module, ast.FunctionDef, ast.AsyncFunctionDef, ast.ClassDef)) and node.body:
            first = node.body[0]
            if isinstance(first, ast.Expr) and isinstance(first.value, ast.Constant) and isinstance(first.value.value, str):
                lines.add(first.lineno)
    return sorted(lines)


def powershell_comment_lines(code):
    lines, i, n = set(), 0, len(code)
    while i < n:
        c = code[i]
        at_line_start = i == 0 or code[i - 1] == "\n"
        if code.startswith("@'", i) or code.startswith('@"', i):
            close = "\n" + code[i + 1] + "@"
            j = code.find(close, i + 2)
            i = n if j < 0 else j + len(close)
        elif c == "'":
            j = i + 1
            while j < n and not (code[j] == "'" and not code.startswith("''", j)):
                j += 2 if code.startswith("''", j) else 1
            i = j + 1
        elif c == '"':
            j = i + 1
            while j < n and code[j] != '"':
                j += 2 if code[j] == "`" else 1
            i = j + 1
        elif c == "`":
            i += 2
        elif code.startswith("<#", i):
            lines.add(line_of(code, i))
            j = code.find("#>", i + 2)
            i = n if j < 0 else j + 2
        elif c == "#" and (at_line_start or code[i - 1] in " \t;({|"):
            lines.add(line_of(code, i))
            j = code.find("\n", i)
            i = n if j < 0 else j
        else:
            i += 1
    return sorted(lines)


def lua_comment_lines(code):
    lines, i, n = set(), 0, len(code)
    while i < n:
        c = code[i]
        if code.startswith("--", i):
            lines.add(line_of(code, i))
            m = LUA_LONG_BRACKET_RX.match(code, i + 2)
            close = "]" + m.group(1) + "]" if m else "\n"
            j = code.find(close, m.end() if m else i)
            i = n if j < 0 else j + len(close)
        elif c in "\"'":
            j = i + 1
            while j < n and code[j] != c and code[j] != "\n":
                j += 2 if code[j] == "\\" else 1
            i = j + 1
        elif (m := LUA_LONG_BRACKET_RX.match(code, i)):
            close = "]" + m.group(1) + "]"
            j = code.find(close, m.end())
            i = n if j < 0 else j + len(close)
        else:
            i += 1
    return sorted(lines)


def xml_comment_lines(code):
    return sorted({line_of(code, m.start()) for m in re.finditer(r"<!--", code)})


def c_like_comment_lines(code):
    return split_code_and_comments(code)[1]


def hash_line_comment_lines(code):
    return [ln for ln, line in enumerate(code.split("\n"), 1) if line.lstrip().startswith("#")]


def ini_comment_lines(code):
    return [ln for ln, line in enumerate(code.split("\n"), 1) if line.lstrip().startswith(("#", ";"))]


COMMENT_FINDERS = {
    ".py": python_comment_lines,
    ".ps1": powershell_comment_lines,
    ".lua": lua_comment_lines,
    ".csproj": xml_comment_lines,
    ".props": xml_comment_lines,
    ".targets": xml_comment_lines,
    ".gdshader": c_like_comment_lines,
    ".gdshaderinc": c_like_comment_lines,
    ".lock": hash_line_comment_lines,
    ".inc": hash_line_comment_lines,
    ".editorconfig": ini_comment_lines,
}


def finder_for(name):
    return COMMENT_FINDERS.get(os.path.splitext(name)[1] or name)


def relative(path):
    return os.path.relpath(path, here).replace("\\", "/")


def scan_comments(path, comments):
    code = open(path, encoding="utf-8-sig").read().replace("\r\n", "\n")
    try:
        found = finder_for(os.path.basename(path))(code)
    except SyntaxError as e:
        comments.append(f"{relative(path)}:{e.lineno} (could not be read: {e.msg})")
        return
    comments.extend(f"{relative(path)}:{ln}" for ln in found)


def scan(path, classes, methods, comments):
    raw = open(path, encoding="utf-8-sig").read()
    code, comment_lines = split_code_and_comments(raw)
    rel = relative(path)
    comments.extend(f"{rel}:{ln}" for ln in comment_lines)
    depth, open_blocks = 0, []
    pending = None
    for ln, line in enumerate(code.split("\n"), 1):
        m = TYPE_RX.match(line)
        if m:
            pending = ("type", m.group(2), ln)
        else:
            mm = METHOD_RX.match(line)
            if mm and mm.group(1) not in NOT_METHOD_NAMES:
                pending = ("method", mm.group(1), ln)
        for ch in line:
            if ch == "{":
                kind, name, start = pending or ("block", "", ln)
                open_blocks.append((kind, name, start, depth)); pending = None
                depth += 1
            elif ch == "}":
                depth -= 1
                if open_blocks and open_blocks[-1][3] == depth:
                    kind, name, start, _ = open_blocks.pop()
                    length = ln - start + 1
                    if kind == "type":
                        outer = [b[1] for b in open_blocks if b[0] == "type"]
                        classes[".".join(outer + [name])].append((rel, length))
                    elif kind == "method":
                        methods.append((length, f"{rel}:{start} {name}"))
        if line.strip().endswith(";") and pending and pending[0] == "method":
            pending = None


def selftest():
    def cs(code):
        return split_code_and_comments(code)[1]
    assert cs("x(); // note\n") == [1]
    assert cs("a();\n/* b\n c */\n") == [2]
    assert cs('var u = "https://x";\n') == []
    assert cs('var s = """\n  // shader\n  """;\n') == []
    assert cs("var c = '/'; var d = '/';\n") == []
    assert python_comment_lines("x = 1  # why\n") == [1]
    assert python_comment_lines('def f():\n    """what"""\n    return 1\n') == [2]
    assert python_comment_lines('s = "# not one"\n') == []
    assert powershell_comment_lines("# note\n$a = 1\n") == [1]
    assert powershell_comment_lines('$a = "#1"; $b = \'#2\'\n') == []
    assert powershell_comment_lines("$a = 1 # why\n") == [1]
    assert powershell_comment_lines("<# a\n b #>\n$a = 1\n") == [1]
    assert powershell_comment_lines("$t = @\"\n# text\n\"@\n") == []
    assert lua_comment_lines("local a = 1 -- why\n") == [1]
    assert lua_comment_lines('local s = "--not"\nlocal l = [[\n-- text\n]]\n') == []
    assert lua_comment_lines("--[[ a\nb ]]\nx = 1\n") == [1]
    assert xml_comment_lines("<A>\n  <!-- why -->\n</A>\n") == [2]
    assert hash_line_comment_lines("# what\nclick \"#2\"\n") == [1]
    assert ini_comment_lines("root = true\n# why\n; also\n") == [2, 3]


def walk_files():
    for name in ROOT_FILES:
        path = os.path.join(here, name)
        if os.path.exists(path):
            yield path
    for r in ROOTS:
        for root, _, files in os.walk(os.path.join(here, r)):
            parts = root.replace("\\", "/").split("/")
            if any(p in parts for p in SKIPPED_FOLDERS + OTHER_PEOPLES_FILES):
                continue
            for f in files:
                yield os.path.join(root, f)


def main():
    selftest()
    classes, methods, comments = defaultdict(list), [], []
    for path in walk_files():
        name = os.path.basename(path)
        if name.endswith(".cs"):
            scan(path, classes, methods, comments)
        elif finder_for(name):
            scan_comments(path, comments)
    totals = sorted(((sum(l for _, l in parts), name, parts) for name, parts in classes.items()), reverse=True)
    big = [t for t in totals if t[0] > MAX_CLASS]
    long_methods = sorted((m for m in methods if m[0] > MAX_METHOD), reverse=True)
    if "--report" in sys.argv:
        print("Largest classes:"); [print(f"  {t:5} {n}") for t, n, _ in totals[:15]]
        print("Longest methods:"); [print(f"  {l:5} {w}") for l, w in sorted(methods, reverse=True)[:15]]
        print(f"Comments: {len(comments)}")
        return 0
    for t, n, parts in big:
        print(f"class over {MAX_CLASS} lines: {n} ({t}) in " + ", ".join(f"{p} {l}" for p, l in parts))
    for l, w in long_methods:
        print(f"method over {MAX_METHOD} lines: {w} ({l})")
    for c in comments:
        print(f"comment (the code should say it): {c}")
    if big or long_methods or comments:
        print(f"codecheck: {len(big)} class(es), {len(long_methods)} method(s), {len(comments)} comment(s) over the rules")
        return 1
    print("codecheck: clean")
    return 0


if __name__ == "__main__":
    sys.exit(main())
