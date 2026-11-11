using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Web.Script.Serialization;
using Microsoft.Win32;

public class Ts3Release
{
    public string Version, Url, Sha256;
}

// TeamSpeak 3: aktuelle Version (offizielle Liste), installierte Version, Prozesse, Installation.
public static class Ts3
{
    public const string VersionsUrl = "https://www.teamspeak.com/versions/client.json";
    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\TeamSpeak 3 Client";

    public static Ts3Release ParseClientJson(string json)
    {
        var root = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(json);
        var win = (Dictionary<string, object>)((Dictionary<string, object>)root["windows"])["x86_64"];
        var mirrors = (Dictionary<string, object>)win["mirrors"];
        string url = null;
        foreach (object m in mirrors.Values) { url = (string)m; break; }
        return new Ts3Release
        {
            Version = (string)win["version"],
            Sha256 = (string)win["checksum"],
            Url = url,
        };
    }

    public static Ts3Release FetchLatest()
    {
        return ParseClientJson(Downloader.GetString(VersionsUrl));
    }

    public static bool IsTs3ProcessName(string name)
    {
        return name.StartsWith("ts3client_", StringComparison.OrdinalIgnoreCase);
    }

    // null = nicht installiert
    public static string InstalledVersion() { return ReadUninstallValue("DisplayVersion"); }

    // Ordner der bestehenden Installation, null wenn unbekannt
    public static string InstalledLocation() { return ReadUninstallValue("InstallLocation"); }

    // NSIS-Setup: /S = still; /D= (ohne Anführungszeichen, als letzter Parameter) = Zielordner
    public static string InstallerArguments(string installDir)
    {
        string dir = installDir == null ? "" : installDir.Trim().Trim('"').TrimEnd('\\');
        return dir.Length == 0 ? "/S" : "/S /D=" + dir;
    }

    static string ReadUninstallValue(string valueName)
    {
        var places = new[]
        {
            new KeyValuePair<RegistryHive, RegistryView>(RegistryHive.CurrentUser, RegistryView.Default),
            new KeyValuePair<RegistryHive, RegistryView>(RegistryHive.LocalMachine, RegistryView.Registry64),
            new KeyValuePair<RegistryHive, RegistryView>(RegistryHive.LocalMachine, RegistryView.Registry32),
        };
        foreach (var place in places)
        {
            try
            {
                using (RegistryKey root = RegistryKey.OpenBaseKey(place.Key, place.Value))
                using (RegistryKey key = root.OpenSubKey(UninstallKey))
                {
                    string v = key == null ? null : key.GetValue(valueName) as string;
                    if (!string.IsNullOrWhiteSpace(v)) return v;
                }
            }
            catch (Exception) { }
        }
        return null;
    }

    public static bool IsRunning()
    {
        bool running = false;
        foreach (Process p in Process.GetProcesses())
        {
            if (IsTs3ProcessName(p.ProcessName)) running = true;
            p.Dispose();
        }
        return running;
    }

    // Erst normal schließen (TeamSpeak speichert dann seine Einstellungen), notfalls beenden.
    public static void Close()
    {
        foreach (Process p in Process.GetProcesses())
        {
            using (p)
            {
                if (!IsTs3ProcessName(p.ProcessName)) continue;
                try
                {
                    p.CloseMainWindow();
                    if (!p.WaitForExit(8000)) { p.Kill(); p.WaitForExit(5000); }
                }
                catch (Exception) { }
            }
        }
    }

    // Startet TeamSpeak wieder, falls der Ordner bekannt ist; sonst bleibt es geschlossen.
    public static void Start()
    {
        string dir = InstalledLocation();
        if (dir == null) return;
        string exe = System.IO.Path.Combine(dir.Trim().Trim('"'), "ts3client_win64.exe");
        if (!System.IO.File.Exists(exe)) return;
        using (Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true })) { }
    }

    // ------------------------------------------------------------ Installation ohne Overwolf

    // Der Installer (3.6.2) fragt beim Start einen Overwolf-Server ab. Antwortet der, startet er auch im
    // stillen Modus „OverwolfInstallerDownloader.exe -silent“, einen Schalter dagegen gibt es nicht.
    // Deshalb läuft er im Admin-Teil des Tools in einem Job, der jedes Overwolf-Programm sofort beendet.
    public const string ElevatedArgument = "--ts3-install";
    public const int InvalidInstallerCode = 9064;

    public static bool IsOverwolfImage(string imagePath)
    {
        if (string.IsNullOrEmpty(imagePath)) return false;
        return System.IO.Path.GetFileName(imagePath).StartsWith("Overwolf", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsTeamSpeakSubject(string subject)
    {
        return System.Text.RegularExpressions.Regex.IsMatch(subject ?? "", @"(^|,\s*)O=""?TeamSpeak Systems GmbH""?\s*(,|$)");
    }

    public static bool IsTeamSpeakSigned(string file)
    {
        if (!System.IO.File.Exists(file) || !RockstarLauncher.HasValidSignature(file)) return false;
        try { return IsTeamSpeakSubject(System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(file).Subject); }
        catch (Exception) { return false; }
    }

    // Admin-Teil: args = { Installer }. Liefert den Fehlercode des Installers.
    public static int RunElevated(string[] args)
    {
        if (args.Length != 1 || !IsTeamSpeakSigned(args[0])) return InvalidInstallerCode;
        int blocked;
        return GuardedProcess.Run(args[0], InstallerArguments(InstalledLocation()), IsOverwolfImage, out blocked);
    }

    // Ist Overwolf schon da (z. B. von einer früheren TeamSpeak-Installation)?
    public static bool IsOverwolfInstalled()
    {
        foreach (RegistryHive hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using (RegistryKey root = RegistryKey.OpenBaseKey(hive, view))
                    using (RegistryKey uninstall = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall"))
                    {
                        if (uninstall == null) continue;
                        foreach (string name in uninstall.GetSubKeyNames())
                            using (RegistryKey app = uninstall.OpenSubKey(name))
                            {
                                string display = app == null ? null : app.GetValue("DisplayName") as string;
                                if (display != null && display.StartsWith("Overwolf", StringComparison.OrdinalIgnoreCase)) return true;
                            }
                    }
                }
                catch (Exception) { }
            }
        return false;
    }

    // „1“ = TeamSpeak speichert seine Einstellungen im Installationsordner (Auswahl im Setup)
    public static string ConfigLocation()
    {
        foreach (var place in new[]
        {
            new KeyValuePair<RegistryHive, RegistryView>(RegistryHive.CurrentUser, RegistryView.Default),
            new KeyValuePair<RegistryHive, RegistryView>(RegistryHive.LocalMachine, RegistryView.Registry64),
            new KeyValuePair<RegistryHive, RegistryView>(RegistryHive.LocalMachine, RegistryView.Registry32),
        })
        {
            try
            {
                using (RegistryKey root = RegistryKey.OpenBaseKey(place.Key, place.Value))
                using (RegistryKey key = root.OpenSubKey(@"Software\TeamSpeak 3 Client"))
                {
                    string v = key == null ? null : key.GetValue("ConfigLocation") as string;
                    if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
                }
            }
            catch (Exception) { }
        }
        return null;
    }
}

public static class Versions
{
    // Vergleicht "3.6.2" und "3.6.10" numerisch. null (= nicht installiert) ist kleiner als alles.
    public static int Compare(string a, string b)
    {
        if (a == null || b == null) return a == null ? (b == null ? 0 : -1) : 1;
        string[] pa = a.Split('.'), pb = b.Split('.');
        for (int i = 0; i < Math.Max(pa.Length, pb.Length); i++)
        {
            int x = i < pa.Length ? Num(pa[i]) : 0;
            int y = i < pb.Length ? Num(pb[i]) : 0;
            if (x != y) return x.CompareTo(y);
        }
        return 0;
    }

    static int Num(string s)
    {
        int n = 0;
        foreach (char c in s)
        {
            if (!char.IsDigit(c)) break;
            n = n * 10 + (c - '0');
        }
        return n;
    }
}

public static class Elevation
{
    // true, wenn der Nutzer die Windows-Admin-Abfrage abgebrochen hat
    public static bool IsUacCancelled(Exception ex)
    {
        var w = ex as Win32Exception;
        return w != null && w.NativeErrorCode == 1223;
    }
}
