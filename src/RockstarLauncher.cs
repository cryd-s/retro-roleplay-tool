using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

// Ein Ordner, dessen Besitzer und Rechte der Fix repariert.
public class RepairTarget
{
    public string Path, OwnerSid;
    public string[] Grants;
    public bool ResetFirst;     // alte Regeln entfernen und vom Elternordner erben: nur bei eigenen Datenordnern
    public string RemoveAccessFor;   // eigene Regeln dieses Kontos entfernen: in Programmordnern hat es nur Leserechte
    public bool RootOnly;       // nur diese eine Regel am Oberordner entfernen, sonst nichts ändern
}

// Rockstar Launcher Fix: Defender-Freigabe, Rechte der Rockstar-Ordner, Dienst, Netzwerk und Launcher-Cache.
public static class RockstarLauncher
{
    public const string ElevatedArgument = "--rockstar-fix";

    // Ergebnis des Admin-Teils als Bitmaske im Exit-Code
    public const int NetworkFailed = 1, DefenderFailed = 2, PermissionsFailed = 4, ServiceFailed = 8;
    public const int MaxFlags = 15;
    const int InvalidArguments = 64;

    const string ServiceName = "Rockstar Service";
    const string SidSystem = "*S-1-5-18", SidAdmins = "*S-1-5-32-544", SidUsers = "*S-1-5-32-545";
    const string Full = ":(OI)(CI)F", Modify = ":(OI)(CI)M", ReadExecute = ":(OI)(CI)RX";
    const int AclTimeoutMs = 300000;

    public const string ConfirmText =
        "Der Fix macht Folgendes:\n\n" +
        "•  Launcher schließen und bei „Überwachter Ordnerzugriff“ in Windows Defender erlauben\n" +
        "•  Besitzer und Rechte der Rockstar-Ordner reparieren (die bisherigen Rechte werden gespeichert)\n" +
        "•  Rockstar-Dienst auf „Automatisch“ stellen\n" +
        "•  DNS-Cache leeren und Winsock zurücksetzen\n" +
        "•  Launcher-Cache löschen und den Launcher neu starten\n\n" +
        "Spielstände und GTA selbst bleiben unberührt. Du musst dich im Launcher neu anmelden.\n\n" +
        "Windows fragt nach Administratorrechten. Schalte vorher VPN, Tailscale und eigene DNS-Filter wie Pi-hole oder AdGuard aus.\n\nFortfahren?";

    public const string GameRunningText = "FiveM oder GTA V läuft gerade. Bitte erst schließen und den Fix dann starten.";

    public static string DoneText(int flags, int filesSkipped, bool launcherStarted, string logFile = null)
    {
        var text = new StringBuilder(flags == 0 ? "Der Fix ist durch." : "Der Fix ist durch, aber nicht alles hat geklappt:");
        if ((flags & NetworkFailed) != 0) text.Append("\n•  DNS-Cache oder Winsock konnten nicht zurückgesetzt werden");
        if ((flags & DefenderFailed) != 0) text.Append("\n•  Defender-Freigabe nicht gesetzt (Launcher nicht von Rockstar signiert, oder ein anderes Antivirenprogramm ist aktiv)");
        if ((flags & PermissionsFailed) != 0) text.Append("\n•  Rechte konnten nicht bei allen Rockstar-Ordnern repariert werden");
        if ((flags & ServiceFailed) != 0) text.Append("\n•  Rockstar-Dienst nicht gefunden oder nicht einstellbar");
        text.Append(launcherStarted
            ? "\n\nDer Launcher wird jetzt gestartet. Melde dich dort neu an."
            : "\n\nStarte den Launcher bitte selbst und melde dich neu an.");
        text.Append("\n\nDas Zurücksetzen von Winsock wirkt erst nach einem Neustart. Bekommt der Launcher keine Verbindung, starte den PC neu " +
                    "und lass VPN, Tailscale und DNS-Filter beim Testen noch aus.");
        if (filesSkipped > 0)
            text.Append("\n\n" + filesSkipped + (filesSkipped == 1 ? " Datei war" : " Dateien waren") +
                        " noch in Benutzung und wurde" + (filesSkipped == 1 ? "" : "n") + " nicht gelöscht.");
        if (flags != 0 && logFile != null) text.Append("\n\nDetails stehen im Protokoll:\n" + logFile);
        return text.ToString();
    }

    public static string CacheDir(string localAppData)
    {
        return Path.Combine(localAppData, "Rockstar Games", "Launcher");
    }

    public static string DocumentsLauncherDir(string documents)
    {
        return Path.Combine(documents, "Rockstar Games", "Launcher");
    }

    // Leert den Launcher-Ordner in den Dokumenten. Logs bleiben für die Fehlersuche, verknüpfte Ordner werden nicht betreten.
    // Spielstände liegen in „Rockstar Games\GTA V“ und sind nicht betroffen. Liefert die Zahl der übersprungenen Dateien.
    public static int CleanKeepingLogs(string dir)
    {
        if (!Directory.Exists(dir)) return 0;
        int skipped = 0;
        foreach (string file in Directory.GetFiles(dir))
        {
            if (file.EndsWith(".log", StringComparison.OrdinalIgnoreCase)) continue;
            try { File.SetAttributes(file, FileAttributes.Normal); File.Delete(file); }
            catch (Exception) { skipped++; }
        }
        foreach (string sub in Directory.GetDirectories(dir))
        {
            try
            {
                if (IsLinked(sub)) { skipped++; continue; }
                skipped += FolderCleaner.CleanContents(sub, null).FilesSkipped;
                Directory.Delete(sub);
            }
            catch (Exception) { /* noch nicht leer: bleibt stehen */ }
        }
        return skipped;
    }

    public static string BackupRoot
    {
        get
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                "RetroRoleplayTool", "Sicherung Rockstar");
        }
    }

    // ------------------------------------------------------------ Admin-Start

    // Argumente für den Admin-Teil. Die Ordner kommen vom angemeldeten Nutzer: Das Admin-Konto kann ein anderes sein.
    public static string BuildElevatedArguments(string userSid, string documents, string localAppData, string roamingAppData, string backupDir)
    {
        var parts = new List<string> { ElevatedArgument };
        foreach (string value in new[] { userSid, documents, localAppData, roamingAppData, backupDir }) parts.Add(Quote(value));
        return string.Join(" ", parts);
    }

    // Ein Backslash direkt vor dem Anführungszeichen würde es sonst aufheben (z. B. bei E:\).
    public static string Quote(string value)
    {
        return "\"" + (value.EndsWith("\\") ? value + "\\" : value) + "\"";
    }

    public static bool IsValidElevatedInput(string userSid, string documents, string localAppData, string roamingAppData, string backupDir)
    {
        if (!Regex.IsMatch(userSid ?? "", @"^S-1-5-21-\d+(-\d+)+$")) return false;
        foreach (string root in new[] { documents, localAppData, roamingAppData })
            if (string.IsNullOrEmpty(root) || !Path.IsPathRooted(root) || root.StartsWith(@"\\")) return false;
        return !string.IsNullOrEmpty(backupDir) && Path.IsPathRooted(backupDir) &&
               backupDir.IndexOf(@"\RetroRoleplayTool\Sicherung Rockstar\", StringComparison.OrdinalIgnoreCase) > 0;
    }

    // Der Teil mit Administratorrechten. Liefert die Bitmaske der fehlgeschlagenen Schritte.
    public static int RunElevated(string[] args)
    {
        if (args.Length != 5 || !IsValidElevatedInput(args[0], args[1], args[2], args[3], args[4])) return InvalidArguments;
        string userSid = args[0], backupDir = args[4];
        int flags = 0;
        Action<string> log = text => WriteLog(backupDir, text);

        log("Fix gestartet");
        Try(() => SystemCommands.RunAll(ServiceStopCommands()));   // läuft er nicht, ist das kein Fehler
        CloseLauncher();
        int network = Try(() => SystemCommands.RunAll(NetworkCommands()));
        log("Netzwerk: Code " + network);
        if (network != 0) flags |= NetworkFailed;
        bool allowed = Try(() => AllowInControlledFolderAccess(AllowedAppCandidates(InstallDir(), SocialClubDir())));
        log("Defender-Freigabe: " + (allowed ? "gesetzt" : "nicht gesetzt"));
        if (!allowed) flags |= DefenderFailed;

        List<RepairTarget> targets = PlanTargets(userSid, args[1], args[2], args[3],
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetEnvironmentVariable("ProgramFiles(x86)"), InstallDir(), SocialClubDir());
        if (!Try(() => RepairAll(targets, backupDir, log))) flags |= PermissionsFailed;

        // Nur einstellen, nicht starten: Der Dienst würde sonst Dateien im Cache sperren, der danach geleert wird.
        // Der Launcher startet ihn selbst.
        int service = Try(() => SystemCommands.RunAll(ServiceConfigCommands()));
        log("Dienst einstellen: Code " + service);
        if (service != 0) flags |= ServiceFailed;
        log("Admin-Teil fertig, Ergebnis " + flags);
        return flags;
    }

    public const string LogFileName = "Protokoll.txt";

    static void WriteLog(string backupDir, string text)
    {
        try
        {
            Directory.CreateDirectory(backupDir);
            File.AppendAllText(Path.Combine(backupDir, LogFileName), DateTime.Now.ToString("HH:mm:ss") + "  " + text + "\r\n");
        }
        catch (Exception) { /* das Protokoll darf den Fix nie aufhalten */ }
    }

    static int Try(Func<int> action)
    {
        try { return action(); }
        catch (Exception) { return 1; }
    }

    static bool Try(Func<bool> action)
    {
        try { return action(); }
        catch (Exception) { return false; }
    }

    // ------------------------------------------------------------ Befehle

    // Vollständige Pfade: Dieser Teil läuft mit Administratorrechten.
    public static List<KeyValuePair<string, string>> NetworkCommands()
    {
        return new List<KeyValuePair<string, string>>
        {
            new KeyValuePair<string, string>(Path.Combine(Environment.SystemDirectory, "ipconfig.exe"), "/flushdns"),
            new KeyValuePair<string, string>(Path.Combine(Environment.SystemDirectory, "netsh.exe"), "winsock reset"),
        };
    }

    // Rockstar empfiehlt für den Dienst den Starttyp „Automatisch“.
    public static List<KeyValuePair<string, string>> ServiceConfigCommands()
    {
        return new List<KeyValuePair<string, string>>
        {
            new KeyValuePair<string, string>(Path.Combine(Environment.SystemDirectory, "sc.exe"), "config \"" + ServiceName + "\" start= auto obj= LocalSystem"),
        };
    }

    public static List<KeyValuePair<string, string>> ServiceStopCommands()
    {
        return new List<KeyValuePair<string, string>>
        {
            new KeyValuePair<string, string>(Path.Combine(Environment.SystemDirectory, "sc.exe"), "stop \"" + ServiceName + "\""),
        };
    }

    // ------------------------------------------------------------ Rechte

    // Eigene Datenordner: Besitzer ist der Nutzer, Regeln werden neu geerbt.
    // Programmordner: Der Dienst läuft von dort als SYSTEM. Normale Konten dürfen dort nur lesen,
    // sonst könnte jedes Programm die Dienst-Datei austauschen. Deshalb nie zurücksetzen und nie Schreibrechte vergeben.
    // Der Spielordner von GTA V ist bewusst nicht dabei.
    public static List<RepairTarget> PlanTargets(string userSid, string documents, string localAppData, string roamingAppData,
        string programData, string programFiles, string programFilesX86, string installDir, string socialClubDir)
    {
        var targets = new List<RepairTarget>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string user = "*" + userSid;

        foreach (string root in new[] { documents, localAppData, roamingAppData })
            Add(targets, seen, new RepairTarget
            {
                Path = Path.Combine(root, "Rockstar Games"),
                OwnerSid = user,
                ResetFirst = true,
                Grants = new[] { user + Full, SidSystem + Full, SidAdmins + Full },
            });

        if (!string.IsNullOrEmpty(programData))
            Add(targets, seen, new RepairTarget
            {
                Path = Path.Combine(programData, "Rockstar Games"),
                OwnerSid = SidAdmins,
                Grants = new[] { SidSystem + Full, SidAdmins + Full, user + Modify },
            });

        var programDirs = new List<string>();
        if (IsSafeProgramDir(installDir)) programDirs.Add(installDir);
        if (IsSafeProgramDir(socialClubDir)) programDirs.Add(socialClubDir);
        foreach (string root in new[] { programFiles, programFilesX86 })
        {
            if (string.IsNullOrEmpty(root)) continue;
            programDirs.Add(Path.Combine(root, "Rockstar Games", "Launcher"));
            programDirs.Add(Path.Combine(root, "Rockstar Games", "Social Club"));
            // Eine hier gesetzte Regel des Kontos würde sonst weiter nach unten vererbt.
            Add(targets, seen, new RepairTarget { Path = Path.Combine(root, "Rockstar Games"), RootOnly = true, RemoveAccessFor = user });
        }
        foreach (string dir in programDirs)
            Add(targets, seen, new RepairTarget
            {
                Path = dir.Trim().Trim('"').TrimEnd('\\'),
                OwnerSid = SidAdmins,
                RemoveAccessFor = user,
                Grants = new[] { SidSystem + Full, SidAdmins + Full, SidUsers + ReadExecute },
            });
        return targets;
    }

    static void Add(List<RepairTarget> targets, HashSet<string> seen, RepairTarget target)
    {
        if (seen.Add(target.Path)) targets.Add(target);
    }

    // Wert aus der Registry: Nie ein Laufwerk oder ein Systemordner.
    public static bool IsSafeProgramDir(string dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return false;
        string clean = dir.Trim().Trim('"').TrimEnd('\\');
        if (clean.StartsWith(@"\\") || !Path.IsPathRooted(clean) || clean.Length <= 3) return false;
        string name = Path.GetFileName(clean);
        foreach (string blocked in new[] { "Windows", "Program Files", "Program Files (x86)", "ProgramData", "Users", "Games", "Rockstar Games" })
            if (string.Equals(name, blocked, StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    public static List<KeyValuePair<string, string>> AclCommands(RepairTarget target)
    {
        string icacls = Path.Combine(Environment.SystemDirectory, "icacls.exe");
        string dir = "\"" + target.Path + "\"";
        if (target.RootOnly)
            return new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>(icacls, dir + " /remove:g " + target.RemoveAccessFor + " /C /Q"),
            };
        var commands = new List<KeyValuePair<string, string>>
        {
            new KeyValuePair<string, string>(icacls, dir + " /setowner " + target.OwnerSid + " /T /C /Q"),
        };
        if (target.ResetFirst) commands.Add(new KeyValuePair<string, string>(icacls, dir + " /reset /T /C /Q"));
        if (target.RemoveAccessFor != null) commands.Add(new KeyValuePair<string, string>(icacls, dir + " /remove:g " + target.RemoveAccessFor + " /T /C /Q"));
        commands.Add(new KeyValuePair<string, string>(icacls, dir + " /grant " + string.Join(" ", target.Grants) + (target.ResetFirst ? "" : " /T") + " /C /Q"));
        return commands;
    }

    public static bool RepairAll(List<RepairTarget> targets, string backupDir, Action<string> log = null)
    {
        bool ok = true;
        int index = 0;
        foreach (RepairTarget target in targets)
        {
            if (!Directory.Exists(target.Path)) continue;
            index++;
            if (!Repair(target, backupDir, index, log)) ok = false;
        }
        return ok;
    }

    // Ohne Sicherung der bisherigen Rechte wird nichts geändert.
    // Verknüpfte Ordner könnten aus dem Rockstar-Ordner herausführen („/setowner /T“ folgt ihnen): Dann bleibt der Ordner unangetastet.
    // Ein Fehler hier stoppt die übrigen Ordner nicht. Der Exit-Code von icacls ist auch bei Fehlern oft 0, deshalb zählt am Ende die Probe.
    public static bool Repair(RepairTarget target, string backupDir, int index, Action<string> log = null)
    {
        Action<string> note = text => { if (log != null) log(target.Path + ": " + text); };
        try
        {
            Directory.CreateDirectory(backupDir);
            string backup = Path.Combine(backupDir, "Rechte " + index + ".txt");
            string save = "\"" + target.Path + "\" /save \"" + backup + "\"" + (target.RootOnly ? "" : " /T") + " /C /Q";
            string icacls = Path.Combine(Environment.SystemDirectory, "icacls.exe");
            SystemCommands.RunAll(new[] { new KeyValuePair<string, string>(icacls, save) }, AclTimeoutMs);
            if (!File.Exists(backup)) { note("Sicherung fehlgeschlagen, nichts geändert"); return false; }
            File.AppendAllText(Path.Combine(backupDir, "Liste.txt"),
                "Rechte " + index + ".txt = " + target.Path + "\r\nZurück: icacls \"" + Path.GetDirectoryName(target.Path) + "\" /restore \"" + backup + "\"\r\n\r\n");

            if (target.RootOnly ? IsLinked(target.Path) : ContainsLinkedFolder(target.Path, dir => Unlock(target, dir)))
            {
                note("enthält einen verknüpften Ordner, nichts repariert");
                return false;
            }

            int code = SystemCommands.RunAll(AclCommands(target), AclTimeoutMs);
            if (code != 0) { note("icacls meldet Code " + code); return false; }
            if (!target.RootOnly && !IsTreeReadable(target.Path)) { note("danach noch nicht vollständig lesbar"); return false; }
            note("repariert");
            return true;
        }
        catch (Exception ex)
        {
            note(ex.GetType().Name + ": " + ex.Message);
            return false;
        }
    }

    // Macht einen gesperrten Ordner lesbar, und nur diesen einen (ohne /T): Sonst ließe sich nicht prüfen, was darin liegt.
    static void Unlock(RepairTarget target, string dir)
    {
        string icacls = Path.Combine(Environment.SystemDirectory, "icacls.exe");
        string quoted = "\"" + dir + "\"";
        SystemCommands.RunAll(new[]
        {
            new KeyValuePair<string, string>(icacls, quoted + " /setowner " + target.OwnerSid + " /C /Q"),
            new KeyValuePair<string, string>(icacls, quoted + (target.ResetFirst ? " /reset /C /Q" : " /grant " + SidAdmins + ":(RX) /C /Q")),
        }, AclTimeoutMs);
    }

    public static bool ContainsLinkedFolder(string dir)
    {
        return ContainsLinkedFolder(dir, null);
    }

    // unlock = was bei einem gesperrten Ordner zu tun ist; danach wird einmal neu versucht.
    static bool ContainsLinkedFolder(string dir, Action<string> unlock)
    {
        if (IsLinked(dir)) return true;
        string[] subs;
        try { subs = Directory.GetDirectories(dir); }
        catch (UnauthorizedAccessException)
        {
            if (unlock == null) throw;
            unlock(dir);
            subs = Directory.GetDirectories(dir);
        }
        foreach (string sub in subs)
            if (ContainsLinkedFolder(sub, unlock)) return true;
        return false;
    }

    public static bool IsTreeReadable(string dir)
    {
        try
        {
            Directory.GetFiles(dir);
            foreach (string sub in Directory.GetDirectories(dir))
                if (!IsTreeReadable(sub)) return false;
            return true;
        }
        catch (UnauthorizedAccessException) { return false; }
    }

    static bool IsLinked(string dir)
    {
        return (new DirectoryInfo(dir).Attributes & FileAttributes.ReparsePoint) != 0;
    }

    // ------------------------------------------------------------ Windows Defender

    // Launcher zuerst, dann seine Helfer. Das Spiel gehört nicht dazu.
    public static List<string> AllowedAppCandidates(string installDir, string socialClubDir)
    {
        var apps = new List<string>();
        if (!string.IsNullOrWhiteSpace(installDir))
        {
            string dir = installDir.Trim().Trim('"');
            foreach (string exe in new[] { "Launcher.exe", "LauncherPatcher.exe", "RockstarService.exe", "RockstarErrorHandler.exe" })
                apps.Add(Path.Combine(dir, exe));
        }
        if (!string.IsNullOrWhiteSpace(socialClubDir))
            apps.Add(Path.Combine(socialClubDir.Trim().Trim('"'), "SocialClubHelper.exe"));
        return apps;
    }

    // Nur echt von Rockstar signierte Dateien werden erlaubt. Eine ausgetauschte Datei bekommt keine Freigabe.
    // Bewusst keine allgemeine Defender-Ausnahme für den Ordner: Der Virenschutz prüft dort weiter.
    // true = der Launcher selbst ist erlaubt; fehlende Helfer sind kein Fehler.
    static bool AllowInControlledFolderAccess(List<string> candidates)
    {
        var allowed = new List<string>();
        foreach (string exe in candidates)
            if (IsRockstarSigned(exe)) allowed.Add(exe);
        if (allowed.Count == 0 || candidates[0] != allowed[0]) return false;

        // Über die Defender-Schnittstelle wie Add-MpPreference: ergänzt die Liste, ohne Vorhandenes zu überschreiben.
        using (var preference = new ManagementClass(@"root\Microsoft\Windows\Defender", "MSFT_MpPreference", null))
        using (ManagementBaseObject input = preference.GetMethodParameters("Add"))
        {
            input["ControlledFolderAccessAllowedApplications"] = allowed.ToArray();
            using (ManagementBaseObject output = preference.InvokeMethod("Add", input, null))
                return Convert.ToInt32(output["ReturnValue"]) == 0;
        }
    }

    public static bool IsRockstarSigned(string file)
    {
        if (!File.Exists(file) || !HasValidSignature(file)) return false;
        try { return IsRockstarSubject(X509Certificate.CreateFromSignedFile(file).Subject); }
        catch (Exception) { return false; }
    }

    // Die Organisation muss exakt stimmen. „OU=Rockstar Games“ oder ein ähnlicher Name reicht nicht.
    public static bool IsRockstarSubject(string subject)
    {
        return Regex.IsMatch(subject ?? "", @"(^|,\s*)O=""?Rockstar Games, Inc\.?""?\s*(,|$)");
    }

    static bool HasValidSignature(string file)
    {
        var fileInfo = new WinTrustFileInfo { FilePath = file };
        fileInfo.Size = Marshal.SizeOf(typeof(WinTrustFileInfo));
        IntPtr pointer = Marshal.AllocHGlobal(fileInfo.Size);
        try
        {
            Marshal.StructureToPtr(fileInfo, pointer, false);
            var data = new WinTrustData { File = pointer, UiChoice = 2 /* keine Anzeige */, UnionChoice = 1 /* Datei */ };
            data.Size = Marshal.SizeOf(typeof(WinTrustData));
            Guid verify = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
            return WinVerifyTrust(new IntPtr(-1), ref verify, ref data) == 0;
        }
        finally
        {
            Marshal.DestroyStructure(pointer, typeof(WinTrustFileInfo));
            Marshal.FreeHGlobal(pointer);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WinTrustFileInfo
    {
        public int Size;
        [MarshalAs(UnmanagedType.LPWStr)] public string FilePath;
        public IntPtr FileHandle;
        public IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct WinTrustData
    {
        public int Size;
        public IntPtr PolicyData, ClientData;
        public int UiChoice, RevocationChecks, UnionChoice;
        public IntPtr File;
        public int StateAction;
        public IntPtr StateData, Url;
        public int ProviderFlags, UiContext;
        public IntPtr SignatureSettings;
    }

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
    static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref WinTrustData data);

    // ------------------------------------------------------------ Launcher und Prozesse

    // Startet den Launcher, wenn er echt ist. false = bitte von Hand starten.
    public static bool StartLauncher()
    {
        string exe = LauncherExe();
        if (exe == null || !IsRockstarSigned(exe)) return false;
        using (Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true })) { }
        return true;
    }

    static string LauncherExe()
    {
        string dir = InstallDir();
        if (string.IsNullOrWhiteSpace(dir)) return null;
        string exe = Path.Combine(dir.Trim().Trim('"'), "Launcher.exe");
        return File.Exists(exe) ? exe : null;
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
            || name.StartsWith("PlayGTA", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "GTAVLauncher", StringComparison.OrdinalIgnoreCase);
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

    // Erst normal schließen, notfalls beenden.
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

    static string InstallDir() { return RegistryFolder("Launcher"); }

    static string SocialClubDir() { return RegistryFolder("Rockstar Games Social Club"); }

    static string RegistryFolder(string product)
    {
        try
        {
            using (RegistryKey root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32))
            using (RegistryKey key = root.OpenSubKey(@"SOFTWARE\Rockstar Games\" + product))
                return key == null ? null : key.GetValue("InstallFolder") as string;
        }
        catch (Exception) { return null; }
    }
}
