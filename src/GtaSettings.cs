using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

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

    static Regex Element(string key)
    {
        return new Regex("(<" + Regex.Escape(key) + "\\s+value=\")([^\"]*)(\"\\s*/>)");
    }

    static Regex Section(string section)
    {
        return new Regex("(<" + section + ">)(.*?)(</" + section + ">)", RegexOptions.Singleline);
    }

    public static string Get(string xml, string key) { return Get(xml, "graphics", key); }

    public static string Get(string xml, string section, string key)
    {
        Match block = Section(section).Match(xml ?? "");
        if (!block.Success) return null;
        Match m = Element(key).Match(block.Groups[2].Value);
        return m.Success ? m.Groups[2].Value : null;
    }

    public static string Apply(string xml, IDictionary<string, string> values) { return Apply(xml, "graphics", values); }

    public static string Apply(string xml, string section, IDictionary<string, string> values)
    {
        Match block = Section(section).Match(xml ?? "");
        if (!block.Success) throw new InvalidDataException("Die Grafik-Einstellungsdatei hat keinen <" + section + ">-Bereich.");

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
                // fehlender Eintrag: vor dem Einzug des schließenden Tags einfügen
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

    public static bool SameValue(string a, string b)
    {
        double x, y;
        if (double.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out x) &&
            double.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out y))
            return Math.Abs(x - y) < 0.0001;
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}

