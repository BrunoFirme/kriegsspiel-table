using System;
using System.Globalization;
using System.Linq;

namespace KTable.Client;

public static class TextSearch
{
    const CompareOptions Loose = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
    static readonly CompareInfo Compare = CultureInfo.InvariantCulture.CompareInfo;

    public static bool Matches(string query, params string?[] fields) =>
        query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .All(word => fields.Any(f => f != null && Compare.IndexOf(f, word, Loose) >= 0));
}
