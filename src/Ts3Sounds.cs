using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

// Minimaler Zugriff auf SQLite über die in Windows 10/11 enthaltene winsqlite3.dll.
public sealed class SqliteDb : IDisposable
{
    const string Lib = "winsqlite3.dll";
    const int ReadOnlyFlag = 1, ReadWriteFlag = 2, CreateFlag = 4;
    const int RowResult = 100, DoneResult = 101;
    const int BusyTimeoutMs = 3000;
    static readonly IntPtr Transient = new IntPtr(-1);   // SQLite kopiert den Text selbst

    IntPtr db;

    [DllImport(Lib, CallingConvention = CallingConvention.StdCall)]
    static extern int sqlite3_open_v2(byte[] utf8Path, out IntPtr database, int flags, IntPtr vfs);
    [DllImport(Lib, CallingConvention = CallingConvention.StdCall)]
    static extern int sqlite3_close(IntPtr database);
    [DllImport(Lib, CallingConvention = CallingConvention.StdCall)]
    static extern int sqlite3_busy_timeout(IntPtr database, int milliseconds);
    [DllImport(Lib, CallingConvention = CallingConvention.StdCall)]
    static extern IntPtr sqlite3_errmsg16(IntPtr database);
    [DllImport(Lib, CallingConvention = CallingConvention.StdCall)]
    static extern int sqlite3_prepare_v2(IntPtr database, byte[] utf8Sql, int bytes, out IntPtr statement, IntPtr tail);
    [DllImport(Lib, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    static extern int sqlite3_bind_text16(IntPtr statement, int index, string value, int bytes, IntPtr destructor);
    [DllImport(Lib, CallingConvention = CallingConvention.StdCall)]
    static extern int sqlite3_bind_int64(IntPtr statement, int index, long value);
    [DllImport(Lib, CallingConvention = CallingConvention.StdCall)]
    static extern int sqlite3_step(IntPtr statement);
    [DllImport(Lib, CallingConvention = CallingConvention.StdCall)]
    static extern IntPtr sqlite3_column_text16(IntPtr statement, int column);
    [DllImport(Lib, CallingConvention = CallingConvention.StdCall)]
    static extern int sqlite3_finalize(IntPtr statement);

    SqliteDb(string path, int flags)
    {
        int result;
        try
        {
            result = sqlite3_open_v2(Utf8(path), out db, flags, IntPtr.Zero);
        }
        catch (DllNotFoundException)
        {
            throw new NotSupportedException("Diese Windows-Version enthält winsqlite3.dll nicht.");
        }
        if (result != 0)
        {
            string message = LastError();
            Dispose();
            throw new IOException("Datenbank nicht lesbar: " + message);
        }
        sqlite3_busy_timeout(db, BusyTimeoutMs);
    }

    public static SqliteDb OpenRead(string path) { return new SqliteDb(path, ReadOnlyFlag); }
    public static SqliteDb OpenWrite(string path) { return new SqliteDb(path, ReadWriteFlag); }
    public static SqliteDb Create(string path) { return new SqliteDb(path, ReadWriteFlag | CreateFlag); }

    // Parameter: string oder long. Liefert die erste Spalte der ersten Zeile, sonst null.
    public string Scalar(string sql, params object[] args)
    {
        IntPtr statement = Prepare(sql, args);
        try
        {
            int step = sqlite3_step(statement);
            if (step == RowResult) return Marshal.PtrToStringUni(sqlite3_column_text16(statement, 0));
            if (step != DoneResult) throw new IOException(LastError());
            return null;
        }
        finally { sqlite3_finalize(statement); }
    }

    public void Execute(string sql, params object[] args)
    {
        IntPtr statement = Prepare(sql, args);
        try
        {
            int step = sqlite3_step(statement);
            if (step != DoneResult && step != RowResult) throw new IOException(LastError());
        }
        finally { sqlite3_finalize(statement); }
    }

    IntPtr Prepare(string sql, object[] args)
    {
        IntPtr statement;
        if (sqlite3_prepare_v2(db, Utf8(sql), -1, out statement, IntPtr.Zero) != 0) throw new IOException(LastError());
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] is long) sqlite3_bind_int64(statement, i + 1, (long)args[i]);
            else sqlite3_bind_text16(statement, i + 1, (string)args[i], -1, Transient);
        }
        return statement;
    }

    string LastError()
    {
        return db == IntPtr.Zero ? "unbekannter Fehler" : Marshal.PtrToStringUni(sqlite3_errmsg16(db));
    }

    static byte[] Utf8(string text)
    {
        return Encoding.UTF8.GetBytes(text + "\0");
    }

    public void Dispose()
    {
        if (db == IntPtr.Zero) return;
        sqlite3_close(db);
        db = IntPtr.Zero;
    }
}

// TeamSpeak-3-Töne über das mitgelieferte Sound-Paket „Sounds deactivated“ abschalten.
// Dieselbe Einstellung wie in TeamSpeak unter Optionen → Benachrichtigungen.
public static class Ts3Sounds
{
    public const string NoSounds = "nosounds";
    public const string DefaultPack = "default";
    const string PreviousKey = "Ts3SoundPackBefore";
    const string TableName = "Notifications";

    // So legt TeamSpeak selbst jede seiner Tabellen an.
    const string CreateTableSql = "CREATE TABLE Notifications (timestamp INTEGER UNSIGNED NOT NULL, key VARCHAR NOT NULL UNIQUE, value VARCHAR)";
    const string CreateIndexSql = "CREATE INDEX index_Notifications_key ON Notifications (key)";

    // Mögliche Orte der settings.db, wahrscheinlichster zuerst. Im Setup lässt sich wählen, ob TeamSpeak
    // seine Einstellungen in AppData (Standard) oder im Installationsordner unter „config“ ablegt.
    public static List<string> Candidates(string appData, string configLocation, string installDir)
    {
        var list = new List<string>();
        string own = Path.Combine(appData, "TS3Client", "settings.db");
        string dir = installDir == null ? "" : installDir.Trim().Trim('"').TrimEnd('\\');
        string inInstall = dir.Length == 0 ? null : Path.Combine(dir, "config", "settings.db");
        if (inInstall != null && configLocation == "1") list.Add(inInstall);
        list.Add(own);
        if (inInstall != null && configLocation != "1") list.Add(inInstall);
        return list;
    }

    // Die Datei, die TeamSpeak benutzt; gibt es noch keine, der Ort, an dem sie entstehen wird.
    public static string FindSettingsDb()
    {
        List<string> all = Candidates(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                      Ts3.ConfigLocation(), Ts3.InstalledLocation());
        foreach (string f in all) if (File.Exists(f)) return f;
        return all[0];
    }

    // TeamSpeak legt die Datei erst beim ersten Start an. Die Tabelle für Benachrichtigungen kommt erst dazu,
    // wenn man dort etwas einstellt; sie fehlt also oft und wird beim Abschalten wie von TeamSpeak angelegt.
    public static bool IsAvailable(string dbPath)
    {
        if (!File.Exists(dbPath)) return false;
        using (SqliteDb db = SqliteDb.OpenRead(dbPath)) return HasTable(db) || IsTs3Database(db);
    }

    public static bool AreDisabled(string dbPath)
    {
        return string.Equals(ReadPack(dbPath), NoSounds, StringComparison.OrdinalIgnoreCase);
    }

    // Name des Sound-Pakets; null = noch nie gesetzt (TeamSpeak nutzt dann sein Standardpaket)
    public static string ReadPack(string dbPath)
    {
        using (SqliteDb db = SqliteDb.OpenRead(dbPath))
            return HasTable(db) ? db.Scalar("SELECT value FROM " + TableName + " WHERE key = 'SoundPack'") : null;
    }

    public static void Disable(string dbPath, AppSettings settings)
    {
        string current = ReadPack(dbPath);
        // Das bisherige Paket merken, aber nie „nosounds“ – sonst ginge der Weg zurück verloren.
        if (!string.IsNullOrEmpty(current) && !string.Equals(current, NoSounds, StringComparison.OrdinalIgnoreCase))
            settings.Set(PreviousKey, current);
        WritePack(dbPath, NoSounds);
    }

    public static void Enable(string dbPath, AppSettings settings)
    {
        WritePack(dbPath, settings.Get(PreviousKey) ?? DefaultPack);
    }

    static void WritePack(string dbPath, string pack)
    {
        using (SqliteDb db = SqliteDb.OpenWrite(dbPath))
        {
            long now = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            db.Execute("BEGIN IMMEDIATE");
            try
            {
                if (!HasTable(db))
                {
                    if (!IsTs3Database(db))
                        throw new InvalidOperationException("Bitte TeamSpeak 3 einmal starten und wieder schließen.");
                    db.Execute(CreateTableSql);
                    db.Execute(CreateIndexSql);
                }
                db.Execute("INSERT OR REPLACE INTO " + TableName + " (timestamp, key, value) VALUES (?, 'SoundPack', ?)", now, pack);
                db.Execute("COMMIT");
            }
            catch
            {
                try { db.Execute("ROLLBACK"); } catch (Exception) { /* schon zurückgerollt */ }
                throw;
            }
        }
    }

    static bool HasTable(SqliteDb db)
    {
        return HasTable(db, TableName);
    }

    static bool HasTable(SqliteDb db, string name)
    {
        return db.Scalar("SELECT name FROM sqlite_master WHERE type = 'table' AND name = ?", name) != null;
    }

    // Eine Datenbank, die TeamSpeak 3 schon angelegt hat (diese Tabelle gibt es ab dem ersten Start)
    static bool IsTs3Database(SqliteDb db)
    {
        return HasTable(db, "General");
    }
}
