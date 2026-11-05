using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using Microsoft.Win32;

// Steam-Dateien sind nicht offiziell dokumentiert. Das Format stammt aus Community-Quellen und wird deshalb
// streng geprüft: Passt eine Datei nicht zum erwarteten Aufbau, wird sie nicht angefasst (InvalidDataException).

public class SteamShortcut
{
    public byte[] Body;          // alle Felder inkl. abschließendem 0x08, unverändert wie in der Datei
    public string AppName, Exe;
    public uint AppId;
    public bool HasAppId;
}

// userdata\<Konto>\config\shortcuts.vdf: Nicht-Steam-Spiele, binäres Format.
public static class SteamShortcuts
{
    const byte TypeMap = 0, TypeString = 1, TypeInt = 2, TypeFloat = 3, TypeLong = 7, TypeEnd = 8;
    const string RootName = "shortcuts";

    public static uint Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        return ~crc;
    }

    // Steam leitet die ID eines Nicht-Steam-Spiels aus der Exe (mit Anführungszeichen) und dem Namen ab.
    public static uint ComputeAppId(string quotedExe, string name)
    {
        return Crc32(Encoding.UTF8.GetBytes(quotedExe + name)) | 0x80000000u;
    }

    static InvalidDataException Bad()
    {
        return new InvalidDataException("Die Steam-Datei shortcuts.vdf hat einen unbekannten Aufbau und wurde nicht verändert.");
    }

    static string ReadCString(byte[] d, ref int pos)
    {
        int start = pos;
        while (pos < d.Length && d[pos] != 0) pos++;
        if (pos >= d.Length) throw Bad();
        string s = Encoding.UTF8.GetString(d, start, pos - start);
        pos++;
        return s;
    }

    static byte ReadByte(byte[] d, ref int pos)
    {
        if (pos >= d.Length) throw Bad();
        return d[pos++];
    }

    static void Skip(byte[] d, ref int pos, int count)
    {
        if (pos + count > d.Length) throw Bad();
        pos += count;
    }

    // Liest eine Map bis einschließlich 0x08. entry != null: Felder der obersten Ebene übernehmen.
    static void ReadMap(byte[] d, ref int pos, SteamShortcut entry)
    {
        while (true)
        {
            byte type = ReadByte(d, ref pos);
            if (type == TypeEnd) return;
            string name = ReadCString(d, ref pos);
            switch (type)
            {
                case TypeMap:
                    ReadMap(d, ref pos, null);
                    break;
                case TypeString:
                    string value = ReadCString(d, ref pos);
                    if (entry != null && name.Equals("AppName", StringComparison.OrdinalIgnoreCase)) entry.AppName = value;
                    if (entry != null && name.Equals("Exe", StringComparison.OrdinalIgnoreCase)) entry.Exe = value;
                    break;
                case TypeInt:
                    int at = pos;
                    Skip(d, ref pos, 4);
                    if (entry != null && name.Equals("appid", StringComparison.OrdinalIgnoreCase))
                    {
                        entry.AppId = BitConverter.ToUInt32(d, at);
                        entry.HasAppId = true;
                    }
                    break;
                case TypeFloat:
                    Skip(d, ref pos, 4);
                    break;
                case TypeLong:
                    Skip(d, ref pos, 8);
                    break;
                default:
                    throw Bad();
            }
        }
    }

    public static List<SteamShortcut> Parse(byte[] data)
    {
        var list = new List<SteamShortcut>();
        if (data == null || data.Length == 0) return list;
        int pos = 0;
        if (ReadByte(data, ref pos) != TypeMap) throw Bad();
        if (!ReadCString(data, ref pos).Equals(RootName, StringComparison.OrdinalIgnoreCase)) throw Bad();
        while (true)
        {
            byte type = ReadByte(data, ref pos);
            if (type == TypeEnd) break;
            if (type != TypeMap) throw Bad();
            ReadCString(data, ref pos);
            int start = pos;
            var entry = new SteamShortcut();
            ReadMap(data, ref pos, entry);
            entry.Body = new byte[pos - start];
            Array.Copy(data, start, entry.Body, 0, entry.Body.Length);
            list.Add(entry);
        }
        if (ReadByte(data, ref pos) != TypeEnd || pos != data.Length) throw Bad();
        return list;
    }

    static void WriteText(Stream s, string text)
    {
        byte[] b = Encoding.UTF8.GetBytes(text);
        s.Write(b, 0, b.Length);
        s.WriteByte(0);
    }

    public static byte[] Build(List<SteamShortcut> list)
    {
        var ms = new MemoryStream();
        ms.WriteByte(TypeMap);
        WriteText(ms, RootName);
        for (int i = 0; i < list.Count; i++)
        {
            ms.WriteByte(TypeMap);
            WriteText(ms, i.ToString());
            ms.Write(list[i].Body, 0, list[i].Body.Length);
        }
        ms.WriteByte(TypeEnd);
        ms.WriteByte(TypeEnd);
        return ms.ToArray();
    }

    static void AddString(Stream s, string name, string value)
    {
        s.WriteByte(TypeString);
        WriteText(s, name);
        WriteText(s, value);
    }

    static void AddInt(Stream s, string name, uint value)
    {
        s.WriteByte(TypeInt);
        WriteText(s, name);
        s.Write(BitConverter.GetBytes(value), 0, 4);
    }

    public static SteamShortcut Create(string name, string exePath)
    {
        string quoted = "\"" + exePath + "\"";
        uint id = ComputeAppId(quoted, name);
        var ms = new MemoryStream();
        AddInt(ms, "appid", id);
        AddString(ms, "AppName", name);
        AddString(ms, "Exe", quoted);
        AddString(ms, "StartDir", "\"" + Path.GetDirectoryName(exePath) + "\\\"");
        AddString(ms, "icon", exePath);
        AddString(ms, "ShortcutPath", "");
        AddString(ms, "LaunchOptions", "");
        AddInt(ms, "IsHidden", 0);
        AddInt(ms, "AllowDesktopConfig", 1);
        AddInt(ms, "AllowOverlay", 1);
        AddInt(ms, "OpenVR", 0);
        AddInt(ms, "Devkit", 0);
        AddString(ms, "DevkitGameID", "");
        AddInt(ms, "DevkitOverrideAppID", 0);
        AddInt(ms, "LastPlayTime", 0);
        AddString(ms, "FlatpakAppID", "");
        ms.WriteByte(TypeMap);
        WriteText(ms, "tags");
        ms.WriteByte(TypeEnd);
        ms.WriteByte(TypeEnd);
        return new SteamShortcut { Body = ms.ToArray(), AppName = name, Exe = quoted, AppId = id, HasAppId = true };
    }

    public static bool IsFiveM(SteamShortcut s)
    {
        if (s.Exe == null) return false;
        string exe = s.Exe.Trim().Trim('"');
        int slash = exe.LastIndexOfAny(new[] { '\\', '/' });
        return exe.Substring(slash + 1).Equals("FiveM.exe", StringComparison.OrdinalIgnoreCase);
    }

    public static uint IdOf(SteamShortcut s)
    {
        return s.HasAppId ? s.AppId : ComputeAppId(s.Exe ?? "", s.AppName ?? "");
    }
}

// Textformat von Steam (localconfig.vdf, loginusers.vdf). Ändert gezielt einzelne Stellen, der Rest bleibt Zeichen für Zeichen gleich.
public static class TextVdf
{
    class Token
    {
        public char Kind;        // 's' = Text, '{', '}'
        public int Start, End;   // End = hinter dem Zeichen
        public string Value;
    }

    class Found
    {
        public int KeyToken, ValueToken;
        public int CloseToken = -1;   // bei Abschnitten: die schließende Klammer
    }

    static InvalidDataException Bad()
    {
        return new InvalidDataException("Eine Steam-Einstellungsdatei hat einen unbekannten Aufbau und wurde nicht verändert.");
    }

    static List<Token> Tokenize(string t)
    {
        var tokens = new List<Token>();
        int i = 0, depth = 0;
        while (i < t.Length)
        {
            char c = t[i];
            if (char.IsWhiteSpace(c) || c == '﻿') { i++; continue; }
            if (c == '/' && i + 1 < t.Length && t[i + 1] == '/')
            {
                while (i < t.Length && t[i] != '\n') i++;
                continue;
            }
            if (c == '{' || c == '}')
            {
                depth += c == '{' ? 1 : -1;
                if (depth < 0) throw Bad();
                tokens.Add(new Token { Kind = c, Start = i, End = i + 1 });
                i++;
                continue;
            }
            if (c != '"') throw Bad();
            int start = i++;
            while (i < t.Length && t[i] != '"') i += t[i] == '\\' ? 2 : 1;
            if (i >= t.Length) throw Bad();
            tokens.Add(new Token { Kind = 's', Start = start, End = i + 1, Value = t.Substring(start + 1, i - start - 1) });
            i++;
        }
        if (depth != 0) throw Bad();
        return tokens;
    }

    // Einträge zwischen den Tokens from (einschließlich) und to (ausschließlich)
    static List<Found> Entries(List<Token> tokens, int from, int to)
    {
        var list = new List<Found>();
        int i = from;
        while (i < to)
        {
            if (tokens[i].Kind != 's' || i + 1 >= to) throw Bad();
            var f = new Found { KeyToken = i, ValueToken = i + 1 };
            if (tokens[i + 1].Kind == 's')
            {
                i += 2;
            }
            else if (tokens[i + 1].Kind == '{')
            {
                int depth = 1, j = i + 2;
                while (j < to && depth > 0)
                {
                    if (tokens[j].Kind == '{') depth++;
                    else if (tokens[j].Kind == '}') depth--;
                    j++;
                }
                if (depth != 0) throw Bad();
                f.CloseToken = j - 1;
                i = j;
            }
            else throw Bad();
            list.Add(f);
        }
        return list;
    }

    static Found Find(List<Token> tokens, int from, int to, string key, bool wantBlock)
    {
        foreach (Found f in Entries(tokens, from, to))
        {
            if ((f.CloseToken >= 0) == wantBlock &&
                string.Equals(tokens[f.KeyToken].Value, key, StringComparison.OrdinalIgnoreCase))
                return f;
        }
        return null;
    }

    // Läuft den Pfad entlang; Ergebnis null = Abschnitt fehlt.
    static Found Walk(List<Token> tokens, string[] path, out int missingAt, out Found deepest)
    {
        int from = 0, to = tokens.Count;
        Found cur = null;
        deepest = null;
        missingAt = -1;
        for (int i = 0; i < path.Length; i++)
        {
            cur = Find(tokens, from, to, path[i], true);
            if (cur == null)
            {
                missingAt = i;
                return null;
            }
            deepest = cur;
            from = cur.ValueToken + 1;
            to = cur.CloseToken;
        }
        return cur;
    }

    public static string Get(string text, string[] path, string key)
    {
        List<Token> tokens = Tokenize(text ?? "");
        Entries(tokens, 0, tokens.Count);
        int missing;
        Found deepest;
        Found block = Walk(tokens, path, out missing, out deepest);
        if (block == null) return null;
        Found value = Find(tokens, block.ValueToken + 1, block.CloseToken, key, false);
        return value == null ? null : tokens[value.ValueToken].Value;
    }

    // Namen aller Abschnitte in einem Abschnitt
    public static List<string> Children(string text, string[] path)
    {
        var names = new List<string>();
        List<Token> tokens = Tokenize(text ?? "");
        int missing;
        Found deepest;
        Found block = Walk(tokens, path, out missing, out deepest);
        if (block == null) return names;
        foreach (Found f in Entries(tokens, block.ValueToken + 1, block.CloseToken))
            if (f.CloseToken >= 0) names.Add(tokens[f.KeyToken].Value);
        return names;
    }

    static string Quote(string s)
    {
        return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    // Stelle direkt vor der Zeile mit der schließenden Klammer
    static string InsertBeforeClose(string text, int closeStart, string lines, string nl)
    {
        int at = closeStart;
        while (at > 0 && (text[at - 1] == ' ' || text[at - 1] == '\t')) at--;
        string prefix = at > 0 && text[at - 1] != '\n' ? nl : "";
        return text.Substring(0, at) + prefix + lines + text.Substring(at);
    }

    public static string Set(string text, string[] path, string key, string value)
    {
        List<Token> tokens = Tokenize(text ?? "");
        Entries(tokens, 0, tokens.Count);
        string nl = text.Contains("\r\n") ? "\r\n" : "\n";
        int missing;
        Found deepest;
        Found block = Walk(tokens, path, out missing, out deepest);

        if (block != null)
        {
            Found existing = Find(tokens, block.ValueToken + 1, block.CloseToken, key, false);
            if (existing != null)
            {
                Token v = tokens[existing.ValueToken];
                return text.Substring(0, v.Start) + Quote(value) + text.Substring(v.End);
            }
            string line = new string('\t', path.Length) + Quote(key) + "\t\t" + Quote(value) + nl;
            return InsertBeforeClose(text, tokens[block.CloseToken].Start, line, nl);
        }

        // Der oberste Abschnitt muss da sein – sonst ist es nicht die erwartete Datei.
        if (missing == 0) throw Bad();

        var sb = new StringBuilder();
        for (int i = missing; i < path.Length; i++)
        {
            string tabs = new string('\t', i);
            sb.Append(tabs).Append(Quote(path[i])).Append(nl).Append(tabs).Append('{').Append(nl);
        }
        sb.Append(new string('\t', path.Length)).Append(Quote(key)).Append("\t\t").Append(Quote(value)).Append(nl);
        for (int i = path.Length - 1; i >= missing; i--)
            sb.Append(new string('\t', i)).Append('}').Append(nl);
        return InsertBeforeClose(text, tokens[deepest.CloseToken].Start, sb.ToString(), nl);
    }

    // Entfernt die Zeile mit dem Wert; fehlt er, bleibt der Text gleich.
    public static string Remove(string text, string[] path, string key)
    {
        List<Token> tokens = Tokenize(text ?? "");
        int missing;
        Found deepest;
        Found block = Walk(tokens, path, out missing, out deepest);
        if (block == null) return text;
        Found value = Find(tokens, block.ValueToken + 1, block.CloseToken, key, false);
        if (value == null) return text;
        int start = tokens[value.KeyToken].Start, end = tokens[value.ValueToken].End;
        while (start > 0 && (text[start - 1] == ' ' || text[start - 1] == '\t')) start--;
        if (end < text.Length && text[end] == '\r') end++;
        if (end < text.Length && text[end] == '\n') end++;
        return text.Substring(0, start) + text.Substring(end);
    }
}

public class SteamStatus
{
    public bool InSteam, ControllerOn;
}

// FiveM als Nicht-Steam-Spiel eintragen und Steam Input für diesen Eintrag einschalten.
public static class SteamLibrary
{
    const string ControllerKey = "UseSteamControllerConfig";
    const string ControllerForceOn = "2";      // laut Steam-Community: 0 = aus, 1 = Standard, 2 = erzwungen an
    const long SteamId64Base = 76561197960265728L;

    static string[] AppPath(uint appId)
    {
        return new[] { "UserLocalConfigStore", "apps", appId.ToString() };
    }

    public static string SteamDir
    {
        get
        {
            string dir = null;
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                    if (k != null) dir = k.GetValue("SteamPath") as string;
                if (string.IsNullOrEmpty(dir))
                    using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam"))
                        if (k != null) dir = k.GetValue("InstallPath") as string;
            }
            catch (Exception) { return null; }
            if (string.IsNullOrEmpty(dir)) return null;
            dir = dir.Replace('/', '\\');
            return File.Exists(Path.Combine(dir, "steam.exe")) ? dir : null;
        }
    }

    public static string PickUser(long activeUser, string loginUsers, IList<string> userDirs)
    {
        var dirs = new List<string>();
        foreach (string d in userDirs)
        {
            long n;
            if (d != "0" && long.TryParse(d, out n)) dirs.Add(d);
        }
        if (activeUser > 0 && dirs.Contains(activeUser.ToString())) return activeUser.ToString();

        if (!string.IsNullOrEmpty(loginUsers))
        {
            try
            {
                foreach (string id in TextVdf.Children(loginUsers, new[] { "users" }))
                {
                    long id64;
                    if (TextVdf.Get(loginUsers, new[] { "users", id }, "MostRecent") == "1" && long.TryParse(id, out id64))
                    {
                        string account = (id64 - SteamId64Base).ToString();
                        if (dirs.Contains(account)) return account;
                    }
                }
            }
            catch (InvalidDataException) { /* dann zählen nur die Ordner */ }
        }
        return dirs.Count == 1 ? dirs[0] : null;
    }

    // Konto-Ordner des aktuellen Steam-Nutzers; null = nicht eindeutig oder noch nie angemeldet
    public static string UserConfigDir(string steamDir)
    {
        string userdata = Path.Combine(steamDir, "userdata");
        if (!Directory.Exists(userdata)) return null;
        var names = new List<string>();
        foreach (string d in Directory.GetDirectories(userdata)) names.Add(Path.GetFileName(d));

        long active = 0;
        try
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam\ActiveProcess"))
                if (k != null) active = Convert.ToInt64(k.GetValue("ActiveUser", 0));
        }
        catch (Exception) { }

        string login = null;
        string loginPath = Path.Combine(steamDir, "config", "loginusers.vdf");
        try { if (File.Exists(loginPath)) login = File.ReadAllText(loginPath, Encoding.UTF8); }
        catch (Exception) { }

        string user = PickUser(active, login, names);
        return user == null ? null : Path.Combine(userdata, user, "config");
    }

    public static SteamStatus ReadStatus(string shortcutsPath, string localConfigPath)
    {
        var status = new SteamStatus();
        if (!File.Exists(shortcutsPath)) return status;
        foreach (SteamShortcut s in SteamShortcuts.Parse(File.ReadAllBytes(shortcutsPath)))
        {
            if (!SteamShortcuts.IsFiveM(s)) continue;
            status.InSteam = true;
            if (File.Exists(localConfigPath))
            {
                Encoding enc;
                string text = GameConfigFiles.ReadText(localConfigPath, out enc);
                status.ControllerOn = TextVdf.Get(text, AppPath(SteamShortcuts.IdOf(s)), ControllerKey) == ControllerForceOn;
            }
            break;
        }
        return status;
    }

    static void WriteBytes(string path, byte[] data)
    {
        string tmp = path + ".rrtool.tmp";
        File.WriteAllBytes(tmp, data);
        if (File.Exists(path))
        {
            FileAttributes a = File.GetAttributes(path);
            if ((a & FileAttributes.ReadOnly) != 0) File.SetAttributes(path, a & ~FileAttributes.ReadOnly);
        }
        File.Copy(tmp, path, true);
        File.Delete(tmp);
    }

    // Erst alles im Speicher vorbereiten; geschrieben wird nur, wenn beide Dateien in Ordnung sind.
    public static void AddFiveM(string shortcutsPath, string localConfigPath, string fiveMAppDir)
    {
        string exe = Path.Combine(Path.GetDirectoryName(fiveMAppDir.TrimEnd('\\', '/')), "FiveM.exe");
        if (!File.Exists(exe)) throw new FileNotFoundException("FiveM.exe wurde nicht gefunden: " + exe, exe);

        List<SteamShortcut> list = SteamShortcuts.Parse(File.Exists(shortcutsPath) ? File.ReadAllBytes(shortcutsPath) : null);
        SteamShortcut entry = list.Find(SteamShortcuts.IsFiveM);
        if (entry == null)
        {
            entry = SteamShortcuts.Create("FiveM", exe);
            list.Add(entry);
        }

        Encoding enc = new UTF8Encoding(false);
        string text = File.Exists(localConfigPath)
            ? GameConfigFiles.ReadText(localConfigPath, out enc)
            : "\"UserLocalConfigStore\"\n{\n}\n";
        string newText = TextVdf.Set(text, AppPath(SteamShortcuts.IdOf(entry)), ControllerKey, ControllerForceOn);

        Directory.CreateDirectory(Path.GetDirectoryName(shortcutsPath));
        WriteBytes(shortcutsPath, SteamShortcuts.Build(list));
        GameConfigFiles.WriteText(localConfigPath, newText, enc);
    }

    public static void RemoveFiveM(string shortcutsPath, string localConfigPath)
    {
        if (!File.Exists(shortcutsPath)) return;
        List<SteamShortcut> list = SteamShortcuts.Parse(File.ReadAllBytes(shortcutsPath));
        List<SteamShortcut> fiveM = list.FindAll(SteamShortcuts.IsFiveM);
        if (fiveM.Count == 0) return;
        list.RemoveAll(SteamShortcuts.IsFiveM);

        Encoding enc = new UTF8Encoding(false);
        string text = null;
        if (File.Exists(localConfigPath))
        {
            text = GameConfigFiles.ReadText(localConfigPath, out enc);
            foreach (SteamShortcut s in fiveM) text = TextVdf.Remove(text, AppPath(SteamShortcuts.IdOf(s)), ControllerKey);
        }
        WriteBytes(shortcutsPath, SteamShortcuts.Build(list));
        if (text != null) GameConfigFiles.WriteText(localConfigPath, text, enc);
    }

    public static string BackupDir
    {
        get
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                "RetroRoleplayTool", "Sicherung Steam");
        }
    }

    public static bool IsRunning()
    {
        Process[] found = Process.GetProcessesByName("steam");
        foreach (Process p in found) p.Dispose();
        return found.Length > 0;
    }

    // Beendet Steam auf dem normalen Weg (nie mit Gewalt). false = Steam läuft nach der Wartezeit noch.
    public static bool Shutdown(string steamDir, int timeoutMs)
    {
        using (Process.Start(Path.Combine(steamDir, "steam.exe"), "-shutdown")) { }
        int waited = 0;
        while (IsRunning())
        {
            if (waited >= timeoutMs) return false;
            Thread.Sleep(500);
            waited += 500;
        }
        return true;
    }

    public static void Start(string steamDir)
    {
        using (Process.Start(Path.Combine(steamDir, "steam.exe"))) { }
    }
}
