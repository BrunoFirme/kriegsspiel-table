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
    public Func<string, string?>? ResolveAsset;
    public bool IsGlobal;
}

public static class TtsTranslator
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
        "addAttachment","addContextMenuItem","addDecal","addForce","addTag","addTorque","addToPlayerSelection","attachHider","attachInvisibleHider","call","clearButtons","clearContextMenu","clearInputs","clone","createButton","createInput","cut","deal","destruct","destroy","drop","editButton","editInput","flip","getAngularVelocity","getAttachments","getBounds","getBoundsNormalized","getButtons","getColorTint","getComponent","getComponents","getCustomObject","getData","getDecals","getDescription","getFogOfWarReveal","getGMNotes","getGUID","getInputs","getJoints","getJSON","getLock","getLuaScript","getMemo","getName","getObjects","getPosition","getPositionSmooth","getQuantity","getRotation","getRotationSmooth","getRotationValue","getRotationValues","getScale","getSelectingPlayers","getSnapPoints","getStateId","getStates","getTable","getTags","getTransformForward","getTransformRight","getTransformUp","getValue","getVar","getVectorLines","getVelocity","getZones","hasTag","highlightOff","highlightOn","isDestroyed","isSmoothMoving","jointTo","positionToLocal","positionToWorld","putObject","randomize","registerCollisions","reload","removeAttachment","removeAttachments","removeButton","removeFromPlayerSelection","removeInput","removeTag","reset","resetAttachments","rotate","scale","setAngularVelocity","setColorTint","setCustomObject","setDecals","setDescription","setFogOfWarReveal","setGMNotes","setHiddenFrom","setInvisibleTo","setLock","setLuaScript","setMemo","setName","setPosition","setPositionSmooth","setRotation","setRotationSmooth","setRotationValue","setRotationValues","setScale","setSnapPoints","setState","setTable","setTags","setValue","setVar","setVectorLines","setVelocity","shuffle","shuffleStates","split","spread","takeObject","translate","unregisterCollisions","getLuaScriptState","setLuaScriptState",
    };

    static readonly HashSet<string> ObjectProps = new(StringComparer.Ordinal)
    {
        "interactable","tooltip","sticky","grid_projection","use_gravity","use_grid","use_snap_points","use_hands","use_rotation_value_flip","auto_raise","bounciness","drag","drag_selectable","angular_drag","hide_when_face_down","ignore_fog_of_war","loading_custom","mass","max_typed_number","measure_movement","script_state","spawning","static_friction","dynamic_friction","value","alt_view_angle","held_flip_index","held_position_offset","held_reduce_force","held_rotation_offset","memo","AssetBundle","type","tag","name","guid","held_by_color","resting","is_face_down",
    };

    static readonly HashSet<string> EventNames = new(StringComparer.Ordinal)
    {
        "onLoad","onload","onSave","onsave","onUpdate","onDrop","onPickUp","onPickup","onDestroy","onCollisionEnter","onCollisionExit","onCollisionStay","onObjectDrop","onObjectPickUp","onObjectPickup","onObjectSpawn","onObjectDestroy","onObjectEnterContainer","onObjectLeaveContainer","onObjectEnterScriptingZone","onObjectLeaveScriptingZone","onPlayerTurn","onPlayerChangeColor","onPlayerConnect","onPlayerDisconnect","onChat","onScriptingButtonDown","onScriptingButtonUp","onObjectStateChange","onObjectRandomize","onPlayerAction","onObjectNumberTyped","onObjectSearchStart","onObjectSearchEnd","onObjectRotate","onRotate","onObjectHover","onHover","onBlindfold","onFixedUpdate","onPlayerPing","onObjectLoopingEffect","onObjectTriggerEffect","onObjectPeek","onObjectFlick","onObjectCollisionEnter","onObjectCollisionExit","onObjectCollisionStay","onObjectPageChange","onPageChange","onZoneGroupSort","onFilter","onExternalMessage",
    };

    static readonly Dictionary<string, string> EventRenames = new(StringComparer.Ordinal)
    {
        ["onLoad"] = "on_load", ["onload"] = "on_load", ["onSave"] = "on_save", ["onsave"] = "on_save", ["onUpdate"] = "on_update", ["onDestroy"] = "on_destroy",
        ["onDrop"] = "on_drop", ["onPickUp"] = "on_pick_up", ["onPickup"] = "on_pick_up", ["onObjectDrop"] = "on_drop", ["onObjectPickUp"] = "on_pick_up", ["onObjectPickup"] = "on_pick_up",
        ["onObjectSpawn"] = "on_spawn", ["onObjectDestroy"] = "on_destroy", ["onObjectStateChange"] = "on_state", ["onPlayerTurn"] = "on_turn", ["onRotate"] = "on_rotate", ["onObjectRotate"] = "on_rotate",
        ["onChat"] = "on_chat", ["onObjectNumberTyped"] = "on_number_typed", ["onScriptingButtonDown"] = "on_scripting_button_down", ["onScriptingButtonUp"] = "on_scripting_button_up",
    };

    static readonly HashSet<string> Stubbed = new(StringComparer.Ordinal)
    {
        "setHiddenFrom", "setInvisibleTo", "attachHider", "attachInvisibleHider", "setFogOfWarReveal", "registerCollisions", "unregisterCollisions", "jointTo",
        "addForce", "addTorque", "setVelocity", "setAngularVelocity", "drop", "cut", "deal", "shuffle", "shuffleStates", "randomize", "reset", "resetAttachments",
        "split", "spread", "setRotationValue", "setRotationValues", "setValue", "getComponent", "getComponents",
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
        readonly Dictionary<string, (string type, double scale)> _spawnShapes = new();

        public Rewriter(TranslateResult res, TranslateContext ctx, SyntaxNode root)
        {
            _res = res;
            _ctx = ctx;
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
            foreach (var asg in root.DescendantNodes().OfType<AssignmentStatementSyntax>())
            {
                if (asg.Variables.Count != 1 || asg.EqualsValues.Values.Count != 1) continue;
                if (asg.EqualsValues.Values[0] is FunctionCallExpressionSyntax sc && sc.Expression is IdentifierNameSyntax id && id.ToString().Trim() == "spawnObject")
                    _spawnShapes[asg.Variables[0].ToString().Trim()] = SpawnShape(sc);
            }
            foreach (var loc in root.DescendantNodes().OfType<LocalVariableDeclarationStatementSyntax>())
            {
                if (loc.Names.Count != 1 || loc.EqualsValues == null || loc.EqualsValues.Values.Count != 1) continue;
                if (loc.EqualsValues.Values[0] is FunctionCallExpressionSyntax sc && sc.Expression is IdentifierNameSyntax id && id.ToString().Trim() == "spawnObject")
                    _spawnShapes[loc.Names[0].ToString().Trim()] = SpawnShape(sc);
            }
        }

        static (string type, double scale) SpawnShape(FunctionCallExpressionSyntax spawn)
        {
            string type = "Custom_Token";
            double scale = 1;
            if (Args(spawn).FirstOrDefault() is TableConstructorExpressionSyntax t)
            {
                foreach (var f in t.Fields.OfType<IdentifierKeyedTableFieldSyntax>())
                {
                    if (f.Identifier.Text == "type" && f.Value is LiteralExpressionSyntax l) type = l.Token.ValueText;
                    if (f.Identifier.Text == "scale" && f.Value is TableConstructorExpressionSyntax st)
                    {
                        foreach (var sf in st.Fields)
                        {
                            string? text = sf switch { IdentifierKeyedTableFieldSyntax k when k.Identifier.Text == "x" => k.Value.ToString(), UnkeyedTableFieldSyntax u => u.Value.ToString(), _ => null };
                            if (text != null && double.TryParse(text.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture,
                                out var v)) { scale = v; break; }
                        }
                    }
                }
            }
            return (type, scale);
        }

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
            var recv = ma.Expression.ToString().Trim();
            var name = ma.MemberName.Text;
            if (GlobalObjectCall(visited, recv, name, a, args, node) is { } global) return global;
            if (recv.EndsWith(".UI"))
            {
                var obj = recv[..^3];
                var mapped = name switch
                {
                    "setXml" or "setXmlTable" => "set", "getXml" => "xml", "getXmlTable" => "tree", "setAttribute" => "set_attribute", "setAttributes" => "set_attributes",
                    "getAttribute" => "get_attribute", "show" => "show", "hide" => "hide", "setValue" => "set_value", "getValue" => "get_value", _ => null,
                };
                return mapped != null ? Direct($"{obj}.ui.{mapped}({All()})", node) : Todo("object UI." + name, $"compat.todo({obj}, \"UI.{name}\")", node);
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
                        "frames" => Direct($"wait.frames({A(1)}, {A(0)})", node),
                        "condition" => Compat($"compat.wait_condition({All()})", node),
                        "stop" => Compat($"compat.wait_stop({All()})", node),
                        "stopAll" => Todo("Wait.stopAll", "compat.todo(nil, \"Wait.stopAll\")", node),
                        _ => Todo("Wait." + name, $"compat.todo(nil, \"Wait.{name}\")", node),
                    };
                case "Global":
                    return name switch
                    {
                        "call" => Direct($"game.call({All()})", node),
                        "getVar" or "getTable" => Direct($"game.get_var({All()})", node),
                        "setVar" or "setTable" => Direct($"game.set_var({All()})", node),
                        _ => Todo("Global." + name, $"compat.todo(nil, \"Global.{name}\")", node),
                    };
                case "Player":
                    return name switch
                    {
                        "getPlayers" => Compat("compat.Player.getPlayers()", node),
                        _ => Compat($"compat.Player.{name}({All()})", node),
                    };
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
                        _ => Todo("UI." + name, $"compat.todo(nil, \"UI.{name}\")", node),
                    };
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
            switch (name)
            {
                case "getObjectFromGUID": return Direct($"board.find({All()})", node);
                case "getAllObjects" or "getObjects": return Direct("board.pieces()", node);
                case "getObjectsWithTag": return Direct($"board.pieces{{ tag = {A(0)} }}", node);
                case "getObjectsWithAnyTags": return Compat($"compat.pieces_with_any_tags({All()})", node);
                case "getObjectsWithAllTags": return Compat($"compat.pieces_with_all_tags({All()})", node);
                case "spawnObject": return Compat($"compat.spawn_object({All()})", node);
                case "spawnObjectJSON": return Compat($"compat.spawn_object_json({All()})", node);
                case "spawnObjectData": return Compat($"compat.spawn_object_data({All()})", node);
                case "broadcastToAll" or "printToAll": return Direct($"game.say({A(0)}, {{ color = compat.color({A(1)}) }})", node);
                case "broadcastToColor" or "printToColor": return Compat($"compat.broadcast_to_color({All()})", node);
                case "startLuaCoroutine": return Compat($"compat.start_coroutine({All()})", node);
                case "getSeatedPlayers": return Compat("compat.seated_players()", node);
                case "logString": return Compat($"compat.log_string({All()})", node);
                case "stringColorToRGB": return Compat($"compat.string_color_to_rgb({All()})", node);
                case "Vector": return Compat($"compat.Vector({All()})", node);
                case "Color": return Compat($"compat.Color({All()})", node);
                case "destroyObject": return Compat($"compat.piece_of({A(0)}).delete()", node);
                case "addHotkey": return Compat($"compat.add_hotkey(_ENV, {All()})", node);
                case "clearHotkeys": return Direct("game.hotkeys_clear()", node);
                case "addContextMenuItem": return Compat($"compat.add_global_context_menu_item(_ENV, {All()})", node);
                case "clearContextMenu": return Direct("game.menu_clear()", node);
                case "logStyle" or "sendExternalMessage" or "setLookingForPlayers" or "flipTable" or "group" or "paste" or "copy" or "clearPixelPaint" or "clearVectorPaint":
                    return Todo(name, $"compat.todo(nil, \"{name}\")", node);
            }
            return null;
        }

        ExpressionSyntax? ObjectCall(string recv, string name, List<string> a, List<ExpressionSyntax> args, SyntaxNode node)
        {
            string A(int i) => i < a.Count ? a[i] : "nil";
            string All() => ArgText(args);
            string Rest() => a.Count == 0 ? "" : ", " + All();
            switch (name)
            {
                case "setName": return Direct($"{recv}.set_name({All()})", node);
                case "getName": return Compat($"compat.name({recv})", node);
                case "setDescription": return Direct($"{recv}.set_note({All()})", node);
                case "getDescription": return Compat($"compat.description({recv})", node);
                case "getGUID": return Compat($"compat.guid({recv})", node);
                case "getStateId": return Direct($"{recv}.state", node);
                case "setState": return Direct($"{recv}.set_state({All()})", node);
                case "getStates": return Compat($"compat.states({recv})", node);
                case "flip": return Direct($"{recv}.flip()", node);
                case "destruct" or "destroy": return Direct($"{recv}.delete()", node);
                case "call": return Direct($"{recv}.call({All()})", node);
                case "getVar" or "getTable": return Direct($"{recv}.get_var({All()})", node);
                case "setVar" or "setTable": return Direct($"{recv}.set_var({All()})", node);
                case "setColorTint": return Direct($"{recv}.set_tint(compat.color({A(0)}))", node);
                case "getColorTint": return Compat($"compat.color_tint({recv})", node);
                case "getPosition" or "getPositionSmooth": return Compat($"compat.position({recv})", node);
                case "setPosition" or "setPositionSmooth": return Compat($"compat.set_position({recv}, {A(0)})", node);
                case "getRotation" or "getRotationSmooth": return Compat($"compat.rotation({recv})", node);
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
                case "getBounds" or "getBoundsNormalized": return Compat($"compat.get_bounds({recv})", node);
                case "createButton": return Compat(Widgets($"compat.create_button({recv}, {A(0)}, _G)"), node);
                case "createInput": return Compat(Widgets($"compat.create_input({recv}, {A(0)}, _G)"), node);
                case "editButton": return Compat(Widgets($"compat.edit_button({recv}, {A(0)}, _G)"), node);
                case "editInput": return Compat(Widgets($"compat.edit_input({recv}, {A(0)}, _G)"), node);
                case "removeButton": return Compat($"compat.remove_button({recv}, {A(0)})", node);
                case "removeInput": return Compat($"compat.remove_input({recv}, {A(0)})", node);
                case "clearButtons": return Compat($"compat.clear_buttons({recv})", node);
                case "clearInputs": return Compat($"compat.clear_inputs({recv})", node);
                case "getButtons": return Compat($"compat.get_buttons({recv})", node);
                case "getInputs": return Compat($"compat.get_inputs({recv})", node);
                case "addContextMenuItem": return Compat($"compat.add_context_menu_item(_ENV, {recv}, {All()})", node);
                case "clearContextMenu": return Direct($"{recv}.menu_clear()", node);
                case "reload": return Direct($"{recv}.reload()", node);
                case "setLuaScript": return Direct($"{recv}.set_script({All()})", node);
                case "getLuaScript": return Compat($"compat.get_script({recv})", node);
                case "getTags": return Direct($"{recv}.tags", node);
                case "hasTag": return Compat($"compat.has_tag({recv}, {A(0)})", node);
                case "addTag": return Direct($"{recv}.add_tag({A(0)})", node);
                case "removeTag": return Direct($"{recv}.remove_tag({A(0)})", node);
                case "setTags": return Direct($"{recv}.set_tags({A(0)})", node);
                case "setLock": return Compat($"compat.set_lock({recv}, {A(0)})", node);
                case "getLock": return Compat($"compat.get_lock({recv})", node);
                case "setMemo": return Direct($"{recv}.set_memo({A(0)})", node);
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
                case var stubbed when Stubbed.Contains(stubbed):
                    return Todo(name, $"compat.todo({recv}, \"{name}\")", node);
                case "getVelocity" or "getAngularVelocity": return Compat("compat.Vector(0, 0, 0)", node);
                case "getRotationValue" or "getRotationValues" or "getValue": return Compat("nil", node);
                case "getJoints" or "getZones": return Compat("{}", node);
                case "isSmoothMoving": return Compat("false", node);
                case "getFogOfWarReveal": return Compat("{ reveal = false, color = \"\", range = 0 }", node);
                case "getLuaScriptState" or "setLuaScriptState": return Todo(name, $"compat.todo({recv}, \"{name}\")", node);
            }
            return null;
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
                case "Time" or "Lighting" or "Grid" or "Hands" or "Notes" or "MusicPlayer" or "Backgrounds": return Compat($"compat.{recv}.{name}", node);
                case "JSON": return Direct($"json.{name}", node);
            }
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
                case "interactable" or "tooltip" or "sticky" or "use_gravity" or "grid_projection" or "auto_raise" or "use_snap_points" or "use_grid" or "hide_when_face_down":
                    if (visited.Parent is AssignmentStatementSyntax) return visited;
                    return Compat($"compat.get_prop({recv}, \"{name}\", {DefaultProp(name)})", node);
            }
            return visited;
        }

        static string DefaultProp(string name) => name switch { "interactable" or "use_gravity" or "auto_raise" => "true", "memo" or "tooltip" => "\"\"", _ => "false" };

        public override SyntaxNode? VisitAssignmentStatement(AssignmentStatementSyntax node)
        {
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
            return base.VisitIdentifierName(node);
        }

        public override SyntaxNode? VisitLiteralExpression(LiteralExpressionSyntax node)
        {
            if (node.IsKind(SyntaxKind.StringLiteralExpression) && _ctx.ResolveAsset != null && node.Token.ValueText is { } url
                && url.StartsWith("http", StringComparison.OrdinalIgnoreCase) && url.Contains("://")
                && node.Parent is not IdentifierKeyedTableFieldSyntax { Identifier.Text: "image" or "diffuse" }
                && _ctx.ResolveAsset(url) is { } asset)
            {
                _res.DirectCalls++;
                return Keep(SyntaxFactory.ParseExpression("\"" + asset + "\""), node);
            }
            return base.VisitLiteralExpression(node);
        }

        public override SyntaxNode? VisitIdentifierKeyedTableField(IdentifierKeyedTableFieldSyntax node)
        {
            var visited = (IdentifierKeyedTableFieldSyntax)base.VisitIdentifierKeyedTableField(node)!;
            if (node.Identifier.Text is "image" or "diffuse" && node.Value is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.StringLiteralExpression) && _ctx.ResolveImage != null)
            {
                var url = lit.Token.ValueText;
                if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    var (type, scale) = ShapeFor(node);
                    var def = _ctx.ResolveImage(url, type, scale);
                    if (def != null)
                        return visited.WithValue(Keep(SyntaxFactory.ParseExpression("\"" + def + "\""), node.Value));
                    _res.Untranslated.Add(new Untranslated(Line(node), "picture", url));
                }
            }
            return visited;
        }

        (string type, double scale) ShapeFor(SyntaxNode node)
        {
            var fn = node.Ancestors().FirstOrDefault(n => n is FunctionDeclarationStatementSyntax or LocalFunctionDeclarationStatementSyntax or AnonymousFunctionExpressionSyntax);
            if (fn == null) return ("Custom_Token", 1);
            foreach (var call in fn.DescendantNodes().OfType<FunctionCallExpressionSyntax>())
                if (call.Expression is MemberAccessExpressionSyntax ma && ma.MemberName.Text == "setCustomObject" && _spawnShapes.TryGetValue(ma.Expression.ToString().Trim(), out var shape))
                    return shape;
            foreach (var call in fn.DescendantNodes().OfType<FunctionCallExpressionSyntax>())
                if (call.Expression is IdentifierNameSyntax id && id.ToString().Trim() == "spawnObject") return SpawnShape(call);
            return ("Custom_Token", 1);
        }

        public override SyntaxNode? VisitFunctionDeclarationStatement(FunctionDeclarationStatementSyntax node)
        {
            var visited = (FunctionDeclarationStatementSyntax)base.VisitFunctionDeclarationStatement(node)!;
            if (visited.Name is not SimpleFunctionNameSyntax simple) return visited;
            var name = simple.ToString().Trim();
            var pars = visited.Parameters.Parameters.Select(p => p.ToString().Trim()).ToList();
            var (newName, sig, prelude) = Signature(name, pars);
            if (newName == null) return visited;
            var body = visited.Body.ToFullString();
            var text = $"function {newName}({sig})\n{(prelude.Length > 0 ? "  " + prelude + "\n" : "")}{body}end";
            return Keep(Stmt(text, node), node);
        }

        (string? name, string sig, string prelude) Signature(string name, List<string> pars)
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
                case "onObjectStateChange": return ("on_state", "__old, __new", Bind(new[] { "self", "tostring(__old)" }));
                case "onPlayerTurn": return ("on_turn", "__turn", Bind(new[] { "compat.player(\"White\")", "nil" }));
                case "onRotate": return ("on_rotate", "__rot, __old, __who", Bind(new[] { "__rot", "self.face_down and 180 or 0", "compat.color_of(__who)", "__old", "self.face_down and 180 or 0" }));
                case "onObjectRotate" when _ctx.IsGlobal:
                    return ("on_rotate", "__piece, __rot, __old, __who", Bind(new[] { "__piece", "__rot", "__piece.face_down and 180 or 0", "compat.color_of(__who)", "__old", "__piece.face_down and 180 or 0" }));
                case "onObjectRotate": return ("on_rotate", "__rot, __old, __who", Bind(new[] { "self", "__rot", "self.face_down and 180 or 0", "compat.color_of(__who)", "__old", "self.face_down and 180 or 0" }));
                case "onChat": return ("on_chat", "__text, __who", Bind(new[] { "__text", "compat.player(compat.color_of(__who))" }));
                case "onObjectNumberTyped": return ("on_number_typed", "__piece, __who, __n", Bind(new[] { "__piece", "compat.color_of(__who)", "__n" }));
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
