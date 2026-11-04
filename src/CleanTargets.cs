using System;
using System.Collections.Generic;
using System.IO;


public class CleanTarget
{
    public string Name;
    public string Path;

    public CleanTarget(string name, string path)
    {
        Name = name;
        Path = path;
    }
}

// Die zu leerenden Ordner – immer für den gerade angemeldeten Nutzer.
public static class CleanTargets
{
    // full = false: Normaler Cache Clear; full = true: Gesamter Cache Clear (+ game-storage, nui-storage).
    // game-storage enthält u. a. das FiveM-Profil – danach muss der FiveM-Name neu gesetzt werden.
    public static List<CleanTarget> FiveM(string fiveMAppDir, bool full)
    {
        string data = System.IO.Path.Combine(fiveMAppDir, "data");
        var list = new List<CleanTarget>
        {
            new CleanTarget("cache", System.IO.Path.Combine(data, "cache")),
            new CleanTarget("server-cache", System.IO.Path.Combine(data, "server-cache")),
            new CleanTarget("server-cache-priv", System.IO.Path.Combine(data, "server-cache-priv")),
            new CleanTarget("logs", System.IO.Path.Combine(fiveMAppDir, "logs")),
            new CleanTarget("crashes", System.IO.Path.Combine(fiveMAppDir, "crashes")),
        };
        if (full)
        {
            list.Add(new CleanTarget("game-storage", System.IO.Path.Combine(data, "game-storage")));
            list.Add(new CleanTarget("nui-storage", System.IO.Path.Combine(data, "nui-storage")));
        }
        return list;
    }

    public static CleanTarget Temp(string tempPath)
    {
        return new CleanTarget("Windows Temp (%TEMP%)", tempPath.TrimEnd('\\', '/'));
    }

    public static string IniPath(string fiveMAppDir)
    {
        return System.IO.Path.Combine(fiveMAppDir, "CitizenFX.ini");
    }
}

