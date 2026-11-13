namespace KTable.Core.Model;

public static class PlaceNames
{
    public const int MostLetters = 60;

    public static string? Tidy(string? name) => name?.Trim() is { Length: > 0 } text ? text[..Math.Min(text.Length, MostLetters)] : null;
}
