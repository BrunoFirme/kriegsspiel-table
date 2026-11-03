using System.Collections.Concurrent;
using MoonSharp.Interpreter;
using static KTable.Server.Scripting.LuaArgs;

namespace KTable.Server.Scripting;

public sealed partial class ScriptHost
{
    public Func<string, Task<string?>>? FetchPicture { get; set; }

    sealed record Fetched(PieceScript Owner, DynValue Fn, string? Picture, DynValue Who);

    readonly ConcurrentQueue<Fetched> _fetched = new();

    DynValue FetchPictureCall(CallbackArguments a)
    {
        var owner = _current ?? throw new ScriptRuntimeException("game.fetch_picture: not inside a script call");
        var link = Arg(a, 0).CastToString() ?? "";
        var fn = Arg(a, 1);
        if (fn.Type != DataType.Function) throw new ScriptRuntimeException("game.fetch_picture: the second argument must be a function");
        Fetch(owner, fn, link, DynValue.Nil);
        return DynValue.Nil;
    }

    void Fetch(PieceScript owner, DynValue fn, string link, DynValue who)
    {
        var fetch = FetchPicture is { } f && IsWebLink(link) ? f(link.Trim()) : Task.FromResult<string?>(null);
        fetch.ContinueWith(t => _fetched.Enqueue(new Fetched(owner, fn, t.IsCompletedSuccessfully ? t.Result : null, who)), TaskContinuationOptions.ExecuteSynchronously);
    }

    public static bool IsWebLink(string text) =>
        Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";

    void DeliverPictures()
    {
        while (_fetched.TryDequeue(out var f))
            Guard(f.Owner.Id, () =>
            {
                if (f.Picture == null && f.Who.Type == DataType.Table && f.Who.Table.Get("id").Type == DataType.Number)
                    Emit(new Core.Protocol.Say { Text = "No picture came from that link.", To = $"p:{(long)f.Who.Table.Get("id").Number}", Color = "#f87" });
                if (f.Owner.Enabled && IsCurrent(f.Owner)) Invoke(f.Owner, f.Fn, f.Picture == null ? DynValue.Nil : DynValue.NewString(f.Picture), f.Who);
            });
    }
}
