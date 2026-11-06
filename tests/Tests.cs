using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

// Kleine Testreihe ohne Framework: arbeitet nur in einem Wegwerf-Ordner unter %TEMP%.
static class Tests
{
    static int passed, failed;
    static string sandbox;

    static int Main()
    {
        sandbox = Path.Combine(Path.GetTempPath(), "GTACleanerTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sandbox);
        try
        {
            IniTests();
            CleanerTests();
            TargetTests();
            ScreenTests();
            ProcessTests();
            FormatTests();
            HostsTests();
            Ts3Tests();
            SaltyTests();
            GtaSettingsTests();
            SteamTests();
            UpdateTests();
            ModCheckTests();
            Ts3SoundTests();
            RockstarTests();
            NetworkFixTests();
            SteamHintTests();
            DownloadTests();
        }
        finally
        {
            try { Directory.Delete(sandbox, true); } catch { }
        }
        Console.WriteLine();
        Console.WriteLine(passed + " bestanden, " + failed + " fehlgeschlagen");
        return failed == 0 ? 0 : 1;
    }

    // ---------------------------------------------------------------- INI

    const string Sample =
        "[Game]\r\nIVPath=C:\\Games\\GTA\r\nSavedBuildNumber=3095\r\nDefaultBuild=3258\r\n\r\n" +
        "[Addons]\r\nReShade5=ID:c540f218 acknowledged\r\n";

    const string SampleStable =
        "[Game]\r\nIVPath=C:\\Games\\GTA\r\nSavedBuildNumber=3095\r\nDefaultBuild=3258\r\nUpdateChannel=production\r\n\r\n" +
        "[Addons]\r\nReShade5=ID:c540f218 acknowledged\r\n";

    static void IniTests()
    {
        Run("INI: fehlender Kanal ergibt null", () =>
            Equal<string>(null, FiveMIni.GetUpdateChannel(Sample), "Kanal"));

        Run("INI: Kanal wird gelesen, auch mit Leerzeichen", () =>
            Equal("canary", FiveMIni.GetUpdateChannel("[Game]\r\nUpdateChannel = canary\r\n"), "Kanal"));

        Run("INI: Kanal aus anderem Abschnitt zählt nicht", () =>
            Equal<string>(null, FiveMIni.GetUpdateChannel("[Other]\r\nUpdateChannel=beta\r\n"), "Kanal"));

        Run("INI: fehlender Schlüssel wird am Ende von [Game] ergänzt, Rest bleibt", () =>
            Equal(SampleStable, FiveMIni.SetUpdateChannel(Sample, "production"), "Text"));

        Run("INI: vorhandener Wert wird ersetzt", () =>
            Equal("[Game]\r\nUpdateChannel=production\r\nX=1\r\n",
                  FiveMIni.SetUpdateChannel("[Game]\r\nUpdateChannel=beta\r\nX=1\r\n", "production"), "Text"));

        Run("INI: Schlüssel in anderem Abschnitt bleibt unberührt", () =>
            Equal("[Game]\r\nX=1\r\nUpdateChannel=production\r\n[Other]\r\nUpdateChannel=beta\r\n",
                  FiveMIni.SetUpdateChannel("[Game]\r\nX=1\r\n[Other]\r\nUpdateChannel=beta\r\n", "production"), "Text"));

        Run("INI: fehlender [Game]-Abschnitt wird angehängt", () =>
            Equal("[Addons]\r\nA=1\r\n[Game]\r\nUpdateChannel=production\r\n",
                  FiveMIni.SetUpdateChannel("[Addons]\r\nA=1\r\n", "production"), "Text"));

        Run("INI: leere Datei bekommt [Game]", () =>
            Equal("[Game]\r\nUpdateChannel=production\r\n", FiveMIni.SetUpdateChannel("", "production"), "Text"));

        Run("INI: LF-Zeilenenden bleiben LF", () =>
            Equal("[Game]\nX=1\nUpdateChannel=production\n", FiveMIni.SetUpdateChannel("[Game]\nX=1\n", "production"), "Text"));

        Run("INI-Datei: fehlende Datei meldet FileMissing", () =>
            Equal(IniResult.FileMissing, FiveMIni.EnsureStable(Path.Combine(sandbox, "gibtsnicht.ini")), "Ergebnis"));

        Run("INI-Datei: wird geändert, Sicherung angelegt, zweiter Lauf ändert nichts", () =>
        {
            string dir = NewDir("ini1");
            string ini = Path.Combine(dir, "CitizenFX.ini");
            File.WriteAllText(ini, Sample);

            Equal(IniResult.Changed, FiveMIni.EnsureStable(ini), "1. Lauf");
            Equal(SampleStable, File.ReadAllText(ini), "Inhalt");
            Equal(Sample, File.ReadAllText(ini + ".bak"), "Sicherung");

            Equal(IniResult.AlreadyStable, FiveMIni.EnsureStable(ini), "2. Lauf");
        });

        Run("INI-Datei: vorhandene Sicherung wird nicht überschrieben", () =>
        {
            string dir = NewDir("ini2");
            string ini = Path.Combine(dir, "CitizenFX.ini");
            File.WriteAllText(ini, Sample);
            FiveMIni.EnsureStable(ini);
            File.WriteAllText(ini, "[Game]\r\nUpdateChannel=beta\r\n");

            Equal(IniResult.Changed, FiveMIni.EnsureStable(ini), "Ergebnis");
            Equal(Sample, File.ReadAllText(ini + ".bak"), "Sicherung bleibt Original");
        });

        Run("INI-Datei: ANSI-Umlaute bleiben erhalten", () =>
        {
            string dir = NewDir("ini3");
            string ini = Path.Combine(dir, "CitizenFX.ini");
            File.WriteAllBytes(ini, Encoding.Default.GetBytes("[Game]\r\nIVPath=C:\\Spiele\\GTA Ä\r\n"));

            FiveMIni.EnsureStable(ini);
            Equal("[Game]\r\nIVPath=C:\\Spiele\\GTA Ä\r\nUpdateChannel=production\r\n",
                  Encoding.Default.GetString(File.ReadAllBytes(ini)), "Inhalt");
        });
    }

    // ---------------------------------------------------------------- Cleaner

    // Verweigert dem aktuellen Benutzer das Ändern von Attributen (wie bei fremden/geschützten Dateien in %TEMP%).
    static void DenyWriteAttributes(string path, bool deny)
    {
        string sid = System.Security.Principal.WindowsIdentity.GetCurrent().User.Value;
        string args = "\"" + path + "\" " + (deny ? "/deny *" + sid + ":(WA)" : "/remove:d *" + sid);
        var psi = new ProcessStartInfo("icacls.exe", args)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        using (Process p = Process.Start(psi))
        {
            p.StandardError.ReadToEndAsync();
            p.StandardOutput.ReadToEnd();
            p.WaitForExit();
        }
    }

    static void CleanerTests()
    {
        Run("Leeren: Zugriff verweigert beim Attribut-Setzen bricht nicht ab", () =>
        {
            string dir = NewDir("denied");
            string lockedDir = Path.Combine(dir, "a_geschuetzt");
            string lockedFile = Path.Combine(dir, "b_schreibgeschuetzt.txt");
            WriteFile(Path.Combine(lockedDir, "x.bin"), 10);
            WriteFile(lockedFile, 10);
            File.SetAttributes(lockedFile, FileAttributes.ReadOnly);
            WriteFile(Path.Combine(dir, "c_normal.bin"), 10);
            DenyWriteAttributes(lockedDir, true);
            DenyWriteAttributes(lockedFile, true);
            try
            {
                CleanResult r = FolderCleaner.CleanContents(dir, null);
                True(!File.Exists(Path.Combine(dir, "c_normal.bin")), "Dateien danach werden trotzdem gelöscht");
                True(r.FilesSkipped >= 1, "gesperrte Datei wird als übersprungen gezählt");
            }
            finally
            {
                DenyWriteAttributes(lockedDir, false);
                DenyWriteAttributes(lockedFile, false);
                if (File.Exists(lockedFile)) File.SetAttributes(lockedFile, FileAttributes.Normal);
            }
        });

        Run("Messen: summiert Dateien in Unterordnern", () =>
        {
            string dir = NewDir("m1");
            WriteFile(Path.Combine(dir, "a.bin"), 100);
            WriteFile(Path.Combine(dir, "sub", "b.bin"), 200);
            WriteFile(Path.Combine(dir, "sub", "deep", "c.bin"), 300);
            Equal(600L, FolderCleaner.MeasureSize(dir), "Größe");
        });

        Run("Messen: fehlender Ordner ergibt 0", () =>
            Equal(0L, FolderCleaner.MeasureSize(Path.Combine(sandbox, "fehlt")), "Größe"));

        Run("Leeren: löscht Dateien und Unterordner, Ordner selbst bleibt", () =>
        {
            string dir = NewDir("c1");
            WriteFile(Path.Combine(dir, "a.bin"), 100);
            WriteFile(Path.Combine(dir, "sub", "b.bin"), 200);
            long reported = 0;

            CleanResult r = FolderCleaner.CleanContents(dir, b => reported += b);

            True(Directory.Exists(dir), "Zielordner muss bleiben");
            Equal(0, Directory.GetFileSystemEntries(dir).Length, "Einträge übrig");
            Equal(300L, r.BytesFreed, "BytesFreed");
            Equal(2, r.FilesDeleted, "FilesDeleted");
            Equal(0, r.FilesSkipped, "FilesSkipped");
            Equal(300L, reported, "gemeldeter Fortschritt");
        });

        Run("Leeren: gesperrte Datei wird übersprungen und gezählt", () =>
        {
            string dir = NewDir("c2");
            string locked = Path.Combine(dir, "locked.bin");
            WriteFile(locked, 50);
            WriteFile(Path.Combine(dir, "free.bin"), 70);

            CleanResult r;
            using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
                r = FolderCleaner.CleanContents(dir, null);

            True(File.Exists(locked), "gesperrte Datei muss noch da sein");
            True(!File.Exists(Path.Combine(dir, "free.bin")), "freie Datei muss weg sein");
            Equal(1, r.FilesSkipped, "FilesSkipped");
            Equal(70L, r.BytesFreed, "BytesFreed");
        });

        Run("Leeren: schreibgeschützte Datei wird gelöscht", () =>
        {
            string dir = NewDir("c3");
            string ro = Path.Combine(dir, "ro.bin");
            WriteFile(ro, 10);
            File.SetAttributes(ro, FileAttributes.ReadOnly);

            FolderCleaner.CleanContents(dir, null);
            True(!File.Exists(ro), "schreibgeschützte Datei muss weg sein");
        });

        Run("Leeren: Junction wird entfernt, ihr Ziel bleibt unberührt", () =>
        {
            string outside = NewDir("outside");
            string precious = Path.Combine(outside, "wichtig.txt");
            WriteFile(precious, 5);
            string dir = NewDir("c4");
            string link = Path.Combine(dir, "link");
            Mklink(link, outside);
            True(Directory.Exists(link), "Junction anlegen fehlgeschlagen");

            Equal(0L, FolderCleaner.MeasureSize(dir), "Messen folgt Junction nicht");
            FolderCleaner.CleanContents(dir, null);

            True(File.Exists(precious), "Datei hinter der Junction darf nicht gelöscht werden");
            True(!Directory.Exists(link), "Junction selbst soll entfernt sein");
        });

        Run("Leeren: fehlender Ordner meldet Missing ohne Fehler", () =>
            True(FolderCleaner.CleanContents(Path.Combine(sandbox, "fehlt2"), null).Missing, "Missing"));

        Run("Leeren: Laufwerkswurzel wird verweigert", () =>
            Throws<ArgumentException>(() => FolderCleaner.CleanContents("C:\\", null)));

        Run("Leeren: relativer oder leerer Pfad wird verweigert", () =>
        {
            Throws<ArgumentException>(() => FolderCleaner.CleanContents("temp", null));
            Throws<ArgumentException>(() => FolderCleaner.CleanContents("", null));
        });
    }

    // ---------------------------------------------------------------- Ziele

    static void TargetTests()
    {
        const string App = "C:\\L\\FiveM\\FiveM.app";

        Run("Ziele: Normaler Cache Clear = cache, server-cache, server-cache-priv, logs, crashes", () =>
        {
            List<CleanTarget> t = CleanTargets.FiveM(App, false);
            Equal(5, t.Count, "Anzahl");
            Equal(App + "\\data\\cache", t[0].Path, "cache");
            Equal(App + "\\data\\server-cache", t[1].Path, "server-cache");
            Equal(App + "\\data\\server-cache-priv", t[2].Path, "server-cache-priv");
            Equal(App + "\\logs", t[3].Path, "logs");
            Equal(App + "\\crashes", t[4].Path, "crashes");
        });

        Run("Ziele: Gesamter Cache Clear = normal + game-storage, nui-storage", () =>
        {
            List<CleanTarget> t = CleanTargets.FiveM(App, true);
            Equal(7, t.Count, "Anzahl");
            for (int i = 0; i < 5; i++)
                Equal(CleanTargets.FiveM(App, false)[i].Path, t[i].Path, "gleich wie normal, Eintrag " + i);
            Equal(App + "\\data\\game-storage", t[5].Path, "game-storage");
            Equal(App + "\\data\\nui-storage", t[6].Path, "nui-storage");
        });

        Run("Ziele: Temp ohne abschließenden Backslash, INI im FiveM.app-Ordner", () =>
        {
            Equal("C:\\T", CleanTargets.Temp("C:\\T\\").Path, "Temp");
            Equal(App + "\\CitizenFX.ini", CleanTargets.IniPath(App), "INI");
        });

        LocatorTests();
    }

    // ---------------------------------------------------------------- FiveM finden

    // Legt eine echte FiveM-Struktur an: <root>\FiveM.exe und <root>\FiveM.app\CitizenFX.ini + data\
    static string MakeFiveM(string name)
    {
        string root = NewDir(name);
        WriteFile(Path.Combine(root, "FiveM.exe"), 1);
        WriteFile(Path.Combine(root, "FiveM.app", "CitizenFX.ini"), 1);
        Directory.CreateDirectory(Path.Combine(root, "FiveM.app", "data", "cache"));
        return root;
    }

    static void LocatorTests()
    {
        Run("FiveM finden: FiveM-Ordner, FiveM.app, FiveM.exe und Unterordner führen zu FiveM.app", () =>
        {
            string root = MakeFiveM("fm1");
            string app = Path.Combine(root, "FiveM.app");
            Equal(app, FiveMLocator.NormalizeAppDir(root), "FiveM-Ordner");
            Equal(app, FiveMLocator.NormalizeAppDir(root + "\\"), "mit Backslash");
            Equal(app, FiveMLocator.NormalizeAppDir("\"" + root + "\""), "in Anführungszeichen");
            Equal(app, FiveMLocator.NormalizeAppDir(app), "FiveM.app selbst");
            Equal(app, FiveMLocator.NormalizeAppDir(Path.Combine(root, "FiveM.exe")), "FiveM.exe");
            Equal(app, FiveMLocator.NormalizeAppDir(Path.Combine(app, "CitizenFX.ini")), "CitizenFX.ini");
            Equal(app, FiveMLocator.NormalizeAppDir(Path.Combine(app, "data", "cache")), "Unterordner");
        });

        Run("FiveM finden: fremde oder fehlende Ordner ergeben null", () =>
        {
            Equal<string>(null, FiveMLocator.NormalizeAppDir(NewDir("nichtfivem")), "fremder Ordner");
            Equal<string>(null, FiveMLocator.NormalizeAppDir(Path.Combine(sandbox, "gibtsnicht")), "fehlt");
            Equal<string>(null, FiveMLocator.NormalizeAppDir(null), "null");
            Equal<string>(null, FiveMLocator.NormalizeAppDir("  "), "leer");
        });

        Run("FiveM finden: leerer FiveM.app-Ordner ohne INI/data/FiveM.exe zählt nicht", () =>
        {
            string root = NewDir("fm-leer");
            Directory.CreateDirectory(Path.Combine(root, "FiveM.app"));
            Equal<string>(null, FiveMLocator.NormalizeAppDir(root), "leer");
        });

        Run("FiveM finden: erster gültiger Kandidat gewinnt, Quelle wird gemeldet", () =>
        {
            string root = MakeFiveM("fm2");
            var candidates = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("gespeichert", Path.Combine(sandbox, "weg")),
                new KeyValuePair<string, string>("Registry", null),
                new KeyValuePair<string, string>("Standardordner", root),
                new KeyValuePair<string, string>("Laufwerkssuche", MakeFiveM("fm3")),
            };
            FiveMLocation loc = FiveMLocator.PickFirst(candidates);
            True(loc.Found, "muss gefunden werden");
            Equal(Path.Combine(root, "FiveM.app"), loc.AppDir, "Ordner");
            Equal("Standardordner", loc.Source, "Quelle");
        });

        Run("FiveM finden: ohne gültigen Kandidaten nicht gefunden", () =>
        {
            FiveMLocation loc = FiveMLocator.PickFirst(new[] { new KeyValuePair<string, string>("x", Path.Combine(sandbox, "nix")) });
            True(!loc.Found, "darf nicht gefunden sein");
            Equal<string>(null, loc.AppDir, "Ordner");
        });

        Run("FiveM finden: Pfad aus fivem://-Befehl wird gelesen", () =>
        {
            Equal("C:\\Users\\x\\AppData\\Local\\FiveM\\FiveM.exe",
                  FiveMLocator.ExeFromCommand("\"C:\\Users\\x\\AppData\\Local\\FiveM\\FiveM.exe\" \"%1\""), "mit Anführungszeichen");
            Equal("D:\\FiveM\\FiveM.exe", FiveMLocator.ExeFromCommand("D:\\FiveM\\FiveM.exe %1"), "ohne");
            Equal<string>(null, FiveMLocator.ExeFromCommand(null), "null");
        });

        Run("Einstellungen: gewählter Ordner wird gespeichert und wieder gelesen", () =>
        {
            string file = Path.Combine(NewDir("settings"), "sub", "settings.ini");
            var s = new AppSettings(file);
            Equal<string>(null, s.Get("FiveMAppDir"), "vorher leer");
            s.Set("FiveMAppDir", "D:\\Spiele\\FiveM\\FiveM.app");
            s.Set("Andere", "1");
            Equal("D:\\Spiele\\FiveM\\FiveM.app", new AppSettings(file).Get("FiveMAppDir"), "nach Neuladen");
            Equal("1", new AppSettings(file).Get("Andere"), "zweiter Wert");
        });
    }

    // ---------------------------------------------------------------- Bildschirm

    static void ScreenTests()
    {
        Run("Bildschirm: passt das Fenster, bleibt die Skalierung", () =>
            Equal(1.0f, Theme.FitScale(1.0f, 766, 822, 1920, 1040), "Skalierung"));

        Run("Bildschirm: Laptop 1080p/150 % wird so verkleinert, dass es passt", () =>
        {
            float s = Theme.FitScale(1.5f, 1149, 1233, 1280, 1032);
            True(s < 1.5f, "muss kleiner werden");
            True(1233 * s / 1.5f <= 1032, "Fensterhöhe muss in den Arbeitsbereich passen, war " + (1233 * s / 1.5f));
        });

        Run("Bildschirm: zu schmaler Bildschirm verkleinert auch", () =>
        {
            float s = Theme.FitScale(1.0f, 766, 600, 700, 1000);
            True(766 * s <= 700, "Fensterbreite muss passen");
        });

        Run("Bildschirm: nie kleiner als 60 %", () =>
            Equal(0.6f, Theme.FitScale(1.0f, 766, 822, 300, 300), "Untergrenze"));
    }

    // ---------------------------------------------------------------- Prozesse

    static void ProcessTests()
    {
        Run("Prozesse: FiveM-Namen werden erkannt, andere nicht", () =>
        {
            True(FiveMProcess.IsFiveMProcessName("FiveM"), "FiveM");
            True(FiveMProcess.IsFiveMProcessName("FiveM_b3095_GTAProcess"), "GTAProcess");
            True(FiveMProcess.IsFiveMProcessName("fivem_ChromeBrowser"), "ChromeBrowser");
            True(!FiveMProcess.IsFiveMProcessName("FiveManager"), "FiveManager");
            True(!FiveMProcess.IsFiveMProcessName("GTACleaner"), "GTACleaner");
            True(!FiveMProcess.IsFiveMProcessName("explorer"), "explorer");
        });
    }

    // ---------------------------------------------------------------- Format

    static void FormatTests()
    {
        Run("Format: Größen auf Deutsch", () =>
        {
            Equal("0 B", SizeFormat.Format(0), "0");
            Equal("512 B", SizeFormat.Format(512), "512");
            Equal("1,5 KB", SizeFormat.Format(1536), "1536");
            Equal("85,0 MB", SizeFormat.Format(85L * 1024 * 1024), "85 MB");
            Equal("47,20 GB", SizeFormat.Format((long)(47.2 * 1024 * 1024 * 1024)), "47,2 GB");
        });
    }

    // ---------------------------------------------------------------- hosts (TS-Moved-nicht-Bugfix)

    const string DefaultHosts =
        "# Copyright (c) 1993-2009 Microsoft Corp.\r\n#\r\n#\t127.0.0.1       localhost\r\n" +
        "#\t127.0.0.1 bindloc.triofilmt.de\r\n192.168.0.10    nas.local";

    static void HostsTests()
    {
        Run("hosts: auskommentierte Zeile zählt nicht als aktiv", () =>
            True(!HostsFix.IsActive(DefaultHosts), "darf nicht aktiv sein"));

        Run("hosts: Eintrag mit Tabs/Großschreibung wird erkannt", () =>
            True(HostsFix.IsActive("a\r\n127.0.0.1\t\tBindLoc.TrioFilmt.de  # fix\r\n"), "muss aktiv sein"));

        Run("hosts: Hinzufügen hängt Zeile mit Zeilenumbruch an", () =>
            Equal(DefaultHosts + "\r\n127.0.0.1 bindloc.triofilmt.de\r\n", HostsFix.Add(DefaultHosts), "Text"));

        Run("hosts: Hinzufügen doppelt nichts", () =>
        {
            string once = HostsFix.Add(DefaultHosts);
            Equal(once, HostsFix.Add(once), "Text");
        });

        Run("hosts: Entfernen nimmt nur den Bugfix-Eintrag raus", () =>
            Equal("x\r\n# 127.0.0.1 bindloc.triofilmt.de\r\ny\r\n",
                  HostsFix.Remove("x\r\n127.0.0.1 bindloc.triofilmt.de\r\n# 127.0.0.1 bindloc.triofilmt.de\r\ny\r\n"), "Text"));

        Run("hosts-Datei: aktivieren und wieder entfernen", () =>
        {
            string file = Path.Combine(NewDir("hosts1"), "hosts");
            File.WriteAllText(file, DefaultHosts, Encoding.Default);
            HostsFix.Apply(file, true);
            True(HostsFix.IsActive(File.ReadAllText(file)), "nach Aktivieren aktiv");
            HostsFix.Apply(file, false);
            True(!HostsFix.IsActive(File.ReadAllText(file)), "nach Entfernen inaktiv");
            True(File.ReadAllText(file).Contains("nas.local"), "übrige Einträge bleiben");
        });
    }

    // ---------------------------------------------------------------- TeamSpeak

    const string Ts3Json =
        "{\"windows\":{\"x86_64\":{\"version\":\"3.6.2\",\"checksum\":\"eab9e0c1\",\"mirrors\":{\"teamspeak.com\":" +
        "\"https:\\/\\/files.teamspeak-services.com\\/releases\\/client\\/3.6.2\\/TeamSpeak3-Client-win64-3.6.2.exe\"}}}," +
        "\"macos\":{\"x86_64\":{\"version\":\"3.6.2\",\"checksum\":\"8a68\",\"mirrors\":{\"teamspeak.com\":\"https:\\/\\/x\\/mac.dmg\"}}}}";

    static void Ts3Tests()
    {
        Run("TS3: Versionsliste wird gelesen (Windows x64)", () =>
        {
            Ts3Release r = Ts3.ParseClientJson(Ts3Json);
            Equal("3.6.2", r.Version, "Version");
            Equal("eab9e0c1", r.Sha256, "Prüfsumme");
            Equal("https://files.teamspeak-services.com/releases/client/3.6.2/TeamSpeak3-Client-win64-3.6.2.exe", r.Url, "URL");
        });

        Run("Versionen: werden numerisch verglichen", () =>
        {
            True(Versions.Compare("3.6.2", "3.6.10") < 0, "3.6.2 < 3.6.10");
            Equal(0, Versions.Compare("3.6", "3.6.0"), "3.6 == 3.6.0");
            True(Versions.Compare("4.1.0", "3.9.9") > 0, "4.1.0 > 3.9.9");
            True(Versions.Compare(null, "3.6.2") < 0, "nicht installiert < alles");
        });

        Run("TS3: Setup-Parameter installieren in den bisherigen Ordner", () =>
        {
            Equal("/S", Ts3.InstallerArguments(null), "ohne bisherige Installation");
            // NSIS: /D= muss am Ende stehen und darf keine Anführungszeichen haben
            Equal("/S /D=C:\\Program Files\\TeamSpeak 3 Client",
                  Ts3.InstallerArguments("\"C:\\Program Files\\TeamSpeak 3 Client\\\""), "mit bisherigem Ordner");
        });

        Run("TS3: Prozessnamen werden erkannt", () =>
        {
            True(Ts3.IsTs3ProcessName("ts3client_win64"), "win64");
            True(Ts3.IsTs3ProcessName("TS3Client_win32"), "win32");
            True(!Ts3.IsTs3ProcessName("TeamSpeak"), "TeamSpeak 5/6 nicht");
        });
    }

    // ---------------------------------------------------------------- SaltyChat

    static void SaltyTests()
    {
        Run("SaltyChat: Skript der Download-Seite wird gefunden", () =>
            Equal("/_next/static/chunks/app/download/page-dcf4c5e05623a341.js",
                  SaltyChat.FindDownloadScript("<script src=\"/_next/static/chunks/webpack-1.js\"></script>" +
                      "\"static/chunks/app/download/page-dcf4c5e05623a341.js\\\"<script src=\"/_next/static/chunks/app/download/page-dcf4c5e05623a341.js\" async>"),
                  "Pfad"));

        Run("SaltyChat: Version wird aus dem Skript gelesen", () =>
            Equal("4.1.0", SaltyChat.ParseVersion("let e=document.createElement(\"a\");e.href=\"/download/4.1.0\",document.body"), "Version"));

        Run("SaltyChat: ohne Versionsangabe kommt null", () =>
            Equal<string>(null, SaltyChat.ParseVersion("nichts hier"), "Version"));

        Run("SaltyChat: Plugin wird installiert, Altlast entfernt, Fremdes bleibt", () =>
        {
            string plugins = NewDir("ts3plugins1");
            WriteFile(Path.Combine(plugins, "SaltyChat.dll"), 3);               // alte 2.1.x-Datei
            WriteFile(Path.Combine(plugins, "clientquery_plugin_win64.dll"), 4); // anderes Plugin
            string zip = MakePluginZip("salty1.zip", "Name = Salty Chat\r\nVersion = 9.9.9\r\n", null);

            Equal<string>(null, SaltyChat.InstalledVersion(Path.Combine(sandbox, "leer")), "vorher nicht installiert");
            SaltyChat.InstallPlugin(zip, plugins);

            True(File.Exists(Path.Combine(plugins, "SaltyChat_win64.dll")), "DLL fehlt");
            True(File.Exists(Path.Combine(plugins, "SaltyChat", "default", "radioOn.wav")), "Sounds fehlen");
            True(!File.Exists(Path.Combine(plugins, "SaltyChat.dll")), "Altlast nicht entfernt");
            True(File.Exists(Path.Combine(plugins, "clientquery_plugin_win64.dll")), "fremdes Plugin gelöscht");
            Equal("9.9.9", SaltyChat.InstalledVersion(plugins), "installierte Version");
        });

        Run("SaltyChat: installiert ohne Versionsdatei ergibt leere Version", () =>
        {
            string plugins = NewDir("ts3plugins2");
            WriteFile(Path.Combine(plugins, "SaltyChat_win64.dll"), 3);
            Equal("", SaltyChat.InstalledVersion(plugins), "Version");
        });

        Run("SaltyChat: falsches Paket wird abgelehnt, nichts installiert", () =>
        {
            string plugins = NewDir("ts3plugins3");
            string zip = MakePluginZip("fremd.zip", "Name = Irgendwas\r\nVersion = 1.0\r\n", null);
            Throws<InvalidDataException>(() => SaltyChat.InstallPlugin(zip, plugins));
            Equal(0, Directory.GetFileSystemEntries(plugins).Length, "Plugin-Ordner muss leer bleiben");
        });

        Run("SaltyChat: Pfade außerhalb des Plugin-Ordners werden abgelehnt", () =>
        {
            string plugins = NewDir("ts3plugins4");
            string zip = MakePluginZip("boese.zip", "Name = Salty Chat\r\nVersion = 1.0\r\n", "plugins/../../boese.txt");
            Throws<InvalidDataException>(() => SaltyChat.InstallPlugin(zip, plugins));
            True(!File.Exists(Path.Combine(sandbox, "boese.txt")), "Datei außerhalb geschrieben");
            Equal(0, Directory.GetFileSystemEntries(plugins).Length, "Plugin-Ordner muss leer bleiben");
        });
    }

    static string MakePluginZip(string name, string packageIni, string extraEntry)
    {
        string path = Path.Combine(sandbox, name);
        using (var fs = new FileStream(path, FileMode.Create))
        using (var zip = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Create))
        {
            AddEntry(zip, "package.ini", packageIni);
            AddEntry(zip, "plugins/SaltyChat_win64.dll", "dll");
            AddEntry(zip, "plugins/SaltyChat/default/radioOn.wav", "wav");
            if (extraEntry != null) AddEntry(zip, extraEntry, "x");
        }
        return path;
    }

    static void AddEntry(System.IO.Compression.ZipArchive zip, string name, string content)
    {
        using (var w = new StreamWriter(zip.CreateEntry(name).Open())) w.Write(content);
    }

    // ---------------------------------------------------------------- Download

    static void DownloadTests()
    {
        Run("Prüfsumme: SHA256 einer Datei", () =>
        {
            string file = Path.Combine(NewDir("sha"), "abc.txt");
            File.WriteAllText(file, "abc", new UTF8Encoding(false));
            Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", Downloader.Sha256OfFile(file), "SHA256");
        });
    }

    // ---------------------------------------------------------------- GTA-/FiveM-Einstellungen

    const string Cfg =
        "// generated by CitizenFX\r\nunbindall\r\nrbind x KEYBOARD NULL \"y\"\r\n" +
        "seta \"cl_drawFPS\" \"true\"\r\nseta \"vid_budgetScale\" \"18\"\r\nseta \"voice_enableNoiseSuppression\" \"true\"\r\n";

    const string Xml =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n\r\n<Settings>\r\n  <version value=\"27\" />\r\n  <graphics>\r\n" +
        "    <ShadowQuality value=\"3\" />\r\n    <MotionBlurStrength value=\"0.500000\" />\r\n    <DoF value=\"true\" />\r\n" +
        "  </graphics>\r\n  <video>\r\n    <ScreenWidth value=\"5120\" />\r\n    <VSync value=\"1\" />\r\n  </video>\r\n</Settings>\r\n";

    // Alle sieben wichtigen Werte stehen absichtlich falsch.
    const string BadXml =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n\r\n<Settings>\r\n  <graphics>\r\n" +
        "    <ShadowQuality value=\"3\" />\r\n    <LodScale value=\"1.000000\" />\r\n    <MSAA value=\"4\" />\r\n" +
        "    <FXAA_Enabled value=\"false\" />\r\n    <CityDensity value=\"0.500000\" />\r\n" +
        "    <PedVarietyMultiplier value=\"0.300000\" />\r\n    <VehicleVarietyMultiplier value=\"0.000000\" />\r\n" +
        "  </graphics>\r\n  <video>\r\n    <ScreenWidth value=\"5120\" />\r\n    <VSync value=\"1\" />\r\n  </video>\r\n</Settings>\r\n";

    static void GtaSettingsTests()
    {
        Run("fivem.cfg: Wert wird gelesen", () =>
        {
            Equal("18", FiveMCfg.Get(Cfg, "vid_budgetScale"), "Budget");
            Equal<string>(null, FiveMCfg.Get(Cfg, "gibtsnicht"), "fehlend");
        });

        Run("fivem.cfg: Wert wird ersetzt, Rest bleibt gleich", () =>
            Equal(Cfg.Replace("\"18\"", "\"20\""), FiveMCfg.Set(Cfg, "vid_budgetScale", "20"), "cfg"));

        Run("fivem.cfg: fehlender Wert wird angehängt (CRLF)", () =>
        {
            string withoutBudget = Cfg.Replace("seta \"vid_budgetScale\" \"18\"\r\n", "");
            string result = FiveMCfg.Set(withoutBudget, "vid_budgetScale", "4");
            Equal(withoutBudget + "seta \"vid_budgetScale\" \"4\"\r\n", result, "cfg");
            Equal("4", FiveMCfg.Get(result, "vid_budgetScale"), "lesen");
        });

        Run("fivem.cfg: ähnlich benannte Werte bleiben unberührt", () =>
        {
            string cfg = "seta \"vid_budgetScale2\" \"1\"\r\n";
            Equal<string>(null, FiveMCfg.Get(cfg, "vid_budgetScale"), "lesen");
            Equal(cfg + "seta \"vid_budgetScale\" \"5\"\r\n", FiveMCfg.Set(cfg, "vid_budgetScale", "5"), "setzen");
        });

        Run("Grafik: Werte nur im <graphics>-Bereich", () =>
        {
            Equal("3", GtaGraphics.Get(Xml, "ShadowQuality"), "Schatten");
            Equal<string>(null, GtaGraphics.Get(Xml, "VSync"), "VSync liegt in <video>");
            Equal("1", GtaGraphics.Get(Xml, "video", "VSync"), "VSync aus <video>");
        });

        Run("Grafik: Profil ändert nur Grafik, Bildschirm und Format bleiben", () =>
        {
            var values = new Dictionary<string, string> { { "ShadowQuality", "1" }, { "DoF", "false" }, { "VSync", "0" } };
            string result = GtaGraphics.Apply(Xml, values);
            Equal("1", GtaGraphics.Get(result, "ShadowQuality"), "Schatten");
            Equal("false", GtaGraphics.Get(result, "DoF"), "DoF");
            True(result.Contains("    <VSync value=\"0\" />\r\n  </graphics>"), "fehlender Wert in <graphics> ergänzt: " + Show(result));
            True(result.Contains("<video>\r\n    <ScreenWidth value=\"5120\" />\r\n    <VSync value=\"1\" />"), "<video> unverändert");
            Equal("0.500000", GtaGraphics.Get(result, "MotionBlurStrength"), "nicht genannter Wert bleibt");
        });

        Run("Grafik: Datei ohne <graphics> wird abgelehnt", () =>
            Throws<InvalidDataException>(() => GtaGraphics.Apply("<Settings />", new Dictionary<string, string> { { "SSAO", "0" } })));

        Run("Grafik: gesetztes Profil wird erkannt", () =>
        {
            Equal<object>(null, GtaGraphics.Matching(Xml), "eigene Einstellungen");
            foreach (GtaGraphics.Preset p in GtaGraphics.All)
                Equal(p.Name, GtaGraphics.Matching(GtaGraphics.Apply(Xml, p.Values)).Name, "Profil");
        });

        Run("Texture Budget: erlaubt sind 50–95 % des Reglers (0–19)", () =>
        {
            Equal(19, TextureBudget.SliderMax, "Regler");
            Equal(10, TextureBudget.MinValue, "50 %");
            Equal(18, TextureBudget.MaxValue, "95 %");
            Equal(95, TextureBudget.Percent(18), "Prozent");
            Equal(10, TextureBudget.Clamp(0), "zu niedrig");
            Equal(18, TextureBudget.Clamp(19), "zu hoch");
            Equal(14, TextureBudget.Clamp(14), "passt");
        });

        Run("RP-Einstellungen: Prüfung zeigt, was nicht passt", () =>
        {
            List<SettingCheck> checks = RpSettings.Check(BadXml, "seta \"vid_budgetScale\" \"0\"\r\n");
            Equal(7, checks.Count, "Anzahl");
            foreach (SettingCheck c in checks) True(!c.Ok, c.Name + " dürfte nicht passen");
        });

        Run("RP-Einstellungen: Übernehmen setzt alles, Rest bleibt", () =>
        {
            string xml = BadXml, cfg = "seta \"vid_budgetScale\" \"19\"\r\nseta \"profile_vidVsync\" \"1\"\r\n";
            RpSettings.Apply(ref xml, ref cfg);
            foreach (SettingCheck c in RpSettings.Check(xml, cfg)) True(c.Ok, c.Name + " passt nicht: " + c.Current);
            Equal("18", FiveMCfg.Get(cfg, "vid_budgetScale"), "Budget auf 95 %");
            Equal("0", FiveMCfg.Get(cfg, "profile_vidVsync"), "VSync im Menü-Profil");
            Equal("0.950000", GtaGraphics.Get(xml, "LodScale"), "Distanz auf 95 %");
            Equal("3", GtaGraphics.Get(xml, "ShadowQuality"), "andere Grafik bleibt");
            Equal("5120", GtaGraphics.Get(xml, "video", "ScreenWidth"), "Auflösung bleibt");
        });

        Run("RP-Einstellungen: passende Werte bleiben unverändert", () =>
        {
            string xml = GtaGraphics.Apply(BadXml, new Dictionary<string, string> { { "LodScale", "0.700000" } });
            string cfg = "seta \"vid_budgetScale\" \"14\"\r\n";
            RpSettings.Apply(ref xml, ref cfg);
            Equal("0.700000", GtaGraphics.Get(xml, "LodScale"), "Distanz unter 95 % bleibt");
            Equal("seta \"vid_budgetScale\" \"14\"\r\n", cfg, "Budget im Bereich bleibt, kein VSync-Eintrag erfunden");
        });

        Run("Sicherung: jeder Stand wird gespeichert, Schritt für Schritt zurück", () =>
        {
            string dir = NewDir("gtasnap");
            string cfg = Path.Combine(dir, "fivem.cfg"), xml = Path.Combine(dir, "gta5_settings.xml");
            string root = Path.Combine(dir, "sicherung");
            File.WriteAllText(cfg, "cfg-1");
            File.WriteAllText(xml, "xml-1");
            GameConfigFiles.Snapshot(root, cfg, xml, Path.Combine(dir, "fehlt.txt"), null);
            File.WriteAllText(cfg, "cfg-2");
            GameConfigFiles.Snapshot(root, cfg, xml);
            File.WriteAllText(cfg, "cfg-3");
            File.WriteAllText(xml, "xml-3");
            File.SetAttributes(xml, FileAttributes.ReadOnly);
            Equal(2, GameConfigFiles.Snapshots(root).Count, "Stände");

            Equal(2, GameConfigFiles.RestoreLatest(root), "Dateien zurückgespielt");
            Equal("cfg-2", File.ReadAllText(cfg), "Stand 2");
            Equal("xml-1", File.ReadAllText(xml), "xml Stand 2");
            Equal(1, GameConfigFiles.Snapshots(root).Count, "noch ein Stand");
            GameConfigFiles.RestoreLatest(root);
            Equal("cfg-1", File.ReadAllText(cfg), "Stand 1");
            Equal(0, GameConfigFiles.Snapshots(root).Count, "alle zurückgespielt");
        });

        Run("Sicherung: höchstens 20 Stände, der älteste bleibt erhalten", () =>
        {
            string dir = NewDir("gtasnapmax");
            string cfg = Path.Combine(dir, "fivem.cfg"), root = Path.Combine(dir, "sicherung");
            for (int i = 0; i < 23; i++)
            {
                File.WriteAllText(cfg, "v" + i);
                GameConfigFiles.Snapshot(root, cfg);
            }
            List<string> all = GameConfigFiles.Snapshots(root);
            Equal(20, all.Count, "Stände");
            for (int i = 0; i < 20; i++) GameConfigFiles.RestoreLatest(root);
            Equal("v0", File.ReadAllText(cfg), "Ursprungszustand bleibt erreichbar");
        });

        Run("Sicherung: Einzelsicherung aus v1.2.0 wird übernommen", () =>
        {
            string dir = NewDir("gtasnapold");
            string cfg = Path.Combine(dir, "fivem.cfg"), root = Path.Combine(dir, "sicherung");
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "0_fivem.cfg"), "alt");
            File.WriteAllText(Path.Combine(root, "dateien.txt"), "0_fivem.cfg|" + cfg + "\r\n");
            File.WriteAllText(cfg, "neu");
            File.WriteAllText(cfg, "neu");
            GameConfigFiles.Snapshot(root, cfg);
            Equal(2, GameConfigFiles.Snapshots(root).Count, "alter + neuer Stand");
            GameConfigFiles.RestoreLatest(root);
            GameConfigFiles.RestoreLatest(root);
            Equal("alt", File.ReadAllText(cfg), "alter Stand zurück");
        });

        Run("Texture Budget: Speicher wie in FiveM berechnet", () =>
        {
            Equal(3145728000L, TextureBudget.BudgetBytes(0), "Stufe 0");
            Equal(7864320000L, TextureBudget.BudgetBytes(18), "Stufe 18");
            Equal("7,3 GB", TextureBudget.FormatGb(TextureBudget.BudgetBytes(18)), "Anzeige");
        });

        Run("Dateien: Kodierung (BOM) bleibt erhalten", () =>
        {
            string dir = NewDir("gtaenc");
            string withBom = Path.Combine(dir, "a.xml"), without = Path.Combine(dir, "b.cfg");
            File.WriteAllText(withBom, "x", new UTF8Encoding(true));
            File.WriteAllText(without, "y", new UTF8Encoding(false));
            foreach (string f in new[] { withBom, without })
            {
                Encoding enc;
                string text = GameConfigFiles.ReadText(f, out enc);
                GameConfigFiles.WriteText(f, text + "ä", enc);
            }
            byte[] a = File.ReadAllBytes(withBom), b = File.ReadAllBytes(without);
            True(a[0] == 0xEF && a.Length == 3 + 1 + 2, "BOM bleibt");
            True(b[0] == (byte)'y' && b.Length == 1 + 2, "ohne BOM bleibt ohne");
            True(!File.Exists(without + ".rrtool.tmp"), "keine Zwischendatei übrig");
        });
    }

    // ---------------------------------------------------------------- Steam

    const string LocalConfig =
        "\"UserLocalConfigStore\"\n{\n\t\"friends\"\n\t{\n\t\t\"name\"\t\t\"a \\\"b\\\" { c }\"\n\t}\n" +
        "\t\"Software\"\n\t{\n\t\t\"apps\"\n\t\t{\n\t\t\t\"7\"\n\t\t\t{\n\t\t\t\t\"x\"\t\t\"1\"\n\t\t\t}\n\t\t}\n\t}\n}\n";

    static byte[] Bytes(params object[] parts)
    {
        var ms = new MemoryStream();
        foreach (object p in parts)
        {
            if (p is string) { byte[] b = Encoding.UTF8.GetBytes((string)p); ms.Write(b, 0, b.Length); }
            else if (p is byte[]) { var b = (byte[])p; ms.Write(b, 0, b.Length); }
            else ms.WriteByte(Convert.ToByte(p));
        }
        return ms.ToArray();
    }

    // Ein fremder Eintrag, wie Steam ihn schreibt, mit einem Feld, das das Tool nicht kennt.
    static byte[] ForeignEntry()
    {
        return Bytes(2, "appid", 0, new byte[] { 0x78, 0x56, 0x34, 0x92 },
                     1, "AppName", 0, "Mein Spiel", 0,
                     1, "Exe", 0, "\"D:\\Spiel\\spiel.exe\"", 0,
                     1, "sortas", 0, "zzz", 0,
                     0, "tags", 0, 1, "0", 0, "Favorit", 0, 8,
                     8);
    }

    static byte[] ShortcutFile(params byte[][] bodies)
    {
        var ms = new MemoryStream();
        byte[] head = Bytes(0, "shortcuts", 0);
        ms.Write(head, 0, head.Length);
        for (int i = 0; i < bodies.Length; i++)
        {
            byte[] key = Bytes(0, i.ToString(), 0);
            ms.Write(key, 0, key.Length);
            ms.Write(bodies[i], 0, bodies[i].Length);
        }
        ms.WriteByte(8);
        ms.WriteByte(8);
        return ms.ToArray();
    }

    static void SameBytes(byte[] expected, byte[] actual, string what)
    {
        Equal(BitConverter.ToString(expected), BitConverter.ToString(actual), what);
    }

    static void SteamHintTests()
    {
        Run("Steam: Hinweis auf den Start über Steam steht vorn und ist eindeutig", () =>
        {
            True(SteamLibrary.SetupDoneTitle.Contains("über Steam starten"), "Titel");
            True(SteamLibrary.SetupDoneText.StartsWith("Der Controller funktioniert nur, wenn du FiveM über Steam startest"), "erste Zeile");
            True(SteamLibrary.StartViaSteamHint.StartsWith("WICHTIG"), "Abfrage vorher");
        });
    }

    static void NetworkFixTests()
    {
        Run("Netzwerk Fix: genau die drei Befehle in der richtigen Reihenfolge", () =>
        {
            string netsh = Path.Combine(Environment.SystemDirectory, "netsh.exe");
            string ipconfig = Path.Combine(Environment.SystemDirectory, "ipconfig.exe");
            var commands = NetworkFix.Commands();
            Equal(3, commands.Count, "Anzahl");
            Equal(netsh, commands[0].Key, "1 Programm");
            Equal("winsock reset", commands[0].Value, "1");
            Equal(netsh, commands[1].Key, "2 Programm");
            Equal("int ip reset", commands[1].Value, "2");
            Equal(ipconfig, commands[2].Key, "3 Programm");
            Equal("/flushdns", commands[2].Value, "3");
        });

        Run("Netzwerk Fix: Sicherung liest nur und landet als Textdatei im Ordner", () =>
        {
            string dir = Path.Combine(NewDir("netfix"), "Sicherung Netzwerk");
            string file = NetworkFix.SaveSnapshot(dir);
            True(File.Exists(file), "Datei da");
            Equal(dir, Path.GetDirectoryName(file), "Ordner");
            True(file.EndsWith(".txt"), "Textdatei");
            True(new FileInfo(file).Length > 50, "nicht leer");
        });

        Run("Netzwerk Fix: Fertig-Text nennt Fehlercode und Sicherung", () =>
        {
            True(NetworkFix.DoneText(0, null).StartsWith("Alle Befehle wurden ausgeführt"), "ok");
            True(!NetworkFix.DoneText(0, null).Contains("Netzwerkdaten"), "ohne Sicherung");
            True(NetworkFix.DoneText(0, @"C:\a.txt").Contains(@"C:\a.txt"), "mit Sicherung");
            True(NetworkFix.DoneText(5, null).Contains("Code 5"), "Fehlercode");
        });
    }

    static void RockstarTests()
    {
        Run("Rockstar: Cache-Ordner ist genau der Launcher-Ordner", () =>
        {
            Equal(@"C:\L\Rockstar Games\Launcher", RockstarLauncher.CacheDir(@"C:\L"), "Pfad");
        });

        Run("Rockstar: Admin-Befehle sind genau die zwei erwarteten", () =>
        {
            var commands = RockstarLauncher.NetworkCommands();
            Equal(2, commands.Count, "Anzahl");
            Equal(Path.Combine(Environment.SystemDirectory, "ipconfig.exe"), commands[0].Key, "ipconfig");
            Equal("/flushdns", commands[0].Value, "flushdns");
            Equal(Path.Combine(Environment.SystemDirectory, "netsh.exe"), commands[1].Key, "netsh");
            Equal("winsock reset", commands[1].Value, "winsock");
            True(File.Exists(commands[0].Key) && File.Exists(commands[1].Key), "Programme vorhanden");
        });

        Run("Rockstar: Fehlercode des ersten Fehlers zählt, alle Befehle laufen durch", () =>
        {
            string cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            string marker = Path.Combine(NewDir("rockstarrun"), "lief.txt");
            Func<string, KeyValuePair<string, string>> c = a => new KeyValuePair<string, string>(cmd, "/c " + a);
            Equal(0, SystemCommands.RunAll(new[] { c("exit 0"), c("exit 0") }), "alles gut");
            Equal(5, SystemCommands.RunAll(new[] { c("exit 5"), c("echo x> \"" + marker + "\""), c("exit 7") }), "erster Fehler");
            True(File.Exists(marker), "zweiter Befehl lief trotzdem");
        });

        Run("Rockstar: Launcher wird am Pfad erkannt, fremde Launcher bleiben in Ruhe", () =>
        {
            True(RockstarLauncher.IsLauncherProcess("Launcher", @"C:\Program Files\Rockstar Games\Launcher\Launcher.exe", null), "Standardordner");
            True(RockstarLauncher.IsLauncherProcess("launcher", @"C:\Games\Rockstar\Launcher.exe", @"C:\Games\Rockstar"), "eigener Ordner");
            True(RockstarLauncher.IsLauncherProcess("Launcher", @"C:\Games\Rockstar\Launcher.exe", "\"C:\\Games\\Rockstar\\\""), "Ordner mit Anführungszeichen");
            True(!RockstarLauncher.IsLauncherProcess("Launcher", @"C:\Games\RockstarOther\Launcher.exe", @"C:\Games\Rockstar"), "ähnlicher Ordner");
            True(!RockstarLauncher.IsLauncherProcess("Launcher", @"C:\Epic\Launcher.exe", @"C:\Games\Rockstar"), "fremder Launcher");
            True(!RockstarLauncher.IsLauncherProcess("Launcher", null, @"C:\Games\Rockstar"), "Pfad unbekannt");
            True(RockstarLauncher.IsLauncherProcess("RockstarService", null, null), "Dienst");
            True(RockstarLauncher.IsLauncherProcess("LauncherPatcher", null, null), "Patcher");
            True(!RockstarLauncher.IsLauncherProcess("notepad", @"C:\Games\Rockstar\notepad.exe", @"C:\Games\Rockstar"), "anderes Programm");
        });

        Run("Rockstar: laufendes Spiel wird erkannt", () =>
        {
            True(RockstarLauncher.IsGameProcessName("GTA5"), "GTA5");
            True(RockstarLauncher.IsGameProcessName("GTA5_Enhanced"), "Enhanced");
            True(RockstarLauncher.IsGameProcessName("PlayGTAV"), "PlayGTAV");
            True(!RockstarLauncher.IsGameProcessName("Launcher"), "Launcher zählt nicht");
        });

        Run("Rockstar: Fertig-Text nennt übersprungene Dateien nur, wenn es welche gibt", () =>
        {
            True(!RockstarLauncher.DoneText(0).Contains("Benutzung"), "keine");
            True(RockstarLauncher.DoneText(1).Contains("1 Datei war noch in Benutzung und wurde nicht"), "eine");
            True(RockstarLauncher.DoneText(3).Contains("3 Dateien waren noch in Benutzung und wurden nicht"), "mehrere");
        });

        Run("Rockstar: Leeren entfernt den Inhalt, der Ordner daneben bleibt", () =>
        {
            string local = NewDir("rockstarclean");
            string cache = RockstarLauncher.CacheDir(local);
            string sub = Path.Combine(cache, "B9908D06");
            string other = Path.Combine(local, "Rockstar Games", "GTA V");
            Directory.CreateDirectory(sub);
            Directory.CreateDirectory(other);
            File.WriteAllText(Path.Combine(cache, "settings_user.dat"), "x");
            File.WriteAllText(Path.Combine(sub, "datei.bin"), "x");
            File.WriteAllText(Path.Combine(other, "behalten.txt"), "x");
            FolderCleaner.CleanContents(cache, null);
            Equal(0, Directory.GetFileSystemEntries(cache).Length, "Cache leer");
            True(File.Exists(Path.Combine(other, "behalten.txt")), "GTA-V-Ordner unberührt");
        });
    }

    // Tabelle wie in der echten settings.db von TeamSpeak 3
    static string NewTs3Db(string name, string pack)
    {
        string file = Path.Combine(NewDir(name), "settings.db");
        using (SqliteDb db = SqliteDb.Create(file))
        {
            db.Execute("CREATE TABLE Notifications (timestamp INTEGER UNSIGNED NOT NULL, key VARCHAR NOT NULL UNIQUE, value VARCHAR)");
            db.Execute("CREATE TABLE General (timestamp INTEGER UNSIGNED NOT NULL, key VARCHAR NOT NULL UNIQUE, value VARCHAR)");
            db.Execute("INSERT INTO General VALUES (1, 'Identity', 'geheim')");
            db.Execute("INSERT INTO Notifications VALUES (2, 'NotifyWhichSounds', '1')");
            db.Execute("INSERT INTO Notifications VALUES (3, 'nosounds/Disabled', '20485,17475')");
            if (pack != null) db.Execute("INSERT INTO Notifications VALUES (4, 'SoundPack', ?)", pack);
        }
        return file;
    }

    static void Ts3SoundTests()
    {
        Run("TS-Sounds: abschalten und zurück auf das vorherige Paket", () =>
        {
            string file = NewTs3Db("ts3a", "default_male");
            var settings = new AppSettings(Path.Combine(Path.GetDirectoryName(file), "s.ini"));
            True(Ts3Sounds.IsAvailable(file), "verfügbar");
            True(!Ts3Sounds.AreDisabled(file), "am Anfang an");

            Ts3Sounds.Disable(file, settings);
            True(Ts3Sounds.AreDisabled(file), "abgeschaltet");
            Equal("nosounds", Ts3Sounds.ReadPack(file), "Paket");

            Ts3Sounds.Enable(file, settings);
            Equal("default_male", Ts3Sounds.ReadPack(file), "vorheriges Paket");
        });

        Run("TS-Sounds: zweimal abschalten verliert das vorherige Paket nicht", () =>
        {
            string file = NewTs3Db("ts3b", "default_speech");
            var settings = new AppSettings(Path.Combine(Path.GetDirectoryName(file), "s.ini"));
            Ts3Sounds.Disable(file, settings);
            Ts3Sounds.Disable(file, settings);
            Ts3Sounds.Enable(file, settings);
            Equal("default_speech", Ts3Sounds.ReadPack(file), "vorheriges Paket");
        });

        Run("TS-Sounds: war schon aus oder nie gesetzt, dann zurück auf Standard", () =>
        {
            string off = NewTs3Db("ts3c", "nosounds");
            var s1 = new AppSettings(Path.Combine(Path.GetDirectoryName(off), "s.ini"));
            Ts3Sounds.Disable(off, s1);
            Ts3Sounds.Enable(off, s1);
            Equal("default", Ts3Sounds.ReadPack(off), "war schon aus");

            string unset = NewTs3Db("ts3d", null);
            var s2 = new AppSettings(Path.Combine(Path.GetDirectoryName(unset), "s.ini"));
            Equal<string>(null, Ts3Sounds.ReadPack(unset), "nie gesetzt");
            Ts3Sounds.Disable(unset, s2);
            Equal("nosounds", Ts3Sounds.ReadPack(unset), "Eintrag wird angelegt");
            Ts3Sounds.Enable(unset, s2);
            Equal("default", Ts3Sounds.ReadPack(unset), "Standard");
        });

        Run("TS-Sounds: alles andere in der Datenbank bleibt unverändert", () =>
        {
            string file = NewTs3Db("ts3e", "default");
            Ts3Sounds.Disable(file, new AppSettings(Path.Combine(Path.GetDirectoryName(file), "s.ini")));
            using (SqliteDb db = SqliteDb.OpenRead(file))
            {
                Equal("geheim", db.Scalar("SELECT value FROM General WHERE key = 'Identity'"), "Identität");
                Equal("1", db.Scalar("SELECT value FROM Notifications WHERE key = 'NotifyWhichSounds'"), "andere Einstellung");
                Equal("20485,17475", db.Scalar("SELECT value FROM Notifications WHERE key = 'nosounds/Disabled'"), "Liste");
                Equal("3", db.Scalar("SELECT count(*) FROM Notifications"), "keine doppelten Zeilen");
                Equal("ok", db.Scalar("PRAGMA integrity_check"), "Datenbank intakt");
            }
        });

        Run("TS-Sounds: fehlende Datei oder Tabelle ändert nichts", () =>
        {
            string dir = NewDir("ts3f");
            string missing = Path.Combine(dir, "settings.db");
            True(!Ts3Sounds.IsAvailable(missing), "Datei fehlt");
            Throws<IOException>(() => Ts3Sounds.Disable(missing, new AppSettings(Path.Combine(dir, "s.ini"))));
            True(!File.Exists(missing), "keine leere Datenbank angelegt");

            string empty = Path.Combine(dir, "empty.db");
            using (SqliteDb db = SqliteDb.Create(empty)) db.Execute("CREATE TABLE Other (a)");
            True(!Ts3Sounds.IsAvailable(empty), "Tabelle fehlt");
            Throws<InvalidOperationException>(() => Ts3Sounds.Enable(empty, new AppSettings(Path.Combine(dir, "s.ini"))));
        });

        Run("TS-Sounds: bei gesperrter Datenbank bleibt der alte Wert", () =>
        {
            string file = NewTs3Db("ts3g", "default");
            using (SqliteDb holder = SqliteDb.OpenWrite(file))
            {
                holder.Execute("BEGIN EXCLUSIVE");
                Throws<IOException>(() => Ts3Sounds.Disable(file, new AppSettings(Path.Combine(Path.GetDirectoryName(file), "s.ini"))));
                holder.Execute("ROLLBACK");
            }
            Equal("default", Ts3Sounds.ReadPack(file), "unverändert");
        });

        Run("TS-Sounds: Pfade mit Umlauten funktionieren", () =>
        {
            string dir = Path.Combine(NewDir("ts3h"), "Müller Ä");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "settings.db");
            File.Move(NewTs3Db("ts3i", "default"), file);
            Ts3Sounds.Disable(file, new AppSettings(Path.Combine(dir, "s.ini")));
            True(Ts3Sounds.AreDisabled(file), "abgeschaltet");
        });
    }

    static void ModCheckTests()
    {
        Run("Shader-Cache: nur die bekannten Ordner, nie der ganze AppData-Ordner", () =>
        {
            var targets = CleanTargets.Shaders(@"C:\L");
            var paths = new List<string>();
            foreach (CleanTarget t in targets) paths.Add(t.Path);
            Equal(@"C:\L\NVIDIA\DXCache|C:\L\AMD\DxCache|C:\L\AMD\DxcCache|C:\L\Intel\ShaderCache|C:\L\D3DSCache",
                  string.Join("|", paths), "Pfade");
        });

        Run("FiveM-Ini: GTA-Ordner wird gelesen", () =>
        {
            Equal(@"C:\Games\GTA V", FiveMIni.GetGamePath("[Game]\r\nIVPath=C:\\Games\\GTA V\r\nUpdateChannel=production\r\n"), "Pfad");
            Equal<string>(null, FiveMIni.GetGamePath("[Game]\r\nUpdateChannel=production\r\n"), "nicht eingetragen");
            Equal<string>(null, FiveMIni.GetGamePath("[Game]\r\nIVPath=\r\n"), "leer");
            Equal<string>(null, FiveMIni.GetGamePath("[Addons]\r\nIVPath=C:\\Falsch\r\n"), "anderer Abschnitt");
        });

        Run("FiveM-Ini: Lesen kommt mit BOM und ANSI zurecht", () =>
        {
            string dir = NewDir("modini");
            string file = Path.Combine(dir, "CitizenFX.ini");
            File.WriteAllText(file, "[Game]\r\nIVPath=C:\\Spiele Müller\\GTA\r\n", new UTF8Encoding(true));
            Equal(@"C:\Spiele Müller\GTA", FiveMIni.GetGamePath(FiveMIni.ReadText(file)), "UTF-8 mit BOM");
            File.WriteAllText(file, "[Game]\r\nIVPath=C:\\Spiele Müller\\GTA\r\n", Encoding.Default);
            Equal(@"C:\Spiele Müller\GTA", FiveMIni.GetGamePath(FiveMIni.ReadText(file)), "ANSI");
        });

        Run("Grafik-Mods: findet Eingeklinktes, Original-Dateien bleiben außen vor", () =>
        {
            string dir = NewDir("modcheck");
            string gta = Path.Combine(dir, "GTA");
            string app = Path.Combine(dir, "FiveM.app");
            string plugins = Path.Combine(app, "plugins");
            string mods = Path.Combine(app, "mods");
            Directory.CreateDirectory(gta);
            Directory.CreateDirectory(Path.Combine(plugins, "reshade-shaders"));
            Directory.CreateDirectory(Path.Combine(mods, "sub"));
            foreach (string f in new[] { "D3D11.dll", "dinput8.dll", "Trainer.asi", "d3dcompiler_46.dll", "bink2w64.dll", "GTA5.exe", "x.ini" })
                File.WriteAllText(Path.Combine(gta, f), "");
            foreach (string f in new[] { "dxgi.dll", "NVE.asi", "Toggler.addon64", "ReShade.ini", "NVE.bin" })
                File.WriteAllText(Path.Combine(plugins, f), "");
            File.WriteAllText(Path.Combine(plugins, "reshade-shaders", "inner.dll"), "");
            File.WriteAllText(Path.Combine(mods, "a.rpf"), "");
            File.WriteAllText(Path.Combine(mods, "sub", "b.rpf"), "");

            ModReport r = ModCheck.Scan(app, gta);
            Equal("D3D11.dll|dinput8.dll|Trainer.asi", string.Join("|", r.GtaFiles), "GTA-Ordner");
            Equal("dxgi.dll|NVE.asi|Toggler.addon64", string.Join("|", r.PluginFiles), "Plugins");
            Equal(2, r.ModFileCount, "Mods");
            Equal(8, r.Total, "Summe");
            Equal(gta, r.GtaDir, "GTA-Ordner gemeldet");
            True(File.Exists(Path.Combine(gta, "D3D11.dll")) && File.Exists(Path.Combine(plugins, "dxgi.dll")), "nichts verändert");
        });

        Run("Grafik-Mods: fehlende Ordner sind kein Fehler", () =>
        {
            string app = Path.Combine(NewDir("modempty"), "FiveM.app");
            Directory.CreateDirectory(app);
            ModReport none = ModCheck.Scan(app, null);
            Equal(0, none.Total, "ohne GTA-Ordner");
            Equal<string>(null, none.GtaDir, "GTA unbekannt");
            Equal<string>(null, ModCheck.Scan(app, Path.Combine(app, "gibtsnicht")).GtaDir, "GTA-Ordner fehlt");
        });
    }

    static string ReleaseJson(string tag, string assetName, string url, string state)
    {
        return "{\"tag_name\":\"" + tag + "\",\"assets\":[" +
               "{\"name\":\"RetroRoleplayTool.exe\",\"state\":\"uploaded\",\"browser_download_url\":\"" + UpdateBase + tag + "/RetroRoleplayTool.exe\"}," +
               "{\"name\":\"" + assetName + "\",\"state\":\"" + state + "\",\"browser_download_url\":\"" + url + "\"}]}";
    }

    const string UpdateBase = "https://github.com/cryd-s/retro-roleplay-tool/releases/download/";
    const string UpdateZip = UpdateBase + "v1.5.0/RetroRoleplayTool.zip";

    static void UpdateTests()
    {
        Run("Update: neuere Version liefert den direkten ZIP-Link", () =>
        {
            UpdateInfo u = UpdateCheck.Evaluate("1.4.0", ReleaseJson("v1.5.0", "RetroRoleplayTool.zip", UpdateZip, "uploaded"));
            Equal("1.5.0", u.Version, "Version");
            Equal(UpdateZip, u.ZipUrl, "ZIP-Link, nicht die exe");
        });

        Run("Update: gleiche oder ältere Version zeigt nichts", () =>
        {
            string json = ReleaseJson("v1.5.0", "RetroRoleplayTool.zip", UpdateZip, "uploaded");
            Equal<UpdateInfo>(null, UpdateCheck.Evaluate("1.5.0", json), "gleich");
            Equal<UpdateInfo>(null, UpdateCheck.Evaluate("1.6.0", json), "lokal neuer");
        });

        Run("Update: Versionen werden numerisch verglichen", () =>
        {
            string url = UpdateBase + "v1.10.0/RetroRoleplayTool.zip";
            string json = ReleaseJson("v1.10.0", "RetroRoleplayTool.zip", url, "uploaded");
            Equal("1.10.0", UpdateCheck.Evaluate("1.9.0", json).Version, "1.10.0 ist neuer als 1.9.0");
        });

        Run("Update: ohne fertige ZIP kein Hinweis", () =>
        {
            Equal<UpdateInfo>(null, UpdateCheck.Evaluate("1.4.0", ReleaseJson("v1.5.0", "Anderes.zip", UpdateZip, "uploaded")), "andere Datei");
            Equal<UpdateInfo>(null, UpdateCheck.Evaluate("1.4.0", ReleaseJson("v1.5.0", "RetroRoleplayTool.zip", UpdateZip, "starter")), "Upload läuft noch");
            Equal<UpdateInfo>(null, UpdateCheck.Evaluate("1.4.0", "{\"tag_name\":\"v1.5.0\"}"), "keine Dateien");
        });

        Run("Update: Links außerhalb des Repositorys werden nie geöffnet", () =>
        {
            foreach (string url in new[]
            {
                "https://example.com/RetroRoleplayTool.zip",
                "http://github.com/cryd-s/retro-roleplay-tool/releases/download/v1.5.0/RetroRoleplayTool.zip",
                "https://github.com/jemand/retro-roleplay-tool/releases/download/v1.5.0/RetroRoleplayTool.zip",
                "https://github.com/cryd-s/retro-roleplay-tool/releases/download.example.com/RetroRoleplayTool.zip",
            })
                Equal<UpdateInfo>(null, UpdateCheck.Evaluate("1.4.0", ReleaseJson("v1.5.0", "RetroRoleplayTool.zip", url, "uploaded")), url);
        });

        Run("Update: unbrauchbare Antworten zeigen nichts", () =>
        {
            foreach (string json in new[] { "", "kein json", "[]", "{}", "{\"tag_name\":5}", "{\"message\":\"API rate limit exceeded\"}" })
                Equal<UpdateInfo>(null, UpdateCheck.Evaluate("1.4.0", json), "Antwort: " + json);
            Equal<UpdateInfo>(null, UpdateCheck.Evaluate("1.4.0", ReleaseJson("neu", "RetroRoleplayTool.zip", UpdateZip, "uploaded")), "Tag ohne Version");
        });
    }

    static void SteamTests()
    {
        Run("Steam: Prüfsumme CRC32 (Standard-Prüfwert)", () =>
            Equal(0xCBF43926u, SteamShortcuts.Crc32(Encoding.ASCII.GetBytes("123456789")), "CRC32"));

        Run("Steam: App-ID aus Exe und Name, oberstes Bit gesetzt", () =>
        {
            uint expected = SteamShortcuts.Crc32(Encoding.UTF8.GetBytes("\"C:\\FiveM\\FiveM.exe\"FiveM")) | 0x80000000u;
            Equal(expected, SteamShortcuts.ComputeAppId("\"C:\\FiveM\\FiveM.exe\"", "FiveM"), "App-ID");
        });

        Run("Steam: fehlende oder leere Datei ergibt leere Liste", () =>
        {
            Equal(0, SteamShortcuts.Parse(new byte[0]).Count, "leer");
            Equal(0, SteamShortcuts.Parse(null).Count, "null");
            SameBytes(Bytes(0, "shortcuts", 0, 8, 8), SteamShortcuts.Build(new List<SteamShortcut>()), "leere Datei");
        });

        Run("Steam: Eintrag hat das erwartete Format", () =>
        {
            SteamShortcut s = SteamShortcuts.Create("FiveM", "C:\\Spiele\\FiveM\\FiveM.exe");
            uint id = SteamShortcuts.ComputeAppId("\"C:\\Spiele\\FiveM\\FiveM.exe\"", "FiveM");
            byte[] expected = Bytes(
                2, "appid", 0, BitConverter.GetBytes(id),
                1, "AppName", 0, "FiveM", 0,
                1, "Exe", 0, "\"C:\\Spiele\\FiveM\\FiveM.exe\"", 0,
                1, "StartDir", 0, "\"C:\\Spiele\\FiveM\\\"", 0,
                1, "icon", 0, "C:\\Spiele\\FiveM\\FiveM.exe", 0,
                1, "ShortcutPath", 0, 0,
                1, "LaunchOptions", 0, 0,
                2, "IsHidden", 0, 0, 0, 0, 0,
                2, "AllowDesktopConfig", 0, 1, 0, 0, 0,
                2, "AllowOverlay", 0, 1, 0, 0, 0,
                2, "OpenVR", 0, 0, 0, 0, 0,
                2, "Devkit", 0, 0, 0, 0, 0,
                1, "DevkitGameID", 0, 0,
                2, "DevkitOverrideAppID", 0, 0, 0, 0, 0,
                2, "LastPlayTime", 0, 0, 0, 0, 0,
                1, "FlatpakAppID", 0, 0,
                0, "tags", 0, 8,
                8);
            SameBytes(expected, s.Body, "Eintrag");
            Equal(id, s.AppId, "App-ID");
        });

        Run("Steam: neue Datei lässt sich wieder lesen", () =>
        {
            var list = new List<SteamShortcut> { SteamShortcuts.Create("FiveM", "C:\\Spiele\\FiveM\\FiveM.exe") };
            List<SteamShortcut> read = SteamShortcuts.Parse(SteamShortcuts.Build(list));
            Equal(1, read.Count, "Anzahl");
            Equal("FiveM", read[0].AppName, "Name");
            Equal("\"C:\\Spiele\\FiveM\\FiveM.exe\"", read[0].Exe, "Exe");
            True(SteamShortcuts.IsFiveM(read[0]), "als FiveM erkannt");
        });

        Run("Steam: vorhandene Spiele bleiben Byte für Byte erhalten", () =>
        {
            byte[] file = ShortcutFile(ForeignEntry());
            List<SteamShortcut> list = SteamShortcuts.Parse(file);
            Equal("Mein Spiel", list[0].AppName, "Name");
            Equal(0x92345678u, list[0].AppId, "App-ID");
            True(!SteamShortcuts.IsFiveM(list[0]), "kein FiveM");
            SameBytes(file, SteamShortcuts.Build(list), "unverändert");
        });

        Run("Steam: beschädigte Datei wird abgelehnt", () =>
        {
            Throws<InvalidDataException>(() => SteamShortcuts.Parse(Bytes(0, "shortcuts", 0, 0, "0", 0, 1, "AppName")));
            Throws<InvalidDataException>(() => SteamShortcuts.Parse(Bytes("kein vdf")));
            Throws<InvalidDataException>(() => SteamShortcuts.Parse(Bytes(0, "shortcuts", 0, 8)));
            Throws<InvalidDataException>(() => SteamShortcuts.Parse(Bytes(0, "shortcuts", 0, 8, 8, 8)));
        });

        Run("VDF-Text: Wert lesen, Klammern und Anführungszeichen in Texten stören nicht", () =>
        {
            Equal("a \\\"b\\\" { c }", TextVdf.Get(LocalConfig, new[] { "UserLocalConfigStore", "friends" }, "name"), "Text");
            Equal("1", TextVdf.Get(LocalConfig, new[] { "userlocalconfigstore", "SOFTWARE", "Apps", "7" }, "X"), "Groß/klein egal");
            Equal<string>(null, TextVdf.Get(LocalConfig, new[] { "UserLocalConfigStore", "apps" }, "x"), "fehlt");
        });

        Run("VDF-Text: fehlende Abschnitte werden angelegt, Rest bleibt gleich", () =>
        {
            string[] path = { "UserLocalConfigStore", "apps", "123" };
            string result = TextVdf.Set(LocalConfig, path, "UseSteamControllerConfig", "2");
            string added = "\t\"apps\"\n\t{\n\t\t\"123\"\n\t\t{\n\t\t\t\"UseSteamControllerConfig\"\t\t\"2\"\n\t\t}\n\t}\n";
            Equal(LocalConfig.Substring(0, LocalConfig.Length - 2) + added + "}\n", result, "Text");
            Equal("2", TextVdf.Get(result, path, "UseSteamControllerConfig"), "lesen");
        });

        Run("VDF-Text: vorhandener Wert wird ersetzt, weiterer ergänzt", () =>
        {
            string[] path = { "UserLocalConfigStore", "Software", "apps", "7" };
            string replaced = TextVdf.Set(LocalConfig, path, "x", "9");
            Equal(LocalConfig.Replace("\"x\"\t\t\"1\"", "\"x\"\t\t\"9\""), replaced, "ersetzt");
            string added = TextVdf.Set(LocalConfig, path, "y", "5");
            Equal(LocalConfig.Replace("\"x\"\t\t\"1\"\n", "\"x\"\t\t\"1\"\n\t\t\t\t\"y\"\t\t\"5\"\n"), added, "ergänzt");
        });

        Run("VDF-Text: Wert entfernen lässt den Rest gleich", () =>
        {
            string[] path = { "UserLocalConfigStore", "Software", "apps", "7" };
            string added = TextVdf.Set(LocalConfig, path, "y", "5");
            Equal(LocalConfig, TextVdf.Remove(added, path, "y"), "entfernt");
            Equal(LocalConfig, TextVdf.Remove(LocalConfig, path, "gibtsnicht"), "nichts zu tun");
        });

        Run("VDF-Text: Windows-Zeilenenden bleiben erhalten", () =>
        {
            string crlf = LocalConfig.Replace("\n", "\r\n");
            string result = TextVdf.Set(crlf, new[] { "UserLocalConfigStore", "apps", "1" }, "k", "v");
            True(!result.Replace("\r\n", "").Contains("\n"), "nur CRLF");
        });

        Run("VDF-Text: beschädigter Text wird abgelehnt", () =>
        {
            string[] path = { "UserLocalConfigStore", "apps" };
            Throws<InvalidDataException>(() => TextVdf.Set("\"UserLocalConfigStore\"\n{\n", path, "k", "v"));
            Throws<InvalidDataException>(() => TextVdf.Set("\"UserLocalConfigStore\"\n{\n}\n}\n", path, "k", "v"));
            Throws<InvalidDataException>(() => TextVdf.Set("\"UserLocalConfigStore\"\n{\n\t\"a\n}\n", path, "k", "v"));
            Throws<InvalidDataException>(() => TextVdf.Set("\"Andere\"\n{\n}\n", path, "k", "v"));
        });

        Run("Steam: aktives Konto hat Vorrang, sonst das zuletzt genutzte", () =>
        {
            string login = "\"users\"\n{\n\t\"76561198000000001\"\n\t{\n\t\t\"MostRecent\"\t\t\"0\"\n\t}\n" +
                           "\t\"76561198060265735\"\n\t{\n\t\t\"MostRecent\"\t\t\"1\"\n\t}\n}\n";
            var dirs = new List<string> { "0", "39734273", "100000007" };
            Equal("39734273", SteamLibrary.PickUser(39734273, login, dirs), "aktiv");
            Equal("100000007", SteamLibrary.PickUser(0, login, dirs), "zuletzt genutzt");
            Equal("100000007", SteamLibrary.PickUser(999, login, dirs), "aktives Konto ohne Ordner");
            Equal("39734273", SteamLibrary.PickUser(0, null, new List<string> { "0", "39734273" }), "nur ein Konto");
            Equal<string>(null, SteamLibrary.PickUser(0, null, dirs), "nicht eindeutig");
        });

        Run("Steam: FiveM einrichten und wieder entfernen, Fremdes bleibt", () =>
        {
            string dir = NewDir("steamlib");
            string appDir = Path.Combine(dir, "FiveM", "FiveM.app");
            Directory.CreateDirectory(appDir);
            string exe = Path.Combine(dir, "FiveM", "FiveM.exe");
            File.WriteAllText(exe, "x");
            string shortcuts = Path.Combine(dir, "config", "shortcuts.vdf"), local = Path.Combine(dir, "config", "localconfig.vdf");
            Directory.CreateDirectory(Path.GetDirectoryName(shortcuts));
            File.WriteAllBytes(shortcuts, ShortcutFile(ForeignEntry()));
            File.WriteAllText(local, LocalConfig, new UTF8Encoding(false));

            Equal(false, SteamLibrary.ReadStatus(shortcuts, local).InSteam, "vorher");
            SteamLibrary.AddFiveM(shortcuts, local, appDir);
            SteamStatus s = SteamLibrary.ReadStatus(shortcuts, local);
            True(s.InSteam && s.ControllerOn, "eingerichtet");
            string appId = SteamShortcuts.ComputeAppId("\"" + exe + "\"", "FiveM").ToString();
            Equal("2", TextVdf.Get(File.ReadAllText(local), new[] { "UserLocalConfigStore", "apps", appId }, "UseSteamControllerConfig"), "Controller");

            SteamLibrary.AddFiveM(shortcuts, local, appDir);        // zweimal: kein doppelter Eintrag
            List<SteamShortcut> list = SteamShortcuts.Parse(File.ReadAllBytes(shortcuts));
            Equal(2, list.Count, "Einträge");
            Equal("Mein Spiel", list[0].AppName, "fremdes Spiel");

            SteamLibrary.RemoveFiveM(shortcuts, local);
            Equal(false, SteamLibrary.ReadStatus(shortcuts, local).InSteam, "entfernt");
            SameBytes(ShortcutFile(ForeignEntry()), File.ReadAllBytes(shortcuts), "fremdes Spiel unverändert");
            Equal<string>(null, TextVdf.Get(File.ReadAllText(local), new[] { "UserLocalConfigStore", "apps", appId }, "UseSteamControllerConfig"), "Controller entfernt");
        });

        Run("Steam: ohne vorhandene shortcuts.vdf wird sie angelegt", () =>
        {
            string dir = NewDir("steamnew");
            string appDir = Path.Combine(dir, "FiveM", "FiveM.app");
            Directory.CreateDirectory(appDir);
            File.WriteAllText(Path.Combine(dir, "FiveM", "FiveM.exe"), "x");
            string shortcuts = Path.Combine(dir, "config", "shortcuts.vdf"), local = Path.Combine(dir, "config", "localconfig.vdf");
            Directory.CreateDirectory(Path.GetDirectoryName(shortcuts));
            File.WriteAllText(local, LocalConfig, new UTF8Encoding(false));
            SteamLibrary.AddFiveM(shortcuts, local, appDir);
            True(SteamLibrary.ReadStatus(shortcuts, local).ControllerOn, "eingerichtet");
        });

        Run("Steam: fehlende FiveM.exe oder kaputte Datei ändert nichts", () =>
        {
            string dir = NewDir("steamfail");
            string appDir = Path.Combine(dir, "FiveM", "FiveM.app");
            Directory.CreateDirectory(appDir);
            string shortcuts = Path.Combine(dir, "shortcuts.vdf"), local = Path.Combine(dir, "localconfig.vdf");
            File.WriteAllBytes(shortcuts, ShortcutFile(ForeignEntry()));
            File.WriteAllText(local, LocalConfig, new UTF8Encoding(false));
            Throws<FileNotFoundException>(() => SteamLibrary.AddFiveM(shortcuts, local, appDir));
            Equal(LocalConfig, File.ReadAllText(local), "localconfig unverändert");

            File.WriteAllText(Path.Combine(dir, "FiveM", "FiveM.exe"), "x");
            File.WriteAllText(local, "\"UserLocalConfigStore\"\n{\n", new UTF8Encoding(false));
            byte[] before = File.ReadAllBytes(shortcuts);
            Throws<InvalidDataException>(() => SteamLibrary.AddFiveM(shortcuts, local, appDir));
            SameBytes(before, File.ReadAllBytes(shortcuts), "shortcuts unverändert");
        });
    }

    // ---------------------------------------------------------------- Helfer

    static void Run(string name, Action test)
    {
        try { test(); passed++; Console.WriteLine("PASS  " + name); }
        catch (Exception ex)
        {
            failed++;
            Console.WriteLine("FAIL  " + name);
            Console.WriteLine("      " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    static void Equal<T>(T expected, T actual, string what)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception(what + ": erwartet <" + Show(expected) + ">, bekommen <" + Show(actual) + ">");
    }

    static string Show(object o)
    {
        return o == null ? "null" : o.ToString().Replace("\r", "\\r").Replace("\n", "\\n");
    }

    static void True(bool condition, string what)
    {
        if (!condition) throw new Exception(what);
    }

    static void Throws<TEx>(Action a) where TEx : Exception
    {
        try { a(); }
        catch (TEx) { return; }
        throw new Exception("erwartet " + typeof(TEx).Name + ", aber keine passende Ausnahme");
    }

    static string NewDir(string name)
    {
        string p = Path.Combine(sandbox, name);
        Directory.CreateDirectory(p);
        return p;
    }

    static void WriteFile(string path, int bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllBytes(path, new byte[bytes]);
    }

    static void Mklink(string link, string target)
    {
        var psi = new ProcessStartInfo("cmd.exe", "/c mklink /J \"" + link + "\" \"" + target + "\"");
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        psi.RedirectStandardOutput = true;
        using (Process p = Process.Start(psi)) { p.StandardOutput.ReadToEnd(); p.WaitForExit(); }
    }
}
