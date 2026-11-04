using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;

// Downloads mit Fortschritt und SHA256-Prüfung. Arbeitsordner: %TEMP%\RetroRoleplayTool
public static class Downloader
{
    const string UserAgent = "RetroRoleplayTool/1.0 (Windows)";

    static Downloader()
    {
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
    }

    public static string WorkDir
    {
        get
        {
            string dir = Path.Combine(Path.GetTempPath(), "RetroRoleplayTool");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string GetString(string url)
    {
        using (var wc = new WebClient())
        {
            wc.Headers[HttpRequestHeader.UserAgent] = UserAgent;
            wc.Encoding = Encoding.UTF8;
            return wc.DownloadString(url);
        }
    }

    // progress: Promille 0..1000 (oder -1, wenn die Größe unbekannt ist)
    public static void DownloadFile(string url, string destination, Action<int> progress)
    {
        var req = (HttpWebRequest)WebRequest.Create(url);
        req.UserAgent = UserAgent;
        req.AllowAutoRedirect = true;
        req.Timeout = 30000;
        req.ReadWriteTimeout = 60000;

        string partial = destination + ".part";
        using (var resp = (HttpWebResponse)req.GetResponse())
        using (Stream input = resp.GetResponseStream())
        using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write))
        {
            long total = resp.ContentLength, done = 0;
            var buffer = new byte[81920];
            int read, lastReported = -2;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                output.Write(buffer, 0, read);
                done += read;
                int permille = total > 0 ? (int)(done * 1000 / total) : -1;
                if (progress != null && permille != lastReported) { progress(permille); lastReported = permille; }
            }
        }
        if (File.Exists(destination)) File.Delete(destination);
        File.Move(partial, destination);
    }

    public static string Sha256OfFile(string path)
    {
        using (SHA256 sha = SHA256.Create())
        using (FileStream fs = File.OpenRead(path))
        {
            byte[] hash = sha.ComputeHash(fs);
            var sb = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }

    public static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (Exception) { }
    }
}
