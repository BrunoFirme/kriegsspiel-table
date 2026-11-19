using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;
using MoonSharp.Interpreter;

namespace KTable.Server.Scripting;

internal sealed class CrowdScripts(ScriptHost host)
{
    public void LoadAll()
    {
        foreach (var owner in host.Server.State.Crowds?.Scripts?.Keys.ToList() ?? [])
            Touch(owner, force: true);
    }

    public void After(Command c)
    {
        foreach (var id in c.ScriptsToReload().Where(CrowdScript.Is).Distinct().ToList())
            Touch(CrowdScript.OwnerOf(id), force: c is SetCrowdScript);
    }

    public bool Touch(string owner, bool force)
    {
        string id = CrowdScript.Of(owner);
        var st = host.Server.State;
        var code = CrowdScript.Owns(st, owner) ? st.Crowds?.Scripts?.GetValueOrDefault(owner) : null;
        if (!force && code != null && host.PieceScripts.ContainsKey(id)) return true;
        host.Guard(id, () => Load(id, owner, code));
        return true;
    }

    void Load(string id, string owner, CrowdScript? code)
    {
        host.UnloadScript(id);
        host._feed.Forget(id);
        if (host.Off || code == null || string.IsNullOrWhiteSpace(code.Script)) return;
        var verdict = ScriptSafety.Inspect(code.Script);
        if (verdict.Hostile)
        {
            host.Error(id, "script refused: " + string.Join("; ", verdict.Reasons));
            return;
        }
        var ps = new PieceScript { Id = id, Env = host.NewEnv(), Source = verdict.Script };
        ps.Env["self"] = new ScriptCrowds(host).Self(owner);
        host.UnderLoadBudget(() =>
        {
            if (!host.Compile(ps, null)) return;
            ps.HasUpdate = ps.Env.Get("on_update").Type == DataType.Function;
            host._pieces[id] = ps;
            host.OnLoad(ps, code.State ?? "");
        });
    }

    public void Save(PieceScript ps)
    {
        if (!host.PieceScripts.TryGetValue(ps.Id, out var current) || current != ps) return;
        string owner = CrowdScript.OwnerOf(ps.Id);
        host.Guard(ps.Id, () =>
        {
            var r = host.Call(ps, "on_save");
            var kept = host.Server.State.Crowds?.Scripts?.GetValueOrDefault(owner);
            if (r is { Type: DataType.String } said && kept != null && said.String != (kept.State ?? "")) host.Emit(new SetCrowdScriptState { Id = owner, State = said.String });
        });
    }
}
