using System;
using System.IO;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace KTable.App;

static class VideoMemory
{
    const string AdapterClass = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
    const string SizeValue = "HardwareInformation.qwMemorySize";
    const int LeastPictureMegabytes = 256, MostPictureMegabytes = 4096, UnknownPictureMegabytes = 512, PictureShare = 4;

    static int? _onCard;
    static bool _asked;

    public static int? MegabytesOnCard()
    {
        if (_asked) return _onCard;
        _asked = true;
        _onCard = OperatingSystem.IsWindows() ? FromRegistry() : null;
        return _onCard;
    }

    public static int SuggestedPictureMegabytes() =>
        MegabytesOnCard() is { } mb ? Math.Clamp(mb / PictureShare, LeastPictureMegabytes, MostPictureMegabytes) : UnknownPictureMegabytes;

    [SupportedOSPlatform("windows")]
    static int? FromRegistry()
    {
        try
        {
            using var adapters = Registry.LocalMachine.OpenSubKey(AdapterClass);
            if (adapters == null) return null;
            long most = 0;
            foreach (var name in adapters.GetSubKeyNames()) most = Math.Max(most, SizeOf(adapters, name));
            return most > 0 ? (int)(most >> 20) : null;
        }
        catch (Exception e) when (e is System.Security.SecurityException or IOException or UnauthorizedAccessException) { return null; }
    }

    [SupportedOSPlatform("windows")]
    static long SizeOf(RegistryKey adapters, string name)
    {
        try
        {
            using var adapter = adapters.OpenSubKey(name);
            return adapter?.GetValue(SizeValue) switch
            {
                long q => q,
                byte[] { Length: 8 } raw => BitConverter.ToInt64(raw, 0),
                _ => 0,
            };
        }
        catch (Exception e) when (e is System.Security.SecurityException or IOException or UnauthorizedAccessException) { return 0; }
    }
}
