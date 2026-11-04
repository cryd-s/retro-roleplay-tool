using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

// TS-Moved-nicht-Bugfix: leitet bindloc.triofilmt.de per hosts-Datei auf 127.0.0.1 um.
// Schreibt den Eintrag nur einmal; Entfernen nimmt nur genau diesen Eintrag heraus.
public static class HostsFix
{
    public const string Host = "bindloc.triofilmt.de";
    public const string Entry = "127.0.0.1 " + Host;

    public static string HostsPath
    {
        get { return Path.Combine(Environment.SystemDirectory, "drivers", "etc", "hosts"); }
    }

    public static bool IsActive(string hostsText)
    {
        foreach (string line in SplitLines(hostsText))
            if (IsFixLine(line)) return true;
        return false;
    }

    public static string Add(string hostsText)
    {
        if (IsActive(hostsText)) return hostsText;
        string nl = NewLine(hostsText);
        string prefix = hostsText.Length == 0 || hostsText.EndsWith("\n") ? hostsText : hostsText + nl;
        return prefix + Entry + nl;
    }

    public static string Remove(string hostsText)
    {
        var kept = new List<string>();
        foreach (string line in SplitLines(hostsText))
            if (!IsFixLine(line)) kept.Add(line);
        return string.Join(NewLine(hostsText), kept);
    }

    public static void Apply(string hostsPath, bool enable)
    {
        string text = File.Exists(hostsPath) ? File.ReadAllText(hostsPath, Encoding.Default) : "";
        string updated = enable ? Add(text) : Remove(text);
        if (updated == text) return;

        FileAttributes attrs = File.Exists(hostsPath) ? File.GetAttributes(hostsPath) : FileAttributes.Normal;
        if ((attrs & FileAttributes.ReadOnly) != 0) File.SetAttributes(hostsPath, attrs & ~FileAttributes.ReadOnly);
        File.WriteAllText(hostsPath, updated, Encoding.Default);
        if ((attrs & FileAttributes.ReadOnly) != 0) File.SetAttributes(hostsPath, attrs);
    }

    public static void FlushDns()
    {
        var psi = new ProcessStartInfo("ipconfig", "/flushdns") { CreateNoWindow = true, UseShellExecute = false };
        using (Process p = Process.Start(psi)) p.WaitForExit(10000);
    }

    static bool IsFixLine(string line)
    {
        string t = line.Trim();
        int hash = t.IndexOf('#');
        if (hash >= 0) t = t.Substring(0, hash);
        string[] parts = t.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 1; i < parts.Length; i++)
            if (string.Equals(parts[i], Host, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    static string[] SplitLines(string text) { return Regex.Split(text, "\r\n|\n"); }

    static string NewLine(string text)
    {
        return text.Contains("\r\n") || !text.Contains("\n") ? "\r\n" : "\n";
    }
}
