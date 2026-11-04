using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

// Einstellungen, die FiveM wirklich verwendet. FiveM hat dafür eigene Dateien, getrennt vom normalen GTA V:
//   %APPDATA%\CitizenFX\gta5_settings.xml  – Grafik (bei alten Installationen noch Dokumente\Rockstar Games\GTA V\settings.xml)
//   %APPDATA%\CitizenFX\fivem.cfg          – FiveM-Optionen, u. a. vid_budgetScale = „Extended Texture Budget“
// Quelle: FiveM-Quellcode (gta-core-five/VidBehavior.cpp, gta-streaming-five/PatchExtendedBudgeting.cpp).

// Zeilen wie  seta "vid_budgetScale" "18"  in fivem.cfg lesen und setzen; alles andere bleibt unverändert.
public static class FiveMCfg
{
    static Regex LineFor(string name)
    {
        return new Regex("^[ \\t]*seta?[ \\t]+\"?" + Regex.Escape(name) + "\"?[ \\t]+\"?([^\"\\r\\n]*?)\"?[ \\t]*\\r?$",
                         RegexOptions.Multiline | RegexOptions.IgnoreCase);
    }

    public static string Get(string cfg, string name)
    {
        Match m = LineFor(name).Match(cfg ?? "");
        return m.Success ? m.Groups[1].Value : null;
    }

    public static string Set(string cfg, string name, string value)
    {
        cfg = cfg ?? "";
        string line = "seta \"" + name + "\" \"" + value + "\"";
        Regex re = LineFor(name);
        if (re.IsMatch(cfg))
            return re.Replace(cfg, m => m.Value.EndsWith("\r") ? line + "\r" : line);

        string nl = cfg.Contains("\r\n") || cfg.Length == 0 ? "\r\n" : "\n";
        if (cfg.Length > 0 && !cfg.EndsWith("\n")) cfg += nl;
        return cfg + line + nl;
    }
}

// Werte im <graphics>-Block der GTA-Einstellungsdatei lesen und setzen. Auflösung/Bildschirm (<video>) bleiben unberührt.
public static class GtaGraphics
{
    public class Preset
    {
        public string Name, Detail;
        public Dictionary<string, string> Values;
    }

    // Gemeinsam für alle Profile: keine Bewegungs- und Tiefenunschärfe, FXAA statt teurem MSAA,
    // die „Erweiterten Grafikeinstellungen“ (sehr teuer, für RP unnötig) aus.
    static Dictionary<string, string> Base(params string[] pairs)
    {
        var d = new Dictionary<string, string>
        {
            { "MSAA", "0" }, { "MSAAFragments", "0" }, { "MSAAQuality", "0" }, { "ReflectionMSAA", "0" },
            { "FXAA_Enabled", "true" }, { "TXAA_Enabled", "false" },
            { "DoF", "false" }, { "MotionBlurStrength", "0.000000" },
            { "UltraShadows_Enabled", "false" }, { "Shadow_LongShadows", "false" }, { "HdStreamingInFlight", "false" },
            { "AnisotropicFiltering", "16" },
        };
        for (int i = 0; i + 1 < pairs.Length; i += 2) d[pairs[i]] = pairs[i + 1];
        return d;
    }

    public static readonly Preset Performance = new Preset
    {
        Name = "Leistung",
        Detail = "mehr FPS · Schatten, Gras, Wasser, Spiegelungen niedrig",
        Values = Base("ShadowQuality", "1", "Shadow_SoftShadows", "1", "ReflectionQuality", "0", "SSAO", "0",
                      "ParticleQuality", "0", "WaterQuality", "0", "GrassQuality", "0", "ShaderQuality", "1",
                      "PostFX", "0", "Tessellation", "0", "Shadow_ParticleShadows", "false"),
    };

    public static readonly Preset Balanced = new Preset
    {
        Name = "Ausgewogen",
        Detail = "gute Optik bei stabilen FPS",
        Values = Base("ShadowQuality", "2", "Shadow_SoftShadows", "2", "ReflectionQuality", "2", "SSAO", "1",
                      "ParticleQuality", "1", "WaterQuality", "1", "GrassQuality", "1", "ShaderQuality", "2",
                      "PostFX", "1", "Tessellation", "1", "Shadow_ParticleShadows", "true"),
    };

    public static readonly Preset Quality = new Preset
    {
        Name = "Qualität",
        Detail = "für starke PCs · hohe Details",
        Values = Base("ShadowQuality", "3", "Shadow_SoftShadows", "3", "ReflectionQuality", "3", "SSAO", "2",
                      "ParticleQuality", "2", "WaterQuality", "2", "GrassQuality", "2", "ShaderQuality", "2",
                      "PostFX", "2", "Tessellation", "3", "Shadow_ParticleShadows", "true"),
    };

    public static readonly Preset[] All = { Performance, Balanced, Quality };

    static readonly Regex Block = new Regex("(<graphics>)(.*?)(</graphics>)", RegexOptions.Singleline);

    static Regex Element(string key)
    {
        return new Regex("(<" + Regex.Escape(key) + "\\s+value=\")([^\"]*)(\"\\s*/>)");
    }

    public static string Get(string xml, string key)
    {
        Match block = Block.Match(xml ?? "");
        if (!block.Success) return null;
        Match m = Element(key).Match(block.Groups[2].Value);
        return m.Success ? m.Groups[2].Value : null;
    }

    public static string Apply(string xml, IDictionary<string, string> values)
    {
        Match block = Block.Match(xml ?? "");
        if (!block.Success) throw new InvalidDataException("Die Grafik-Einstellungsdatei hat keinen <graphics>-Bereich.");

        string body = block.Groups[2].Value;
        string nl = body.Contains("\r\n") ? "\r\n" : "\n";
        string indent = "    ";
        Match firstIndent = Regex.Match(body, "\\n([ \\t]+)<");
        if (firstIndent.Success) indent = firstIndent.Groups[1].Value;

        foreach (var kv in values)
        {
            Regex re = Element(kv.Key);
            string v = kv.Value;
            if (re.IsMatch(body))
            {
                body = re.Replace(body, m => m.Groups[1].Value + v + m.Groups[3].Value);
            }
            else
            {
                // fehlender Eintrag: vor dem Einzug von </graphics> einfügen
                string trimmed = body.TrimEnd(' ', '\t');
                string tail = body.Substring(trimmed.Length);
                if (!trimmed.EndsWith("\n")) trimmed += nl;
                body = trimmed + indent + "<" + kv.Key + " value=\"" + v + "\" />" + nl + tail;
            }
        }
        return xml.Substring(0, block.Groups[2].Index) + body + xml.Substring(block.Groups[2].Index + block.Groups[2].Length);
    }

    // Welches Profil ist gerade gesetzt? null = eigene Einstellungen
    public static Preset Matching(string xml)
    {
        foreach (Preset p in All)
        {
            bool all = true;
            foreach (var kv in p.Values)
            {
                string cur = Get(xml, kv.Key);
                if (cur == null || !SameValue(cur, kv.Value)) { all = false; break; }
            }
            if (all) return p;
        }
        return null;
    }

    static bool SameValue(string a, string b)
    {
        double x, y;
        if (double.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out x) &&
            double.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out y))
            return Math.Abs(x - y) < 0.0001;
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}

// „Extended Texture Budget“ (FiveM-Grafikmenü) = vid_budgetScale in fivem.cfg.
// FiveM rechnet: Texturspeicher = 3 GB × (Wert / 12 + 1), mit GB = 1000 × 1024 × 1024 Bytes.
public static class TextureBudget
{
    public const string Convar = "vid_budgetScale";
    public const int Max = 24;                       // ×3 – höher setzt das Tool nie
    const double FiveMGB = 1000.0 * 1024 * 1024;

    public static long BudgetBytes(int value)
    {
        return (long)(3 * FiveMGB * (value / 12.0 + 1));
    }

    // Empfehlung: Texturen dürfen höchstens etwa die Hälfte des Grafikspeichers belegen,
    // der Rest bleibt für Spiel, Mods und Windows. -1 = Grafikspeicher unbekannt.
    public static int Recommended(long vramBytes)
    {
        if (vramBytes <= 0) return -1;
        double v = 12 * (vramBytes * 0.5 / (3 * FiveMGB) - 1);
        if (v < 0) return 0;
        return Math.Min(Max, (int)Math.Floor(v));
    }

    // Größte Grafikkarte im System (Name, Speicher in Bytes); Speicher 0 = unbekannt.
    public static KeyValuePair<string, long> DetectGpu()
    {
        string bestName = null;
        long best = 0;
        try
        {
            using (RegistryKey cls = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"))
            {
                if (cls != null)
                {
                    foreach (string sub in cls.GetSubKeyNames())
                    {
                        try
                        {
                            using (RegistryKey k = cls.OpenSubKey(sub))
                            {
                                if (k == null) continue;
                                long mem = ToLong(k.GetValue("HardwareInformation.qwMemorySize"));
                                if (mem <= 0) mem = ToLong(k.GetValue("HardwareInformation.MemorySize"));
                                if (mem > best)
                                {
                                    best = mem;
                                    bestName = k.GetValue("DriverDesc") as string;
                                }
                            }
                        }
                        catch (Exception) { /* z. B. „Properties“ ohne Leserecht */ }
                    }
                }
            }
        }
        catch (Exception) { }
        return new KeyValuePair<string, long>(bestName, best);
    }

    static long ToLong(object o)
    {
        if (o is long) return (long)o;
        if (o is int) return (uint)(int)o;
        var bytes = o as byte[];
        if (bytes != null && bytes.Length >= 8) return BitConverter.ToInt64(bytes, 0);
        if (bytes != null && bytes.Length >= 4) return BitConverter.ToUInt32(bytes, 0);
        return 0;
    }

    public static string FormatGb(long bytes)
    {
        return (bytes / (1024.0 * 1024 * 1024)).ToString("0.0", CultureInfo.GetCultureInfo("de-DE")) + " GB";
    }
}

// Dateien lesen/schreiben (Kodierung und BOM bleiben erhalten) und eine einmalige Sicherung anlegen.
public static class GameConfigFiles
{
    public static string CitizenFxDir
    {
        get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CitizenFX"); }
    }

    public static string CfgPath { get { return Path.Combine(CitizenFxDir, "fivem.cfg"); } }

    // Die Grafikdatei, die FiveM benutzt; null = noch nicht vorhanden (FiveM noch nie gestartet)
    public static string GraphicsPath
    {
        get
        {
            string own = Path.Combine(CitizenFxDir, "gta5_settings.xml");
            if (File.Exists(own)) return own;
            string legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                                         "Rockstar Games", "GTA V", "settings.xml");
            return File.Exists(legacy) ? legacy : null;
        }
    }

    public static string DefaultBackupDir
    {
        get
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                "RetroRoleplayTool", "Sicherung GTA-Einstellungen");
        }
    }

    public static string ReadText(string path, out Encoding encoding)
    {
        byte[] bytes = File.ReadAllBytes(path);
        bool bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        encoding = new UTF8Encoding(bom);
        return new UTF8Encoding(false).GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));
    }

    public static void WriteText(string path, string text, Encoding encoding)
    {
        string tmp = path + ".rrtool.tmp";
        File.WriteAllText(tmp, text, encoding);
        FileAttributes attrs = File.GetAttributes(path);
        if ((attrs & FileAttributes.ReadOnly) != 0) File.SetAttributes(path, attrs & ~FileAttributes.ReadOnly);
        File.Copy(tmp, path, true);
        File.Delete(tmp);
    }

    const string IndexName = "dateien.txt";

    public static bool HasBackup(string backupDir)
    {
        return File.Exists(Path.Combine(backupDir, IndexName));
    }

    public static DateTime BackupTime(string backupDir)
    {
        return File.GetLastWriteTime(Path.Combine(backupDir, IndexName));
    }

    // Sichert die Dateien nur, wenn es noch keine Sicherung gibt – so bleibt der Zustand vor der ersten Änderung erhalten.
    public static void BackupOnce(string backupDir, params string[] files)
    {
        if (HasBackup(backupDir)) return;
        Directory.CreateDirectory(backupDir);
        var index = new StringBuilder();
        int n = 0;
        foreach (string f in files)
        {
            if (f == null || !File.Exists(f)) continue;
            string name = n++ + "_" + Path.GetFileName(f);
            File.Copy(f, Path.Combine(backupDir, name), true);
            index.Append(name).Append('|').Append(f).Append("\r\n");
        }
        File.WriteAllText(Path.Combine(backupDir, IndexName), index.ToString(), new UTF8Encoding(false));
    }

    // Spielt die Sicherung zurück und löscht sie danach.
    public static int Restore(string backupDir)
    {
        string indexPath = Path.Combine(backupDir, IndexName);
        int restored = 0;
        foreach (string line in File.ReadAllLines(indexPath, Encoding.UTF8))
        {
            int bar = line.IndexOf('|');
            if (bar <= 0) continue;
            string saved = Path.Combine(backupDir, line.Substring(0, bar));
            string target = line.Substring(bar + 1);
            if (!File.Exists(saved)) continue;
            if (File.Exists(target))
            {
                FileAttributes a = File.GetAttributes(target);
                if ((a & FileAttributes.ReadOnly) != 0) File.SetAttributes(target, a & ~FileAttributes.ReadOnly);
            }
            File.Copy(saved, target, true);
            restored++;
        }
        Directory.Delete(backupDir, true);
        return restored;
    }
}
