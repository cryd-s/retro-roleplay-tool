using System;
using System.Collections.Generic;
using System.IO;

public class ModReport
{
    public readonly List<string> GtaFiles = new List<string>();
    public readonly List<string> PluginFiles = new List<string>();
    public int ModFileCount;
    public string GtaDir;       // null = Ordner unbekannt oder nicht vorhanden

    public int Total { get { return GtaFiles.Count + PluginFiles.Count + ModFileCount; } }
}

// Sucht nach Dateien, die sich in GTA/FiveM einklinken und Abstürze oder Grafikfehler auslösen können.
// Liest nur; gelöscht oder verschoben wird nichts.
public static class ModCheck
{
    // Im GTA-Ordner nur diese Namen: Dort liegen auch viele Original-DLLs des Spiels (z. B. d3dcompiler_46.dll).
    static readonly HashSet<string> GtaHookDlls = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "d3d8.dll", "d3d9.dll", "d3d10.dll", "d3d10_1.dll", "d3d10core.dll", "d3d11.dll", "d3d12.dll", "dxgi.dll",
        "dinput8.dll", "dsound.dll", "opengl32.dll", "version.dll", "winmm.dll", "ScriptHookV.dll",
    };

    public static ModReport Scan(string fiveMAppDir, string gtaDir)
    {
        var report = new ModReport();

        if (!string.IsNullOrEmpty(gtaDir) && Directory.Exists(gtaDir))
        {
            report.GtaDir = gtaDir;
            foreach (string name in FileNames(gtaDir))
                if (GtaHookDlls.Contains(name) || HasExtension(name, ".asi")) report.GtaFiles.Add(name);
        }

        string plugins = Path.Combine(fiveMAppDir, "plugins");
        if (Directory.Exists(plugins))
            foreach (string name in FileNames(plugins))
                if (HasExtension(name, ".dll") || HasExtension(name, ".asi") || HasExtension(name, ".addon64"))
                    report.PluginFiles.Add(name);

        string mods = Path.Combine(fiveMAppDir, "mods");
        if (Directory.Exists(mods))
            report.ModFileCount = Directory.GetFiles(mods, "*", SearchOption.AllDirectories).Length;

        return report;
    }

    static List<string> FileNames(string dir)
    {
        var names = new List<string>();
        foreach (string file in Directory.GetFiles(dir)) names.Add(Path.GetFileName(file));
        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    static bool HasExtension(string name, string extension)
    {
        return name.EndsWith(extension, StringComparison.OrdinalIgnoreCase);
    }
}
