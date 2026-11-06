using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

public static class SystemCommands
{
    // 0 = alles geklappt, sonst der Fehlercode des ersten fehlgeschlagenen Befehls. Läuft alle Befehle durch.
    public static int RunAll(IEnumerable<KeyValuePair<string, string>> commands)
    {
        const int TimeoutMs = 30000;
        const int TimedOut = -1;
        int result = 0;
        foreach (KeyValuePair<string, string> command in commands)
        {
            var psi = new ProcessStartInfo(command.Key, command.Value) { CreateNoWindow = true, UseShellExecute = false };
            int code;
            using (Process p = Process.Start(psi))
                code = p.WaitForExit(TimeoutMs) ? p.ExitCode : TimedOut;
            if (result == 0) result = code;
        }
        return result;
    }
}

// Netzwerk Fix: Winsock und TCP/IP zurücksetzen, DNS-Cache leeren.
public static class NetworkFix
{
    public const string ElevatedArgument = "--network-fix";

    public const string ConfirmText =
        "Der Fix führt diese Befehle mit Administratorrechten aus:\n\n" +
        "•  netsh winsock reset\n" +
        "•  netsh int ip reset\n" +
        "•  ipconfig /flushdns\n\n" +
        "Danach musst du den PC neu starten.\n\n" +
        "Achtung: Feste IP-Adressen und eigene DNS-Einstellungen können dabei verloren gehen, " +
        "und manche VPN-Programme müssen neu eingerichtet werden. " +
        "Deine aktuellen Netzwerkdaten speichert das Tool vorher in eine Textdatei.\n\nFortfahren?";

    public static string BackupDir
    {
        get
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                "RetroRoleplayTool", "Sicherung Netzwerk");
        }
    }

    // Vollständige Pfade: Dieser Teil läuft mit Administratorrechten.
    public static List<KeyValuePair<string, string>> Commands()
    {
        string netsh = Path.Combine(Environment.SystemDirectory, "netsh.exe");
        return new List<KeyValuePair<string, string>>
        {
            new KeyValuePair<string, string>(netsh, "winsock reset"),
            new KeyValuePair<string, string>(netsh, "int ip reset"),
            new KeyValuePair<string, string>(Path.Combine(Environment.SystemDirectory, "ipconfig.exe"), "/flushdns"),
        };
    }

    // Speichert „ipconfig /all“ als Textdatei und liefert ihren Pfad. So lassen sich feste Adressen nachschlagen.
    public static string SaveSnapshot(string backupDir)
    {
        var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "ipconfig.exe"), "/all")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            StandardOutputEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage),
        };
        string output;
        using (Process p = Process.Start(psi))
        {
            output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
        }
        Directory.CreateDirectory(backupDir);
        string file = Path.Combine(backupDir, "Netzwerk " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + ".txt");
        File.WriteAllText(file, output, new UTF8Encoding(true));
        return file;
    }

    // backupFile = null, wenn das Speichern nicht geklappt hat
    public static string DoneText(int exitCode, string backupFile)
    {
        string text = exitCode == 0
            ? "Alle Befehle wurden ausgeführt.\n\nStarte jetzt bitte den PC neu, erst dann wirkt der Fix."
            : "Alle Befehle wurden gestartet, mindestens einer meldet aber einen Fehler (Code " + exitCode + ").\n\n" +
              "Starte den PC trotzdem neu. Hilft das nicht, gib die Befehle einzeln in einer Eingabeaufforderung als Administrator ein, " +
              "dann siehst du die Fehlermeldung.";
        if (backupFile != null)
            text += "\n\nDeine Netzwerkdaten von vorher liegen hier:\n" + backupFile;
        return text;
    }
}
