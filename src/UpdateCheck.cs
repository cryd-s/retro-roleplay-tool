using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

public class UpdateInfo
{
    public string Version, ZipUrl;
}

// Fragt bei GitHub die neueste Version ab. Installiert nichts, liefert nur den Link zur ZIP.
public static class UpdateCheck
{
    const string LatestReleaseApi = "https://api.github.com/repos/cryd-s/retro-roleplay-tool/releases/latest";
    const string DownloadPrefix = "https://github.com/cryd-s/retro-roleplay-tool/releases/download/";
    const string ZipName = "RetroRoleplayTool.zip";

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
        return zip == null ? null : new UpdateInfo { Version = latest, ZipUrl = zip };
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
