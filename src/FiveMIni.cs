using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

public enum IniResult { AlreadyStable, Changed, FileMissing }

// Liest und setzt den FiveM-Update-Kanal in der CitizenFX.ini.
// Geändert wird ausschließlich die Zeile "UpdateChannel=" im Abschnitt [Game].
public static class FiveMIni
{
    public const string StableChannel = "production";
    const string Section = "Game";
    const string Key = "UpdateChannel";

    public static string GetUpdateChannel(string iniText)
    {
        string[] lines = SplitLines(iniText);
        int start, end;
        if (!FindSection(lines, out start, out end)) return null;
        int idx = FindKey(lines, start, end);
        if (idx < 0) return null;
        return lines[idx].Substring(lines[idx].IndexOf('=') + 1).Trim();
    }

    public static string SetUpdateChannel(string iniText, string channel)
    {
        string nl = iniText.Contains("\r\n") || !iniText.Contains("\n") ? "\r\n" : "\n";
        var lines = new List<string>(SplitLines(iniText));
        string entry = Key + "=" + channel;

        int start, end;
        if (!FindSection(lines.ToArray(), out start, out end))
        {
            string prefix = iniText.Length == 0 || iniText.EndsWith("\n") ? iniText : iniText + nl;
            return prefix + "[" + Section + "]" + nl + entry + nl;
        }

        int idx = FindKey(lines.ToArray(), start, end);
        if (idx >= 0)
        {
            lines[idx] = entry;
        }
        else
        {
            // hinter der letzten nicht-leeren Zeile des Abschnitts einfügen
            int insertAt = end;
            while (insertAt > start + 1 && lines[insertAt - 1].Trim().Length == 0) insertAt--;
            lines.Insert(insertAt, entry);
        }
        return string.Join(nl, lines);
    }

    public static IniResult EnsureStable(string iniPath)
    {
        if (!File.Exists(iniPath)) return IniResult.FileMissing;

        byte[] bytes = File.ReadAllBytes(iniPath);
        bool bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        int offset = bom ? 3 : 0;

        Encoding enc;
        string text;
        try
        {
            enc = new UTF8Encoding(false, true);
            text = enc.GetString(bytes, offset, bytes.Length - offset);
        }
        catch (DecoderFallbackException)
        {
            enc = Encoding.Default; // ANSI-Datei
            text = enc.GetString(bytes);
        }

        string updated = SetUpdateChannel(text, StableChannel);
        if (updated == text) return IniResult.AlreadyStable;

        string backup = iniPath + ".bak";
        if (!File.Exists(backup)) File.Copy(iniPath, backup);

        byte[] body = enc.GetBytes(updated);
        using (var fs = new FileStream(iniPath, FileMode.Create, FileAccess.Write))
        {
            if (bom) fs.Write(new byte[] { 0xEF, 0xBB, 0xBF }, 0, 3);
            fs.Write(body, 0, body.Length);
        }
        return IniResult.Changed;
    }

    public static string DisplayName(string channel)
    {
        if (channel == null) return "Stable (Standard)";
        switch (channel.ToLowerInvariant())
        {
            case "production": return "Stable";
            case "beta": return "Beta";
            case "canary": return "Unstable (Canary)";
            default: return channel;
        }
    }

    static string[] SplitLines(string text)
    {
        return Regex.Split(text, "\r\n|\n");
    }

    // start = Index der Kopfzeile, end = Index der nächsten Kopfzeile bzw. Zeilenanzahl
    static bool FindSection(string[] lines, out int start, out int end)
    {
        start = -1;
        end = lines.Length;
        for (int i = 0; i < lines.Length; i++)
        {
            string t = lines[i].Trim();
            bool header = t.StartsWith("[") && t.EndsWith("]");
            if (!header) continue;
            if (start >= 0) { end = i; break; }
            if (string.Equals(t.Substring(1, t.Length - 2).Trim(), Section, StringComparison.OrdinalIgnoreCase))
                start = i;
        }
        if (start < 0) return false;
        // Bei Text mit abschließendem Zeilenumbruch ist die letzte "Zeile" leer – nicht zum Abschnitt zählen.
        if (end == lines.Length && lines.Length > 0 && lines[lines.Length - 1].Length == 0) end--;
        return true;
    }

    static int FindKey(string[] lines, int start, int end)
    {
        for (int i = start + 1; i < end; i++)
        {
            int eq = lines[i].IndexOf('=');
            if (eq > 0 && string.Equals(lines[i].Substring(0, eq).Trim(), Key, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }
}
