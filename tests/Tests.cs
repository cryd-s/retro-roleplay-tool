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

    static void CleanerTests()
    {
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

        Run("Texture Budget: Empfehlung nach Grafikspeicher", () =>
        {
            long gib = 1024L * 1024 * 1024;
            Equal(-1, TextureBudget.Recommended(0), "unbekannt");
            Equal(0, TextureBudget.Recommended(4 * gib), "4 GB");
            Equal(4, TextureBudget.Recommended(8 * gib), "8 GB");
            Equal(12, TextureBudget.Recommended(12 * gib), "12 GB");
            Equal(20, TextureBudget.Recommended(17171480576L), "RTX 4080 SUPER");
            Equal(TextureBudget.Max, TextureBudget.Recommended(48 * gib), "Obergrenze");
        });

        Run("Texture Budget: Speicher wie in FiveM berechnet", () =>
        {
            Equal(3145728000L, TextureBudget.BudgetBytes(0), "Stufe 0");
            Equal(7864320000L, TextureBudget.BudgetBytes(18), "Stufe 18");
            Equal("7,3 GB", TextureBudget.FormatGb(TextureBudget.BudgetBytes(18)), "Anzeige");
        });

        Run("Sicherung: einmalig anlegen und zurückspielen", () =>
        {
            string dir = NewDir("gtacfg");
            string cfg = Path.Combine(dir, "fivem.cfg"), xml = Path.Combine(dir, "gta5_settings.xml");
            string backup = Path.Combine(dir, "sicherung");
            File.WriteAllText(cfg, "original-cfg");
            File.WriteAllText(xml, "original-xml");

            GameConfigFiles.BackupOnce(backup, cfg, xml, Path.Combine(dir, "fehlt.txt"), null);
            True(GameConfigFiles.HasBackup(backup), "Sicherung vorhanden");
            File.WriteAllText(cfg, "geändert-1");
            GameConfigFiles.BackupOnce(backup, cfg, xml);      // darf die erste Sicherung nicht überschreiben
            File.WriteAllText(cfg, "geändert-2");
            File.WriteAllText(xml, "geändert-2");
            File.SetAttributes(xml, FileAttributes.ReadOnly);

            Equal(2, GameConfigFiles.Restore(backup), "Dateien zurückgespielt");
            Equal("original-cfg", File.ReadAllText(cfg), "cfg");
            Equal("original-xml", File.ReadAllText(xml), "xml");
            True(!GameConfigFiles.HasBackup(backup), "Sicherung danach gelöscht");
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
