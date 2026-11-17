using System.Text.Json.Serialization;

namespace KTable.Core.Model;

public sealed class PlacePin
{
    public const int MostNameLength = 80, MostPins = 500, ViewLength = 6;
    public const string IdPrefix = "pl";

    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Sheet { get; set; }
    public Vec2 Pos { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public float[]? View { get; set; }

    public PlacePin Clone() => new() { Id = Id, Name = Name, Sheet = Sheet, Pos = Pos, View = View?.ToArray() };
}
