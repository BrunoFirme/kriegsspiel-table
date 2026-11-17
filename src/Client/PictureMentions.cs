using System.Text;
using System.Text.RegularExpressions;
using KTable.Core;

namespace KTable.Client;

public static class PictureMentions
{
    static readonly Regex HashText = new("[0-9a-fA-F]{64}", RegexOptions.Compiled);

    public static void In(string? text, HashSet<string> found)
    {
        if (string.IsNullOrEmpty(text)) return;
        foreach (Match m in HashText.Matches(text)) found.Add(m.Value.ToLowerInvariant());
    }

    public static void InFile(string file, string packId, HashSet<string> found)
    {
        if (!File.Exists(file)) return;
        var text = Encoding.UTF8.GetString(Json.Unpacked(File.ReadAllBytes(file)));
        if (text.Contains(packId, StringComparison.Ordinal)) In(text, found);
    }
}
