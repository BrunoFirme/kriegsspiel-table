using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Godot;
using KTable.Core;

namespace KTable.App;

public static class ConverterTool
{
    public const int StoppedCode = -2;

    static readonly object Gate = new();
    public static ConverterRun? Writer { get; private set; }

    public static string? Find()
    {
        if (System.Environment.GetEnvironmentVariable("KTABLE_CONVERTER") is { Length: > 0 } env && File.Exists(env)) return env;
        var exeDir = Path.GetDirectoryName(OS.GetExecutablePath()) ?? "";
        var candidates = new List<string>
        {
            Path.Combine(exeDir, "converter", "Converter.exe"),
            Path.Combine(exeDir, "converter", "Converter"),
        };
        var projectDir = ProjectSettings.GlobalizePath("res://");
        for (var d = projectDir.Length > 0 ? new DirectoryInfo(projectDir) : null; d != null; d = d.Parent)
        {
            candidates.Add(Path.Combine(d.FullName, "converter", "Converter.exe"));
            foreach (var cfg in new[] { "Debug", "Release" })
            {
                var bin = Path.Combine(d.FullName, "tools", "Converter", "bin", cfg);
                if (!Directory.Exists(bin)) continue;
                foreach (var tfm in Directory.EnumerateDirectories(bin, "net*").OrderByDescending(x => x, StringComparer.Ordinal))
                {
                    candidates.Add(Path.Combine(tfm, "win-x64", "Converter.exe"));
                    candidates.Add(Path.Combine(tfm, "Converter.exe"));
                    candidates.Add(Path.Combine(tfm, "Converter"));
                }
            }
        }
        return candidates.FirstOrDefault(File.Exists);
    }

    public static ConverterRun? Run(IEnumerable<string> args, Action<string> onLine, Action<int> onExit, string? writesTo = null, IDisposable? hold = null)
    {
        var exe = Find();
        if (exe == null)
        {
            hold?.Dispose();
            onLine("Converter not found. Build it once with: dotnet build tools/Converter");
            onExit(-1);
            return null;
        }
        var run = new ConverterRun(writesTo);
        if (writesTo != null)
            lock (Gate)
            {
                if (Writer != null)
                {
                    hold?.Dispose();
                    onLine("Another import is still running. Wait for it to finish, or stop it, before starting this one.");
                    onExit(-1);
                    return null;
                }
                Writer = run;
            }
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
            StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        Task.Run(() =>
        {
            int code;
            try
            {
                using var p = Process.Start(psi)!;
                KeepWithApp(p);
                p.OutputDataReceived += (_, e) => { if (e.Data != null) { run.Saw(e.Data); Post(() => onLine(e.Data)); } };
                p.ErrorDataReceived += (_, e) => { if (e.Data != null) Post(() => onLine(e.Data)); };
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                while (!p.WaitForExit(250))
                    if (run.CancelRequested || Assets.Quitting)
                    {
                        run.Stopped = true;
                        BestEffort.Try(() => p.Kill(entireProcessTree: true));
                    }
                p.WaitForExit();
                code = run.Stopped ? StoppedCode : p.ExitCode;
            }
            catch (Exception e) { Post(() => onLine("Converter failed to start: " + e.Message)); code = -1; }
            if (run.Stopped && !Assets.Quitting && PutBack(run, onLine)) code = 0;
            run.Finished = true;
            hold?.Dispose();
            lock (Gate) if (Writer == run) Writer = null;
            Post(() => onExit(code));
        });
        return run;
    }

    static bool PutBack(ConverterRun run, Action<string> onLine)
    {
        if (run.PackagesRoot == null) return false;
        bool finished = false;
        try
        {
            foreach (var u in InstallJournal.RecoverAll(run.PackagesRoot, SaveStore.ForRoot(run.PackagesRoot), Repeatable.UtcNow))
            {
                finished |= u.WasFinished && run.Journal != null && string.Equals(Path.GetFullPath(u.File), Path.GetFullPath(run.Journal), StringComparison.OrdinalIgnoreCase);
                foreach (var b in u.Back) Post(() => onLine("PUTBACK " + b));
                foreach (var t in u.Trashed) Post(() => onLine($"What the import had begun of {t} is in the trash."));
                foreach (var pr in u.Problems) Post(() => onLine(char.ToUpperInvariant(pr[0]) + pr[1..] + ": restore it from the trash at the foot of Piece studio or Host a game."));
                if (u.Notes.Count > 0) Post(() => onLine("The import's record: " + string.Join("; ", u.Notes) + "."));
            }
        }
        catch (Exception ex) { Post(() => onLine("Could not put back what the import had moved aside: " + ex.Message + ". It is in the trash.")); }
        return finished;
    }

    static void Post(Action a) { if (!Assets.Quitting) Callable.From(a).CallDeferred(); }

    static readonly IntPtr Job = MakeJob();

    static void KeepWithApp(Process p)
    {
        if (Job == IntPtr.Zero) return;
        BestEffort.Try(() => AssignProcessToJobObject(Job, p.Handle));
    }

    static IntPtr MakeJob()
    {
        if (!OperatingSystem.IsWindows()) return IntPtr.Zero;
        try
        {
            var job = CreateJobObject(IntPtr.Zero, null);
            if (job == IntPtr.Zero) return IntPtr.Zero;
            var info = new JobExtendedLimits { Basic = new JobBasicLimits { LimitFlags = KillOnJobClose } };
            int size = Marshal.SizeOf<JobExtendedLimits>();
            var ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(info, ptr, false);
                if (SetInformationJobObject(job, JobObjectExtendedLimitInformation, ptr, (uint)size)) return job;
            }
            finally { Marshal.FreeHGlobal(ptr); }
            CloseHandle(job);
            return IntPtr.Zero;
        }
        catch (Exception e) { Diagnostics.Write(LogLevel.Warning, "[converter] no job object to tie the converter to: " + e.Message); return IntPtr.Zero; }
    }

    const uint KillOnJobClose = 0x2000;
    const int JobObjectExtendedLimitInformation = 9;

    [StructLayout(LayoutKind.Sequential)]
    struct JobBasicLimits
    {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass, SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct IoCounters { public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount; }

    [StructLayout(LayoutKind.Sequential)]
    struct JobExtendedLimits
    {
        public JobBasicLimits Basic;
        public IoCounters Io;
        public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint length);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool CloseHandle(IntPtr handle);
}

public sealed class ConverterRun
{
    readonly object _lock = new();
    readonly List<string> _trashed = new();
    volatile bool _cancel, _stopped, _finished;

    public ConverterRun(string? packagesRoot) { PackagesRoot = packagesRoot; }

    public string? PackagesRoot { get; }
    public bool CancelRequested => _cancel;
    public bool Stopped { get => _stopped; internal set => _stopped = value; }
    public bool Finished { get => _finished; internal set => _finished = value; }

    public void Cancel() => _cancel = true;

    public IReadOnlyList<string> Trashed { get { lock (_lock) return _trashed.ToList(); } }

    public string? Journal { get; private set; }

    internal void Saw(string line)
    {
        if (line.StartsWith("TRASHED ")) lock (_lock) _trashed.Add(line[8..].Trim());
        else if (line.StartsWith("JOURNAL ")) Journal = line[8..].Trim();
    }
}

public sealed class TtsEntry
{
    public string Kind { get; set; } = "";
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string? Thumb { get; set; }
    public long Size { get; set; }
    public int Fetch { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public bool Measured { get; set; }
}
