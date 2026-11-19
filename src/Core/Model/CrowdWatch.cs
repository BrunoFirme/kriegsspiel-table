namespace KTable.Core.Model;

public sealed record CrowdOwner(string Id, string Name, bool Made, int People, int Out);

public sealed record CrowdSeen(string Id, string Name, string Calling, string Sex, string Band, string Wealth, string Owner, string? Place, Vec2 At, double Heading, bool Staying);

public interface ICrowdWatch
{
    IReadOnlyList<CrowdOwner> Owners();
    IReadOnlyList<CrowdSeen> OutNow(string? owner);
    CrowdSeen? Find(string id);
}
