namespace KTable.Core.Model;

public sealed class PlayerRights
{
    public bool Take { get; set; } = true;
    public bool Dice { get; set; } = true;
    public bool Measure { get; set; } = true;
    public bool Ping { get; set; } = true;
    public bool Write { get; set; } = true;
    public bool Speak { get; set; } = true;

    public PlayerRights Clone() => (PlayerRights)MemberwiseClone();
}
