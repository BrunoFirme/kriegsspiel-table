using KTable.Core;
using KTable.Core.Model;

namespace KTable.Converter;

public sealed class CarriedLooks(DefFactory defs, Report rep)
{
    public DefResult Of(ModuleObj o, List<ModuleObj> states, int self, DefResult r, PackOut pack, double bs, double heightScale)
    {
        var where = $"{defs.Context} {o.Name} '{o.Nickname}' {o.Guid}";
        var looks = states.Count > 1 ? states.Select(defs.ModelOfBundle).ToList() : null;
        var own = looks?.Select((l, i) => l == null ? defs.OwnLook(states[i], pack, bs, where, heightScale) : null).ToList();
        bool stated = looks != null && looks.Select((l, i) => l != null || own![i] != null).All(ok => ok);
        if (looks != null && !stated) rep.Note($"{where}: some of its states cannot be shown and its other states are left out");
        if (!stated && !states[self].Children.Any()) return r;
        var copy = Json.Deserialize<PieceDef>(Json.Serialize(r.Def));
        var from = new HashSet<string>(StringComparer.Ordinal);
        if (r.FromPack != null) from.Add(r.FromPack);
        if (stated)
        {
            copy.States = states.Select((s, i) => looks![i] is { } look ? BundleState(s, look, copy, Math.Abs(s.ScaleX) / bs, i) : own![i]!).ToList();
            from.UnionWith(looks!.OfType<(PieceDef Def, string Pack)>().Select(l => l.Pack));
            ModuleSources.Fill(copy, states[0]);
            ModuleSources.States(copy, states, (text, at) => defs.RewriteUrls(text, pack, at), where);
        }
        defs.Glued.Fill(copy, stated ? states : [states[self]], bs, 1 / r.Fit, heightScale, pack, where, Mirror.Of(o));
        if (!stated && copy.Parts is not { Count: > 0 }) return r;
        var key = $"carried|{r.Def.Id}|{DefFactory.Hash8(Json.Serialize(new PieceDef { States = copy.States, Parts = copy.Parts }))}";
        return defs.Shared.Own(r, pack, copy, key, "glued", from) with { State = stated ? self : r.State };
    }

    static PieceStateDef BundleState(ModuleObj s, (PieceDef Def, string Pack) look, PieceDef shown, double k, int i)
    {
        var size = new Size2(Math.Round(look.Def.Size.W * k, 4), Math.Round(look.Def.Size.H * k, 4));
        double thickness = Math.Round(look.Def.Thickness * k, 4);
        return new PieceStateDef
        {
            Name = s.Nickname.Length > 0 ? s.Nickname : $"State {i + 1}",
            Mesh = look.Def.Mesh, Texture = look.Def.Texture,
            Size = Math.Abs(size.W - shown.Size.W) > 0.01 * shown.Size.W || Math.Abs(size.H - shown.Size.H) > 0.01 * shown.Size.H ? size : null,
            Thickness = Math.Abs(thickness - shown.Thickness) > 0.001 ? thickness : null,
        };
    }
}
