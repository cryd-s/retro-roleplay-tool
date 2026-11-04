using System;
using System.Collections.Generic;
using System.Diagnostics;

// Erkennt laufende FiveM-Prozesse (FiveM.exe und FiveM_*-Unterprozesse) und beendet sie.
public static class FiveMProcess
{
    public static bool IsFiveMProcessName(string name)
    {
        return string.Equals(name, "FiveM", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("FiveM_", StringComparison.OrdinalIgnoreCase);
    }

    public static List<Process> FindRunning()
    {
        var found = new List<Process>();
        foreach (Process p in Process.GetProcesses())
        {
            if (IsFiveMProcessName(p.ProcessName)) found.Add(p);
            else p.Dispose();
        }
        return found;
    }

    public static bool IsRunning()
    {
        List<Process> list = FindRunning();
        foreach (Process p in list) p.Dispose();
        return list.Count > 0;
    }

    public static void KillAll()
    {
        foreach (Process p in FindRunning())
        {
            using (p)
            {
                try { p.Kill(); p.WaitForExit(5000); }
                catch (Exception) { /* bereits beendet oder kein Zugriff */ }
            }
        }
    }
}
