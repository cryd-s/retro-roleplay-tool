using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

// DS4Windows: PS4-/PS5-Controller erscheinen in Windows als Xbox-Controller, den FiveM versteht.
// Installiert wird die letzte Version der bekannten Fortführung von schmaldeo (das Projekt ist eingestellt,
// es kommt also nichts mehr nach). Alle Downloads sind mit festen Prüfsummen hinterlegt.
// DS4Windows selbst braucht keine Installation (Ordner + Verknüpfungen), dazu kommen still mit Adminrechten:
// der Treiber ViGEmBus, die .NET 8 Desktop Runtime und die Visual C++ Runtime, jeweils nur wenn sie fehlen.
public static class Ds4Windows
{
    public const string Version = "3.9.9";
    public const string ZipUrl = "https://github.com/schmaldeo/DS4Windows/releases/download/v3.9.9/DS4Windows_3.9.9_x64.zip";
    public const string ZipSha256 = "70c4fe79ef63c039e603e298f73dc41a971a9884348288c8a7eb9dc739c4cb9f";
    public const string ViGEmUrl = "https://github.com/nefarius/ViGEmBus/releases/download/v1.22.0/ViGEmBus_1.22.0_x64_x86_arm64.exe";
    public const string ViGEmSha256 = "89220a7865076b342892f98865f3499fb7c4cfd673159e89d352c360fd014c6a";
    // .NET 8 ist seit 10.11.2026 ohne Support, 8.0.34 ist die letzte Version (Prüfsumme mit Microsofts SHA-512 abgeglichen).
    public const string DotNetUrl = "https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/8.0.34/windowsdesktop-runtime-8.0.34-win-x64.exe";
    public const string DotNetSha256 = "37aa8a02acc1973ddd7e3de993a02007ccef26b48fa35b09d087fa8ab4602795";

    public const string ElevatedArgument = "--ds4-setup";
    public const string Skip = "-";
    // Ergebnis des Admin-Teils als Bits
    public const int VcFailed = 1, DotNetFailed = 2, ViGEmFailed = 4, RestartNeeded = 8, InvalidInput = 64;

    const string Microsoft = "Microsoft Corporation";
    const string Nefarius = "Nefarius Software Solutions e.U.";
    const string ExeName = "DS4Windows.exe";

    public static string InstallDir
    {
        get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DS4Windows"); }
    }

    public static string ExePath(string dir) { return Path.Combine(dir, ExeName); }

    // null = nicht installiert, "" = Version unbekannt
    public static string InstalledVersion(string dir)
    {
        string exe = ExePath(dir);
        if (!File.Exists(exe)) return null;
        try
        {
            string v = FileVersionInfo.GetVersionInfo(exe).FileVersion;
            return string.IsNullOrWhiteSpace(v) ? "" : v.Trim();
        }
        catch (Exception) { return ""; }
    }

    public static bool IsViGEmInstalled()
    {
        try
        {
            using (RegistryKey root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (RegistryKey key = root.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\ViGEmBus"))
                return key != null;
        }
        catch (Exception) { return false; }
    }

    // DS4Windows 3.9.9 läuft mit jeder .NET-8-Version (Desktop und Basis), aber nicht mit 9 oder 10.
    public static bool IsDotNet8Installed(string programFiles)
    {
        string shared = Path.Combine(programFiles, "dotnet", "shared");
        return HasMajor(Path.Combine(shared, "Microsoft.WindowsDesktop.App"), 8) && HasMajor(Path.Combine(shared, "Microsoft.NETCore.App"), 8);
    }

    static bool HasMajor(string dir, int major)
    {
        if (!Directory.Exists(dir)) return false;
        foreach (string d in Directory.GetDirectories(dir))
            if (Path.GetFileName(d).StartsWith(major + ".", StringComparison.Ordinal)) return true;
        return false;
    }

    // ------------------------------------------------------------ Programmdateien

    // Ziel einer Datei aus dem ZIP („DS4Windows\Lang\de\x.dll“ → <Ordner>\Lang\de\x.dll).
    // null für Ordner und für alles, was aus dem Zielordner hinausführen würde.
    public static string TargetPath(string entryName, string installDir)
    {
        if (string.IsNullOrEmpty(entryName)) return null;
        string rel = entryName.Replace('/', '\\');
        if (rel.StartsWith("DS4Windows\\", StringComparison.OrdinalIgnoreCase)) rel = rel.Substring("DS4Windows\\".Length);
        if (rel.Length == 0 || rel.EndsWith("\\") || Path.IsPathRooted(rel) || rel.Contains(":")) return null;
        string root = Path.GetFullPath(installDir).TrimEnd('\\') + "\\";
        string full = Path.GetFullPath(Path.Combine(root, rel));
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    // Entpackt die Programmdateien. Eigene Profile und Einstellungen stehen nicht im ZIP und bleiben erhalten.
    public static int Extract(string zipPath, string installDir)
    {
        using (ZipArchive zip = ZipFile.OpenRead(zipPath))
        {
            bool hasExe = false;
            foreach (ZipArchiveEntry e in zip.Entries)
            {
                string t = TargetPath(e.FullName, installDir);
                if (t != null && string.Equals(Path.GetFileName(t), ExeName, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(Path.GetDirectoryName(t), Path.GetFullPath(installDir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                    hasExe = true;
            }
            if (!hasExe) throw new InvalidDataException("Das DS4Windows-Paket enthält kein " + ExeName + ".");

            int count = 0;
            foreach (ZipArchiveEntry e in zip.Entries)
            {
                string target = TargetPath(e.FullName, installDir);
                if (target == null) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                if (File.Exists(target)) File.SetAttributes(target, FileAttributes.Normal);
                e.ExtractToFile(target, true);
                count++;
            }
            return count;
        }
    }

    public static string DesktopShortcut
    {
        get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "DS4Windows.lnk"); }
    }

    public static string StartMenuShortcut
    {
        get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "DS4Windows.lnk"); }
    }

    public static void CreateShortcut(string linkPath, string target, string description)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(linkPath));
        var link = (IShellLinkW)new ShellLink();
        try
        {
            link.SetPath(target);
            link.SetWorkingDirectory(Path.GetDirectoryName(target));
            link.SetDescription(description);
            link.SetIconLocation(target, 0);
            ((IPersistFile)link).Save(linkPath, true);
        }
        finally { Marshal.ReleaseComObject(link); }
    }

    public static string ShortcutTarget(string linkPath)
    {
        var link = (IShellLinkW)new ShellLink();
        try
        {
            ((IPersistFile)link).Load(linkPath, 0);
            var sb = new StringBuilder(1024);
            link.GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
            return sb.ToString();
        }
        finally { Marshal.ReleaseComObject(link); }
    }

    // ------------------------------------------------------------ Laufendes Programm

    public static bool IsRunning()
    {
        Process[] all = Process.GetProcessesByName("DS4Windows");
        foreach (Process p in all) p.Dispose();
        return all.Length > 0;
    }

    // Erst normal schließen (DS4Windows gibt dabei den virtuellen Controller frei), notfalls beenden.
    public static void Close()
    {
        foreach (Process p in Process.GetProcessesByName("DS4Windows"))
        {
            using (p)
            {
                try
                {
                    p.CloseMainWindow();
                    if (!p.WaitForExit(8000)) { p.Kill(); p.WaitForExit(5000); }
                }
                catch (Exception) { }
            }
        }
    }

    public static void Start(string dir)
    {
        using (Process.Start(new ProcessStartInfo(ExePath(dir)) { UseShellExecute = true, WorkingDirectory = dir })) { }
    }

    // ------------------------------------------------------------ Admin-Teil

    public static string BuildElevatedArguments(string vcFile, string dotNetFile, string viGEmFile)
    {
        return ElevatedArgument + " " + RockstarLauncher.Quote(vcFile ?? Skip) + " " +
               RockstarLauncher.Quote(dotNetFile ?? Skip) + " " + RockstarLauncher.Quote(viGEmFile ?? Skip);
    }

    public static bool IsSubjectOf(string subject, string organization)
    {
        return Regex.IsMatch(subject ?? "", @"(^|,\s*)O=""?" + Regex.Escape(organization) + @"""?\s*(,|$)");
    }

    static bool IsSignedBy(string file, string organization)
    {
        if (!File.Exists(file) || !RockstarLauncher.HasValidSignature(file)) return false;
        try { return IsSubjectOf(X509Certificate.CreateFromSignedFile(file).Subject, organization); }
        catch (Exception) { return false; }
    }

    // 0 = ok, 3010/1641 = ok mit Neustart, 1638 = neuere Version schon da
    public static bool IsSuccess(int code) { return code == 0 || code == 3010 || code == 1641 || code == 1638; }
    public static bool NeedsRestart(int code) { return code == 3010 || code == 1641; }

    // args = { VC++-Setup, .NET-Setup, ViGEmBus-Setup }, jeweils "-" = nicht nötig.
    // Geprüft wird alles vor dem ersten Start: jede Datei muss vom erwarteten Hersteller gültig signiert sein.
    public static int RunElevated(string[] args)
    {
        if (args.Length != 3) return InvalidInput;
        string[] owners = { Microsoft, Microsoft, Nefarius };
        for (int i = 0; i < 3; i++)
            if (args[i] != Skip && !IsSignedBy(args[i], owners[i])) return InvalidInput;

        int flags = 0;
        flags |= RunInstaller(args[0], "/install /quiet /norestart", VcFailed);
        flags |= RunInstaller(args[1], "/install /quiet /norestart", DotNetFailed);
        flags |= RunInstaller(args[2], "/exenoui /qn /norestart", ViGEmFailed);
        return flags;
    }

    static int RunInstaller(string file, string arguments, int failedFlag)
    {
        if (file == Skip) return 0;
        try
        {
            using (Process p = Process.Start(new ProcessStartInfo(file, arguments) { UseShellExecute = false, CreateNoWindow = true }))
            {
                if (!p.WaitForExit(15 * 60 * 1000)) return failedFlag;
                if (!IsSuccess(p.ExitCode)) return failedFlag;
                return NeedsRestart(p.ExitCode) ? RestartNeeded : 0;
            }
        }
        catch (Exception) { return failedFlag; }
    }

    public static string DoneText(int flags)
    {
        var text = new StringBuilder();
        var failed = new List<string>();
        if ((flags & VcFailed) != 0) failed.Add("Visual C++ Runtime");
        if ((flags & DotNetFailed) != 0) failed.Add(".NET 8 Desktop Runtime");
        if ((flags & ViGEmFailed) != 0) failed.Add("Treiber ViGEmBus");
        if (failed.Count > 0)
            text.Append("DS4Windows ist entpackt, aber nicht alles ließ sich installieren: ").Append(string.Join(", ", failed.ToArray()))
                .Append(". Ohne diese Teile startet DS4Windows nicht oder findet keinen Controller. Bitte noch einmal versuchen.\n\n");
        else
            text.Append("DS4Windows " + Version + " ist installiert, mit Verknüpfung auf dem Desktop und im Startmenü.\n\n");
        if ((flags & RestartNeeded) != 0)
            text.Append("Windows möchte einmal neu starten, damit der Treiber sicher arbeitet.\n\n");
        text.Append("So geht es weiter:\n" +
                    "•  DS4Windows starten. Beim ersten Start fragt es, wo es seine Einstellungen speichert – „Appdata“ ist richtig.\n" +
                    "•  Controller per USB-Kabel oder Bluetooth verbinden. Er erscheint in DS4Windows und in FiveM als Xbox-Controller.\n" +
                    "•  DS4Windows muss laufen, solange du spielst.");
        return text.ToString();
    }

    // ------------------------------------------------------------ Verknüpfungen (Shell)

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    class ShellLink { }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int maxPath, IntPtr findData, int flags);
        void GetIDList(out IntPtr idList);
        void SetIDList(IntPtr idList);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int maxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int maxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int maxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCmd);
        void SetShowCmd(int showCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int maxIconPath, out int icon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int icon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relPath, int reserved);
        void Resolve(IntPtr window, int flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }
}
