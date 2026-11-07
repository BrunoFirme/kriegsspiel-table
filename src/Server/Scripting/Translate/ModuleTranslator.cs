using System.Text;
using System.Text.RegularExpressions;
using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Lua.Syntax;

namespace KTable.Server.Scripting.Translate;

public sealed record Untranslated(int Line, string What, string Text);

public sealed class TranslateResult
{
    public string Script = "";
    public int CompatCalls;
    public int DirectCalls;
    public readonly List<Untranslated> Untranslated = new();
    public bool Parsed = true;
}

public sealed class TranslateContext
{
    public Func<string, string, double, string?>? ResolveImage;
    public Func<string, string?, double, double, string?>? ResolveModel;
    public Func<string, string?>? ResolveAsset;
    public bool IsGlobal;
    public int Depth;
}

public static class ModuleTranslator
{
    public static TranslateResult Translate(string lua, TranslateContext? ctx = null)
    {
        ctx ??= new TranslateContext();
        var res = new TranslateResult();
        if (string.IsNullOrWhiteSpace(lua)) return res;
        var tree = LuaSyntaxTree.ParseText(lua, new LuaParseOptions(LuaSyntaxOptions.Lua52));
        if (tree.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            res.Parsed = false;
            foreach (var d in tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Take(5))
                res.Untranslated.Add(new Untranslated(d.Location.GetLineSpan().StartLinePosition.Line + 1, "syntax", d.GetMessage()));
            res.Script = lua;
            return res;
        }
        var root = tree.GetRoot();
        var rw = new Rewriter(res, ctx, root);
        var newRoot = rw.Visit(root)!;
        res.Script = newRoot.ToFullString();
        if (rw.MakesWidgets) res.Script = $"__tts_bound = {{ {string.Join(", ", rw.BoundHandlers.Order(StringComparer.Ordinal).Select(n => $"[\"{n}\"] = true"))} }} " + res.Script;
        return res;
    }

    static readonly HashSet<string> ObjectApi = new(StringComparer.Ordinal)
    {
        "addAttachment","addContextMenuItem","addDecal","addForce","addTag","addTorque","addToPlayerSelection","attachHider","attachInvisibleHider","call","clearButtons","clearContextMenu","clearInputs","clone","createButton","createInput","cut","deal","destruct","destroy","drop","editButton","editInput","flip","getAngularVelocity","getAttachments","getBounds","getBoundsNormalized","getButtons","getColorTint","getComponent","getComponents","getCustomObject","getData","getDecals","getDescription","getFogOfWarReveal","getGMNotes","getGUID","getInputs","getJoints","getJSON","getLock","getLuaScript","getMemo","getName","getObjects","getPosition","getPositionSmooth","getQuantity","getRotation","getRotationSmooth","getRotationValue","getRotationValues","roll","getScale","getSelectingPlayers","getSnapPoints","getStateId","getStates","getTable","getTags","getTransformForward","getTransformRight","getTransformUp","getValue","getVar","getVectorLines","getVelocity","getZones","hasTag","highlightOff","highlightOn","isDestroyed","isSmoothMoving","jointTo","positionToLocal","positionToWorld","putObject","randomize","registerCollisions","reload","removeAttachment","removeAttachments","removeButton","removeFromPlayerSelection","removeInput","removeTag","reset","resetAttachments","rotate","scale","setAngularVelocity","setColorTint","setCustomObject","setDecals","setDescription","setFogOfWarReveal","setGMNotes","setHiddenFrom","setInvisibleTo","setLock","setLuaScript","setMemo","setName","setPosition","setPositionSmooth","setRotation","setRotationSmooth","setRotationValue","setRotationValues","setScale","setSnapPoints","setState","setTable","setTags","setValue","setVar","setVectorLines","setVelocity","shuffle","shuffleStates","split","spread","takeObject","translate","unregisterCollisions","getLuaScriptState","setLuaScriptState",
    };

    static readonly HashSet<string> ObjectProps = new(StringComparer.Ordinal)
    {
        "interactable","tooltip","sticky","grid_projection","use_gravity","use_grid","use_snap_points","use_hands","use_rotation_value_flip","auto_raise","bounciness","drag","drag_selectable","angular_drag","hide_when_face_down","ignore_fog_of_war","loading_custom","mass","max_typed_number","measure_movement","script_state","spawning","static_friction","dynamic_friction","value","value_flags","alt_view_angle","held_flip_index","held_spin_index","pick_up_position","pick_up_rotation","held_position_offset","held_reduce_force","held_rotation_offset","memo","AssetBundle","type","tag","name","guid","held_by_color","resting","is_face_down","locked",
    };

    static readonly HashSet<string> EventNames = new(StringComparer.Ordinal)
    {
        "onLoad","onload","onSave","onsave","onUpdate","onDrop","onPickUp","onPickup","onDestroy","onCollisionEnter","onCollisionExit","onCollisionStay","onObjectDrop","onObjectPickUp","onObjectPickup","onObjectSpawn","onObjectDestroy","onObjectEnterContainer","onObjectLeaveContainer","onObjectEnterScriptingZone","onObjectLeaveScriptingZone","onPlayerTurn","onPlayerChangeColor","onPlayerConnect","onPlayerDisconnect","onChat","onScriptingButtonDown","onScriptingButtonUp","onObjectStateChange","onObjectRandomize","onRandomize","onPlayerAction","onObjectNumberTyped","onObjectSearchStart","onObjectSearchEnd","onObjectRotate","onRotate","onObjectHover","onHover","onBlindfold","onFixedUpdate","onPlayerPing","onObjectLoopingEffect","onObjectTriggerEffect","onObjectPeek","onObjectFlick","onObjectCollisionEnter","onObjectCollisionExit","onObjectCollisionStay","onObjectPageChange","onPageChange","onZoneGroupSort","onFilter","onExternalMessage",
    };

    static readonly Dictionary<string, string> EventRenames = new(StringComparer.Ordinal)
    {
        ["onLoad"] = "on_load", ["onload"] = "on_load", ["onSave"] = "on_save", ["onsave"] = "on_save", ["onUpdate"] = "on_update", ["onDestroy"] = "on_destroy",
        ["onDrop"] = "on_drop", ["onPickUp"] = "on_pick_up", ["onPickup"] = "on_pick_up", ["onObjectDrop"] = "on_drop", ["onObjectPickUp"] = "on_pick_up", ["onObjectPickup"] = "on_pick_up",
        ["onObjectSpawn"] = "on_spawn", ["onObjectDestroy"] = "on_destroy", ["onObjectStateChange"] = "on_state", ["onPlayerTurn"] = "on_turn", ["onRotate"] = "on_rotate", ["onObjectRotate"] = "on_rotate",
        ["onPlayerTurnStart"] = "on_turn", ["onPlayerTurnEnd"] = "on_turn",
        ["onChat"] = "on_chat", ["onObjectNumberTyped"] = "on_number_typed", ["onScriptingButtonDown"] = "on_scripting_button_down", ["onScriptingButtonUp"] = "on_scripting_button_up",
        ["onPlayerAction"] = "on_player_action", ["onObjectRandomize"] = "on_roll", ["onRandomize"] = "on_roll", ["onPageChange"] = "on_page", ["onObjectPageChange"] = "on_page", ["onObjectLoopingEffect"] = "on_effect", ["onObjectTriggerEffect"] = "on_effect", ["onObjectEnterContainer"] = "on_stowed", ["onObjectLeaveContainer"] = "on_unstowed", ["tryObjectEnterContainer"] = "try_stow",
    };

    static readonly HashSet<string> EveryScriptEvents = new(StringComparer.Ordinal)
    {
        "onObjectSpawn", "onObjectDestroy", "onObjectPickUp", "onObjectPickup", "onObjectDrop", "onObjectRotate", "onObjectRandomize", "onObjectPageChange",
        "onObjectLoopingEffect", "onObjectTriggerEffect", "onObjectEnterContainer", "onObjectLeaveContainer", "onObjectStateChange", "onChat",
    };

    static readonly HashSet<string> Stubbed = new(StringComparer.Ordinal)
    {
        "setHiddenFrom", "setInvisibleTo", "attachHider", "attachInvisibleHider", "setFogOfWarReveal", "registerCollisions", "unregisterCollisions", "jointTo",
        "addForce", "addTorque", "setVelocity", "setAngularVelocity", "drop", "cut", "deal", "shuffle", "reset", "resetAttachments",
        "split", "spread", "getComponent", "getComponents",
    };

    static string LowerFirst(string s) => s.Length == 0 ? s : char.ToLowerInvariant(s[0]) + s[1..];

    static string Camelify(string s)
    {
        var parts = s.Split('_');
        return parts.Length == 1 ? s : parts[0] + string.Concat(parts.Skip(1).Select(p => p.Length == 0 ? p : char.ToUpperInvariant(p[0]) + p[1..]));
    }

    public static string FoldMember(string name)
    {
        foreach (var candidate in new[] { name, LowerFirst(name), Camelify(name), LowerFirst(Camelify(name)) })
            if (ObjectApi.Contains(candidate) || ObjectProps.Contains(candidate)) return candidate;
        return name;
    }

    sealed class Rewriter : LuaSyntaxRewriter
    {
        readonly TranslateResult _res;
        readonly TranslateContext _ctx;
        readonly HashSet<string> _clickHandlers = new(), _inputHandlers = new(), _uiHandlers = new();
        public IEnumerable<string> BoundHandlers => _clickHandlers.Concat(_inputHandlers).Distinct();
        public bool MakesWidgets { get; private set; }

        string Widgets(string call)
        {
            MakesWidgets = true;
            return call;
        }
        readonly HashSet<SyntaxNode> _scriptLiterals = new();
        readonly HashSet<string> _ownGlobals = new(StringComparer.Ordinal);
        readonly SyntaxNode _root;

        public Rewriter(TranslateResult res, TranslateContext ctx, SyntaxNode root)
        {
            _res = res;
            _ctx = ctx;
            _root = root;
            foreach (var f in root.DescendantNodes().OfType<IdentifierKeyedTableFieldSyntax>())
            {
                var key = f.Identifier.Text;
                if (key is not ("click_function" or "input_function")) continue;
                if (f.Value is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.StringLiteralExpression))
                    (key == "click_function" ? _clickHandlers : _inputHandlers).Add(lit.Token.ValueText);
            }
            foreach (Match m in Regex.Matches(root.ToFullString(), "\\b(onClick|onValueChanged|onEndEdit|onMouseEnter|onMouseExit|onSubmit)\\s*=\\s*\"([^\"]*)\""))
            {
                var v = m.Groups[2].Value;
                var slash = v.LastIndexOf('/');
                var fn = (slash >= 0 ? v[(slash + 1)..] : v).Trim();
                if (fn.Length > 0 && fn.All(ch => char.IsLetterOrDigit(ch) || ch == '_')) _uiHandlers.Add(fn);
            }
            foreach (var n in root.DescendantNodes()) OwnGlobal(n);
            foreach (var call in root.DescendantNodes().OfType<FunctionCallExpressionSyntax>())
            {
                if (call.Expression is not MemberAccessExpressionSyntax ma || ma.MemberName.Text != "setLuaScript") continue;
                if (Args(call).FirstOrDefault() is { } arg && LiteralOf(arg, Scope(call)) is { } lit) _scriptLiterals.Add(lit);
            }
        }

        void OwnGlobal(SyntaxNode n)
        {
            switch (n)
            {
                case FunctionDeclarationStatementSyntax { Name: SimpleFunctionNameSyntax f }:
                    _ownGlobals.Add(f.ToString().Trim());
                    break;
                case LocalFunctionDeclarationStatementSyntax lf:
                    _ownGlobals.Add(lf.Name.ToString().Trim());
                    break;
                case LocalVariableDeclarationStatementSyntax lv:
                    foreach (var v in lv.Names) _ownGlobals.Add(v.IdentifierName.ToString().Trim());
                    break;
                case AssignmentStatementSyntax asg:
                    foreach (var v in asg.Variables.OfType<IdentifierNameSyntax>()) _ownGlobals.Add(v.ToString().Trim());
                    break;
            }
        }

        static SyntaxNode Scope(SyntaxNode node) =>
            node.Ancestors().FirstOrDefault(n => n is FunctionDeclarationStatementSyntax or LocalFunctionDeclarationStatementSyntax or AnonymousFunctionExpressionSyntax)
            ?? node.SyntaxTree.GetRoot();

        static ExpressionSyntax? AssignedBefore(ExpressionSyntax arg, SyntaxNode scope)
        {
            if (arg is not IdentifierNameSyntax id) return null;
            var name = id.ToString().Trim();
            ExpressionSyntax? found = null;
            foreach (var n in scope.DescendantNodes().Where(n => n.SpanStart < arg.SpanStart))
            {
                if (n is AssignmentStatementSyntax asg && asg.Variables.Count == 1 && asg.EqualsValues.Values.Count == 1 && asg.Variables[0].ToString().Trim() == name)
                    found = asg.EqualsValues.Values[0];
                else if (n is LocalVariableDeclarationStatementSyntax loc && loc.Names.Count == 1 && loc.EqualsValues is { } ev && ev.Values.Count == 1 && loc.Names[0].ToString().Trim() == name)
                    found = ev.Values[0];
            }
            return found;
        }

        static LiteralExpressionSyntax? LiteralOf(ExpressionSyntax arg, SyntaxNode scope)
        {
            var lit = arg as LiteralExpressionSyntax ?? AssignedBefore(arg, scope) as LiteralExpressionSyntax;
            return lit != null && lit.IsKind(SyntaxKind.StringLiteralExpression) ? lit : null;
        }

        static TableConstructorExpressionSyntax? TableOf(ExpressionSyntax arg, SyntaxNode scope) =>
            arg as TableConstructorExpressionSyntax ?? AssignedBefore(arg, scope) as TableConstructorExpressionSyntax;

        static (string type, double sx, double sy) SpawnShape(FunctionCallExpressionSyntax spawn)
        {
            string type = "Custom_Token";
            double sx = 1, sy = 1;
            if (Args(spawn).FirstOrDefault() is { } arg && TableOf(arg, Scope(spawn)) is { } t)
            {
                foreach (var f in t.Fields.OfType<IdentifierKeyedTableFieldSyntax>())
                {
                    if (f.Identifier.Text == "type" && f.Value is LiteralExpressionSyntax l) type = l.Token.ValueText;
                    if (f.Identifier.Text == "scale" && f.Value is TableConstructorExpressionSyntax st) (sx, sy) = Axes(st);
                }
            }
            return (type, sx, sy);
        }

        static (double x, double y) Axes(TableConstructorExpressionSyntax t)
        {
            double x = 1, y = 1;
            int loose = 0;
            foreach (var f in t.Fields)
            {
                switch (f)
                {
                    case IdentifierKeyedTableFieldSyntax k when k.Identifier.Text is "x" or "y":
                        if (Number(k.Value) is { } kv) { if (k.Identifier.Text == "x") x = kv; else y = kv; }
                        break;
                    case UnkeyedTableFieldSyntax u:
                        if (Number(u.Value) is { } uv && loose < 2) { if (loose == 0) x = uv; else y = uv; }
                        loose++;
                        break;
                }
            }
            return (x, y);
        }

        static double? Number(ExpressionSyntax e) =>
            double.TryParse(e.ToString().Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? v : null;

        static List<ExpressionSyntax> Args(FunctionCallExpressionSyntax call) => call.Argument switch
        {
            ExpressionListFunctionArgumentSyntax e => e.Expressions.ToList(),
            TableConstructorFunctionArgumentSyntax t => new() { t.TableConstructor },
            StringFunctionArgumentSyntax s => new() { s.Expression },
            _ => new(),
        };

        static string ArgText(IEnumerable<ExpressionSyntax> args) => string.Join(", ", args.Select(a => a.ToString().Trim()));

        int Line(SyntaxNode n) => n.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

        T Keep<T>(T fresh, SyntaxNode old) where T : SyntaxNode => fresh.WithLeadingTrivia(old.GetLeadingTrivia()).WithTrailingTrivia(old.GetTrailingTrivia());

        ExpressionSyntax Expr(string text, SyntaxNode old) => Keep(SyntaxFactory.ParseExpression(text), old);

        ExpressionSyntax Direct(string text, SyntaxNode old) { _res.DirectCalls++; return Expr(text, old); }
        ExpressionSyntax Compat(string text, SyntaxNode old) { _res.CompatCalls++; return Expr(text, old); }
        ExpressionSyntax Todo(string what, string text, SyntaxNode old)
        {
            _res.Untranslated.Add(new Untranslated(Line(old), what, old.ToString().Trim()));
            _res.CompatCalls++;
            return Expr(text, old);
        }

        public override SyntaxNode? VisitFunctionCallExpression(FunctionCallExpressionSyntax node)
        {
            var visited = (FunctionCallExpressionSyntax)base.VisitFunctionCallExpression(node)!;
            var args = Args(visited);
            var a = args.Select(x => x.ToString().Trim()).ToList();
            if (visited.Expression is MemberAccessExpressionSyntax ma) return MemberCall(visited, ma, a, args, node);
            if (visited.Expression is IdentifierNameSyntax id) return GlobalFunctionCall(id, a, args, node) ?? visited;
            return visited;
        }

        SyntaxNode MemberCall(FunctionCallExpressionSyntax visited, MemberAccessExpressionSyntax ma, List<string> a, List<ExpressionSyntax> args, SyntaxNode node)
        {
            string All() => ArgText(args);
            string A(int i) => i < a.Count ? a[i] : "nil";
            var recv = ma.Expression.ToString().Trim();
            var name = ma.MemberName.Text;
            if (GlobalObjectCall(visited, recv, name, a, args, node) is { } global) return global;
            if (recv.EndsWith(".UI"))
            {
                var obj = recv[..^3];
                var mapped = name switch
                {
                    "setXml" or "setXmlTable" => "set", "getXml" => "xml", "getXmlTable" => "tree", "setAttribute" => "set_attribute", "setAttributes" => "set_attributes",
                    "getAttribute" => "get_attribute", "show" => "show", "hide" => "hide", "setValue" => "set_value", "getValue" => "get_value",
                    "getAttributes" => "get_attributes", "setClass" => "set_class", "getCustomAssets" => "pictures", "setCustomAssets" => "set_pictures", _ => null,
                };
                return mapped != null ? Direct($"{obj}.ui.{mapped}({All()})", node) : Todo("object UI." + name, $"compat.todo({obj}, \"UI.{name}\")", node);
            }
            if (recv.EndsWith(".AssetBundle")) return Compat($"compat.bundle({recv[..^12]}).{name}({All()})", node);
            if (recv.EndsWith(".TextTool")) return Compat($"compat.text_tool({recv[..^9]}).{name}({All()})", node);
            if (recv.EndsWith(".Book"))
            {
                var book = recv[..^5];
                return name switch
                {
                    "getPage" => Compat($"compat.book_page({book}, {A(0)})", node),
                    "setPage" => Compat($"compat.set_book_page({book}, {A(0)}, {A(1)})", node),
                    "setHighlight" => Compat($"compat.book_highlight({book}{(args.Count > 0 ? ", " + All() : "")})", node),
                    "clearHighlight" => Compat($"compat.book_highlight({book})", node),
                    _ => Todo("Book." + name, $"compat.todo({book}, \"Book.{name}\")", node),
                };
            }
            if (recv.StartsWith("Player[") || recv.StartsWith("Player."))
            {
                var inner = recv.StartsWith("Player[") ? recv[7..^1] : "\"" + recv[7..] + "\"";
                return Compat($"compat.player({inner}).{name}({All()})", node);
            }
            var o = ObjectCall(recv, FoldMember(name), a, args, node);
            if (o != null) return o;
            return visited;
        }

        SyntaxNode? GlobalObjectCall(FunctionCallExpressionSyntax visited, string recv, string name, List<string> a, List<ExpressionSyntax> args, SyntaxNode node)
        {
            string A(int i) => i < a.Count ? a[i] : "nil";
            string All() => ArgText(args);
            switch (recv)
            {
                case "JSON":
                    return name switch
                    {
                        "encode" or "encode_pretty" => Direct($"json.encode({All()})", node),
                        "decode" => Direct($"json.decode({All()})", node),
                        _ => Todo("JSON." + name, $"compat.todo(nil, \"JSON.{name}\")", node),
                    };
                case "Wait":
                    return name switch
                    {
                        "time" => Compat($"compat.wait_time({All()})", node),
                        "frames" => Compat($"compat.wait_frames({All()})", node),
                        "condition" => Compat($"compat.wait_condition({All()})", node),
                        "stop" => Compat($"compat.wait_stop({All()})", node),
                        "stopAll" => Compat("compat.wait_stop_all()", node),
                        _ => Todo("Wait." + name, $"compat.todo(nil, \"Wait.{name}\")", node),
                    };
                case "Global":
                    return name switch
                    {
                        "call" => Compat($"compat.global_call({A(0)}, {A(1)})", node),
                        "getVar" => Direct($"game.get_var({A(0)})", node),
                        "getTable" => Compat($"compat.global_get_table({A(0)})", node),
                        "setVar" => Compat($"compat.global_set_var({A(0)}, {A(1)})", node),
                        "setTable" => Compat($"compat.global_set_table({A(0)}, {A(1)})", node),
                        "getVectorLines" => Compat("compat.global_vector_lines()", node),
                        "setVectorLines" => Compat($"compat.set_global_vector_lines({All()})", node),
                        _ => Todo("Global." + name, $"compat.todo(nil, \"Global.{name}\")", node),
                    };
                case "Player":
                    return name switch
                    {
                        "getPlayers" => Compat("compat.Player.getPlayers()", node),
                        _ => Compat($"compat.Player.{name}({All()})", node),
                    };
                case "Vector" or "Color":
                    return Compat($"compat.{recv}.{name}({All()})", node);
                case "Physics":
                    return name == "cast" ? Compat($"compat.cast({All()})", node) : Todo("Physics." + name, $"compat.todo(nil, \"Physics.{name}\")", node);
                case "UI" or "Global.UI":
                    return name switch
                    {
                        "setXml" or "setXmlTable" => Direct($"ui.set({A(0)})", node),
                        "getXml" => Direct("ui.xml()", node),
                        "getXmlTable" => Direct("ui.tree()", node),
                        "setAttribute" => Direct($"ui.set_attribute({All()})", node),
                        "setAttributes" => Direct($"ui.set_attributes({All()})", node),
                        "getAttribute" => Direct($"ui.get_attribute({All()})", node),
                        "show" => Direct($"ui.show({All()})", node),
                        "hide" => Direct($"ui.hide({All()})", node),
                        "setValue" => Direct($"ui.set_value({All()})", node),
                        "getValue" => Direct($"ui.get_value({All()})", node),
                        "getAttributes" => Direct($"ui.get_attributes({All()})", node),
                        "setClass" => Direct($"ui.set_class({All()})", node),
                        "getCustomAssets" => Direct("ui.pictures()", node),
                        "setCustomAssets" => Direct($"ui.set_pictures({All()})", node),
                        _ => Todo("UI." + name, $"compat.todo(nil, \"UI.{name}\")", node),
                    };
                case "Timer" when name == "create":
                    return Compat($"compat.timer_create(_ENV, {A(0)})", node);
                case "Turns" or "Notes" or "Lighting" or "Grid" or "Hands" or "MusicPlayer" or "Backgrounds" or "Timer":
                    return Compat($"compat.{recv}.{name}({All()})", node);
                case "Time" or "Info" or "Tables" or "WebRequest":
                    return Todo($"{recv}.{name}", $"compat.todo(nil, \"{recv}.{name}\")", node);
                case "string" or "math" or "table" or "os" or "coroutine" or "bit32" or "json" or "compat" or "board" or "game" or "wait":
                    return visited;
            }
            return null;
        }

        ExpressionSyntax? GlobalFunctionCall(IdentifierNameSyntax id, List<string> a, List<ExpressionSyntax> args, SyntaxNode node)
        {
            string A(int i) => i < a.Count ? a[i] : "nil";
            string All() => ArgText(args);
            var name = id.ToString().Trim();
            if (EventRenames.TryGetValue(name, out var renamed)) return Direct($"{renamed}({All()})", node);
            if (_ownGlobals.Contains(name)) return null;
            switch (name)
            {
                case "getObjectFromGUID": return Direct($"board.find({A(0)})", node);
                case "getAllObjects" or "getObjects": return Direct("board.pieces()", node);
                case "getObjectsWithTag": return Compat($"compat.pieces_with_tag({A(0)})", node);
                case "getObjectsWithAnyTags": return Compat($"compat.pieces_with_any_tags({A(0)})", node);
                case "getObjectsWithAllTags": return Compat($"compat.pieces_with_all_tags({A(0)})", node);
                case "spawnObject": return Compat($"compat.spawn_object({All()})", node);
                case "spawnObjectJSON": return Compat($"compat.spawn_object_json({All()})", node);
                case "spawnObjectData": return Compat($"compat.spawn_object_data({All()})", node);
                case "broadcastToAll": return Compat($"compat.print_to_all({A(0)}, {A(1)}, true)", node);
                case "printToAll": return Compat($"compat.print_to_all({A(0)}, {A(1)}, false)", node);
                case "broadcastToColor": return Compat($"compat.print_to_color({A(0)}, {A(1)}, {A(2)}, \"BroadcastToColor\")", node);
                case "printToColor": return Compat($"compat.print_to_color({A(0)}, {A(1)}, {A(2)}, \"printToColor\")", node);
                case "startLuaCoroutine": return Compat($"compat.start_coroutine({All()})", node);
                case "getSeatedPlayers": return Compat("compat.seated_players()", node);
                case "logString": return Compat($"compat.log_string({All()})", node);
                case "log": return Compat($"compat.log({All()})", node);
                case "debugLog": return Compat($"compat.debug_log({A(0)})", node);
                case "stringColorToRGB": return Compat($"compat.string_color_to_rgb({A(0)})", node);
                case "Vector": return Compat($"compat.Vector({All()})", node);
                case "vector": return Compat($"compat.Vector({All()})", node);
                case "Color": return Compat($"compat.Color({All()})", node);
                case "color": return Compat($"compat.Color({All()})", node);
                case "destroyObject": return Compat($"compat.destroy_object({A(0)})", node);
                case "addHotkey": return Compat($"compat.add_hotkey(_ENV, {All()})", node);
                case "clearHotkeys": return Compat("compat.clear_hotkeys()", node);
                case "addContextMenuItem": return Compat($"compat.add_global_context_menu_item(_ENV, {All()})", node);
                case "clearContextMenu": return Compat("compat.clear_global_context_menu()", node);
                case "getGlobalScriptVar": return Direct($"game.get_var({A(0)})", node);
                case "setGlobalScriptVar": return Compat($"compat.global_set_var({A(0)}, {A(1)})", node);
                case "getGlobalScriptTable": return Compat($"compat.global_get_table({A(0)})", node);
                case "setGlobalScriptTable": return Compat($"compat.global_set_table({A(0)}, {A(1)})", node);
                case "callLuaFunctionInOtherScript": return Compat($"compat.call({A(0)}, {A(1)})", node);
                case "callLuaFunctionInOtherScriptWithParams": return Compat($"compat.call({A(0)}, {A(1)}, {A(2)})", node);
                case "getPlayer": return Compat($"compat.get_player({A(0)})", node);
                case "colorToPlayerName": return Compat($"compat.color_to_player_name({A(0)})", node);
                case "getNotes" or "setNotes" or "getNotebookTabs" or "editNotebookTab" or "addNotebookTab" or "removeNotebookTab":
                    return Compat($"compat.Notes.{name}({All()})", node);
                case "clearPixelPaint" or "clearVectorPaint": return Compat("compat.erase_all()", node);
                case "getAllTags": return Compat("compat.all_tags()", node);
                case "logStyle" or "sendExternalMessage" or "setLookingForPlayers" or "flipTable" or "group" or "paste" or "copy":
                    return Todo(name, $"compat.todo(nil, \"{name}\")", node);
            }
            return null;
        }

        ExpressionSyntax DieCall(string recv, string name, string value, SyntaxNode node) => name switch
        {
            "getRotationValues" => Compat($"compat.get_rotation_values({recv})", node),
            "setRotationValues" => Compat($"compat.set_rotation_values({recv}, {value})", node),
            "getRotationValue" => Compat($"compat.get_rotation_value({recv})", node),
            "setRotationValue" => Compat($"compat.set_rotation_value({recv}, {value})", node),
            "setValue" => Compat($"compat.set_value({recv}, {value})", node),
            "roll" or "randomize" => Compat($"compat.randomize({recv})", node),
            "shuffleStates" => Compat($"compat.shuffle_states({recv})", node),
            _ => Compat($"compat.get_value({recv})", node),
        };

        ExpressionSyntax? ObjectCall(string recv, string name, List<string> a, List<ExpressionSyntax> args, SyntaxNode node)
        {
            string A(int i) => i < a.Count ? a[i] : "nil";
            string All() => ArgText(args);
            string Rest() => a.Count == 0 ? "" : ", " + All();
            switch (name)
            {
                case "setName": return Compat($"compat.set_name({recv}, {A(0)})", node);
                case "getName": return Compat($"compat.name({recv})", node);
                case "setDescription": return Compat($"compat.set_description({recv}, {A(0)})", node);
                case "getDescription": return Compat($"compat.description({recv})", node);
                case "getGUID": return Compat($"compat.guid({recv})", node);
                case "getStateId": return Compat($"compat.state_id({recv})", node);
                case "setState": return Compat($"compat.set_state({recv}, {A(0)})", node);
                case "getStates": return Compat($"compat.states({recv})", node);
                case "flip": return Compat($"compat.flip({recv})", node);
                case "destruct" or "destroy": return Compat($"compat.destroy({recv})", node);
                case "call": return Compat($"compat.call({recv}, {A(0)}, {A(1)})", node);
                case "getVar": return Direct($"{recv}.get_var({A(0)})", node);
                case "getTable": return Compat($"compat.get_table({recv}, {A(0)})", node);
                case "setVar": return Compat($"compat.set_var({recv}, {A(0)}, {A(1)})", node);
                case "setTable": return Compat($"compat.set_table({recv}, {A(0)}, {A(1)})", node);
                case "setColorTint": return Compat($"compat.set_color_tint({recv}, {A(0)})", node);
                case "getColorTint": return Compat($"compat.color_tint({recv})", node);
                case "getPosition": return Compat($"compat.position({recv})", node);
                case "getPositionSmooth" or "getRotationSmooth": return Compat($"compat.none({recv})", node);
                case "setPosition" or "setPositionSmooth": return Compat($"compat.set_position({recv}, {A(0)})", node);
                case "getRotation": return Compat($"compat.rotation({recv})", node);
                case "setRotation" or "setRotationSmooth": return Compat($"compat.set_rotation({recv}, {A(0)})", node);
                case "getScale": return Compat($"compat.scale3({recv})", node);
                case "setScale": return Compat($"compat.set_scale({recv}, {A(0)})", node);
                case "scale": return Compat($"compat.scale_by({recv}, {A(0)})", node);
                case "rotate": return Compat($"compat.rotate({recv}, {A(0)})", node);
                case "translate": return Compat($"compat.translate({recv}, {A(0)})", node);
                case "positionToWorld": return Compat($"compat.position_to_world({recv}, {A(0)})", node);
                case "positionToLocal": return Compat($"compat.position_to_local({recv}, {A(0)})", node);
                case "getTransformForward": return Compat($"compat.transform_forward({recv})", node);
                case "getTransformRight": return Compat($"compat.transform_right({recv})", node);
                case "getTransformUp": return Compat($"compat.transform_up({recv})", node);
                case "getBounds": return Compat($"compat.get_bounds({recv})", node);
                case "getBoundsNormalized": return Compat($"compat.get_bounds_normalized({recv})", node);
                case "addContextMenuItem": return Compat($"compat.add_context_menu_item(_ENV, {recv}, {All()})", node);
                case "clearContextMenu": return Compat($"compat.clear_context_menu({recv})", node);
                case "reload": return Compat($"compat.reload({recv})", node);
                case "setLuaScript": return Compat($"compat.set_lua_script({recv}, {A(0)})", node);
                case "getLuaScript": return Compat($"compat.get_script({recv})", node);
                case "getTags": return Compat($"compat.tags({recv})", node);
                case "hasTag": return Compat($"compat.has_tag({recv}, {A(0)})", node);
                case "addTag": return Compat($"compat.add_tag({recv}, {A(0)})", node);
                case "removeTag": return Compat($"compat.remove_tag({recv}, {A(0)})", node);
                case "setTags": return Compat($"compat.set_tags({recv}, {A(0)})", node);
                case "setLock": return Compat($"compat.set_lock({recv}, {A(0)})", node);
                case "getLock": return Compat($"compat.get_lock({recv})", node);
                case "setMemo": return Compat($"compat.set_memo({recv}, {A(0)})", node);
                case "getMemo": return Direct($"{recv}.memo", node);
                case "setGMNotes": return Compat($"compat.set_gm_notes({recv}, {A(0)})", node);
                case "getGMNotes": return Compat($"compat.get_gm_notes({recv})", node);
                case "highlightOn": return Compat($"compat.highlight({recv}{Rest()})", node);
                case "highlightOff": return Compat($"compat.highlight_off({recv})", node);
                case "setVectorLines": return Compat($"compat.set_vector_lines({recv}, {A(0)})", node);
                case "getVectorLines": return Compat($"compat.get_vector_lines({recv})", node);
                case "setDecals" or "addDecal": return Compat($"compat.{(name == "addDecal" ? "add_decal" : "set_decals")}({recv}, {A(0)})", node);
                case "getDecals": return Compat($"compat.get_decals({recv})", node);
                case "setSnapPoints": return Compat($"compat.set_snap_points({recv}, {A(0)})", node);
                case "getSnapPoints": return Compat($"compat.get_snap_points({recv})", node);
                case "setCustomObject": return Compat($"compat.set_custom_object({recv}, {A(0)})", node);
                case "getCustomObject": return Compat($"compat.get_custom_object({recv})", node);
                case "getData": return Compat($"compat.get_data({recv})", node);
                case "getJSON": return Compat($"compat.get_json({recv})", node);
                case "getObjects": return Compat($"compat.get_objects({recv})", node);
                case "getQuantity": return Compat($"compat.get_quantity({recv})", node);
                case "takeObject": return Compat($"compat.take_object({recv}, {A(0)})", node);
                case "putObject": return Compat($"compat.put_object({recv}, {A(0)})", node);
                case "getAttachments": return Compat($"compat.get_attachments({recv})", node);
                case "addAttachment": return Direct($"{recv}.attach({A(0)})", node);
                case "removeAttachment": return Compat($"compat.remove_attachment({recv}, {A(0)})", node);
                case "removeAttachments": return Compat($"compat.remove_attachments({recv})", node);
                case "isDestroyed": return Compat($"compat.is_destroyed({recv})", node);
                case "getSelectingPlayers": return Compat($"compat.get_selecting_players({recv})", node);
                case "addToPlayerSelection": return Compat($"compat.select({recv}, {A(0)}, true)", node);
                case "removeFromPlayerSelection": return Compat($"compat.select({recv}, {A(0)}, false)", node);
                case "clone": return Compat($"compat.clone({recv}{Rest()})", node);
                case var widget when WidgetCall(recv, widget, a, node) is { } w: return w;
                case var stubbed when Stubbed.Contains(stubbed):
                    return Todo(name, $"compat.todo({recv}, \"{name}\")", node);
                case "getVelocity" or "getAngularVelocity": return Compat("compat.Vector(0, 0, 0)", node);
                case "getRotationValue" or "getValue" or "getRotationValues" or "setRotationValues" or "setRotationValue" or "setValue" or "roll" or "randomize" or "shuffleStates":
                    return DieCall(recv, name, A(0), node);
                case "getJoints" or "getZones": return Compat("{}", node);
                case "isSmoothMoving": return Compat($"compat.no({recv})", node);
                case "getFogOfWarReveal": return Compat("{ reveal = false, color = \"\", range = 0 }", node);
            }
            return StateOrTagCall(recv, name, a, node);
        }

        ExpressionSyntax? WidgetCall(string recv, string name, List<string> a, SyntaxNode node)
        {
            string A(int i) => i < a.Count ? a[i] : "nil";
            return name switch
            {
                "createButton" => Compat(Widgets($"compat.create_button({recv}, {A(0)}, _G)"), node),
                "createInput" => Compat(Widgets($"compat.create_input({recv}, {A(0)}, _G)"), node),
                "editButton" => Compat(Widgets($"compat.edit_button({recv}, {A(0)}, _G)"), node),
                "editInput" => Compat(Widgets($"compat.edit_input({recv}, {A(0)}, _G)"), node),
                "removeButton" => Compat($"compat.remove_button({recv}, {A(0)})", node),
                "removeInput" => Compat($"compat.remove_input({recv}, {A(0)})", node),
                "clearButtons" => Compat($"compat.clear_buttons({recv})", node),
                "clearInputs" => Compat($"compat.clear_inputs({recv})", node),
                "getButtons" => Compat($"compat.get_buttons({recv})", node),
                "getInputs" => Compat($"compat.get_inputs({recv})", node),
                _ => null,
            };
        }

        ExpressionSyntax? StateOrTagCall(string recv, string name, List<string> a, SyntaxNode node)
        {
            string A(int i) => i < a.Count ? a[i] : "nil";
            return name switch
            {
                "getLuaScriptState" or "getScriptState" => Compat($"compat.get_prop({recv}, \"script_state\", \"\")", node),
                "setLuaScriptState" => Compat($"compat.set_prop({recv}, \"script_state\", {A(0)})", node),
                "hasAnyTag" => Compat($"compat.has_any_tag({recv})", node),
                "hasMatchingTag" => Compat($"compat.has_matching_tag({recv}, {A(0)})", node),
                _ => null,
            };
        }

        public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            var visited = (MemberAccessExpressionSyntax)base.VisitMemberAccessExpression(node)!;
            var recv = visited.Expression.ToString().Trim();
            var name = visited.MemberName.Text;
            if (visited.Parent is FunctionCallExpressionSyntax call && call.Expression == node) return visited;
            switch (recv)
            {
                case "Turns": return name == "turn_number" ? Direct("game.turn", node) : Compat($"compat.Turns.{name}", node);
                case "Player": return Compat($"compat.Player.{name}", node);
                case "Vector" or "Color": return Compat($"compat.{recv}.{name}", node);
                case "Time" or "Lighting" or "Grid" or "Hands" or "Notes" or "MusicPlayer" or "Backgrounds": return Compat($"compat.{recv}.{name}", node);
                case "JSON": return Direct($"json.{name}", node);
                case "UI" or "Global.UI": return Direct($"ui.{name}", node);
            }
            if (recv.EndsWith(".UI")) return Direct($"{recv[..^3]}.ui.{name}", node);
            if (recv.EndsWith(".Book") && name == "page_offset" && visited.Parent is not AssignmentStatementSyntax) return Compat($"compat.book_offset({recv[..^5]})", node);
            name = FoldMember(name);
            switch (name)
            {
                case "is_face_down": return Direct($"{recv}.face_down", node);
                case "held_by_color": return Compat("nil", node);
                case "resting": return Compat("true", node);
                case "spawning" or "loading_custom": return Compat("false", node);
                case "memo":
                    if (visited.Parent is AssignmentStatementSyntax) return visited;
                    return Direct($"{recv}.memo", node);
                case "interactable" or "tooltip" or "sticky" or "use_gravity" or "grid_projection" or "auto_raise" or "use_snap_points" or "use_grid" or "hide_when_face_down" or "locked"
                    or "drag_selectable" or "measure_movement" or "value" or "value_flags"
                    or "held_spin_index" or "held_flip_index" or "pick_up_position" or "pick_up_rotation":
                    if (visited.Parent is AssignmentStatementSyntax) return visited;
                    return Compat($"compat.get_prop({recv}, \"{name}\", {DefaultProp(name)})", node);
            }
            return visited;
        }

        static string DefaultProp(string name) => name switch
        {
            "interactable" or "use_gravity" or "auto_raise" or "sticky" or "tooltip" or "drag_selectable" => "true", "memo" => "\"\"", "value" or "value_flags" => "0", _ => "false",
        };

        public override SyntaxNode? VisitAssignmentStatement(AssignmentStatementSyntax node)
        {
            if (node.Variables.Count == 1 && node.EqualsValues.Values.Count == 1 && node.Variables[0] is MemberAccessExpressionSyntax pa && pa.MemberName.Text == "page_offset"
                && pa.Expression.ToString().Trim() is var owner && owner.EndsWith(".Book"))
            {
                var value = (ExpressionSyntax)Visit(node.EqualsValues.Values[0])!;
                _res.CompatCalls++;
                return Stmt($"compat.set_book_offset({owner[..^5]}, {value.ToString().Trim()})", node);
            }
            if (node.Variables.Count == 1 && node.EqualsValues.Values.Count == 1 && node.Variables[0] is MemberAccessExpressionSyntax ma && ObjectProps.Contains(FoldMember(ma.MemberName.Text))
                && ma.Expression.ToString().Trim() is var recv && recv is not ("Turns" or "Lighting" or "Grid" or "Hands" or "Notes" or "Time"))
            {
                var value = (ExpressionSyntax)Visit(node.EqualsValues.Values[0])!;
                _res.CompatCalls++;
                var stmt = Stmt($"compat.set_prop({recv}, \"{FoldMember(ma.MemberName.Text)}\", {value.ToString().Trim()})", node);
                return stmt;
            }
            return base.VisitAssignmentStatement(node);
        }

        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {
            var name = node.ToString().Trim();
            if (name == "Global" && node.Parent is not MemberAccessExpressionSyntax) return Keep(SyntaxFactory.ParseExpression("compat.Global"), node);
            if (name == "JSON" && node.Parent is not MemberAccessExpressionSyntax) return Keep(SyntaxFactory.ParseExpression("json"), node);
            if ((name is "Vector" or "Color") && node.Parent is not MemberAccessExpressionSyntax && node.Parent is not FunctionCallExpressionSyntax)
                return Keep(SyntaxFactory.ParseExpression("compat." + name), node);
            return base.VisitIdentifierName(node);
        }

        const int MostNestedScripts = 3;

        public override SyntaxNode? VisitLiteralExpression(LiteralExpressionSyntax node)
        {
            if (_scriptLiterals.Contains(node) && _ctx.Depth < MostNestedScripts) return Keep(SyntaxFactory.ParseExpression(Spawned(node.Token.ValueText)), node);
            if (node.IsKind(SyntaxKind.StringLiteralExpression) && _ctx.ResolveAsset != null && node.Token.ValueText is { } url
                && url.StartsWith("http", StringComparison.OrdinalIgnoreCase) && url.Contains("://")
                && node.Parent is not IdentifierKeyedTableFieldSyntax { Identifier.Text: "image" or "diffuse" or "mesh" }
                && _ctx.ResolveAsset(url) is { } asset)
            {
                _res.DirectCalls++;
                return Keep(SyntaxFactory.ParseExpression("\"" + asset + "\""), node);
            }
            return base.VisitLiteralExpression(node);
        }

        string Spawned(string lua)
        {
            var inner = Translate(lua, new TranslateContext
            {
                ResolveImage = _ctx.ResolveImage, ResolveModel = _ctx.ResolveModel, ResolveAsset = _ctx.ResolveAsset, Depth = _ctx.Depth + 1,
            });
            _res.CompatCalls += inner.CompatCalls;
            _res.DirectCalls += inner.DirectCalls;
            foreach (var u in inner.Untranslated) _res.Untranslated.Add(new Untranslated(u.Line, "in a spawned script: " + u.What, u.Text));
            return LongString(inner.Script);
        }

        static string LongString(string text)
        {
            var level = "";
            while (text.Contains("]" + level + "]", StringComparison.Ordinal)) level += "=";
            return $"[{level}[\n{text}\n]{level}]";
        }

        public override SyntaxNode? VisitIdentifierKeyedTableField(IdentifierKeyedTableFieldSyntax node)
        {
            var visited = (IdentifierKeyedTableFieldSyntax)base.VisitIdentifierKeyedTableField(node)!;
            var key = node.Identifier.Text;
            if (key == "mesh" && Url(node) is { } meshUrl && _ctx.ResolveModel != null)
            {
                var (_, sx, sy) = ModelShape(node);
                var def = _ctx.ResolveModel(meshUrl, Sibling(node, "diffuse"), sx, sy);
                if (def != null)
                    return visited.WithValue(Keep(SyntaxFactory.ParseExpression("\"" + def + "\""), node.Value))
                        .WithIdentifier(SyntaxFactory.Identifier(visited.Identifier.LeadingTrivia, "def", visited.Identifier.TrailingTrivia));
                _res.Untranslated.Add(new Untranslated(Line(node), "model", meshUrl));
                return visited;
            }
            if (key is "image" or "diffuse" && Url(node) is { } url && _ctx.ResolveImage != null)
            {
                if (Sibling(node, "mesh") != null) return visited;
                var (type, scale, _) = ShapeFor(node);
                var def = _ctx.ResolveImage(url, type, scale);
                if (def != null)
                    return visited.WithValue(Keep(SyntaxFactory.ParseExpression("\"" + def + "\""), node.Value));
                _res.Untranslated.Add(new Untranslated(Line(node), "picture", url));
            }
            return visited;
        }

        static string? Url(IdentifierKeyedTableFieldSyntax field) =>
            field.Value is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.StringLiteralExpression)
            && lit.Token.ValueText is { } text && text.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? text : null;

        static string? Sibling(IdentifierKeyedTableFieldSyntax field, string key) =>
            field.Parent is TableConstructorExpressionSyntax t
                ? t.Fields.OfType<IdentifierKeyedTableFieldSyntax>().Where(f => f.Identifier.Text == key).Select(Url).FirstOrDefault(u => u != null)
                : null;

        static (string type, double sx, double sy) ShapeFor(SyntaxNode node) =>
            Spawn(Scope(node), model: false) is { } own ? SpawnShape(own) : ("Custom_Token", 1, 1);

        (string type, double sx, double sy) ModelShape(SyntaxNode node)
        {
            var scope = Scope(node);
            if (Spawn(scope, model: true) is { } own) return SpawnShape(own);
            if (!ReferenceEquals(scope, _root) && Spawn(_root, model: true) is { } elsewhere) return SpawnShape(elsewhere);
            return ("Custom_Model", 1, 1);
        }

        static FunctionCallExpressionSyntax? Spawn(SyntaxNode scope, bool model) =>
            scope.DescendantNodes().OfType<FunctionCallExpressionSyntax>()
                .Where(c => c.Expression is IdentifierNameSyntax id && id.ToString().Trim() == "spawnObject")
                .FirstOrDefault(c => !model || SpawnShape(c).type == "Custom_Model");

        public override SyntaxNode? VisitFunctionDeclarationStatement(FunctionDeclarationStatementSyntax node)
        {
            var visited = (FunctionDeclarationStatementSyntax)base.VisitFunctionDeclarationStatement(node)!;
            if (visited.Name is not SimpleFunctionNameSyntax simple) return visited;
            var name = simple.ToString().Trim();
            var pars = visited.Parameters.Parameters.Select(p => p.ToString().Trim()).ToList();
            bool everyone = !_ctx.IsGlobal && EveryScriptEvents.Contains(name);
            var (newName, sig, prelude) = Signature(name, pars, _ctx.IsGlobal || everyone);
            if (newName == null) return visited;
            if (everyone) newName = KTable.Server.Scripting.EveryScript.ModulePrefix + newName;
            var body = visited.Body.ToFullString();
            var head = prelude.Length > 0 ? "  " + prelude + "\n" : "";
            if (Answered.TryGetValue(name, out var reader))
            {
                var inner = "__module_" + newName;
                var wrapped = $"do\nfunction {inner}({sig})\n{head}{body}end\nfunction {newName}({sig}) return {reader}(({inner}({sig}))) end\nend";
                return Keep(Stmt(wrapped, node), node);
            }
            var text = $"function {newName}({sig})\n{head}{body}end";
            return Keep(Stmt(text, node), node);
        }

        static readonly Dictionary<string, string> Answered = new(StringComparer.Ordinal)
        {
            ["onSave"] = "compat.save_answer", ["onsave"] = "compat.save_answer", ["tryObjectEnterContainer"] = "compat.try_answer",
        };

        (string? name, string sig, string prelude) Signature(string name, List<string> pars, bool global)
        {
            string Bind(IEnumerable<string> values) => Prelude(pars, values.ToList());
            switch (name)
            {
                case "onLoad" or "onload": return ("on_load", pars.Count > 0 ? pars[0] : "saved", "");
                case "onSave" or "onsave": return ("on_save", "", "");
                case "onUpdate": return ("on_update", "", "");
                case "onDestroy": return ("on_destroy", "", "");
                case "onDrop": return ("on_drop", "__who", Bind(new[] { "compat.color_of(__who)" }));
                case "onPickUp" or "onPickup": return ("on_pick_up", "__who", Bind(new[] { "compat.color_of(__who)" }));
                case "onObjectDrop": return ("on_drop", "__piece, __who", Bind(new[] { "compat.color_of(__who)", "__piece" }));
                case "onObjectPickUp" or "onObjectPickup": return ("on_pick_up", "__piece, __who", Bind(new[] { "compat.color_of(__who)", "__piece" }));
                case "onObjectSpawn": return ("on_spawn", "__piece", Bind(new[] { "__piece" }));
                case "onObjectDestroy": return ("on_destroy", "__piece", Bind(new[] { "__piece" }));
                case "onObjectStateChange" when global: return ("on_state", "__piece, __old, __new", Bind(new[] { "__piece", "tostring(__old)" }));
                case "onObjectStateChange": return ("on_state", "__old, __new", Bind(new[] { "self", "tostring(__old)" }));
                case "onPageChange": return ("on_page", "", "");
                case "onObjectLoopingEffect" or "onObjectTriggerEffect" when global: return ("on_effect", "__piece, __index", Bind(new[] { "__piece", "__index" }));
                case "onObjectLoopingEffect" or "onObjectTriggerEffect": return ("on_effect", "__index", Bind(new[] { "self", "__index" }));
                case "onObjectPageChange" when global: return ("on_page", "__piece", Bind(new[] { "__piece" }));
                case "onObjectPageChange": return ("on_page", "", Bind(new[] { "self" }));
                case "onPlayerTurn": return ("on_turn", "__turn", Bind(new[] { "compat.player(\"White\")", "nil" }));
                case "onPlayerTurnStart" or "onPlayerTurnEnd": return ("on_turn", "__turn", Bind(new[] { "\"White\"", "nil" }));
                case "onRotate": return ("on_rotate", "__rot, __old, __who", Bind(new[] { "__rot", "self.face_down and 180 or 0", "compat.color_of(__who)", "__old", "self.face_down and 180 or 0" }));
                case "onObjectRotate" when global:
                    return ("on_rotate", "__piece, __rot, __old, __who", Bind(new[] { "__piece", "__rot", "__piece.face_down and 180 or 0", "compat.color_of(__who)", "__old", "__piece.face_down and 180 or 0" }));
                case "onObjectRotate": return ("on_rotate", "__rot, __old, __who", Bind(new[] { "self", "__rot", "self.face_down and 180 or 0", "compat.color_of(__who)", "__old", "self.face_down and 180 or 0" }));
                case "onChat": return ("on_chat", "__text, __who", Bind(new[] { "__text", "compat.player(compat.color_of(__who))" }));
                case "onPlayerAction": return ("on_player_action", "__who, __action, __targets", Bind(new[] { "compat.player(compat.color_of(__who))", "__action", "__targets" }));
                case "onObjectEnterContainer": return ("on_stowed", "__holder, __piece", Bind(new[] { "__holder", "__piece" }));
                case "onObjectLeaveContainer": return ("on_unstowed", "__holder, __piece", Bind(new[] { "__holder", "__piece" }));
                case "tryObjectEnterContainer": return ("try_stow", "__holder, __piece", Bind(new[] { "__holder", "__piece" }));
                case "onObjectNumberTyped": return ("on_number_typed", "__piece, __who, __n", Bind(new[] { "__piece", "compat.color_of(__who)", "__n" }));
                case "onRandomize": return ("on_roll", "__who", Bind(new[] { "compat.color_of(__who)" }));
                case "onObjectRandomize" when global: return ("on_roll", "__piece, __who", Bind(new[] { "__piece", "compat.color_of(__who)" }));
                case "onObjectRandomize": return ("on_roll", "__who", Bind(new[] { "self", "compat.color_of(__who)" }));
                case "onScriptingButtonDown": return ("on_scripting_button_down", "__index, __who", Bind(new[] { "__index", "compat.color_of(__who)" }));
                case "onScriptingButtonUp": return ("on_scripting_button_up", "__index, __who", Bind(new[] { "__index", "compat.color_of(__who)" }));
            }
            if (EventNames.Contains(name))
            {
                _res.Untranslated.Add(new Untranslated(0, "event", name + " (no such event here)"));
                return (null, "", "");
            }
            if (_uiHandlers.Contains(name)) return (name, "__who, __value, __id", Bind(new[] { "compat.player(compat.color_of(__who))", "__value", "__id" }));
            if (_clickHandlers.Contains(name)) return (name, "__who, __alt, __index, __piece", Bind(new[] { "__piece or self", "compat.color_of(__who)", "__alt" }));
            if (_inputHandlers.Contains(name)) return (name, "__who, __value, __index, __piece", Bind(new[] { "__piece or self", "compat.color_of(__who)", "__value", "false" }));
            return (null, "", "");
        }

        static string Prelude(List<string> pars, List<string> values)
        {
            var names = new List<string>();
            var vals = new List<string>();
            for (int i = 0; i < pars.Count && i < values.Count; i++)
            {
                if (pars[i] == "..." || pars[i].Length == 0) continue;
                names.Add(pars[i]);
                vals.Add(values[i]);
            }
            return names.Count == 0 ? "" : $"local {string.Join(", ", names)} = {string.Join(", ", vals)}";
        }

        static StatementSyntax Stmt(string text, SyntaxNode old)
        {
            var unit = SyntaxFactory.ParseCompilationUnit(text);
            var stmt = unit.Statements.Statements.First();
            return stmt.WithLeadingTrivia(old.GetLeadingTrivia()).WithTrailingTrivia(old.GetTrailingTrivia());
        }
    }
}
