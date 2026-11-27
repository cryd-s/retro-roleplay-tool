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
            Ds4Tests();
            UpdateTests();
            SelfUpdateTests();
            ModCheckTests();
            Ts3SoundTests();
            RockstarTests();
            NetworkFixTests();
            SaltyDnsTests();
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
    // Eine erhöhte Git-Bash aktiviert das Backup-Recht und erbt es an diesen Prozess weiter: Sperren greifen dann nicht,
    // Tests mit Sperren könnten nichts prüfen. Das Tool und tools\release.ps1 laufen ohne dieses Recht.
    static bool? denyRulesApply;

    static bool DenyRulesApply()
    {
        if (denyRulesApply == null)
        {
            string dir = NewDir("denyprobe");
            string sid = System.Security.Principal.WindowsIdentity.GetCurrent().User.Value;
            string icacls = Path.Combine(Environment.SystemDirectory, "icacls.exe");
            Equal(0, SystemCommands.RunAll(new[] { new KeyValuePair<string, string>(icacls, "\"" + dir + "\" /deny *" + sid + ":(RD)") }), "Probe-Sperre");
            try { Directory.GetFiles(dir); denyRulesApply = false; }
            catch (UnauthorizedAccessException) { denyRulesApply = true; }
            SystemCommands.RunAll(new[] { new KeyValuePair<string, string>(icacls, "\"" + dir + "\" /remove:d *" + sid) });
        }
        if (denyRulesApply == false)
            Console.WriteLine("      HINWEIS: Sperren greifen in dieser Shell nicht (Backup-Recht aktiv), Test übersprungen. Bitte in PowerShell prüfen.");
        return denyRulesApply == true;
    }

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
            if (!DenyRulesApply()) return;
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

        Run("TS3: Overwolf-Programme werden erkannt, TeamSpeak-Teile nicht", () =>
        {
            True(Ts3.IsOverwolfImage(@"C:\Users\x\AppData\Local\Temp\nsA1B2.tmp\OverwolfInstallerDownloader.exe"), "Downloader");
            True(Ts3.IsOverwolfImage(@"C:\Temp\nsx.tmp\OverwolfTeamSpeakInstaller.exe"), "Installer");
            True(Ts3.IsOverwolfImage(@"C:\Program Files (x86)\Overwolf\OverwolfLauncher.exe"), "Launcher");
            True(!Ts3.IsOverwolfImage(@"C:\Temp\nsx.tmp\vc_redist.x64.exe"), "VC++ bleibt");
            True(!Ts3.IsOverwolfImage(@"C:\Program Files\TeamSpeak 3 Client\ts3client_win64.exe"), "TeamSpeak bleibt");
            True(!Ts3.IsOverwolfImage(null), "unbekannt bleibt");
        });

        Run("TS3: Admin-Teil startet nur von TeamSpeak signierte Installer", () =>
        {
            Equal(Ts3.InvalidInstallerCode, Ts3.RunElevated(new string[0]), "ohne Datei");
            Equal(Ts3.InvalidInstallerCode, Ts3.RunElevated(new[] { @"C:\gibt\es\nicht.exe" }), "fehlende Datei");
            Equal(Ts3.InvalidInstallerCode, Ts3.RunElevated(new[] { Path.Combine(Environment.SystemDirectory, "cmd.exe") }),
                  "Windows-Programm ist nicht TeamSpeak");
            True(Ts3.IsTeamSpeakSubject("CN=TeamSpeak Systems GmbH, O=TeamSpeak Systems GmbH, L=Krün, S=Bayern, C=DE"), "echt");
            True(!Ts3.IsTeamSpeakSubject("CN=x, O=TeamSpeak Systems GmbH Fake, C=DE"), "ähnlicher Name");
            True(!Ts3.IsTeamSpeakSubject("CN=x, OU=TeamSpeak Systems GmbH, O=Andere, C=DE"), "nur Abteilung");
        });

        Run("Job-Wächter: verbotenes Unterprogramm wird sofort beendet, Rest läuft durch", () =>
        {
            string dir = NewDir("guard");
            string fake = Path.Combine(dir, "OverwolfInstallerDownloader.exe");
            File.Copy(Path.Combine(Environment.SystemDirectory, "PING.EXE"), fake);
            string ping = Path.Combine(Environment.SystemDirectory, "PING.EXE");
            string cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            // „Installer“: startet Overwolf ohne zu warten (wie NSIS Exec), dann ein erlaubtes Programm, dann Ende mit Code 7.
            // Der Wächter ist so schnell, dass schon der Start scheitert; cmd meldet das als „Zugriff verweigert“ (2>nul).
            string script = "/c start \"\" /b \"" + fake + "\" -n 30 127.0.0.1 >nul 2>nul & \"" + ping + "\" -n 2 127.0.0.1 >nul & exit 7";
            int blocked;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            int code = GuardedProcess.Run(cmd, script, Ts3.IsOverwolfImage, out blocked);
            Equal(7, code, "Fehlercode des Installers");
            Equal(1, blocked, "ein Programm verhindert");
            True(watch.Elapsed.TotalSeconds < 15, "wartet nicht auf das beendete Programm: " + watch.Elapsed.TotalSeconds);
            foreach (var p in System.Diagnostics.Process.GetProcessesByName("OverwolfInstallerDownloader"))
                using (p) True(!string.Equals(p.MainModule.FileName, fake, StringComparison.OrdinalIgnoreCase), "läuft nicht mehr");
        });

        Run("Job-Wächter: ohne verbotene Programme bleibt alles gleich", () =>
        {
            int blocked;
            string cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            Equal(0, GuardedProcess.Run(cmd, "/c exit 0", Ts3.IsOverwolfImage, out blocked), "Code");
            Equal(0, blocked, "nichts verhindert");
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

        Run("Texture Budget: vorbelegt sind 95 %, gemerkte Wahl gilt nur im erlaubten Bereich", () =>
        {
            Equal(18, TextureBudget.DefaultValue, "Vorgabe");
            Equal(18, TextureBudget.FromStored(null), "nichts gemerkt");
            Equal(18, TextureBudget.FromStored("kaputt"), "unlesbar");
            Equal(14, TextureBudget.FromStored("14"), "gemerkt");
            Equal(10, TextureBudget.FromStored("3"), "zu niedrig");
            Equal(18, TextureBudget.FromStored("19"), "zu hoch");
            Equal("95 %  ·  7,3 GB", TextureBudget.Label(18), "Anzeige");
        });

        Run("Texture Budget: das Ziel folgt dem Regler", () =>
        {
            string cfg = "seta \"vid_budgetScale\" \"14\"\r\n";
            SettingCheck same = RpSettings.Check(BadXml, cfg, 14)[0];
            True(same.Ok, "gleicher Wert passt");
            Equal("74 %", same.Target, "Ziel gewählt");
            SettingCheck other = RpSettings.Check(BadXml, cfg, 18)[0];
            True(!other.Ok, "anderer Wert passt nicht");
            Equal("95 %", other.Target, "Ziel Vorgabe");
            Equal("95 %", RpSettings.Check(BadXml, cfg)[0].Target, "ohne Wahl gilt die Vorgabe");
        });

        Run("Texture Budget: Übernehmen setzt genau die Wahl", () =>
        {
            foreach (string start in new[] { "seta \"vid_budgetScale\" \"18\"\r\n", "seta \"vid_budgetScale\" \"0\"\r\n", "" })
            {
                string xml = BadXml, cfg = start;
                RpSettings.Apply(ref xml, ref cfg, 12);
                Equal("12", FiveMCfg.Get(cfg, "vid_budgetScale"), "Wahl aus: " + start);
                True(RpSettings.Check(xml, cfg, 12)[0].Ok, "passt danach");
            }
            string x = BadXml, c = "";
            RpSettings.Apply(ref x, ref c, 99);
            Equal("18", FiveMCfg.Get(c, "vid_budgetScale"), "nie über 95 %");
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
            RpSettings.Apply(ref xml, ref cfg, 14);
            Equal("0.700000", GtaGraphics.Get(xml, "LodScale"), "Distanz unter 95 % bleibt");
            Equal("seta \"vid_budgetScale\" \"14\"\r\n", cfg, "Budget wie gewählt bleibt, kein VSync-Eintrag erfunden");
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

    static void SaltyDnsTests()
    {
        const string Id = "{11111111-2222-3333-4444-555555555555}", Other = "{AAAAAAAA-2222-3333-4444-555555555555}";
        Func<List<KeyValuePair<string, string>>, string> args = list => string.Join("\n", list.ConvertAll(c => c.Value));

        Run("DNS: genau die vier Google-Server in der richtigen Reihenfolge", () =>
        {
            var commands = SaltyDns.CommandsOn(new[] { new DnsAdapter { Id = Id, Index = 17, HasIPv6 = true } });
            Equal(string.Join("\n", new[]
            {
                "interface ipv4 set dnsservers name=17 source=static address=8.8.8.8 register=primary validate=no",
                "interface ipv4 add dnsservers name=17 address=8.8.4.4 index=2 validate=no",
                "interface ipv6 set dnsservers name=17 source=static address=2001:4860:4860::8888 register=primary validate=no",
                "interface ipv6 add dnsservers name=17 address=2001:4860:4860::8844 index=2 validate=no",
                "/flushdns",
            }), args(commands), "Befehle");
            Equal(Path.Combine(Environment.SystemDirectory, "netsh.exe"), commands[0].Key, "voller Pfad");
        });

        Run("DNS: ohne IPv6 am Adapter gibt es keine IPv6-Befehle", () =>
        {
            string all = args(SaltyDns.CommandsOn(new[] { new DnsAdapter { Id = Id, Index = 17, HasIPv6 = false } }));
            True(all.Contains("ipv4") && !all.Contains("ipv6"), "nur IPv4");
        });

        Run("DNS: Zurück stellt feste Server oder automatisch wieder her", () =>
        {
            var adapters = new[] { new DnsAdapter { Id = Id, Index = 5, HasIPv6 = true }, new DnsAdapter { Id = Other, Index = 6, HasIPv6 = false } };
            var backup = new List<DnsBackupEntry> { new DnsBackupEntry { Id = Id, V4 = new[] { "1.1.1.1", "1.0.0.1" } } };
            string all = args(SaltyDns.CommandsOff(adapters, backup));
            True(all.Contains("ipv4 set dnsservers name=5 source=static address=1.1.1.1 register=primary"), "fester Server");
            True(all.Contains("ipv4 add dnsservers name=5 address=1.0.0.1 index=2"), "zweiter Server");
            True(all.Contains("ipv6 set dnsservers name=5 source=dhcp"), "IPv6 war automatisch");
            True(all.Contains("ipv4 set dnsservers name=6 source=dhcp"), "ohne Eintrag automatisch");
            True(!all.Contains("8.8.8.8"), "kein Google mehr");
        });

        Run("DNS: Google wird erkannt, IPv6 zählt nur wenn vorhanden", () =>
        {
            True(SaltyDns.IsGoogle(new DnsAdapter { V4 = SaltyDns.GoogleV4, HasIPv6 = false }), "nur IPv4");
            True(!SaltyDns.IsGoogle(new DnsAdapter { V4 = SaltyDns.GoogleV4, HasIPv6 = true }), "IPv6 fehlt");
            True(SaltyDns.IsGoogle(new DnsAdapter { V4 = SaltyDns.GoogleV4, V6 = SaltyDns.GoogleV6, HasIPv6 = true }), "beide");
            True(!SaltyDns.IsGoogle(new DnsAdapter { V4 = new[] { "8.8.8.8" }, HasIPv6 = false }), "nur ein Server");
            True(!SaltyDns.IsGoogle(new DnsAdapter { V4 = new[] { "8.8.4.4", "1.1.1.1" }, HasIPv6 = false }), "anderer Server");
            True(SaltyDns.IsGoogle(new DnsAdapter { V4 = SaltyDns.GoogleV4, V6 = new[] { "2001:4860:4860:0:0:0:0:8888", "2001:4860:4860:0::8844" }, HasIPv6 = true }), "andere Schreibweise");
        });

        Run("DNS: Zurücksetzen gilt nur als geklappt, wenn Google wirklich weg ist", () =>
        {
            var google = new DnsAdapter { Id = Id, V4 = SaltyDns.GoogleV4, HasIPv6 = false };
            var auto = new DnsAdapter { Id = Id, HasIPv6 = false };
            True(SaltyDns.IsRestored(new[] { auto }, new List<DnsBackupEntry>()), "automatisch");
            True(!SaltyDns.IsRestored(new[] { google }, new List<DnsBackupEntry>()), "keine Sicherung, Google noch da");
            True(!SaltyDns.IsRestored(new[] { google }, new List<DnsBackupEntry> { new DnsBackupEntry { Id = Id } }), "war automatisch, Google noch da");
            True(!SaltyDns.IsRestored(new[] { google }, new List<DnsBackupEntry> { new DnsBackupEntry { Id = Id, V4 = new[] { "1.1.1.1" } } }), "war Cloudflare, Google noch da");
            True(SaltyDns.IsRestored(new[] { google }, new List<DnsBackupEntry> { new DnsBackupEntry { Id = Id, V4 = SaltyDns.GoogleV4 } }), "war schon vorher Google");
            True(!SaltyDns.IsRestored(new[] { google }, Path.Combine(NewDir("dnsmissing"), "dns.txt")), "fehlende Datei, Google noch da");
        });

        Run("DNS: Nur gültige Adressen kommen in Befehle", () =>
        {
            Equal("8.8.8.8|1.1.1.1", string.Join("|", SaltyDns.ParseServers("8.8.8.8, 1.1.1.1", System.Net.Sockets.AddressFamily.InterNetwork)), "Komma und Leerzeichen");
            Equal(0, SaltyDns.ParseServers(null, System.Net.Sockets.AddressFamily.InterNetwork).Length, "leer");
            Equal(0, SaltyDns.ParseServers("8.8.8.8 & calc", System.Net.Sockets.AddressFamily.InterNetworkV6).Length, "falsche Art");
            Equal("8.8.8.8", string.Join("|", SaltyDns.ParseServers("8.8.8.8;& calc.exe", System.Net.Sockets.AddressFamily.InterNetwork)), "kein eingeschleuster Text");
        });

        Run("DNS: Sicherung merkt sich den Zustand und überschreibt ihn nicht mit Google", () =>
        {
            string first = SaltyDns.BuildBackup("", new[] { new DnsAdapter { Id = Id, V4 = new[] { "1.1.1.1" } }, new DnsAdapter { Id = Other } });
            var entries = SaltyDns.ParseBackup(first);
            Equal(2, entries.Count, "zwei Adapter");
            Equal("1.1.1.1", entries[0].V4[0], "fester Server");
            Equal(0, entries[1].V4.Length, "automatisch");

            string second = SaltyDns.BuildBackup(first, new[] { new DnsAdapter { Id = Id, V4 = SaltyDns.GoogleV4, HasIPv6 = false } });
            Equal("1.1.1.1", SaltyDns.ParseBackup(second)[0].V4[0], "Google überschreibt die Sicherung nicht");
            Equal(2, SaltyDns.ParseBackup(second).Count, "anderer Adapter bleibt");

            string unknown = SaltyDns.BuildBackup("", new[] { new DnsAdapter { Id = Id, V4 = SaltyDns.GoogleV4, HasIPv6 = false } });
            Equal(0, SaltyDns.ParseBackup(unknown)[0].V4.Length, "war schon Google: zurück auf automatisch");
        });

        Run("DNS: Beschädigte Sicherung wird nicht ausgeführt", () =>
        {
            var entries = SaltyDns.ParseBackup("kaputt\r\nnoch|mehr\r\n" + Id + "|8.8.8.8 & calc|x\r\n");
            Equal(1, entries.Count, "nur die gültige Zeile");
            Equal("8.8.8.8", string.Join("|", entries[0].V4), "nur die Adresse");
            Equal(0, entries[0].V6.Length, "Unsinn verworfen");
        });

        Run("DNS: Nur echte verbundene Netzwerkkarten", () =>
        {
            var up = System.Net.NetworkInformation.OperationalStatus.Up;
            var eth = System.Net.NetworkInformation.NetworkInterfaceType.Ethernet;
            True(SaltyDns.IsCandidate(eth, up, true, "Intel(R) Ethernet Controller I226-V"), "Ethernet");
            True(SaltyDns.IsCandidate(System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211, up, true, "MediaTek Wi-Fi 6E"), "WLAN");
            True(!SaltyDns.IsCandidate(eth, System.Net.NetworkInformation.OperationalStatus.Down, true, "Intel"), "getrennt");
            True(!SaltyDns.IsCandidate(eth, up, false, "Intel"), "ohne Internetweg");
            True(!SaltyDns.IsCandidate(System.Net.NetworkInformation.NetworkInterfaceType.Loopback, up, true, "Software Loopback"), "Loopback");
            foreach (string vpn in new[] { "Tailscale Tunnel", "NordLynx Tunnel", "TAP-NordVPN Windows Adapter V9", "Hyper-V Virtual Ethernet Adapter", "VMware Virtual Ethernet", "WireGuard Tunnel" })
                True(!SaltyDns.IsCandidate(eth, up, true, vpn), "bleibt unberührt: " + vpn);
        });

        Run("DNS: Admin-Teil lehnt falsche Eingaben ab, ohne etwas zu ändern", () =>
        {
            const string ok = @"C:\U\R\RetroRoleplayTool\Sicherung DNS\dns.txt";
            True(SaltyDns.IsValidBackupPath(ok), "gültig");
            foreach (string bad in new[] { null, "", "dns.txt", @"C:\Windows\dns.txt", @"\\server\x\RetroRoleplayTool\Sicherung DNS\dns.txt" })
                True(!SaltyDns.IsValidBackupPath(bad), "abgelehnt: " + bad);
            Equal(64, SaltyDns.RunElevated(new[] { "on" }), "fehlendes Argument");
            Equal(64, SaltyDns.RunElevated(new[] { "evil", ok }), "falscher Modus");
            Equal(64, SaltyDns.RunElevated(new[] { "on", @"C:\Windows\dns.txt" }), "falscher Pfad");
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
            True(RockstarLauncher.IsGameProcessName("GTAVLauncher"), "GTAVLauncher");
            True(!RockstarLauncher.IsGameProcessName("Launcher"), "Launcher zählt nicht");
        });

        Run("Rockstar: Fertig-Text nennt übersprungene Dateien nur, wenn es welche gibt", () =>
        {
            True(!RockstarLauncher.DoneText(0, 0, true).Contains("Benutzung"), "keine");
            True(RockstarLauncher.DoneText(0, 1, true).Contains("1 Datei war noch in Benutzung und wurde nicht"), "eine");
            True(RockstarLauncher.DoneText(0, 3, true).Contains("3 Dateien waren noch in Benutzung und wurden nicht"), "mehrere");
        });

        Run("Rockstar: Fertig-Text nennt genau die fehlgeschlagenen Schritte", () =>
        {
            string ok = RockstarLauncher.DoneText(0, 0, true);
            True(ok.StartsWith("Der Fix ist durch.") && !ok.Contains("•"), "alles gut");
            string bad = RockstarLauncher.DoneText(RockstarLauncher.DefenderFailed | RockstarLauncher.ServiceFailed, 0, false);
            True(bad.Contains("Defender-Freigabe") && bad.Contains("Rockstar-Dienst"), "gemeldet");
            True(!bad.Contains("Winsock konnten") && !bad.Contains("Rechte konnten"), "nur die betroffenen");
            True(bad.Contains("selbst"), "Launcher von Hand");
        });

        Run("Rockstar: Defender-Freigabe nur für die exakte Organisation", () =>
        {
            True(RockstarLauncher.IsRockstarSubject("CN=\"Rockstar Games, Inc.\", OU=Rockstar Partners, O=\"Rockstar Games, Inc.\", L=New York, S=New York, C=US"), "echt");
            True(!RockstarLauncher.IsRockstarSubject("CN=\"Rockstar Games, Inc.\", O=Fremd GmbH, C=DE"), "nur CN");
            True(!RockstarLauncher.IsRockstarSubject("OU=\"Rockstar Games, Inc.\", O=Fremd"), "nur OU");
            True(!RockstarLauncher.IsRockstarSubject("O=\"Rockstar Games, Inc. Fake\", C=US"), "ähnlicher Name");
            True(!RockstarLauncher.IsRockstarSubject(null), "leer");
        });

        Run("Rockstar: Unsignierte Dateien gelten nie als echt", () =>
        {
            string file = Path.Combine(NewDir("rockstarsig"), "Launcher.exe");
            File.WriteAllText(file, "MZ gefaelscht");
            True(!RockstarLauncher.IsRockstarSigned(file), "unsigniert");
            True(!RockstarLauncher.IsRockstarSigned(file + ".fehlt"), "fehlt");
            True(!RockstarLauncher.IsRockstarSigned(Path.Combine(Environment.SystemDirectory, "cmd.exe")), "fremde Signatur");
        });

        Run("Rockstar: Programmordner bekommen nie Schreibrechte oder einen Reset", () =>
        {
            const string sid = "S-1-5-21-1-2-3-1001";
            var targets = RockstarLauncher.PlanTargets(sid, @"E:\Dok", @"C:\U\L", @"C:\U\R", @"C:\ProgramData",
                @"C:\Program Files", @"C:\Program Files (x86)", @"C:\Games\Rockstar", @"C:\Program Files\Rockstar Games\Social Club");
            var byPath = new Dictionary<string, RepairTarget>(StringComparer.OrdinalIgnoreCase);
            foreach (RepairTarget t in targets) { True(!byPath.ContainsKey(t.Path), "doppelt: " + t.Path); byPath[t.Path] = t; }

            foreach (string own in new[] { @"E:\Dok\Rockstar Games", @"C:\U\L\Rockstar Games", @"C:\U\R\Rockstar Games" })
            {
                True(byPath[own].ResetFirst, "Reset: " + own);
                Equal("*" + sid, byPath[own].OwnerSid, "Besitzer: " + own);
            }
            foreach (string program in new[] { @"C:\Games\Rockstar", @"C:\Program Files\Rockstar Games\Social Club", @"C:\Program Files\Rockstar Games\Launcher", @"C:\Program Files (x86)\Rockstar Games\Social Club" })
            {
                RepairTarget t = byPath[program];
                True(!t.ResetFirst, "kein Reset: " + program);
                Equal("*S-1-5-32-544", t.OwnerSid, "Besitzer: " + program);
                foreach (string g in t.Grants)
                    True(!g.Contains(sid) && (g.EndsWith(":(OI)(CI)RX") || g.StartsWith("*S-1-5-18") || g.StartsWith("*S-1-5-32-544")), "nur lesen: " + g);
            }
            foreach (RepairTarget t in targets)
                True(t.Path.IndexOf("Grand Theft Auto", StringComparison.OrdinalIgnoreCase) < 0 && (t.RootOnly || !t.Path.EndsWith(@"Program Files\Rockstar Games")), "GTA bleibt: " + t.Path);
        });

        Run("Rockstar: Programmordner verlieren Schreibrechte des eigenen Kontos, eigene Ordner nicht", () =>
        {
            const string sid = "S-1-5-21-1-2-3-1001";
            var targets = RockstarLauncher.PlanTargets(sid, @"E:\Dok", @"C:\U\L", @"C:\U\R", @"C:\ProgramData",
                @"C:\Program Files", null, @"C:\Games\Rockstar", null);
            foreach (RepairTarget t in targets)
            {
                string all = "";
                foreach (var c in RockstarLauncher.AclCommands(t)) all += c.Value + "\n";
                bool isProgram = t.Path.StartsWith(@"C:\Games\Rockstar") || t.Path.StartsWith(@"C:\Program Files");
                Equal(isProgram, all.Contains("/remove:g *" + sid), "Regel entfernen: " + t.Path);
                if (isProgram && !t.RootOnly) True(all.IndexOf("/remove:g") < all.IndexOf("/grant") && !all.Contains("/reset"), "Reihenfolge: " + t.Path);
            }
        });

        Run("Rockstar: Am Oberordner wird nur die Regel des Kontos entfernt, nicht rekursiv", () =>
        {
            const string sid = "S-1-5-21-1-2-3-1001";
            var targets = RockstarLauncher.PlanTargets(sid, @"E:\Dok", @"C:\U\L", @"C:\U\R", @"C:\ProgramData", @"C:\Program Files", null, null, null);
            RepairTarget root = targets.Find(t => t.Path == @"C:\Program Files\Rockstar Games");
            True(root != null && root.RootOnly, "Oberordner geplant");
            var commands = RockstarLauncher.AclCommands(root);
            Equal(1, commands.Count, "nur ein Befehl");
            Equal("\"C:\\Program Files\\Rockstar Games\" /remove:g *" + sid + " /C /Q", commands[0].Value, "genau dieser Befehl");
        });

        Run("Rockstar: Dokumente-Ordner wird geleert, Logs und Verknüpfungen bleiben", () =>
        {
            string root = NewDir("rockstardocs");
            string docs = Path.Combine(root, "Dokumente");
            string launcher = RockstarLauncher.DocumentsLauncherDir(docs);
            string save = Path.Combine(docs, "Rockstar Games", "GTA V", "Profiles", "SGTA50000");
            string outside = Path.Combine(root, "Woanders");
            Directory.CreateDirectory(Path.Combine(launcher, "Profiles"));
            Directory.CreateDirectory(Path.GetDirectoryName(save));
            Directory.CreateDirectory(outside);
            File.WriteAllText(save, "Spielstand");
            File.WriteAllText(Path.Combine(launcher, "launcher.log"), "log");
            File.WriteAllText(Path.Combine(launcher, "settings.dat"), "x");
            File.WriteAllText(Path.Combine(launcher, "Profiles", "a.dat"), "x");
            File.WriteAllText(Path.Combine(outside, "fremd.txt"), "x");
            string cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            Equal(0, SystemCommands.RunAll(new[] { new KeyValuePair<string, string>(cmd, "/c mklink /J \"" + Path.Combine(launcher, "Link") + "\" \"" + outside + "\"") }), "Verknüpfung");

            Equal(1, RockstarLauncher.CleanKeepingLogs(launcher), "Verknüpfung übersprungen");
            True(File.Exists(Path.Combine(launcher, "launcher.log")), "Log bleibt");
            True(!File.Exists(Path.Combine(launcher, "settings.dat")), "Datei weg");
            True(!Directory.Exists(Path.Combine(launcher, "Profiles")), "Unterordner weg");
            True(File.Exists(Path.Combine(outside, "fremd.txt")), "Ziel der Verknüpfung unberührt");
            Equal("Spielstand", File.ReadAllText(save), "Spielstand unberührt");
            Equal(0, RockstarLauncher.CleanKeepingLogs(Path.Combine(root, "fehlt")), "fehlender Ordner");
        });

        Run("Rockstar: Defender erlaubt nur Rockstar-Programme, nie das Spiel oder einen Ordner", () =>
        {
            var apps = RockstarLauncher.AllowedAppCandidates(@"C:\Games\Rockstar", @"C:\Program Files\Rockstar Games\Social Club");
            Equal(@"C:\Games\Rockstar\Launcher.exe", apps[0], "Launcher zuerst");
            True(apps.Contains(@"C:\Games\Rockstar\RockstarService.exe"), "Dienst");
            True(apps.Contains(@"C:\Program Files\Rockstar Games\Social Club\SocialClubHelper.exe"), "Social Club");
            foreach (string app in apps) True(app.EndsWith(".exe") && app.IndexOf("GTA", StringComparison.OrdinalIgnoreCase) < 0, "nur Programme: " + app);
            Equal(0, RockstarLauncher.AllowedAppCandidates(null, null).Count, "nichts bekannt");
        });

        Run("Rockstar: Gefährliche Pfade aus der Registry werden abgelehnt", () =>
        {
            foreach (string bad in new[] { null, "", @"C:\", @"C:", @"C:\Windows", @"C:\Program Files", @"C:\Program Files\", @"C:\Program Files (x86)", @"C:\Users", @"C:\Games", @"C:\Program Files\Rockstar Games", @"\\server\share\x", "relativ" })
                True(!RockstarLauncher.IsSafeProgramDir(bad), "abgelehnt: " + bad);
            True(RockstarLauncher.IsSafeProgramDir(@"C:\Games\Rockstar"), "eigener Ordner");
            True(RockstarLauncher.IsSafeProgramDir("\"C:\\Program Files\\Rockstar Games\\Launcher\\\""), "mit Anführungszeichen");
        });

        Run("Rockstar: Admin-Teil prüft seine Eingaben", () =>
        {
            const string sid = "S-1-5-21-1-2-3-1001", backup = @"C:\U\R\RetroRoleplayTool\Sicherung Rockstar\2026";
            True(RockstarLauncher.IsValidElevatedInput(sid, @"E:\", @"C:\U\L", @"C:\U\R", backup), "gültig");
            True(!RockstarLauncher.IsValidElevatedInput("S-1-5-18", @"E:\", @"C:\U\L", @"C:\U\R", backup), "falsche SID");
            True(!RockstarLauncher.IsValidElevatedInput(sid + " /x", @"E:\", @"C:\U\L", @"C:\U\R", backup), "SID mit Zusatz");
            True(!RockstarLauncher.IsValidElevatedInput(sid, "relativ", @"C:\U\L", @"C:\U\R", backup), "relativer Pfad");
            True(!RockstarLauncher.IsValidElevatedInput(sid, @"\\server\x", @"C:\U\L", @"C:\U\R", backup), "Netzwerkpfad");
            True(!RockstarLauncher.IsValidElevatedInput(sid, @"E:\", @"C:\U\L", @"C:\U\R", @"C:\Windows"), "falscher Sicherungsordner");
            Equal(64, RockstarLauncher.RunElevated(new[] { "falsch" }), "falsche Anzahl bricht ab");
            Equal(64, RockstarLauncher.RunElevated(new[] { "S-1-5-18", @"E:\", @"C:\U\L", @"C:\U\R", backup }), "ungültig bricht ab");
        });

        Run("Rockstar: Argumente überstehen Leerzeichen und E:\\", () =>
        {
            Equal("\"C:\\Mein Ordner\"", RockstarLauncher.Quote(@"C:\Mein Ordner"), "Leerzeichen");
            Equal("\"E:\\\\\"", RockstarLauncher.Quote(@"E:\"), "Backslash am Ende");
            True(RockstarLauncher.BuildElevatedArguments("S", @"E:\", "L", "R", "B").StartsWith(RockstarLauncher.ElevatedArgument + " \"S\" \"E:\\\\\" "), "Reihenfolge");
        });

        Run("Rockstar: Dienst wird auf Automatisch gestellt", () =>
        {
            Equal("config \"Rockstar Service\" start= auto obj= LocalSystem", RockstarLauncher.ServiceConfigCommands()[0].Value, "config");
            Equal("stop \"Rockstar Service\"", RockstarLauncher.ServiceStopCommands()[0].Value, "stop");
        });

        Run("Rockstar: Reparatur entfernt Sperren, sichert Rechte und lässt Dateien in Ruhe", () =>
        {
            string root = NewDir("rockstaracl");
            string dir = Path.Combine(root, "Rockstar Games");
            string save = Path.Combine(dir, "GTA V", "Profiles");
            Directory.CreateDirectory(save);
            string file = Path.Combine(save, "SGTA50000");
            File.WriteAllText(file, "Spielstand");
            string sid = System.Security.Principal.WindowsIdentity.GetCurrent().User.Value;

            // Schreibsperre setzen, wie sie der Launcher-Fehler auslösen würde
            string icacls = Path.Combine(Environment.SystemDirectory, "icacls.exe");
            Equal(0, SystemCommands.RunAll(new[] { new KeyValuePair<string, string>(icacls, "\"" + file + "\" /deny *" + sid + ":(W)") }), "Sperre gesetzt");
            Throws<UnauthorizedAccessException>(() => File.AppendAllText(file, "x"));

            var target = new RepairTarget { Path = dir, OwnerSid = "*" + sid, ResetFirst = true, Grants = new[] { "*" + sid + ":(OI)(CI)F" } };
            string backup = Path.Combine(root, "Sicherung");
            True(RockstarLauncher.Repair(target, backup, 1), "Reparatur meldet Erfolg");

            Equal("Spielstand", File.ReadAllText(file), "Inhalt unverändert");
            File.AppendAllText(file, "x");
            True(File.Exists(Path.Combine(backup, "Rechte 1.txt")), "Rechte gesichert");
            True(File.ReadAllText(Path.Combine(backup, "Liste.txt")).Contains("/restore"), "Rückweg notiert");
        });

        Run("Rockstar: Ordner mit verknüpften Unterordnern werden nicht angefasst", () =>
        {
            string root = NewDir("rockstarlink");
            string dir = Path.Combine(root, "Rockstar Games");
            string outside = Path.Combine(root, "Woanders");
            Directory.CreateDirectory(dir);
            Directory.CreateDirectory(outside);
            string cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            Equal(0, SystemCommands.RunAll(new[] { new KeyValuePair<string, string>(cmd, "/c mklink /J \"" + Path.Combine(dir, "Link") + "\" \"" + outside + "\"") }), "Verknüpfung");
            True(RockstarLauncher.ContainsLinkedFolder(dir), "erkannt");
            string backup = Path.Combine(root, "Sicherung");
            var target = new RepairTarget { Path = dir, OwnerSid = "*S-1-5-32-544", ResetFirst = true, Grants = new[] { "*S-1-5-18:(OI)(CI)F" } };
            string before = new DirectoryInfo(dir).GetAccessControl().GetSecurityDescriptorSddlForm(System.Security.AccessControl.AccessControlSections.All);
            string outsideBefore = new DirectoryInfo(outside).GetAccessControl().GetSecurityDescriptorSddlForm(System.Security.AccessControl.AccessControlSections.All);
            True(!RockstarLauncher.Repair(target, backup, 1), "abgelehnt");
            Equal(before, new DirectoryInfo(dir).GetAccessControl().GetSecurityDescriptorSddlForm(System.Security.AccessControl.AccessControlSections.All), "Ordner unverändert");
            Equal(outsideBefore, new DirectoryInfo(outside).GetAccessControl().GetSecurityDescriptorSddlForm(System.Security.AccessControl.AccessControlSections.All), "Ziel unverändert");
        });

        Run("Rockstar: Gesperrter Unterordner wird repariert statt abzustürzen", () =>
        {
            if (!DenyRulesApply()) return;
            string root = NewDir("rockstarlocked");
            string dir = Path.Combine(root, "Rockstar Games");
            string locked = Path.Combine(dir, "GTA V");
            Directory.CreateDirectory(Path.Combine(locked, "Tiefer"));
            string file = Path.Combine(locked, "Tiefer", "save.dat");
            File.WriteAllText(file, "Spielstand");
            string sid = System.Security.Principal.WindowsIdentity.GetCurrent().User.Value;
            string icacls = Path.Combine(Environment.SystemDirectory, "icacls.exe");
            Equal(0, SystemCommands.RunAll(new[] { new KeyValuePair<string, string>(icacls, "\"" + locked + "\" /deny *" + sid + ":(RD)") }), "Sperre gesetzt");
            True(!RockstarLauncher.IsTreeReadable(dir), "vorher gesperrt");

            var log = new List<string>();
            var target = new RepairTarget { Path = dir, OwnerSid = "*" + sid, ResetFirst = true, Grants = new[] { "*" + sid + ":(OI)(CI)F" } };
            True(RockstarLauncher.Repair(target, Path.Combine(root, "Sicherung"), 1, log.Add), "repariert");

            True(RockstarLauncher.IsTreeReadable(dir), "danach lesbar");
            Equal("Spielstand", File.ReadAllText(file), "Inhalt unverändert");
            True(log.Count == 1 && log[0].EndsWith("repariert"), "im Protokoll");
        });

        Run("Rockstar: Nicht reparierbarer Ordner meldet Fehler und stoppt die anderen nicht", () =>
        {
            if (!DenyRulesApply()) return;
            string root = NewDir("rockstarpartial");
            string bad = Path.Combine(root, "A", "Rockstar Games");
            string good = Path.Combine(root, "B", "Rockstar Games");
            string locked = Path.Combine(bad, "Gesperrt");
            Directory.CreateDirectory(locked);
            Directory.CreateDirectory(good);
            string sid = System.Security.Principal.WindowsIdentity.GetCurrent().User.Value;
            string icacls = Path.Combine(Environment.SystemDirectory, "icacls.exe");
            Equal(0, SystemCommands.RunAll(new[] { new KeyValuePair<string, string>(icacls, "\"" + locked + "\" /deny *" + sid + ":(RD)") }), "Sperre gesetzt");

            // Ohne Zurücksetzen bleibt die Sperre: icacls meldet trotzdem Code 0, die Probe muss es merken.
            var log = new List<string>();
            var targets = new List<RepairTarget>
            {
                new RepairTarget { Path = bad, OwnerSid = "*" + sid, Grants = new[] { "*" + sid + ":(OI)(CI)R" } },
                new RepairTarget { Path = Path.Combine(root, "Fehlt"), OwnerSid = "*" + sid, Grants = new[] { "*" + sid + ":(OI)(CI)R" } },
                new RepairTarget { Path = good, OwnerSid = "*" + sid, ResetFirst = true, Grants = new[] { "*" + sid + ":(OI)(CI)F" } },
            };
            True(!RockstarLauncher.RepairAll(targets, Path.Combine(root, "Sicherung"), log.Add), "Gesamtergebnis ist kein Erfolg");
            Equal(2, log.Count, "beide vorhandenen Ordner bearbeitet");
            True(log[1].StartsWith(good) && log[1].EndsWith("repariert"), "zweiter Ordner trotzdem repariert");
            Equal(0, SystemCommands.RunAll(new[] { new KeyValuePair<string, string>(icacls, "\"" + locked + "\" /remove:d *" + sid) }), "Sperre entfernt");
        });

        Run("Rockstar: Protokoll wird nur bei Problemen im Fertig-Text genannt", () =>
        {
            True(!RockstarLauncher.DoneText(0, 0, true, @"C:\p.txt").Contains(@"C:\p.txt"), "alles gut");
            True(RockstarLauncher.DoneText(RockstarLauncher.PermissionsFailed, 0, true, @"C:\p.txt").Contains(@"C:\p.txt"), "bei Fehler");
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

        Run("TS-Sounds: frische Installation ohne Benachrichtigungs-Tabelle wird abgeschaltet", () =>
        {
            // So sieht settings.db aus, solange in TeamSpeak nie die Benachrichtigungen geöffnet wurden.
            string dir = NewDir("ts3fresh");
            string file = Path.Combine(dir, "settings.db");
            using (SqliteDb db = SqliteDb.Create(file))
            {
                db.Execute("CREATE TABLE General (timestamp INTEGER UNSIGNED NOT NULL, key VARCHAR NOT NULL UNIQUE, value VARCHAR)");
                db.Execute("CREATE INDEX index_General_key ON General (key)");
                db.Execute("INSERT INTO General VALUES (1, 'Identity', 'geheim')");
            }
            var settings = new AppSettings(Path.Combine(dir, "s.ini"));
            True(Ts3Sounds.IsAvailable(file), "TeamSpeak lief schon, also verfügbar");
            True(!Ts3Sounds.AreDisabled(file), "Töne an");
            Equal<string>(null, Ts3Sounds.ReadPack(file), "noch kein Paket");

            Ts3Sounds.Disable(file, settings);
            True(Ts3Sounds.AreDisabled(file), "abgeschaltet");
            using (SqliteDb db = SqliteDb.OpenRead(file))
            {
                Equal("CREATE TABLE Notifications (timestamp INTEGER UNSIGNED NOT NULL, key VARCHAR NOT NULL UNIQUE, value VARCHAR)",
                      db.Scalar("SELECT sql FROM sqlite_master WHERE name = 'Notifications'"), "Tabelle wie bei TeamSpeak");
                Equal("CREATE INDEX index_Notifications_key ON Notifications (key)",
                      db.Scalar("SELECT sql FROM sqlite_master WHERE name = 'index_Notifications_key'"), "Index wie bei TeamSpeak");
                Equal("geheim", db.Scalar("SELECT value FROM General WHERE key = 'Identity'"), "Rest bleibt");
                Equal("ok", db.Scalar("PRAGMA integrity_check"), "Datenbank intakt");
            }
            Ts3Sounds.Enable(file, settings);
            Equal("default", Ts3Sounds.ReadPack(file), "wieder an mit Standardpaket");
        });

        Run("TS-Sounds: Einstellungen im Installationsordner werden gefunden", () =>
        {
            string appData = @"C:\Users\x\AppData\Roaming", install = @"C:\Spiele\TeamSpeak 3 Client";
            string inAppData = @"C:\Users\x\AppData\Roaming\TS3Client\settings.db";
            string inInstall = @"C:\Spiele\TeamSpeak 3 Client\config\settings.db";
            List<string> own = Ts3Sounds.Candidates(appData, "1", install + "\\");
            Equal(inInstall, own[0], "Speicherort Installationsordner zuerst");
            Equal(inAppData, own[1], "danach AppData");
            List<string> normal = Ts3Sounds.Candidates(appData, "0", install);
            Equal(inAppData, normal[0], "normal zuerst AppData");
            Equal(inInstall, normal[1], "danach Installationsordner");
            Equal(1, Ts3Sounds.Candidates(appData, null, null).Count, "ohne Installation nur AppData");
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

    const string ExeDigest = "41a3b449da4065d9fd922070b5a8e382c6a33351d1d3df55cae27163fc4e4290";

    static string ReleaseJson(string tag, string assetName, string url, string state)
    {
        return ReleaseJson(tag, assetName, url, state, UpdateBase + tag + "/RetroRoleplayTool.exe", "sha256:" + ExeDigest);
    }

    static string ReleaseJson(string tag, string assetName, string url, string state, string exeUrl, string exeDigest)
    {
        return "{\"tag_name\":\"" + tag + "\",\"assets\":[" +
               "{\"name\":\"RetroRoleplayTool.exe\",\"state\":\"uploaded\",\"browser_download_url\":\"" + exeUrl + "\"" +
               (exeDigest == null ? "" : ",\"digest\":\"" + exeDigest + "\"") + "}," +
               "{\"name\":\"" + assetName + "\",\"state\":\"" + state + "\",\"browser_download_url\":\"" + url + "\"}]}";
    }

    // Kopiert ein Programm als „app.exe“ in einen Testordner.
    static string FakeApp(string dir, string content)
    {
        string exe = Path.Combine(dir, "app.exe");
        File.WriteAllText(exe, content);
        return exe;
    }

    static void SelfUpdateTests()
    {
        Run("Selbst-Update: neue exe und ihre Prüfsumme werden gelesen", () =>
        {
            UpdateInfo u = UpdateCheck.Evaluate("1.4.0", ReleaseJson("v1.5.0", "RetroRoleplayTool.zip", UpdateZip, "uploaded"));
            Equal(UpdateBase + "v1.5.0/RetroRoleplayTool.exe", u.ExeUrl, "exe-Link");
            Equal(ExeDigest, u.ExeSha256, "Prüfsumme");
            True(u.CanSelfUpdate, "Selbst-Update möglich");
        });

        Run("Selbst-Update: ohne Prüfsumme oder mit fremdem Link nur der ZIP-Hinweis", () =>
        {
            string zip = "RetroRoleplayTool.zip", exe = UpdateBase + "v1.5.0/RetroRoleplayTool.exe";
            foreach (string[] c in new[]
            {
                new[] { exe, null },
                new[] { exe, "md5:abc" },
                new[] { exe, "sha256:zu-kurz" },
                new[] { "https://example.com/RetroRoleplayTool.exe", "sha256:" + ExeDigest },
                new[] { UpdateBase + "v1.4.0/RetroRoleplayTool.exe", "sha256:" + ExeDigest },
            })
            {
                UpdateInfo u = UpdateCheck.Evaluate("1.4.0", ReleaseJson("v1.5.0", zip, UpdateZip, "uploaded", c[0], c[1]));
                True(u != null && !u.CanSelfUpdate, "kein Selbst-Update: " + c[0] + " " + c[1]);
                Equal(UpdateZip, u.ZipUrl, "ZIP-Hinweis bleibt");
            }
        });

        Run("Selbst-Update: nur das echte Tool in der erwarteten Version wird akzeptiert", () =>
        {
            string self = System.Reflection.Assembly.GetExecutingAssembly().Location;
            var name = System.Reflection.AssemblyName.GetAssemblyName(self);
            string sha = Downloader.Sha256OfFile(self);
            string version = name.Version.Major + "." + name.Version.Minor + "." + name.Version.Build;
            SelfUpdate.Verify(self, sha, name.Name, version);
            Throws<InvalidDataException>(() => SelfUpdate.Verify(self, ExeDigest, name.Name, version));
            Throws<InvalidDataException>(() => SelfUpdate.Verify(self, sha, "AnderesProgramm", version));
            Throws<InvalidDataException>(() => SelfUpdate.Verify(self, sha, name.Name, "9.9.9"));
            Throws<InvalidDataException>(() => SelfUpdate.Verify(self, null, name.Name, version));
            string text = Path.Combine(NewDir("upd0"), "kaputt.exe");
            File.WriteAllText(text, "kein Programm");
            Throws<InvalidDataException>(() => SelfUpdate.Verify(text, Downloader.Sha256OfFile(text), name.Name, version));
        });

        Run("Selbst-Update: Tausch und Zurücklegen", () =>
        {
            string dir = NewDir("upd1");
            string exe = FakeApp(dir, "alt");
            File.WriteAllText(SelfUpdate.NewPath(exe), "neu");
            SelfUpdate.Swap(exe, SelfUpdate.NewPath(exe));
            Equal("neu", File.ReadAllText(exe), "neue Version an Ort");
            Equal("alt", File.ReadAllText(SelfUpdate.OldPath(exe)), "alte Version daneben");
            True(!File.Exists(SelfUpdate.NewPath(exe)), "keine .new mehr");
            SelfUpdate.RollBack(exe);
            Equal("alt", File.ReadAllText(exe), "zurückgelegt");
            True(!File.Exists(SelfUpdate.OldPath(exe)), "keine .old mehr");
        });

        Run("Selbst-Update: laufendes Programm lässt sich tauschen, Reste werden aufgeräumt", () =>
        {
            string dir = NewDir("upd2");
            string exe = Path.Combine(dir, "app.exe");
            File.Copy(Path.Combine(Environment.SystemDirectory, "PING.EXE"), exe);
            File.WriteAllText(SelfUpdate.NewPath(exe), "neu");
            File.WriteAllText(SelfUpdate.OldPath(exe), "Rest vom letzten Mal");
            var psi = new ProcessStartInfo(exe, "-n 30 127.0.0.1") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
            using (Process running = Process.Start(psi))
            {
                try
                {
                    SelfUpdate.Swap(exe, SelfUpdate.NewPath(exe));
                    Equal("neu", File.ReadAllText(exe), "getauscht, obwohl die alte läuft");
                    True(!SelfUpdate.CleanUp(exe, 1), "alte Datei ist noch in Benutzung");
                }
                finally { running.Kill(); running.WaitForExit(5000); }
            }
            True(SelfUpdate.CleanUp(exe, 20), "nach dem Ende aufgeräumt");
            True(!File.Exists(SelfUpdate.OldPath(exe)) && !File.Exists(SelfUpdate.NewPath(exe)), "keine Reste");
            Equal("neu", File.ReadAllText(exe), "neue Version bleibt");
        });

        Run("Selbst-Update: neue Version erkennt, dass sie gerade aktualisiert wurde", () =>
        {
            Equal("1.15.0", SelfUpdate.UpdatedFrom(new[] { "app.exe", "--updated", "1.15.0" }), "mit Version");
            Equal<string>(null, SelfUpdate.UpdatedFrom(new[] { "app.exe" }), "normaler Start");
            Equal<string>(null, SelfUpdate.UpdatedFrom(new[] { "app.exe", "--updated" }), "ohne Version");
            Equal<string>(null, SelfUpdate.UpdatedFrom(new[] { "app.exe", "--updated", "<script>" }), "Unsinn");
        });

        Run("Selbst-Update: Ordner ohne Schreibrecht wird erkannt", () =>
        {
            True(SelfUpdate.CanWriteNextTo(FakeApp(NewDir("upd3"), "x")), "eigener Ordner");
            bool admin = new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent())
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            True(admin || !SelfUpdate.CanWriteNextTo(Path.Combine(Environment.SystemDirectory, "notepad.exe")), "System32 nur mit Adminrechten");
            True(!SelfUpdate.CanWriteNextTo(@"Q:\gibt\es\nicht\app.exe"), "fehlender Ordner");
        });
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

    // ---------------------------------------------------------------- DS4Windows

    static string Ds4Zip(string dir, params string[] names)
    {
        string zip = Path.Combine(dir, "ds4.zip");
        using (var archive = System.IO.Compression.ZipFile.Open(zip, System.IO.Compression.ZipArchiveMode.Create))
            foreach (string n in names)
            {
                var entry = archive.CreateEntry(n);
                if (n.EndsWith("/")) continue;
                using (var w = new StreamWriter(entry.Open())) w.Write("neu:" + n);
            }
        return zip;
    }

    static void Ds4Tests()
    {
        Run("DS4Windows: Downloads sind fest hinterlegt und geprüft", () =>
        {
            foreach (string url in new[] { Ds4Windows.ZipUrl, Ds4Windows.ViGEmUrl, Ds4Windows.DotNetUrl })
                True(url.StartsWith("https://github.com/schmaldeo/DS4Windows/releases/download/v3.9.9/") ||
                     url.StartsWith("https://github.com/nefarius/ViGEmBus/releases/download/v1.22.0/") ||
                     url.StartsWith("https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/8.0."), "Quelle: " + url);
            foreach (string hash in new[] { Ds4Windows.ZipSha256, Ds4Windows.ViGEmSha256, Ds4Windows.DotNetSha256 })
                True(System.Text.RegularExpressions.Regex.IsMatch(hash, "^[0-9a-f]{64}$"), "Prüfsumme: " + hash);
            True(Ds4Windows.InstallDir.EndsWith(@"\AppData\Local\DS4Windows", StringComparison.OrdinalIgnoreCase), "Ordner wie beim Original");
        });

        Run("DS4Windows: Dateien landen nur im Zielordner", () =>
        {
            string dir = @"C:\Users\x\AppData\Local\DS4Windows";
            Equal(dir + @"\DS4Windows.exe", Ds4Windows.TargetPath("DS4Windows/DS4Windows.exe", dir), "Programm");
            Equal(dir + @"\Lang\de\DS4Windows.resources.dll", Ds4Windows.TargetPath(@"DS4Windows\Lang\de\DS4Windows.resources.dll", dir), "Unterordner");
            Equal(dir + @"\NLog.config", Ds4Windows.TargetPath("NLog.config", dir), "ohne Oberordner");
            Equal<string>(null, Ds4Windows.TargetPath("DS4Windows/Lang/", dir), "Ordner-Eintrag");
            Equal<string>(null, Ds4Windows.TargetPath("DS4Windows/../../evil.exe", dir), "nach oben");
            Equal<string>(null, Ds4Windows.TargetPath(@"..\DS4Windows2\x.dll", dir), "Nachbarordner mit gleichem Anfang");
            Equal<string>(null, Ds4Windows.TargetPath(@"C:\Windows\evil.dll", dir), "absoluter Pfad");
            Equal<string>(null, Ds4Windows.TargetPath("", dir), "leer");
        });

        Run("DS4Windows: Entpacken ersetzt Programmdateien, eigene Profile bleiben", () =>
        {
            string dir = NewDir("ds4");
            string target = Path.Combine(dir, "DS4Windows");
            Directory.CreateDirectory(Path.Combine(target, "Profiles"));
            File.WriteAllText(Path.Combine(target, "Profiles.xml"), "meins");
            File.WriteAllText(Path.Combine(target, "Profiles", "FiveM.xml"), "mein Profil");
            File.WriteAllText(Path.Combine(target, "DS4Windows.exe"), "alt");
            File.SetAttributes(Path.Combine(target, "DS4Windows.exe"), FileAttributes.ReadOnly);
            string zip = Ds4Zip(dir, "DS4Windows/", "DS4Windows/DS4Windows.exe", "DS4Windows/Lang/de/a.dll", "DS4Windows/../boese.txt");

            Equal(2, Ds4Windows.Extract(zip, target), "Dateien");
            Equal("neu:DS4Windows/DS4Windows.exe", File.ReadAllText(Path.Combine(target, "DS4Windows.exe")), "Programm ersetzt");
            True(File.Exists(Path.Combine(target, "Lang", "de", "a.dll")), "Unterordner angelegt");
            Equal("meins", File.ReadAllText(Path.Combine(target, "Profiles.xml")), "Einstellungen bleiben");
            Equal("mein Profil", File.ReadAllText(Path.Combine(target, "Profiles", "FiveM.xml")), "Profil bleibt");
            True(!File.Exists(Path.Combine(dir, "boese.txt")), "nichts außerhalb");
        });

        Run("DS4Windows: Paket ohne Programm wird abgelehnt, nichts entpackt", () =>
        {
            string dir = NewDir("ds4bad");
            string target = Path.Combine(dir, "DS4Windows");
            string zip = Ds4Zip(dir, "DS4Windows/NLog.config", "DS4Windows/Lang/DS4Windows.exe");
            Throws<InvalidDataException>(() => Ds4Windows.Extract(zip, target));
            True(!Directory.Exists(target), "nichts angelegt");
        });

        Run("DS4Windows: .NET 8 wird erkannt, 9 oder 10 reicht nicht", () =>
        {
            string pf = NewDir("ds4net");
            string shared = Path.Combine(pf, "dotnet", "shared");
            True(!Ds4Windows.IsDotNet8Installed(pf), "nichts da");
            Directory.CreateDirectory(Path.Combine(shared, "Microsoft.WindowsDesktop.App", "10.0.1"));
            Directory.CreateDirectory(Path.Combine(shared, "Microsoft.NETCore.App", "10.0.1"));
            True(!Ds4Windows.IsDotNet8Installed(pf), "nur .NET 10");
            Directory.CreateDirectory(Path.Combine(shared, "Microsoft.WindowsDesktop.App", "8.0.23"));
            True(!Ds4Windows.IsDotNet8Installed(pf), "Basis fehlt");
            Directory.CreateDirectory(Path.Combine(shared, "Microsoft.NETCore.App", "8.0.23"));
            True(Ds4Windows.IsDotNet8Installed(pf), "8.0.23 reicht");
        });

        Run("DS4Windows: Verknüpfung zeigt auf das Programm", () =>
        {
            string dir = NewDir("ds4lnk");
            string exe = Path.Combine(dir, "DS4Windows", "DS4Windows.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(exe));
            File.WriteAllText(exe, "x");
            string link = Path.Combine(dir, "Startmenü", "DS4Windows.lnk");
            Ds4Windows.CreateShortcut(link, exe, "DS4Windows");
            True(File.Exists(link), "angelegt");
            Equal(exe, Ds4Windows.ShortcutTarget(link), "Ziel");
        });

        Run("DS4Windows: Admin-Teil prüft alle Dateien, bevor etwas startet", () =>
        {
            Equal(Ds4Windows.InvalidInput, Ds4Windows.RunElevated(new string[0]), "ohne Angaben");
            Equal(Ds4Windows.InvalidInput, Ds4Windows.RunElevated(new[] { "-", "-" }), "zu wenige");
            Equal(Ds4Windows.InvalidInput, Ds4Windows.RunElevated(new[] { "-", "-", @"C:\gibt\es\nicht.exe" }), "fehlende Datei");
            string cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            Equal(Ds4Windows.InvalidInput, Ds4Windows.RunElevated(new[] { cmd, "-", "-" }), "Windows-Programm statt Setup");
            Equal(0, Ds4Windows.RunElevated(new[] { "-", "-", "-" }), "nichts zu tun");
            True(Ds4Windows.IsSubjectOf("CN=Nefarius Software Solutions e.U., O=Nefarius Software Solutions e.U., L=Wels, C=AT", "Nefarius Software Solutions e.U."), "ViGEm");
            True(Ds4Windows.IsSubjectOf("CN=.NET, O=Microsoft Corporation, L=Redmond, S=Washington, C=US", "Microsoft Corporation"), ".NET");
            True(!Ds4Windows.IsSubjectOf("CN=x, O=Microsoft Corporation Fake, C=US", "Microsoft Corporation"), "ähnlicher Name");
            True(!Ds4Windows.IsSubjectOf("CN=x, O=Nefarius Software SolutionsXe.U., C=AT", "Nefarius Software Solutions e.U."), "Punkt ist kein Platzhalter");
            Equal(Ds4Windows.ElevatedArgument + " \"-\" \"C:\\a b\\net.exe\" \"-\"",
                  Ds4Windows.BuildElevatedArguments(null, @"C:\a b\net.exe", null), "Argumente");
        });

        Run("DS4Windows: Fehlercodes und Fertig-Text", () =>
        {
            foreach (int ok in new[] { 0, 3010, 1641, 1638 }) True(Ds4Windows.IsSuccess(ok), "ok: " + ok);
            foreach (int bad in new[] { 1, 1602, 1603, -1 }) True(!Ds4Windows.IsSuccess(bad), "Fehler: " + bad);
            True(Ds4Windows.NeedsRestart(3010) && !Ds4Windows.NeedsRestart(0), "Neustart");
            string fine = Ds4Windows.DoneText(0);
            True(fine.StartsWith("DS4Windows 3.9.9 ist installiert"), "Erfolg vorn");
            True(!fine.Contains("neu starten"), "kein Neustart ohne Grund");
            True(Ds4Windows.DoneText(Ds4Windows.RestartNeeded).Contains("neu starten"), "Neustart genannt");
            string bad2 = Ds4Windows.DoneText(Ds4Windows.ViGEmFailed | Ds4Windows.DotNetFailed);
            True(bad2.Contains("Treiber ViGEmBus") && bad2.Contains(".NET 8") && !bad2.Contains("Visual C++"), "genau die Fehler: " + bad2);
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
