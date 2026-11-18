namespace KTable.Core.Model;

public sealed class PlayerRights
{
    public bool Take { get; set; } = true;
    public bool Dice { get; set; } = true;
    public bool Measure { get; set; } = true;
    public bool Ping { get; set; } = true;
    public bool Write { get; set; } = true;
    public bool Speak { get; set; } = true;
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] public bool Dress { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] public string? DressAs { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] public string? DressWealth { get; set; }

    public PlayerRights Clone() => (PlayerRights)MemberwiseClone();
}
