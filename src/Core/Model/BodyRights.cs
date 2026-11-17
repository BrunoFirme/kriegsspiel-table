namespace KTable.Core.Model;

public sealed class BodyRights
{
    public bool Fly { get; set; }
    public bool View { get; set; } = true;
    public bool Walk { get; set; } = true;

    [System.Text.Json.Serialization.JsonIgnore] public bool Usual => !Fly && View && Walk;

    public BodyRights Clone() => (BodyRights)MemberwiseClone();
}
