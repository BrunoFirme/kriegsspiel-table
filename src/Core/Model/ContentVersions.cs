namespace KTable.Core.Model;

public sealed class ContentTooNewException(string message) : IOException(message);

public static class ContentVersions
{
    public static void Check(string kind, string name, int format, int currentFormat, int apiVersion)
    {
        var what = name.Length > 0 ? $"the {kind} \"{name}\"" : $"this {kind}";
        if (format > currentFormat)
            throw new ContentTooNewException($"{what} was made with a newer version of Kriegsspiel Table ({kind} format {format}, this build reads up to {currentFormat}) — update the app");
        if (apiVersion > ScriptApi.Version)
            throw new ContentTooNewException($"{what} was made with a newer version of Kriegsspiel Table (scripting API {apiVersion}, this build has up to {ScriptApi.Version}) — update the app");
    }
}
