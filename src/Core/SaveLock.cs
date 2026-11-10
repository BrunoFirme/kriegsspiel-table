using System.Globalization;
using System.Text;

namespace KTable.Core;

public sealed class SaveInUseException(string message) : IOException(message);

public sealed class SaveLock : IDisposable
{
    const int SharingViolation = 0x20;
    readonly FileStream _file;

    SaveLock(string savePath, FileStream file)
    {
        SavePath = savePath;
        _file = file;
    }

    public string SavePath { get; }

    public static string PathFor(string savePath) => savePath + ".lock";

    static bool IsSharingViolation(IOException e) => (e.HResult & 0xFFFF) == SharingViolation;

    public static SaveLock? Take(string savePath)
    {
        var path = PathFor(savePath);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var file = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
            try
            {
                file.SetLength(0);
                file.Write(Encoding.UTF8.GetBytes(DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)));
                file.Flush(flushToDisk: true);
            }
            catch
            {
                file.Dispose();
                throw;
            }
            return new SaveLock(savePath, file);
        }
        catch (IOException e) when (IsSharingViolation(e)) { throw InUse(savePath); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            BestEffort.Note(e);
            return null;
        }
    }

    public static SaveInUseException InUse(string savePath) =>
        new($"{Path.GetFileNameWithoutExtension(savePath)} is open in another copy of the app on this PC{Since(PathFor(savePath))}: leave the table there, or close that copy, then try again");

    static string Since(string lockPath)
    {
        try
        {
            using var read = new FileStream(lockPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var text = new StreamReader(read, Encoding.UTF8);
            return DateTime.TryParse(text.ReadToEnd(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)
                ? $" (since {at.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture)})" : "";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return ""; }
    }

    public static bool IsTaken(string savePath)
    {
        var path = PathFor(savePath);
        if (!File.Exists(path)) return false;
        try
        {
            using var probe = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
            return false;
        }
        catch (IOException e) when (IsSharingViolation(e)) { return true; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    public static void ClearStale(string savePath)
    {
        if (!IsTaken(savePath)) BestEffort.DeleteFile(PathFor(savePath));
    }

    public void Dispose()
    {
        _file.Dispose();
        BestEffort.DeleteFile(PathFor(SavePath));
    }
}
