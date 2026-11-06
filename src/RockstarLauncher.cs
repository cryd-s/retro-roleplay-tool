using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

// Rockstar Launcher Fix: DNS-Cache leeren, Winsock zurücksetzen, Launcher-Cache löschen.
public static class RockstarLauncher
{
    public const string ElevatedArgument = "--network-reset";

    public const string ConfirmText =
        "Der Fix macht drei Dinge:\n\n" +
        "•  DNS-Cache leeren und Winsock zurücksetzen (Windows fragt nach Administratorrechten)\n" +
        "•  Rockstar Launcher schließen, falls er läuft\n" +
        "•  Launcher-Cache löschen: %LOCALAPPDATA%\\Rockstar Games\\Launcher\n\n" +
        "Danach musst du den PC neu starten und dich im Launcher neu anmelden.\n\n" +
        "Schalte vorher VPN, Tailscale (besonders mit Exit-Node) und eigene DNS-Filter wie Pi-hole oder AdGuard aus. " +
        "Das kann das Tool nicht für dich tun.\n\nFortfahren?";

    public const string GameRunningText = "FiveM oder GTA V läuft gerade. Bitte erst schließen und den Fix dann starten.";

    public static string DoneText(int filesSkipped)
    {
        string text = "Der Fix ist durch.\n\n" +
                      "Starte jetzt bitte den PC neu, erst dann wirkt das Zurücksetzen von Winsock. " +
                      "Melde dich danach im Rockstar Launcher neu an.\n\n" +
                      "Lass VPN, Tailscale und DNS-Filter beim Testen noch aus.";
        if (filesSkipped > 0)
            text += "\n\n" + filesSkipped + (filesSkipped == 1 ? " Datei war" : " Dateien waren") +
                    " noch in Benutzung und wurde" + (filesSkipped == 1 ? "" : "n") +
                    " nicht gelöscht. Starte den Fix nach dem Neustart noch einmal, falls das Problem bleibt.";
        return text;
    }

    public static string CacheDir(string localAppData)
    {
        return Path.Combine(localAppData, "Rockstar Games", "Launcher");
    }

    // Vollständige Pfade: Dieser Teil läuft mit Administratorrechten.
    public static List<KeyValuePair<string, string>> NetworkCommands()
    {
        return new List<KeyValuePair<string, string>>
        {
            new KeyValuePair<string, string>(Path.Combine(Environment.SystemDirectory, "ipconfig.exe"), "/flushdns"),
            new KeyValuePair<string, string>(Path.Combine(Environment.SystemDirectory, "netsh.exe"), "winsock reset"),
        };
    }

    // Der Prozess heißt schlicht „Launcher“. Das können auch andere Programme sein, deshalb zählt dort der Pfad.
    public static bool IsLauncherProcess(string name, string exePath, string installDir)
    {
        foreach (string unique in new[] { "LauncherPatcher", "RockstarService", "RockstarSteamHelper", "RockstarErrorHandler", "SocialClubHelper" })
            if (string.Equals(name, unique, StringComparison.OrdinalIgnoreCase)) return true;
        if (!string.Equals(name, "Launcher", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(exePath)) return false;

        if (exePath.IndexOf(@"\Rockstar Games\", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (string.IsNullOrEmpty(installDir)) return false;
        string folder = installDir.Trim().Trim('"').TrimEnd('\\') + "\\";
        return exePath.StartsWith(folder, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsGameProcessName(string name)
    {
        return name.StartsWith("GTA5", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("PlayGTA", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsGameRunning()
    {
        if (FiveMProcess.IsRunning()) return true;
        bool running = false;
        foreach (Process p in Process.GetProcesses())
        {
            if (IsGameProcessName(p.ProcessName)) running = true;
            p.Dispose();
        }
        return running;
    }

    // Erst normal schließen, notfalls beenden. Ohne Rechte (z. B. beim Dienst) bleibt der Prozess einfach offen.
    public static void CloseLauncher()
    {
        string installDir = InstallDir();
        foreach (Process p in Process.GetProcesses())
        {
            using (p)
            {
                try
                {
                    if (!IsLauncherProcess(p.ProcessName, ExePath(p), installDir)) continue;
                    if (!p.CloseMainWindow() || !p.WaitForExit(8000))
                    {
                        p.Kill();
                        p.WaitForExit(5000);
                    }
                }
                catch (Exception) { /* bereits beendet oder kein Zugriff */ }
            }
        }
    }

    static string ExePath(Process p)
    {
        if (!string.Equals(p.ProcessName, "Launcher", StringComparison.OrdinalIgnoreCase)) return null;
        try { return p.MainModule.FileName; }
        catch (Exception) { return null; }
    }

    static string InstallDir()
    {
        try
        {
            using (RegistryKey root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32))
            using (RegistryKey key = root.OpenSubKey(@"SOFTWARE\Rockstar Games\Launcher"))
                return key == null ? null : key.GetValue("InstallFolder") as string;
        }
        catch (Exception) { return null; }
    }
}
