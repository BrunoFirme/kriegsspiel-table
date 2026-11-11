using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Globalization;
using KTable.Core;

namespace KTable.Client.Updates;

public static class LinuxUpdate
{
    public const string Folder = "kriegsspiel-table", Script = "install.sh";
    const string Shell = "/bin/sh";
    const string WaitThenInstall = "while kill -0 \"$1\" 2>/dev/null; do sleep 0.2; done; " + Shell + " \"$2\" >\"$3\" 2>&1 && exec \"$4\"";

    public static string InstalledDir(string home) => Path.Combine(home, ".local", "opt", Folder);

    public static bool InstalledAt(string exeDir, string home) =>
        string.Equals(Path.GetFullPath(exeDir).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(InstalledDir(home)).TrimEnd(Path.DirectorySeparatorChar), StringComparison.Ordinal)
        && File.Exists(Path.Combine(exeDir, Script));

    public static string Unpack(string archive, string into)
    {
        BestEffort.DeleteFolder(into);
        Directory.CreateDirectory(into);
        using (var file = File.OpenRead(archive))
        using (var gz = new GZipStream(file, CompressionMode.Decompress))
            TarFile.ExtractToDirectory(gz, into, overwriteFiles: false);
        var script = Path.Combine(into, Folder, Script);
        return File.Exists(script) ? script : throw new InvalidDataException("the update has no " + Script);
    }

    public static ProcessStartInfo AfterExit(int pid, string script, string log, string exe)
    {
        var start = new ProcessStartInfo(Shell) { UseShellExecute = false };
        foreach (var arg in new[] { "-c", WaitThenInstall, "sh", pid.ToString(CultureInfo.InvariantCulture), script, log, exe }) start.ArgumentList.Add(arg);
        return start;
    }
}
