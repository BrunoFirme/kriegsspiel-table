using System.Text.Json.Serialization;
using KTable.Core.Model;

namespace KTable.Core.Protocol;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "msg")]
[JsonDerivedType(typeof(JoinRequest), "Join")]
[JsonDerivedType(typeof(JoinResult), "JoinResult")]
[JsonDerivedType(typeof(CommandMessage), "Command")]
[JsonDerivedType(typeof(Snapshot), "Snapshot")]
[JsonDerivedType(typeof(ErrorMessage), "Error")]
[JsonDerivedType(typeof(NeedAsset), "NeedAsset")]
[JsonDerivedType(typeof(AssetChunk), "AssetChunk")]
[JsonDerivedType(typeof(PeerList), "PeerList")]
[JsonDerivedType(typeof(UploadAsset), "UploadAsset")]
[JsonDerivedType(typeof(ChunkAck), "ChunkAck")]
[JsonDerivedType(typeof(GetTurns), "GetTurns")]
[JsonDerivedType(typeof(TurnList), "TurnList")]
[JsonDerivedType(typeof(GetTurn), "GetTurn")]
[JsonDerivedType(typeof(TurnState), "TurnState")]
[JsonDerivedType(typeof(Knock), "Knock")]
[JsonDerivedType(typeof(Admit), "Admit")]
[JsonDerivedType(typeof(Refuse), "Refuse")]
[JsonDerivedType(typeof(SetRole), "SetRole")]
[JsonDerivedType(typeof(Revoke), "Revoke")]
[JsonDerivedType(typeof(SetPassword), "SetPassword")]
[JsonDerivedType(typeof(RoleChanged), "RoleChanged")]
[JsonDerivedType(typeof(AccessChanged), "AccessChanged")]
[JsonDerivedType(typeof(GetSnapshot), "GetSnapshot")]
[JsonDerivedType(typeof(ScriptConsole), "ScriptConsole")]
[JsonDerivedType(typeof(RunLua), "RunLua")]
[JsonDerivedType(typeof(GetScripts), "GetScripts")]
[JsonDerivedType(typeof(ScriptList), "ScriptList")]
public abstract class Message { }

public sealed class GetSnapshot : Message { }

public sealed class GetTurns : Message { }
public sealed class TurnList : Message
{
    public List<TurnInfo> Turns { get; set; } = new();
}
public sealed class GetTurn : Message { public int Index { get; set; } }
public sealed class TurnState : Message
{
    public int Index { get; set; }
    public TurnInfo Info { get; set; } = new();
    public GameState State { get; set; } = new();
}

public static class ProtocolVersion
{
    public const int Current = 6;
}

public sealed class JoinRequest : Message
{
    public string Name { get; set; } = "";
    public string Password { get; set; } = "";
    public string? Token { get; set; }
    public string? AppVersion { get; set; }
    public int Protocol { get; set; }
}

public sealed class JoinResult : Message
{
    public bool Ok { get; set; }
    public string Error { get; set; } = "";
    public bool Waiting { get; set; }
    public long PeerId { get; set; }
    public Role Role { get; set; }
    public GameState? View { get; set; }
    public string? Token { get; set; }
    public GameAccess? Access { get; set; }
    public string? Created { get; set; }
}

public sealed class Knock : Message
{
    public long PeerId { get; set; }
    public string Name { get; set; } = "";
}
public sealed class Admit : Message
{
    public long PeerId { get; set; }
    public Role Role { get; set; } = Role.Observer;
}
public sealed class Refuse : Message { public long PeerId { get; set; } }
public sealed class SetRole : Message
{
    public long PeerId { get; set; }
    public Role Role { get; set; }
}
public sealed class Revoke : Message { public long PeerId { get; set; } }
public sealed class SetPassword : Message
{
    public string Password { get; set; } = "";
}
public sealed class RoleChanged : Message
{
    public Role Role { get; set; }
    public GameState View { get; set; } = new();
}
public sealed class AccessChanged : Message
{
    public GameAccess Access { get; set; } = new();
}

public sealed class CommandMessage : Message
{
    public Command Command { get; set; } = null!;
}

public sealed class Snapshot : Message
{
    public GameState View { get; set; } = new();
}

public sealed class ErrorMessage : Message
{
    public string Text { get; set; } = "";
}

public sealed class NeedAsset : Message
{
    public string Package { get; set; } = "";
    public string Path { get; set; } = "";
    public long Offset { get; set; }
    public bool Urgent { get; set; }
}

public abstract class FilePiece : Message
{
    public string Package { get; set; } = "";
    public string Path { get; set; } = "";
    public long Offset { get; set; }
    public long Total { get; set; }
    [JsonIgnore] public byte[] Data { get; set; } = Array.Empty<byte>();
}

public sealed class AssetChunk : FilePiece { }

public sealed class UploadAsset : FilePiece
{
    public const long MaxFileBytes = 25L * 1024 * 1024;

    public const string ReliefPackage = "sheets";

    public static bool IsRelief(string package, string path) => package == ReliefPackage && Model.ReliefFiles.IsReliefPath(path);

    public const string WebPictures = "pictures";

    public static bool IsWebPicture(string package, string path) =>
        package == ReliefPackage && System.Text.RegularExpressions.Regex.IsMatch(path, @"^pictures/[0-9a-f]{64}\.(png|jpg|webp|bmp)$");

    public const string WebModels = "models";

    public static bool IsWebModel(string package, string path) =>
        package == ReliefPackage && System.Text.RegularExpressions.Regex.IsMatch(path, @"^models/[0-9a-f]{64}\.obj$");

    public static bool PathAllowed(string package, string path) => IsRelief(package, path) || IsWebPicture(package, path) || IsWebModel(package, path) ||
        Model.PackManifest.IsUserPackId(package) &&
        (path == "pack.json" || System.Text.RegularExpressions.Regex.IsMatch(path, @"^assets/[0-9a-f]{64}\.(png|jpg|jpeg|webp|obj)$"));
}

public sealed class ChunkAck : Message
{
    public string Package { get; set; } = "";
    public string Path { get; set; } = "";
    public long Offset { get; set; }
}

public sealed class PeerList : Message
{
    public List<PeerInfo> Peers { get; set; } = new();
    public List<PeerInfo> Waiting { get; set; } = new();
}

public sealed class ScriptConsole : Message
{
    public List<ConsoleLine> Lines { get; set; } = new();
    public bool Backlog { get; set; }
}

public sealed class ConsoleLine
{
    public long Seq { get; set; }
    public string At { get; set; } = "";
    public string Kind { get; set; } = "print";
    public string Source { get; set; } = "";
    public string? Name { get; set; }
    public int Line { get; set; }
    public string Text { get; set; } = "";
}

public sealed class RunLua : Message
{
    public string Target { get; set; } = "";
    public string Code { get; set; } = "";
}

public sealed class GetScripts : Message { }

public sealed class ScriptList : Message
{
    public List<ScriptInfo> Scripts { get; set; } = new();
}

public sealed class ScriptInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Guid { get; set; }
    public string? Def { get; set; }
    public bool Own { get; set; }
    public string Status { get; set; } = "running";
    public string? LastError { get; set; }
    public int ErrorLine { get; set; }
    public int Timers { get; set; }
}
