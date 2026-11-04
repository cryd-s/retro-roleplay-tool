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

    // Startet den Installer still – bei bestehender Installation in denselben Ordner.
    // Windows zeigt dabei ggf. die Admin-Abfrage.
    public static int RunSilentInstaller(string installerPath)
    {
        var psi = new ProcessStartInfo(installerPath, InstallerArguments(InstalledLocation())) { UseShellExecute = true };
        using (Process p = Process.Start(psi))
        {
            p.WaitForExit();
            return p.ExitCode;
        }
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
