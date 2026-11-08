using KTable.Core;
using KTable.Core.Model;

namespace KTable.Converter;

public sealed class CarriedLooks(DefFactory defs, Report rep)
{
    public DefResult Of(ModuleObj o, List<ModuleObj> states, int self, DefResult r, PackOut pack, double bs, double heightScale)
    {
        var where = $"{defs.Context} {o.Name} '{o.Nickname}' {o.Guid}";
        var looks = states.Count > 1 ? states.Select(defs.ModelOfBundle).ToList() : null;
        bool stated = looks != null && looks.All(l => l != null);
        if (looks != null && !stated) rep.Note($"{where}: its other states are not all asset-bundle figures and are left out");
        if (!stated && !states[0].Children.Any()) return r;
        var copy = Json.Deserialize<PieceDef>(Json.Serialize(r.Def));
        var from = new HashSet<string>(StringComparer.Ordinal);
        if (r.FromPack != null) from.Add(r.FromPack);
        if (stated)
        {
            copy.States = states.Select((s, i) => BundleState(s, looks![i]!.Value, copy, Math.Abs(s.ScaleX) / bs, i)).ToList();
            from.UnionWith(looks!.Select(l => l!.Value.Pack));
            ModuleSources.Fill(copy, states[0]);
            ModuleSources.States(copy, states);
        }
        defs.Glued.Fill(copy, stated ? states : [states[0]], bs, 1 / r.Fit, heightScale, pack, where, Mirror.Of(o));
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
