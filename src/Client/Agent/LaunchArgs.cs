namespace KTable.Client.Agent;

public static class LaunchArgs
{
    const string Flag = "--", UserArgs = "--", UserArgsToo = "++", Password = "--password", PasswordIs = "--password=";

    public static List<string> Recorded(IEnumerable<string> commandLine)
    {
        var kept = new List<string>();
        bool value = false;
        foreach (var a in commandLine.TakeWhile(a => a is not (UserArgs or UserArgsToo)))
        {
            bool secret = value && !a.StartsWith(Flag, StringComparison.Ordinal);
            value = a == Password;
            if (!secret && !value && !a.StartsWith(PasswordIs, StringComparison.Ordinal)) kept.Add(a);
        }
        return kept;
    }
}
