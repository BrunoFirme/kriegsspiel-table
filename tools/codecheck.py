import ast, io, os, re, sys, tokenize
from collections import defaultdict

USAGE = "python -I tools/codecheck.py [--report]"
MAX_CLASS, MAX_METHOD, MAX_LINE = 1500, 100, 200
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
MODIFIER = r"(?:public|internal|private|protected|static|readonly|const|event|override|sealed|abstract|virtual|required|volatile|unsafe|extern|partial)\b"
MEMBER_LINE_RX = re.compile(r"^\s*(?:\[[^\]]*\]\s*)*" + MODIFIER)
NEXT_MEMBER_RX = re.compile(r"\s+" + MODIFIER)
CATCH_RX = re.compile(r"\bcatch\b\s*(\([^)]*\))?\s*(when\b[^{]*)?\{")
BROAD_CATCH_RX = re.compile(r"\(\s*(?:System\.)?Exception\s+(\w+)\s*\)")
UNNAMED_CATCH_RX = re.compile(r"\(\s*(?:System\.)?Exception\s*\)")
HAND_DIMMED_RX = re.compile(r"\bModulate\s*=\s*new\s+Color\(\s*1f?\s*,\s*1f?\s*,\s*1f?\s*,")
HAND_GAP_RX = re.compile(r'(?:AddThemeConstantOverride|SetConstant)\(\s*"(?:h_|v_)?separation"\s*,\s*\d')
HAND_LABEL_WIDTH_RX = re.compile(r"\bUi\.(?:Form\(\s*\d|RowLabel\([^,]+,\s*\d)")
UI_FOLDER, UI_HOME = "src/App/", "src/App/scripts/Ui.cs"
NAME_RX = re.compile(r"TTS|Tts|(?<![A-Za-z_])tts(?!\.)|(?i:tabletop ?simulator|berserk games)")
NAME_ROOTS, NAME_ROOT_FILES = ["docs"], ["README.md", "CONTRIBUTING.md", "THIRD-PARTY-NOTICES.md"]
NAME_CHECKED = (".cs", ".py", ".ps1", ".lua", ".json", ".md", ".lock", ".inc", ".tscn", ".tres", ".gdshader", ".gdshaderinc",
                ".csproj", ".props", ".iss", ".txt", ".sln", ".editorconfig")
NAME_RULE_HOME = "tools/codecheck.py"
FOLDER_NAME, CACHE_FOLDER = '"Tabletop Simulator"', '"cache", "tts"'
NAMES_THE_IMPORTER_NEEDS = {
    "tools/Converter/ModuleFolders.cs": [FOLDER_NAME],
    "tools/Converter.Tests/ConverterTests.cs": [FOLDER_NAME],
    "tools/module-surface.py": [FOLDER_NAME],
    "tools/bundle_models.py": [FOLDER_NAME, CACHE_FOLDER],
    "tests/lock/converter_diff.py": [FOLDER_NAME, CACHE_FOLDER],
    "tools/effects_extract.py": [CACHE_FOLDER],
    "tools/import-sweep.py": [CACHE_FOLDER],
    "tools/Converter/Config.cs": [CACHE_FOLDER],
    "src/Client/Caches.cs": ['Root, "tts"'],
    "tools/surface/ledger.json": ["api.tabletopsimulator.com/ui/introUI"],
}


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
    prefix = code[i:j]
    verbatim, interpolated = "@" in prefix, "$" in prefix
    n = len(code)
    holes = 0
    while j < n:
        c = code[j]
        if holes > 0:
            if c == "{": holes += 1
            elif c == "}": holes -= 1
            elif starts_string(code, j):
                j = end_of_string(code, j)
                continue
            elif c == "'":
                j = end_of_char(code, j)
                continue
            j += 1
            continue
        if interpolated and code.startswith("{{", j): j += 2; continue
        if interpolated and c == "{": holes = 1; j += 1; continue
        if verbatim and code.startswith('""', j): j += 2; continue
        if not verbatim and c == "\\": j += 2; continue
        if c == '"': return j + 1
        if c == "\n" and not verbatim: return j
        j += 1
    return n


def end_of_char(code, i):
    j = i + 1
    while j < len(code) and code[j] != "'":
        j += 2 if code[j] == "\\" else 1
    return j + 1


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
            j = end_of_char(code, i)
            out.append(" " * (j - i)); i = j
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


def depths(line):
    parens = braces = 0
    for i, ch in enumerate(line):
        if ch == "(": parens += 1
        elif ch == ")": parens -= 1
        elif ch == "{": braces += 1
        elif ch == "}": braces -= 1
        yield i, ch, parens, braces


def members_on(line):
    if not MEMBER_LINE_RX.match(line):
        return 1
    return 1 + sum(1 for i, ch, p, b in depths(line) if ch in ";}" and p == 0 and b == 0 and NEXT_MEMBER_RX.match(line, i + 1))


def statements_on(line):
    return sum(1 for _, ch, p, b in depths(line) if ch == ";" and p == 0 and b == 0)


def dense_blocks(line):
    found, i = [], 0
    while i < len(line):
        if line[i] != "{":
            i += 1
            continue
        depth, semis, j = 1, 0, i + 1
        while j < len(line) and depth > 0:
            if line[j] == "{": depth += 1
            elif line[j] == "}": depth -= 1
            elif line[j] == ";": semis += 1
            j += 1
        if depth > 0:
            break
        if semis > 2:
            found.append((i, j, semis))
        i = j
    return found


def block_after(code, open_i):
    depth, j = 0, open_i
    while j < len(code):
        if code[j] == "{": depth += 1
        elif code[j] == "}":
            depth -= 1
            if depth == 0: return code[open_i + 1:j]
        j += 1
    return code[open_i + 1:]


def catch_problems(code, raw=None):
    raw = code if raw is None else raw
    for m in CATCH_RX.finditer(code):
        clause, guard = m.group(1), m.group(2)
        if guard:
            continue
        body = block_after(raw, m.end() - 1)
        if clause is None or UNNAMED_CATCH_RX.fullmatch(clause):
            if not re.search(r"\bthrow\s*;", body):
                yield line_of(code, m.start()), "a catch names what it expects, or rethrows"
        elif (named := BROAD_CATCH_RX.fullmatch(clause)) and not re.search(r"\b" + named.group(1) + r"\b", body):
            yield line_of(code, m.start()), "a broad catch does something with the exception"


def code_length(line, raw_line):
    return len(raw_line) - sum(1 for a, b in zip(raw_line, line) if b == " " and a != " ")


def density_of(rel, ln, line, raw_line):
    if (length := code_length(line, raw_line)) > MAX_LINE:
        yield f"{rel}:{ln} ({length} characters of code, at most {MAX_LINE})"
    if members_on(line) > 1:
        yield f"{rel}:{ln} (one member declaration per line)"
    if statements_on(line) > 1:
        yield f"{rel}:{ln} (one statement per line)"
    if dense_blocks(line):
        yield f"{rel}:{ln} (a one-line block holds at most two statements)"


def scan(path, classes, methods, comments, density, catches):
    raw = open(path, encoding="utf-8-sig").read()
    code, comment_lines = split_code_and_comments(raw)
    rel = relative(path)
    comments.extend(f"{rel}:{ln}" for ln in comment_lines)
    catches.extend(f"{rel}:{ln} ({why})" for ln, why in catch_problems(code, raw))
    raw_lines = raw.split("\n")
    depth, open_blocks = 0, []
    pending = None
    for ln, line in enumerate(code.split("\n"), 1):
        density.extend(density_of(rel, ln, line, raw_lines[ln - 1].rstrip("\r")))
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


def ui_problems(rel, code):
    if not rel.startswith(UI_FOLDER) or rel == UI_HOME:
        return
    for ln, line in enumerate(code.split("\n"), 1):
        if HAND_DIMMED_RX.search(line):
            yield f"{rel}:{ln} (text dimmed by hand: Ui.RowLabel, Ui.Fact or Ui.Muted)"
        if HAND_GAP_RX.search(line):
            yield f"{rel}:{ln} (spacing by hand: a Ui.Gap step)"
        if HAND_LABEL_WIDTH_RX.search(line):
            yield f"{rel}:{ln} (label width by hand: Ui.DialogLabels, Ui.PanelLabels or a named constant)"


def name_problems(rel, text):
    if rel == NAME_RULE_HOME:
        return
    allowed = NAMES_THE_IMPORTER_NEEDS.get(rel, [])
    lines = text.split("\n")
    for ln, line in enumerate(lines, 1):
        after = lines[ln] if ln < len(lines) else ""
        split = NAME_RX.search(line.rstrip() + " " + after.lstrip()) and not NAME_RX.search(after)
        if (NAME_RX.search(line) or split) and not any(a in line for a in allowed):
            yield f"{rel}:{ln}"


def name_files():
    for name in NAME_ROOT_FILES:
        path = os.path.join(here, name)
        if os.path.exists(path):
            yield path
    for r in NAME_ROOTS:
        for root, _, files in os.walk(os.path.join(here, r)):
            for f in files:
                yield os.path.join(root, f)
    yield from walk_files()


def selftest():
    def cs(code):
        return split_code_and_comments(code)[1]
    assert cs("x(); // note\n") == [1]
    assert cs("a();\n/* b\n c */\n") == [2]
    assert cs('var u = "https://x";\n') == []
    assert cs('var s = """\n  // shader\n  """;\n') == []
    assert cs("var c = '/'; var d = '/';\n") == []
    assert cs('var s = $"{(a ? "x" : "y")}"; // c\n') == [1]
    assert statements_on('a($"{(q ? "x;" : "y")}"); b();') == 2
    assert statements_on("a($\"{x:N0} {{y}} {'\"'}\"); b();") == 2
    assert code_length('        x = "abcdefghij";', '        x = "abcdefghij";') == 25
    assert code_length('        x =             ;', '        x = "abcdefghij";') == 13
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
    assert members_on("    public int A { get; set; } public int B { get; set; }") == 2
    assert members_on('    public string Id { get; set; } = ""; public int State { get; set; }') == 2
    assert members_on("    public bool S { get => _s; internal set => _s = value; }") == 1
    assert members_on("    public override Sender SentBy() => Sender.Umpire;") == 1
    assert statements_on("        a = 1; b = 2;") == 2
    assert statements_on("        for (int i = 0; i < n; i++) x();") == 1
    assert statements_on("        try { a(); b(); } catch (IOException) { c(); }") == 0
    assert [n for _, _, n in dense_blocks("        if (x) { a(); b(); c(); }")] == [3]
    assert dense_blocks("        try { a(); return true; } catch (IOException) { return false; }") == []
    assert dense_blocks("    public int A { get; set; }") == []
    assert list(catch_problems("try { a(); } catch { return null; }")) != []
    assert list(catch_problems("try { a(); } catch { Discard(t); throw; }")) == []
    assert list(catch_problems("try { a(); } catch (Exception) { return false; }")) != []
    assert list(catch_problems("try { a(); } catch (Exception e) { return false; }")) != []
    assert list(catch_problems("try { a(); } catch (Exception e) when (e is IOException) { }")) == []
    assert list(catch_problems("try { a(); } catch (IOException) { }")) == []
    assert list(catch_problems("try { a(); } catch (Exception e) { Log(e.Message); }")) == []
    assert list(catch_problems("try { a(); }\ncatch (Exception e)\n{\n    Log(e);\n}")) == []
    assert list(ui_problems("src/App/scripts/X.cs", "var l = new Label { Modulate = new Color(1, 1, 1, 0.7f) };")) != []
    assert list(ui_problems("src/App/scripts/Ui.cs", "l.Modulate = new Color(1, 1, 1, 0.7f);")) == []
    assert list(ui_problems("src/App/scripts/X.cs", "g.SelfModulate = new Color(1, 1, 1, a);")) == []
    assert list(ui_problems("src/App/scripts/X.cs", 'row.AddThemeConstantOverride("separation", 10);')) != []
    assert list(ui_problems("src/App/scripts/X.cs", 'g.AddThemeConstantOverride("h_separation", Ui.Gap.Row);')) == []
    assert list(ui_problems("src/App/scripts/X.cs", "var g = Ui.Form(84);")) != []
    assert list(ui_problems("src/App/scripts/X.cs", 'r.AddChild(Ui.RowLabel("Tint", 44));')) != []
    assert list(ui_problems("src/App/scripts/X.cs", "var g = Ui.Form(Ui.PanelLabels);")) == []
    assert list(name_problems("docs/x.md", "Convert to TTS")) != []
    assert list(name_problems("src/X.cs", "var ttsRot = 1;")) != []
    assert list(name_problems("src/X.cs", "class TtsSave")) != []
    assert list(name_problems("src/X.cs", "a Tabletop Simulator module")) != []
    assert list(name_problems("src/X.cs", 'var id = "tts." + slug;')) == []
    assert list(name_problems("src/X.cs", "if (env.__tts_bound) go();")) == []
    assert list(name_problems("src/X.cs", "five watts")) == []
    assert list(name_problems("docs/x.md", "imports a Tabletop\nSimulator module")) == ["docs/x.md:1"]
    assert list(name_problems("docs/x.md", "a table\ntop")) == []
    assert list(name_problems("tools/Converter/ModuleFolders.cs", 'const string Game = "Tabletop Simulator";')) == []
    assert list(name_problems("tools/Converter/ModuleFolders.cs", "the Tabletop Simulator folder")) != []
    told = 'try { a(); } catch (Exception e) { Say($"no: {e.Message}"); }'
    assert list(catch_problems(split_code_and_comments(told)[0], told)) == []


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
    classes, methods, comments, density, catches, ui = defaultdict(list), [], [], [], [], []
    for path in walk_files():
        name = os.path.basename(path)
        if name.endswith(".cs"):
            scan(path, classes, methods, comments, density, catches)
            ui.extend(ui_problems(relative(path), open(path, encoding="utf-8-sig").read()))
        elif finder_for(name):
            scan_comments(path, comments)
    names = []
    for path in name_files():
        if path.endswith(NAME_CHECKED) or os.path.basename(path) in ROOT_FILES:
            names.extend(name_problems(relative(path), open(path, encoding="utf-8-sig").read()))
    totals = sorted(((sum(l for _, l in parts), name, parts) for name, parts in classes.items()), reverse=True)
    big = [t for t in totals if t[0] > MAX_CLASS]
    long_methods = sorted((m for m in methods if m[0] > MAX_METHOD), reverse=True)
    if "--report" in sys.argv:
        print("Largest classes:"); [print(f"  {t:5} {n}") for t, n, _ in totals[:15]]
        print("Longest methods:"); [print(f"  {l:5} {w}") for l, w in sorted(methods, reverse=True)[:15]]
        print(f"Comments: {len(comments)}")
        print(f"Dense lines: {len(density)}")
        print(f"Catches over the rules: {len(catches)}")
        print(f"UI built by hand: {len(ui)}")
        print(f"Product names: {len(names)}")
        return 0
    for t, n, parts in big:
        print(f"class over {MAX_CLASS} lines: {n} ({t}) in " + ", ".join(f"{p} {l}" for p, l in parts))
    for l, w in long_methods:
        print(f"method over {MAX_METHOD} lines: {w} ({l})")
    for c in comments:
        print(f"comment (the code should say it): {c}")
    for d in density:
        print(f"dense line: {d}")
    for c in catches:
        print(f"catch: {c}")
    for u in ui:
        print(f"ui: {u}")
    for n in names:
        print(f"another product's name (say digital tabletop module): {n}")
    if big or long_methods or comments or density or catches or ui or names:
        print(f"codecheck: {len(big)} class(es), {len(long_methods)} method(s), {len(comments)} comment(s), {len(density)} dense line(s), {len(catches)} catch(es), {len(ui)} hand-built UI line(s), {len(names)} product name(s) over the rules")
        return 1
    print("codecheck: clean")
    return 0


if __name__ == "__main__":
    sys.exit(main())
