namespace KTable.Client;

public sealed class ImportPlan
{
    public List<string> Packs { get; } = new();
    public List<string> Scenarios { get; } = new();
    public string? Scenario => Scenarios.FirstOrDefault();
    public string? File { get; private set; }

    public bool Read(string line)
    {
        if (line.StartsWith("PLAN pack ")) Packs.Add(line[10..].Trim());
        else if (line.StartsWith("PLAN scenario ")) Scenarios.Add(line[14..].Trim());
        else if (line.StartsWith("PLAN file ")) File = line[10..].Trim();
        else if (!line.StartsWith("PLAN ") && !line.StartsWith("Output") && !line.StartsWith("Cache")) return true;
        return false;
    }
}
