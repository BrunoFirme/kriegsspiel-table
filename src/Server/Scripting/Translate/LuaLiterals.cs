using System.Globalization;
using System.Text.Json.Nodes;
using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Lua.Syntax;

namespace KTable.Server.Scripting.Translate;

static class LuaLiterals
{
    public static JsonNode? Json(ExpressionSyntax e) => e switch
    {
        TableConstructorExpressionSyntax t => Table(t),
        LiteralExpressionSyntax l when l.IsKind(SyntaxKind.StringLiteralExpression) => JsonValue.Create(l.Token.ValueText),
        LiteralExpressionSyntax l when l.IsKind(SyntaxKind.TrueLiteralExpression) => JsonValue.Create(true),
        LiteralExpressionSyntax l when l.IsKind(SyntaxKind.FalseLiteralExpression) => JsonValue.Create(false),
        LiteralExpressionSyntax l when double.TryParse(l.ToString().Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) => JsonValue.Create(n),
        UnaryExpressionSyntax u when u.IsKind(SyntaxKind.UnaryMinusExpression) => Negated(u.Operand),
        _ => null,
    };

    static JsonNode? Negated(ExpressionSyntax e) => Json(e) is JsonValue v && v.TryGetValue<double>(out var n) ? JsonValue.Create(-n) : null;

    static JsonNode? Table(TableConstructorExpressionSyntax t)
    {
        if (t.Fields.Count > 0 && t.Fields.All(f => f is UnkeyedTableFieldSyntax))
        {
            var items = t.Fields.Cast<UnkeyedTableFieldSyntax>().Select(f => Json(f.Value)).ToList();
            return items.Any(i => i == null) ? null : new JsonArray(items.ToArray());
        }
        var o = new JsonObject();
        foreach (var f in t.Fields)
        {
            switch (f)
            {
                case IdentifierKeyedTableFieldSyntax k when Json(k.Value) is { } v:
                    o[k.Identifier.Text] = v;
                    break;
                case ExpressionKeyedTableFieldSyntax k when k.Key is LiteralExpressionSyntax key && Json(k.Value) is { } v:
                    o[key.Token.ValueText] = v;
                    break;
                default:
                    return null;
            }
        }
        return o;
    }
}
