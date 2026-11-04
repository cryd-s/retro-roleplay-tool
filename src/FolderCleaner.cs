using System;
using System.Globalization;
using System.IO;

public class CleanResult
{
    public long BytesFreed;
    public int FilesDeleted;
    public int FilesSkipped;
    public bool Missing;
}

// Misst und leert Ordnerinhalte. Der Zielordner selbst bleibt immer stehen.
// Verknüpfte Ordner (Junctions/Symlinks) werden nie betreten, nur die Verknüpfung entfernt.
public static class FolderCleaner
{
    public static long MeasureSize(string dir)
    {
        if (!Directory.Exists(dir)) return 0;
        return MeasureDir(new DirectoryInfo(dir));
    }

    public static CleanResult CleanContents(string dir, Action<long> onFileDeleted)
    {
        Validate(dir);
        var result = new CleanResult();
        if (!Directory.Exists(dir))
        {
            result.Missing = true;
            return result;
        }
        CleanDir(new DirectoryInfo(dir), result, onFileDeleted);
        return result;
    }

    static void Validate(string dir)
    {
        if (string.IsNullOrWhiteSpace(dir) || !Path.IsPathRooted(dir))
            throw new ArgumentException("Ungültiger Pfad: \"" + dir + "\"");
        string full = Path.GetFullPath(dir).TrimEnd('\\', '/');
        string root = Path.GetPathRoot(full).TrimEnd('\\', '/');
        if (string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Laufwerkswurzel wird nicht geleert: \"" + dir + "\"");
    }

    static bool IsLink(FileSystemInfo info)
    {
        return (info.Attributes & FileAttributes.ReparsePoint) != 0;
    }

    static long MeasureDir(DirectoryInfo dir)
    {
        long total = 0;
        FileSystemInfo[] entries;
        try { entries = dir.GetFileSystemInfos(); }
        catch (Exception ex) { if (ex is IOException || ex is UnauthorizedAccessException) return 0; throw; }

        foreach (FileSystemInfo e in entries)
        {
            var sub = e as DirectoryInfo;
            if (sub != null)
            {
                if (!IsLink(sub)) total += MeasureDir(sub);
            }
            else if (!IsLink(e))
            {
                total += ((FileInfo)e).Length;
            }
        }
        return total;
    }

    static void CleanDir(DirectoryInfo dir, CleanResult result, Action<long> onFileDeleted)
    {
        FileSystemInfo[] entries;
        try { entries = dir.GetFileSystemInfos(); }
        catch (Exception ex) { if (ex is IOException || ex is UnauthorizedAccessException) return; throw; }

        foreach (FileSystemInfo e in entries)
        {
            var sub = e as DirectoryInfo;
            if (sub != null)
            {
                if (!IsLink(sub)) CleanDir(sub, result, onFileDeleted);
                // Nicht rekursiv: entfernt nur leere Ordner bzw. bei Junctions nur die Verknüpfung.
                try { sub.Attributes = FileAttributes.Directory; sub.Delete(false); }
                catch (Exception ex) { if (!(ex is IOException || ex is UnauthorizedAccessException)) throw; }
            }
            else
            {
                var file = (FileInfo)e;
                long size = IsLink(file) ? 0 : file.Length;
                try
                {
                    if ((file.Attributes & FileAttributes.ReadOnly) != 0) file.Attributes = FileAttributes.Normal;
                    file.Delete();
                    result.FilesDeleted++;
                    result.BytesFreed += size;
                    if (onFileDeleted != null) onFileDeleted(size);
                }
                catch (Exception ex)
                {
                    if (!(ex is IOException || ex is UnauthorizedAccessException)) throw;
                    result.FilesSkipped++;
                }
            }
        }
    }
}

public static class SizeFormat
{
    static readonly CultureInfo De = new CultureInfo("de-DE");

    public static string Format(long bytes)
    {
        const double KB = 1024, MB = KB * 1024, GB = MB * 1024;
        if (bytes < KB) return bytes.ToString(De) + " B";
        if (bytes < MB) return (bytes / KB).ToString("0.0", De) + " KB";
        if (bytes < GB) return (bytes / MB).ToString("0.0", De) + " MB";
        return (bytes / GB).ToString("0.00", De) + " GB";
    }
}
