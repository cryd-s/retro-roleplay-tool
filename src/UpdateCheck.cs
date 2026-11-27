using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

public class UpdateInfo
{
    public string Version, ZipUrl;
    // Für das Selbst-Update: die einzelne exe dieses Releases und ihre SHA-256 laut GitHub (null = nicht vorhanden)
    public string ExeUrl, ExeSha256;

    public bool CanSelfUpdate { get { return ExeUrl != null && ExeSha256 != null; } }
}

// Fragt bei GitHub die neueste Version ab und liefert die Links zur ZIP und zur exe.
public static class UpdateCheck
{
    const string LatestReleaseApi = "https://api.github.com/repos/cryd-s/retro-roleplay-tool/releases/latest";
    const string DownloadPrefix = "https://github.com/cryd-s/retro-roleplay-tool/releases/download/";
    const string ZipName = "RetroRoleplayTool.zip";
    const string ExeName = "RetroRoleplayTool.exe";

    // Wirft bei Netzwerkfehlern; der Aufrufer zeigt dann einfach keinen Hinweis.
    public static UpdateInfo Fetch(string currentVersion)
    {
        return Evaluate(currentVersion, Downloader.GetString(LatestReleaseApi));
    }

    // null = keine neuere Version oder keine brauchbare Antwort.
    // Ohne hochgeladene ZIP (z. B. Release noch im Aufbau) gibt es keinen Hinweis, damit der Link nie ins Leere führt.
    public static UpdateInfo Evaluate(string currentVersion, string json)
    {
        Dictionary<string, object> root;
        try { root = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>; }
        catch (ArgumentException) { return null; }
        catch (InvalidOperationException) { return null; }
        if (root == null) return null;

        Match tag = Regex.Match(Text(root, "tag_name"), @"^v?(\d+\.\d+\.\d+)$");
        if (!tag.Success) return null;
        string latest = tag.Groups[1].Value;
        if (Versions.Compare(currentVersion, latest) >= 0) return null;

        string zip = ZipUrl(root);
        if (zip == null) return null;
        var info = new UpdateInfo { Version = latest, ZipUrl = zip };

        // Selbst-Update nur mit der exe genau dieses Releases und einer SHA-256 von GitHub
        Dictionary<string, object> exe = Asset(root, ExeName);
        if (exe != null)
        {
            string url = Text(exe, "browser_download_url");
            Match digest = Regex.Match(Text(exe, "digest"), "^sha256:([0-9a-f]{64})$");
            if (url == DownloadPrefix + Text(root, "tag_name") + "/" + ExeName && digest.Success)
            {
                info.ExeUrl = url;
                info.ExeSha256 = digest.Groups[1].Value;
            }
        }
        return info;
    }

    static Dictionary<string, object> Asset(Dictionary<string, object> root, string name)
    {
        object value;
        if (!root.TryGetValue("assets", out value)) return null;
        var assets = value as object[];
        if (assets == null) return null;
        foreach (object item in assets)
        {
            var asset = item as Dictionary<string, object>;
            if (asset != null && Text(asset, "name") == name && Text(asset, "state") == "uploaded") return asset;
        }
        return null;
    }

    static string ZipUrl(Dictionary<string, object> root)
    {
        object value;
        if (!root.TryGetValue("assets", out value)) return null;
        var assets = value as object[];
        if (assets == null) return null;
        foreach (object item in assets)
        {
            var asset = item as Dictionary<string, object>;
            if (asset == null || Text(asset, "name") != ZipName || Text(asset, "state") != "uploaded") continue;
            // Der Link wird im Browser geöffnet, deshalb nur Downloads aus diesem Repository.
            string url = Text(asset, "browser_download_url");
            if (url.StartsWith(DownloadPrefix, StringComparison.Ordinal)) return url;
        }
        return null;
    }

    static string Text(Dictionary<string, object> map, string key)
    {
        object value;
        return map.TryGetValue(key, out value) ? value as string ?? "" : "";
    }
}

// Das Tool tauscht sich selbst aus: Eine laufende exe lässt sich unter Windows umbenennen, aber nicht überschreiben.
// Ablauf: neue Version als „.new“ daneben laden und prüfen, die laufende in „.old“ umbenennen, die neue an ihren
// Platz legen, neu starten. Die neue Version räumt „.old“ beim Start weg.
public static class SelfUpdate
{
    public const string UpdatedArgument = "--updated";

    public static string NewPath(string exe) { return exe + ".new"; }
    public static string OldPath(string exe) { return exe + ".old"; }

    public static bool CanWriteNextTo(string exe)
    {
        try
        {
            string probe = Path.Combine(Path.GetDirectoryName(exe), ".rrtool-schreibtest-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception) { return false; }
    }

    // Wirft InvalidDataException, wenn die Datei nicht genau das erwartete Programm in der erwarteten Version ist.
    public static void Verify(string file, string expectedSha256, string expectedName, string expectedVersion)
    {
        if (string.IsNullOrEmpty(expectedSha256) ||
            !string.Equals(Downloader.Sha256OfFile(file), expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Die Prüfsumme des Updates stimmt nicht.");
        AssemblyName name;
        try { name = AssemblyName.GetAssemblyName(file); }
        catch (BadImageFormatException) { throw new InvalidDataException("Das Update ist kein gültiges Programm."); }
        string version = name.Version.Major + "." + name.Version.Minor + "." + name.Version.Build;
        if (!string.Equals(name.Name, expectedName, StringComparison.Ordinal) || version != expectedVersion)
            throw new InvalidDataException("Das Update passt nicht (" + name.Name + " " + version + " statt " +
                                           expectedName + " " + expectedVersion + ").");
    }

    public static void Swap(string exe, string newFile)
    {
        string old = OldPath(exe);
        if (File.Exists(old))
        {
            File.SetAttributes(old, FileAttributes.Normal);
            File.Delete(old);
        }
        Retry(() => File.Move(exe, old));
        try { Retry(() => File.Move(newFile, exe)); }
        catch
        {
            File.Move(old, exe);
            throw;
        }
    }

    // Legt die alte Version zurück, wenn die neue nicht startet.
    public static void RollBack(string exe)
    {
        string old = OldPath(exe);
        if (!File.Exists(old)) return;
        if (File.Exists(exe)) File.Delete(exe);
        File.Move(old, exe);
    }

    // Entfernt Reste eines Updates. Die alte Version kann sich noch kurz beenden, deshalb mehrere Versuche.
    // true = nichts mehr übrig.
    public static bool CleanUp(string exe, int attempts)
    {
        for (int i = 0; i < attempts; i++)
        {
            bool done = true;
            foreach (string f in new[] { OldPath(exe), NewPath(exe) })
            {
                if (!File.Exists(f)) continue;
                try
                {
                    File.SetAttributes(f, FileAttributes.Normal);
                    File.Delete(f);
                }
                catch (Exception) { done = false; }
            }
            if (done) return true;
            if (i < attempts - 1) Thread.Sleep(250);
        }
        return false;
    }

    // Version, von der aus aktualisiert wurde (Start mit „--updated 1.15.0“), sonst null
    public static string UpdatedFrom(string[] args)
    {
        for (int i = 0; i + 1 < args.Length; i++)
            if (args[i] == UpdatedArgument && Regex.IsMatch(args[i + 1], @"^\d+\.\d+\.\d+$")) return args[i + 1];
        return null;
    }

    // Virenscanner sehen sich neue Dateien kurz an und halten sie dabei fest.
    static void Retry(Action action)
    {
        for (int i = 0; ; i++)
        {
            try { action(); return; }
            catch (IOException) { if (i >= 9) throw; Thread.Sleep(300); }
            catch (UnauthorizedAccessException) { if (i >= 9) throw; Thread.Sleep(300); }
        }
    }
}
