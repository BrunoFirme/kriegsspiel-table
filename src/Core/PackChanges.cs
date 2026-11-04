
namespace KTable.Core;

public enum PackChange
{
    None,
    AsConverted,
    Changed,
    Unknown,
}

public static class PackChanges
{
    public const string SealFile = "converted.sha256";

    public static void Seal(string packDir) =>
        Json.WriteAtomic(Path.Combine(packDir, SealFile), FileHashes.Sha256(Path.Combine(packDir, "pack.json")));

    public static (PackChange change, string why) Check(string packDir)
    {
        if (!Directory.Exists(packDir)) return (PackChange.None, "not here");
        var art = Path.Combine(packDir, "art");
        if (Directory.Exists(art) && Directory.EnumerateFiles(art, "*.json").Any())
            return (PackChange.Changed, "the piece studio keeps documents of faces painted or models built in it");
        var manifest = Path.Combine(packDir, "pack.json");
        if (!File.Exists(manifest)) return (PackChange.AsConverted, "it has no pack.json, so the piece studio cannot have opened it");
        var seal = Path.Combine(packDir, SealFile);
        if (File.Exists(seal))
        {
            string sealedHash, nowHash;
            try { sealedHash = File.ReadAllText(seal).Trim(); nowHash = FileHashes.Sha256(manifest); }
            catch (Exception e) { return (PackChange.Unknown, $"what the converter wrote could not be compared with what is there ({e.Message})"); }
            return string.Equals(sealedHash, nowHash, StringComparison.OrdinalIgnoreCase)
                ? (PackChange.AsConverted, "pack.json is as the converter wrote it")
                : (PackChange.Changed, "pack.json has been rewritten since the converter wrote it");
        }
        return WrittenCompact(manifest)
            ? (PackChange.Changed, "pack.json was last written by the piece studio")
            : (PackChange.Unknown, "it was converted before the app kept a record of what the converter wrote");
    }

    static bool WrittenCompact(string file)
    {
        try
        {
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var buf = new byte[64];
            int n = fs.Read(buf, 0, buf.Length);
            int i = 0;
            if (n >= 3 && buf[0] == 0xEF && buf[1] == 0xBB && buf[2] == 0xBF) i = 3;
            while (i < n && buf[i] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') i++;
            if (i >= n || buf[i] != (byte)'{') return false;
            for (i++; i < n; i++)
            {
                if (buf[i] is (byte)'\r' or (byte)'\n') return false;
                if (buf[i] == (byte)'"') return true;
            }
            return false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }
}
