using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

// Einfache Einstellungsdatei (Schlüssel=Wert), standardmäßig %APPDATA%\RetroRoleplayTool\settings.ini
public class AppSettings
{
    readonly string path;
    readonly Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public static string DefaultPath
    {
        get
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                "RetroRoleplayTool", "settings.ini");
        }
    }

    public AppSettings(string path)
    {
        this.path = path;
        try
        {
            if (!File.Exists(path)) return;
            foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
            {
                int eq = line.IndexOf('=');
                if (eq > 0) values[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
        }
        catch (Exception) { /* kaputte/gesperrte Datei: mit leeren Einstellungen weiterarbeiten */ }
    }

    public string Get(string key)
    {
        string v;
        return values.TryGetValue(key, out v) && v.Length > 0 ? v : null;
    }

    public void Set(string key, string value)
    {
        values[key] = value ?? "";
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var sb = new StringBuilder();
        foreach (var kv in values) sb.Append(kv.Key).Append('=').Append(kv.Value).Append("\r\n");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }
}
