using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

public class FiveMLocation
{
    public string AppDir;   // ...\FiveM.app, null = nicht gefunden
    public string Source;   // wo es gefunden wurde
    public bool Found { get { return AppDir != null; } }
}

// Findet die FiveM-Installation (den Ordner FiveM.app) – auch bei eigenem Installationsort.
// Sucht der Reihe nach an mehreren Stellen und nimmt den ersten echten Treffer.
public static class FiveMLocator
{
    const string AppFolder = "FiveM.app";

    public static FiveMLocation Locate(string localAppData, string savedPath)
    {
        return PickFirst(Candidates(localAppData, savedPath));
    }

    // Wird nur so weit abgearbeitet, bis ein Treffer da ist (teure Suchen kommen zuletzt).
    static IEnumerable<KeyValuePair<string, string>> Candidates(string localAppData, string savedPath)
    {
        yield return Pair("von dir gewählt", savedPath);
        yield return Pair("Windows-Registry", ReadInstallLocation("FiveM"));
        yield return Pair("fivem://-Verknüpfung", ExeFromCommand(ReadProtocolCommand()));
        yield return Pair("Standardordner", Path.Combine(localAppData, "FiveM"));
        foreach (string exe in RunningFiveMExes()) yield return Pair("laufendes FiveM", exe);
        foreach (string target in ShortcutTargets()) yield return Pair("Verknüpfung", target);
        foreach (string guess in DriveGuesses()) yield return Pair("Laufwerkssuche", guess);
    }

    static KeyValuePair<string, string> Pair(string source, string path)
    {
        return new KeyValuePair<string, string>(source, path);
    }

    public static FiveMLocation PickFirst(IEnumerable<KeyValuePair<string, string>> candidates)
    {
        foreach (var c in candidates)
        {
            string app = null;
            try { app = NormalizeAppDir(c.Value); } catch (Exception) { }
            if (app != null) return new FiveMLocation { AppDir = app, Source = c.Key };
        }
        return new FiveMLocation();
    }

    // Akzeptiert FiveM-Ordner, FiveM.app, FiveM.exe oder eine Datei/einen Ordner innerhalb von FiveM.app.
    public static string NormalizeAppDir(string path)
    {
        if (path == null) return null;
        string p = path.Trim().Trim('"').Trim();
        if (p.Length == 0 || p.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || !Path.IsPathRooted(p)) return null;
        p = Path.GetFullPath(p).TrimEnd('\\', '/');

        string start = File.Exists(p) ? Path.GetDirectoryName(p) : p;
        if (!Directory.Exists(start)) return null;

        string child = Path.Combine(start, AppFolder);
        if (IsValidAppDir(child)) return child;

        for (string d = start; d != null; d = Path.GetDirectoryName(d))
            if (string.Equals(Path.GetFileName(d), AppFolder, StringComparison.OrdinalIgnoreCase) && IsValidAppDir(d))
                return d;
        return null;
    }

    static bool IsValidAppDir(string app)
    {
        if (!Directory.Exists(app)) return false;
        string parent = Path.GetDirectoryName(app);
        return File.Exists(Path.Combine(app, "CitizenFX.ini"))
            || Directory.Exists(Path.Combine(app, "data"))
            || (parent != null && File.Exists(Path.Combine(parent, "FiveM.exe")));
    }

    // "\"C:\...\FiveM.exe\" \"%1\""  →  C:\...\FiveM.exe
    public static string ExeFromCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        string c = command.Trim();
        if (c.StartsWith("\""))
        {
            int end = c.IndexOf('"', 1);
            return end > 1 ? c.Substring(1, end - 1) : null;
        }
        int exe = c.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exe > 0) return c.Substring(0, exe + 4);
        int space = c.IndexOf(' ');
        return space > 0 ? c.Substring(0, space) : c;
    }

    // ------------------------------------------------------------ Quellen

    static string ReadInstallLocation(string displayName)
    {
        const string uninstall = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";
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
                using (RegistryKey list = root.OpenSubKey(uninstall))
                {
                    if (list == null) continue;
                    foreach (string name in list.GetSubKeyNames())
                    {
                        using (RegistryKey app = list.OpenSubKey(name))
                        {
                            if (app == null) continue;
                            if (!string.Equals(app.GetValue("DisplayName") as string, displayName, StringComparison.OrdinalIgnoreCase)) continue;
                            string loc = app.GetValue("InstallLocation") as string;
                            if (!string.IsNullOrWhiteSpace(loc)) return loc;
                        }
                    }
                }
            }
            catch (Exception) { }
        }
        return null;
    }

    // FiveM registriert sich für fivem://-Links (Server-Beitritt aus dem Browser).
    static string ReadProtocolCommand()
    {
        foreach (RegistryKey root in new[] { Registry.CurrentUser, Registry.ClassesRoot })
        {
            try
            {
                string sub = root == Registry.CurrentUser ? @"Software\Classes\fivem\shell\open\command" : @"fivem\shell\open\command";
                using (RegistryKey key = root.OpenSubKey(sub))
                {
                    string cmd = key == null ? null : key.GetValue("") as string;
                    if (!string.IsNullOrWhiteSpace(cmd)) return cmd;
                }
            }
            catch (Exception) { }
        }
        return null;
    }

    static IEnumerable<string> RunningFiveMExes()
    {
        var result = new List<string>();
        foreach (Process p in Process.GetProcesses())
        {
            using (p)
            {
                if (!FiveMProcess.IsFiveMProcessName(p.ProcessName)) continue;
                try { result.Add(p.MainModule.FileName); } catch (Exception) { }
            }
        }
        return result;
    }

    static IEnumerable<string> ShortcutTargets()
    {
        var folders = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
        };
        var links = new List<string>();
        foreach (string f in folders) FindLinks(f, links, 0);
        foreach (string link in links)
        {
            string target = ResolveShortcut(link);
            if (target != null) yield return target;
        }
    }

    static void FindLinks(string dir, List<string> found, int depth)
    {
        if (string.IsNullOrEmpty(dir) || depth > 4) return;
        try
        {
            foreach (string f in Directory.GetFiles(dir, "*FiveM*.lnk")) found.Add(f);
            foreach (string d in Directory.GetDirectories(dir)) FindLinks(d, found, depth + 1);
        }
        catch (Exception) { }
    }

    static string ResolveShortcut(string lnkPath)
    {
        object shell = null, link = null;
        try
        {
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            if (t == null) return null;
            shell = Activator.CreateInstance(t);
            link = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath });
            return link.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, link, null) as string;
        }
        catch (Exception) { return null; }
        finally
        {
            if (link != null && Marshal.IsComObject(link)) Marshal.ReleaseComObject(link);
            if (shell != null && Marshal.IsComObject(shell)) Marshal.ReleaseComObject(shell);
        }
    }

    static IEnumerable<string> DriveGuesses()
    {
        var subs = new[] { "FiveM", @"Games\FiveM", @"Spiele\FiveM", @"Program Files\FiveM", @"Program Files (x86)\FiveM" };
        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            bool usable;
            try { usable = drive.DriveType == DriveType.Fixed && drive.IsReady; } catch (Exception) { usable = false; }
            if (!usable) continue;
            foreach (string sub in subs) yield return Path.Combine(drive.RootDirectory.FullName, sub);
        }
    }
}
