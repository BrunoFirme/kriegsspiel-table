namespace KTable.Core.Model;

public sealed class PlayerRights
{
    public bool Fly { get; set; }
    public bool Take { get; set; } = true;
    public bool View { get; set; } = true;
    public bool Dice { get; set; } = true;
    public bool Measure { get; set; } = true;
    public bool Ping { get; set; } = true;

    public static PlayerRights All => new() { Fly = true };

    public PlayerRights Clone() => (PlayerRights)MemberwiseClone();
}
