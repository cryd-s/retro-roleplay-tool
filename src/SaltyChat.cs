using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using Microsoft.Win32;

// SaltyChat (TeamSpeak-3-Plugin von saltyhub.net): Version ermitteln und Plugin installieren.
public static class SaltyChat
{
    public const string FallbackVersion = "4.1.0";
    const string Site = "https://saltyhub.net";
    const string MarkerFile = "installed-version.txt";

    public static string PluginsDir
    {
        get
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TS3Client", "plugins");
        }
    }

    public static string DownloadUrl(string version) { return Site + "/download/" + version; }

    public static string FindDownloadScript(string html)
    {
        Match m = Regex.Match(html, @"/_next/static/chunks/app/download/page-[0-9a-f]+\.js");
        return m.Success ? m.Value : null;
    }

    public static string ParseVersion(string script)
    {
        Match m = Regex.Match(script, @"/download/(\d+(?:\.\d+)+)");
        return m.Success ? m.Groups[1].Value : null;
    }

    // Liest die aktuelle Version von saltyhub.net; klappt das nicht, gilt die zuletzt bekannte.
    public static string FetchLatestVersion(out bool fromFallback)
    {
        fromFallback = false;
        try
        {
            string script = FindDownloadScript(Downloader.GetString(Site + "/download"));
            string version = script == null ? null : ParseVersion(Downloader.GetString(Site + script));
            if (version != null) return version;
        }
        catch (Exception) { }
        fromFallback = true;
        return FallbackVersion;
    }

    // null = nicht installiert, "" = installiert, Version unbekannt
    public static string InstalledVersion(string pluginsDir)
    {
        bool present = File.Exists(Path.Combine(pluginsDir, "SaltyChat_win64.dll"))
                    || File.Exists(Path.Combine(pluginsDir, "SaltyChat.dll"));
        if (!present) return null;
        string marker = Path.Combine(pluginsDir, "SaltyChat", MarkerFile);
        return File.Exists(marker) ? File.ReadAllText(marker).Trim() : "";
    }

    // Prüft das Paket vollständig, bevor irgendetwas geschrieben wird.
    public static void InstallPlugin(string zipPath, string pluginsDir)
    {
        string root = Path.GetFullPath(pluginsDir).TrimEnd('\\') + "\\";
        using (ZipArchive zip = ZipFile.OpenRead(zipPath))
        {
            ZipArchiveEntry ini = zip.GetEntry("package.ini");
            if (ini == null) throw new InvalidDataException("Kein TeamSpeak-Plugin-Paket (package.ini fehlt).");
            Dictionary<string, string> info = ReadIni(ini);
            string name, version;
            info.TryGetValue("name", out name);
            info.TryGetValue("version", out version);
            if (name == null || name.IndexOf("Salty Chat", StringComparison.OrdinalIgnoreCase) < 0)
                throw new InvalidDataException("Das Paket ist kein SaltyChat-Plugin (Name: " + name + ").");

            var files = new List<KeyValuePair<ZipArchiveEntry, string>>();
            foreach (ZipArchiveEntry e in zip.Entries)
            {
                string entry = e.FullName.Replace('\\', '/');
                if (!entry.StartsWith("plugins/", StringComparison.OrdinalIgnoreCase) || entry.EndsWith("/")) continue;
                string dest = Path.GetFullPath(Path.Combine(root, entry.Substring("plugins/".Length).Replace('/', '\\')));
                if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Ungültiger Pfad im Paket: " + e.FullName);
                files.Add(new KeyValuePair<ZipArchiveEntry, string>(e, dest));
            }
            if (files.Count == 0) throw new InvalidDataException("Das Paket enthält keine Plugin-Dateien.");

            Directory.CreateDirectory(root);
            // Altlasten bis Version 2.1.2 laut SaltyHub-FAQ
            DeleteIfExists(Path.Combine(root, "SaltyChat.dll"));
            DeleteIfExists(Path.Combine(root, "SaltyChat", "VoiceDistortion.dll"));

            foreach (var f in files)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(f.Value));
                f.Key.ExtractToFile(f.Value, true);
            }

            Directory.CreateDirectory(Path.Combine(root, "SaltyChat"));
            File.WriteAllText(Path.Combine(root, "SaltyChat", MarkerFile), (version ?? "").Trim());
        }
    }

    static Dictionary<string, string> ReadIni(ZipArchiveEntry entry)
    {
        var result = new Dictionary<string, string>();
        using (var r = new StreamReader(entry.Open()))
        {
            string line;
            while ((line = r.ReadLine()) != null)
            {
                int eq = line.IndexOf('=');
                if (eq > 0) result[line.Substring(0, eq).Trim().ToLowerInvariant()] = line.Substring(eq + 1).Trim().Trim('"');
            }
        }
        return result;
    }

    static void DeleteIfExists(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}

// Microsoft Visual C++ Runtime x64 (für SaltyChat ab 2.3.3 mit TeamSpeak x64 nötig).
public static class VcRedist
{
    public const string Url = "https://aka.ms/vs/17/release/vc_redist.x64.exe";

    public static bool IsInstalled()
    {
        try
        {
            using (RegistryKey root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (RegistryKey key = root.OpenSubKey(@"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64"))
                return key != null && Convert.ToInt32(key.GetValue("Installed", 0)) == 1;
        }
        catch (Exception) { return false; }
    }

    // 0 = ok, 3010 = ok (Neustart empfohlen), 1638 = neuere Version schon da
    public static int RunSilentInstaller(string path)
    {
        var psi = new ProcessStartInfo(path, "/install /quiet /norestart") { UseShellExecute = true };
        using (Process p = Process.Start(psi))
        {
            p.WaitForExit();
            return p.ExitCode;
        }
    }
}
