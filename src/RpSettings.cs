using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

// „Extended Texture Budget“ (FiveM-Grafikmenü) = vid_budgetScale in fivem.cfg.
// FiveM rechnet: Texturspeicher = 3 GB × (Wert / 12 + 1), mit GB = 1000 × 1024 × 1024 Bytes.
// Der Regler im Menü geht von 0 bis 19 (gemessen: 18 ist ein Schritt vor ganz rechts).
public static class TextureBudget
{
    public const string Convar = "vid_budgetScale";
    public const int SliderMax = 19;
    public const int MinValue = 10;                  // 50 % (aufgerundet)
    public const int MaxValue = 18;                  // 95 %
    public const int DefaultValue = MaxValue;
    public const string SettingKey = "TextureBudget";
    const double FiveMGB = 1000.0 * 1024 * 1024;

    // Die im Tool gemerkte Wahl; fehlt sie oder ist sie unlesbar, gilt die Vorgabe.
    public static int FromStored(string stored)
    {
        int v;
        return int.TryParse(stored, out v) ? Clamp(v) : DefaultValue;
    }

    public static string Label(int value)
    {
        return Percent(value) + " %  ·  " + FormatGb(BudgetBytes(value));
    }

    public static int Percent(int value)
    {
        return (int)Math.Round(value * 100.0 / SliderMax);
    }

    public static int Clamp(int value)
    {
        return Math.Max(MinValue, Math.Min(MaxValue, value));
    }

    public static long BudgetBytes(int value)
    {
        return (long)(3 * FiveMGB * (value / 12.0 + 1));
    }

    public static string FormatGb(long bytes)
    {
        return (bytes / (1024.0 * 1024 * 1024)).ToString("0.0", CultureInfo.GetCultureInfo("de-DE")) + " GB";
    }
}

public class SettingCheck
{
    public string Name, Current, Target;
    public bool Ok;
}

// Die wichtigsten FiveM-Einstellungen für Retro Roleplay.
public static class RpSettings
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    static double Num(string s, double fallback)
    {
        double d;
        return s != null && double.TryParse(s, NumberStyles.Float, Inv, out d) ? d : fallback;
    }

    static string Pct(string s)
    {
        return s == null ? "–" : (int)Math.Round(Num(s, 0) * 100) + " %";
    }

    static string OnOff(bool on) { return on ? "an" : "aus"; }

    static int Budget(string cfg)
    {
        int v;
        return int.TryParse(FiveMCfg.Get(cfg, TextureBudget.Convar), out v) ? v : 0;
    }

    public static List<SettingCheck> Check(string xml, string cfg, int budgetTarget = TextureBudget.DefaultValue)
    {
        var list = new List<SettingCheck>();
        int budget = Budget(cfg);
        budgetTarget = TextureBudget.Clamp(budgetTarget);
        list.Add(new SettingCheck
        {
            Name = "Extended Texture Budget",
            Current = TextureBudget.Percent(budget) + " %  (Stufe " + budget + ")",
            Target = TextureBudget.Percent(budgetTarget) + " %",
            Ok = budget == budgetTarget,
        });

        string fxaa = GtaGraphics.Get(xml, "FXAA_Enabled");
        list.Add(new SettingCheck { Name = "FXAA", Current = fxaa == null ? "–" : OnOff(fxaa == "true"), Target = "an", Ok = fxaa == "true" });

        string msaa = GtaGraphics.Get(xml, "MSAA");
        list.Add(new SettingCheck
        {
            Name = "MSAA", Current = msaa == null ? "–" : msaa == "0" ? "aus" : msaa + "×", Target = "aus", Ok = msaa == "0",
        });

        string vsync = GtaGraphics.Get(xml, "video", "VSync");
        list.Add(new SettingCheck { Name = "VSync", Current = vsync == null ? "–" : OnOff(vsync != "0"), Target = "aus", Ok = vsync == "0" });

        string density = GtaGraphics.Get(xml, "CityDensity");
        list.Add(new SettingCheck
        {
            Name = "Bevölkerungsdichte", Current = Pct(density), Target = "0 %", Ok = density != null && Num(density, 1) == 0,
        });

        string ped = GtaGraphics.Get(xml, "PedVarietyMultiplier"), veh = GtaGraphics.Get(xml, "VehicleVarietyMultiplier");
        list.Add(new SettingCheck
        {
            Name = "Bevölkerungsvielfalt",
            Current = Pct(ped),
            Target = "0 %",
            Ok = ped != null && veh != null && Num(ped, 1) == 0 && Num(veh, 1) == 0,
        });

        string lod = GtaGraphics.Get(xml, "LodScale");
        list.Add(new SettingCheck
        {
            Name = "Darstellungsdistanz", Current = Pct(lod), Target = "höchstens 95 %",
            Ok = lod != null && Num(lod, 1) <= 0.95 + 1e-6,
        });
        return list;
    }

    // Setzt nur, was nicht passt; alles andere in beiden Dateien bleibt unverändert.
    public static void Apply(ref string xml, ref string cfg, int budgetTarget = TextureBudget.DefaultValue)
    {
        budgetTarget = TextureBudget.Clamp(budgetTarget);
        if (Budget(cfg) != budgetTarget)
            cfg = FiveMCfg.Set(cfg, TextureBudget.Convar, budgetTarget.ToString(Inv));
        // Das Pausenmenü merkt sich VSync zusätzlich im Profil
        string profileVsync = FiveMCfg.Get(cfg, "profile_vidVsync");
        if (profileVsync != null && profileVsync != "0") cfg = FiveMCfg.Set(cfg, "profile_vidVsync", "0");

        var g = new Dictionary<string, string>();
        foreach (SettingCheck c in Check(xml, cfg))
        {
            if (c.Ok) continue;
            switch (c.Name)
            {
                case "FXAA": g["FXAA_Enabled"] = "true"; break;
                case "MSAA": g["MSAA"] = "0"; g["MSAAFragments"] = "0"; g["MSAAQuality"] = "0"; break;
                case "Bevölkerungsdichte": g["CityDensity"] = "0.000000"; break;
                case "Bevölkerungsvielfalt": g["PedVarietyMultiplier"] = "0.000000"; g["VehicleVarietyMultiplier"] = "0.000000"; break;
                case "Darstellungsdistanz": g["LodScale"] = "0.950000"; break;
                case "VSync": xml = GtaGraphics.Apply(xml, "video", new Dictionary<string, string> { { "VSync", "0" } }); break;
            }
        }
        if (g.Count > 0) xml = GtaGraphics.Apply(xml, g);
    }
}

// Dateien lesen/schreiben (Kodierung und BOM bleiben erhalten) und vor jeder Änderung den Stand speichern.
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
        if (File.Exists(path))
        {
            FileAttributes attrs = File.GetAttributes(path);
            if ((attrs & FileAttributes.ReadOnly) != 0) File.SetAttributes(path, attrs & ~FileAttributes.ReadOnly);
        }
        File.Copy(tmp, path, true);
        File.Delete(tmp);
    }

    const string IndexName = "dateien.txt";
    public const int MaxSnapshots = 20;

    // Gespeicherte Stände, älteste zuerst. Jeder Stand ist ein Unterordner „000001_JJJJMMTT-HHMMSS“.
    public static List<string> Snapshots(string root)
    {
        MigrateSingleBackup(root);
        var list = new List<string>();
        if (!Directory.Exists(root)) return list;
        foreach (string d in Directory.GetDirectories(root))
            if (File.Exists(Path.Combine(d, IndexName)) && Sequence(d) > 0) list.Add(d);
        list.Sort((a, b) => Sequence(a).CompareTo(Sequence(b)));
        return list;
    }

    public static DateTime SnapshotTime(string snapshot)
    {
        return File.GetLastWriteTime(Path.Combine(snapshot, IndexName));
    }

    static int Sequence(string dir)
    {
        string name = Path.GetFileName(dir);
        int us = name.IndexOf('_');
        int n;
        return us > 0 && int.TryParse(name.Substring(0, us), out n) ? n : 0;
    }

    // v1.2.0 hat eine einzelne Sicherung direkt im Ordner abgelegt – sie wird zum ältesten Stand (Nr. 1),
    // vorhandene Stände rücken dahinter.
    static void MigrateSingleBackup(string root)
    {
        string index = Path.Combine(root, IndexName);
        if (!File.Exists(index)) return;
        var later = new List<string>(Directory.GetDirectories(root));
        later.Sort((a, b) => Sequence(b).CompareTo(Sequence(a)));     // neueste zuerst umbenennen
        foreach (string d in later)
        {
            int seq = Sequence(d);
            if (seq <= 0) continue;
            string name = Path.GetFileName(d);
            Directory.Move(d, Path.Combine(root, (seq + 1).ToString("D6") + name.Substring(name.IndexOf('_'))));
        }
        string target = Path.Combine(root, "000001_" + File.GetLastWriteTime(index).ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(target);
        DateTime time = File.GetLastWriteTime(index);
        foreach (string f in Directory.GetFiles(root))
            File.Move(f, Path.Combine(target, Path.GetFileName(f)));
        File.SetLastWriteTime(Path.Combine(target, IndexName), time);
    }

    // Speichert den aktuellen Stand der Dateien als neuen Stand. Bei mehr als MaxSnapshots fällt der
    // zweitälteste weg – der älteste (Zustand vor der allerersten Änderung) bleibt immer erhalten.
    public static string Snapshot(string root, params string[] files)
    {
        List<string> existing = Snapshots(root);
        int next = existing.Count == 0 ? 1 : Sequence(existing[existing.Count - 1]) + 1;
        string dir = Path.Combine(root, next.ToString("D6") + "_" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(dir);
        var index = new StringBuilder();
        int n = 0;
        foreach (string f in files)
        {
            if (f == null || !File.Exists(f)) continue;
            string name = n++ + "_" + Path.GetFileName(f);
            string copy = Path.Combine(dir, name);
            File.Copy(f, copy, true);
            File.SetAttributes(copy, FileAttributes.Normal);
            index.Append(name).Append('|').Append(f).Append("\r\n");
        }
        File.WriteAllText(Path.Combine(dir, IndexName), index.ToString(), new UTF8Encoding(false));

        existing.Add(dir);
        while (existing.Count > MaxSnapshots)
        {
            Directory.Delete(existing[1], true);
            existing.RemoveAt(1);
        }
        return dir;
    }

    // Spielt den neuesten Stand zurück und entfernt ihn danach (ein Schritt zurück).
    public static int RestoreLatest(string root)
    {
        List<string> all = Snapshots(root);
        if (all.Count == 0) return 0;
        string dir = all[all.Count - 1];
        int restored = 0;
        foreach (string line in File.ReadAllLines(Path.Combine(dir, IndexName), Encoding.UTF8))
        {
            int bar = line.IndexOf('|');
            if (bar <= 0) continue;
            string saved = Path.Combine(dir, line.Substring(0, bar));
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
        Directory.Delete(dir, true);
        return restored;
    }
}
