using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

// Zeile mit Titel, Beschreibung, Status und Aktions-Button rechts.
public class FeatureRow : NeonControl
{
    public readonly NeonButton Action;
    string title = "", detail = "", value = "";
    Color valueColor = Theme.Muted;
    readonly Font titleFont = Theme.MakeFont("Segoe UI Semibold", 12f);
    readonly Font detailFont = Theme.MakeFont("Segoe UI", 8.5f);
    readonly Font valueFont = Theme.MakeFont("Segoe UI Semibold", 9.5f);

    public FeatureRow(int buttonWidth)
    {
        Action = new NeonButton { Primary = true, Text = "…" };
        Controls.Add(Action);
        this.buttonWidth = buttonWidth;
    }

    readonly int buttonWidth;

    public string Title { get { return title; } set { title = value; Invalidate(); } }
    public string Detail { get { return detail; } set { detail = value; Invalidate(); } }
    public string Value { get { return value; } set { this.value = value; Invalidate(); } }
    public Color ValueColor { get { return valueColor; } set { valueColor = value; Invalidate(); } }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        int h = Theme.S(54);
        Action.Bounds = new Rectangle(Width - buttonWidth + Theme.S(6), (Height - h) / 2, buttonWidth, h);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        Theme.Prepare(g);
        int textWidth = Width - buttonWidth - Theme.S(12);
        int y = (Height - Theme.S(65)) / 2;    // Textblock senkrecht mittig, auch bei niedrigeren Zeilen
        TextRenderer.DrawText(g, title, titleFont, new Rectangle(0, y, textWidth, Theme.S(24)), Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        y += Theme.S(25);
        TextRenderer.DrawText(g, detail, detailFont, new Rectangle(0, y, textWidth, Theme.S(18)), Theme.Muted,
            TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        y += Theme.S(20);
        TextRenderer.DrawText(g, value, valueFont, new Rectangle(0, y, textWidth, Theme.S(20)), valueColor,
            TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        using (var pen = new Pen(Theme.Line)) g.DrawLine(pen, 0, Height - 1, Width, Height - 1);
    }
}

// Unterseite „Sonderfunktionen“ mit zwei Reitern:
// TeamSpeak (TeamSpeak 3, SaltyChat, TS-Sounds, TS-Moved-nicht-Bugfix) und
// Fixes & Tools (Rockstar Launcher Fix, Netzwerk Fix, FiveM in Steam, Grafik-Mods).
public class ExtrasPage : Panel
{
    readonly MainForm host;
    readonly FeatureRow ts3Row, saltyRow, soundRow, hostsRow, rockstarRow, networkRow, steamRow, modRow;
    readonly NeonButton tabTeamSpeak, tabTools;
    readonly AppSettings settings = new AppSettings(AppSettings.DefaultPath);
    ModReport modReport;                 // null = FiveM nicht gefunden oder nicht lesbar
    bool soundsAvailable, soundsDisabled;

    Ts3Release ts3Latest;
    string saltyLatest;
    bool hostsActive;
    bool refreshing;
    string steamDir, steamConfigDir;     // null = Steam oder Konto nicht gefunden
    bool steamReady;                     // FiveM ist eingetragen und der Controller aktiv

    // top = wo der Inhalt beginnt (darüber liegt der Seitenkopf des Hauptfensters)
    public ExtrasPage(MainForm host, int margin, int inner, int top)
    {
        this.host = host;
        BackColor = Theme.Bg;
        int y = top;

        int rowHeight = Theme.S(84), buttonWidth = Theme.S(230);

        int tabH = Theme.S(48), tabW = Theme.S(210), glow = Theme.S(6);
        tabTeamSpeak = new NeonButton { Text = "TEAMSPEAK", Bounds = new Rectangle(margin - glow, y, tabW, tabH) };
        tabTools = new NeonButton { Text = "FIXES & TOOLS", Bounds = new Rectangle(margin - glow + tabW + Theme.S(4), y, tabW, tabH) };
        tabTeamSpeak.Click += delegate { ShowGroup(true); };
        tabTools.Click += delegate { ShowGroup(false); };
        Controls.Add(tabTeamSpeak);
        Controls.Add(tabTools);
        y += tabH + Theme.S(6);
        int rowsTop = y;

        ts3Row = AddRow(margin, inner, ref y, rowHeight, buttonWidth,
            "TeamSpeak 3", "Lädt die aktuelle Version von teamspeak.com, prüft sie und installiert still.");
        saltyRow = AddRow(margin, inner, ref y, rowHeight, buttonWidth,
            "SaltyChat", "Installiert das aktuelle Plugin von saltyhub.net in TeamSpeak 3.");
        soundRow = AddRow(margin, inner, ref y, rowHeight, buttonWidth,
            "TeamSpeak-Sounds", "Schaltet alle TeamSpeak-Töne dauerhaft ab. Sprache bleibt.");
        hostsRow = AddRow(margin, inner, ref y, rowHeight, buttonWidth,
            "TS-Moved-nicht-Bugfix", "Leitet " + HostsFix.Host + " in der hosts-Datei auf 127.0.0.1 um.");
        y = rowsTop;   // zweiter Reiter: dieselbe Fläche
        rockstarRow = AddRow(margin, inner, ref y, rowHeight, buttonWidth,
            "Rockstar Launcher Fix", "Setzt DNS-Cache und Winsock zurück und leert den Launcher-Cache.");
        rockstarRow.Action.Text = "FIX STARTEN";
        networkRow = AddRow(margin, inner, ref y, rowHeight, buttonWidth,
            "Netzwerk Fix", "Setzt Winsock und TCP/IP zurück und leert den DNS-Cache.");
        networkRow.Action.Text = "FIX STARTEN";
        steamRow = AddRow(margin, inner, ref y, rowHeight, buttonWidth,
            "FiveM in Steam + Controller", "Aktiviert den Controller über Steam. FiveM muss danach immer über Steam starten.");
        modRow = AddRow(margin, inner, ref y, rowHeight, buttonWidth,
            "Grafik-Mods prüfen", "Zeigt ReShade, ASI-Plugins, fremde DLLs und Mods. Ändert nichts.");
        modRow.Action.Text = "DETAILS";
        modRow.Action.Primary = false;

        ts3Row.Action.Click += async delegate { await Guard(InstallTs3Async); };
        saltyRow.Action.Click += async delegate { await Guard(InstallSaltyAsync); };
        soundRow.Action.Click += async delegate { await Guard(ToggleSoundsAsync); };
        hostsRow.Action.Click += async delegate { await Guard(ToggleHostsAsync); };
        rockstarRow.Action.Click += async delegate { await Guard(RunRockstarFixAsync); };
        networkRow.Action.Click += async delegate { await Guard(RunNetworkFixAsync); };
        steamRow.Action.Click += async delegate { await Guard(ToggleSteamAsync); };
        modRow.Action.Click += delegate { ShowModDetails(); };
        ContentHeight = y + Theme.S(12);
        ShowGroup(true);
    }

    void ShowGroup(bool teamSpeak)
    {
        foreach (FeatureRow r in new[] { ts3Row, saltyRow, soundRow, hostsRow }) r.Visible = teamSpeak;
        foreach (FeatureRow r in new[] { rockstarRow, networkRow, steamRow, modRow }) r.Visible = !teamSpeak;
        tabTeamSpeak.Primary = teamSpeak;
        tabTools.Primary = !teamSpeak;
        tabTeamSpeak.Invalidate();
        tabTools.Invalidate();
    }

    // Benötigte Höhe der Seite (das Hauptfenster richtet sich danach)
    public int ContentHeight { get; private set; }

    FeatureRow AddRow(int margin, int inner, ref int y, int height, int buttonWidth, string title, string detail)
    {
        var row = new FeatureRow(buttonWidth) { Title = title, Detail = detail, Value = "wird geprüft …", Bounds = new Rectangle(margin, y, inner, height) };
        Controls.Add(row);
        y += height;
        return row;
    }

    public void SetButtonsEnabled(bool enabled)
    {
        tabTeamSpeak.Enabled = enabled;
        tabTools.Enabled = enabled;
        networkRow.Action.Enabled = enabled && !refreshing;
        ts3Row.Action.Enabled = enabled && !refreshing;
        saltyRow.Action.Enabled = enabled && !refreshing;
        soundRow.Action.Enabled = enabled && !refreshing && soundsAvailable;
        hostsRow.Action.Enabled = enabled && !refreshing;
        rockstarRow.Action.Enabled = enabled && !refreshing;
        steamRow.Action.Enabled = enabled && !refreshing && steamConfigDir != null && (steamReady || host.FiveMAppDir != null);
        modRow.Action.Enabled = enabled && !refreshing && modReport != null;
    }

    // ------------------------------------------------------------ Status

    public async Task RefreshAsync()
    {
        if (refreshing) return;
        refreshing = true;
        SetButtonsEnabled(!host.Busy);
        foreach (FeatureRow r in new[] { ts3Row, saltyRow, soundRow, hostsRow, rockstarRow, networkRow, steamRow, modRow })
        {
            r.Value = "wird geprüft …";
            r.ValueColor = Theme.Muted;
        }

        // hosts (lokal, schnell)
        try { hostsActive = HostsFix.IsActive(File.ReadAllText(HostsFix.HostsPath)); }
        catch (Exception) { hostsActive = false; }
        hostsRow.Value = hostsActive ? "aktiv  ✔" : "nicht aktiv";
        hostsRow.ValueColor = hostsActive ? Theme.Good : Theme.Pink;
        hostsRow.Action.Text = hostsActive ? "ENTFERNEN" : "AKTIVIEREN";
        hostsRow.Action.Primary = !hostsActive;

        // Steam (lokal, schnell)
        ShowSteamState();

        // Grafik-Mods (lokal, schnell)
        ShowModState();

        // TeamSpeak-Sounds (lokal, schnell)
        ShowSoundState();

        // Rockstar Launcher (lokal, schnell)
        ShowRockstarState();
        networkRow.Value = "danach PC neu starten";

        // TeamSpeak 3
        string ts3Installed = Ts3.InstalledVersion();
        try { ts3Latest = await Task.Run(() => Ts3.FetchLatest()); }
        catch (Exception) { ts3Latest = null; }
        ShowVersionState(ts3Row, ts3Installed, ts3Latest == null ? null : ts3Latest.Version, false);

        // SaltyChat
        string saltyInstalled = SaltyChat.InstalledVersion(SaltyChat.PluginsDir);
        bool fallback = false;
        saltyLatest = await Task.Run(() => SaltyChat.FetchLatestVersion(out fallback));
        ShowVersionState(saltyRow, saltyInstalled, saltyLatest, fallback);

        refreshing = false;
        SetButtonsEnabled(!host.Busy);
    }

    void ShowSteamState()
    {
        steamReady = false;
        steamRow.Action.Text = "EINRICHTEN";
        steamRow.Action.Primary = true;
        steamRow.ValueColor = Theme.Pink;
        try
        {
            steamDir = SteamLibrary.SteamDir;
            steamConfigDir = steamDir == null ? null : SteamLibrary.UserConfigDir(steamDir);
            if (steamDir == null)
            {
                steamRow.Value = "Steam ist nicht installiert";
                return;
            }
            if (steamConfigDir == null)
            {
                steamRow.Value = "kein Steam-Konto gefunden – bitte einmal in Steam anmelden";
                return;
            }
            SteamStatus s = SteamLibrary.ReadStatus(Path.Combine(steamConfigDir, "shortcuts.vdf"),
                                                    Path.Combine(steamConfigDir, "localconfig.vdf"));
            steamReady = s.InSteam && s.ControllerOn;
            if (steamReady)
            {
                steamRow.Value = "Controller aktiv  ✔  FiveM immer über Steam starten!";
                steamRow.ValueColor = Theme.Orange;
                steamRow.Action.Text = "ENTFERNEN";
                steamRow.Action.Primary = false;
            }
            else if (host.FiveMAppDir == null)
            {
                steamRow.Value = "FiveM nicht gefunden – im Cache Cleaner auswählen";
            }
            else
            {
                steamRow.Value = s.InSteam ? "in Steam, Controller nicht aktiv" : "nicht in Steam";
            }
        }
        catch (Exception ex)
        {
            steamConfigDir = null;
            steamRow.Value = "Steam-Dateien nicht lesbar: " + ex.Message;
        }
    }

    static string RockstarCacheDir
    {
        get { return RockstarLauncher.CacheDir(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)); }
    }

    void ShowRockstarState()
    {
        string dir = RockstarCacheDir;
        bool found = Directory.Exists(dir);
        rockstarRow.Value = found ? "Launcher-Cache: " + SizeFormat.Format(FolderCleaner.MeasureSize(dir)) : "kein Launcher-Cache gefunden";
        rockstarRow.ValueColor = found ? Theme.Orange : Theme.Muted;
    }

    async Task RunRockstarFixAsync()
    {
        if (RockstarLauncher.IsGameRunning())
        {
            NeonDialog.Show(FindForm(), RockstarLauncher.GameRunningText, "Rockstar Launcher Fix", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (NeonDialog.Show(FindForm(), RockstarLauncher.ConfirmText, "Rockstar Launcher Fix", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        host.SetBusy(true);
        // Zuerst die Admin-Abfrage: Wird sie abgelehnt, ist noch nichts verändert.
        host.SetStatus("Bitte die Windows-Admin-Abfrage bestätigen …", Theme.Text);
        int code = await RunElevatedAsync(RockstarLauncher.ElevatedArgument);
        if (code != 0) throw new Exception("DNS-Cache oder Winsock konnten nicht zurückgesetzt werden (Code " + code + "). Der Launcher-Cache wurde nicht angefasst.");

        host.SetStatus("Rockstar Launcher wird geschlossen …", Theme.Text);
        await Task.Run(() => RockstarLauncher.CloseLauncher());

        host.SetStatus("Launcher-Cache wird gelöscht …", Theme.Text);
        string dir = RockstarCacheDir;
        int skipped = 0;
        if (Directory.Exists(dir))
        {
            CleanResult result = await Task.Run(() => FolderCleaner.CleanContents(dir, null));
            skipped = result.FilesSkipped;
        }
        host.SetStatus("Fertig!  Bitte jetzt den PC neu starten.", Theme.Orange);
        NeonDialog.Show(FindForm(), RockstarLauncher.DoneText(skipped), "Rockstar Launcher Fix", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    async Task RunNetworkFixAsync()
    {
        if (NeonDialog.Show(FindForm(), NetworkFix.ConfirmText, "Netzwerk Fix", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        host.SetBusy(true);
        host.SetStatus("Netzwerkdaten werden gespeichert …", Theme.Text);
        string backup = await Task.Run(() =>
        {
            try { return NetworkFix.SaveSnapshot(NetworkFix.BackupDir); }
            catch (Exception) { return null; }
        });
        if (backup == null &&
            NeonDialog.Show(FindForm(), "Die Netzwerkdaten konnten nicht gespeichert werden.\n\nTrotzdem fortfahren?", "Netzwerk Fix",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        host.SetStatus("Bitte die Windows-Admin-Abfrage bestätigen …", Theme.Text);
        int code = await RunElevatedAsync(NetworkFix.ElevatedArgument);

        host.SetStatus(code == 0 ? "Fertig!  Bitte jetzt den PC neu starten." : "Fehler gemeldet. Bitte den PC trotzdem neu starten.", code == 0 ? Theme.Orange : Theme.Pink);
        NeonDialog.Show(FindForm(), NetworkFix.DoneText(code, backup), "Netzwerk Fix", MessageBoxButtons.OK,
            code == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    // Das Programm startet sich selbst kurz mit Adminrechten, nur für diesen Schritt.
    static Task<int> RunElevatedAsync(string arguments)
    {
        var psi = new ProcessStartInfo(Application.ExecutablePath, arguments)
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        return Task.Run(() =>
        {
            using (Process p = Process.Start(psi))
            {
                p.WaitForExit();
                return p.ExitCode;
            }
        });
    }

    void ShowSoundState()
    {
        soundsAvailable = soundsDisabled = false;
        soundRow.Action.Text = "DEAKTIVIEREN";
        soundRow.Action.Primary = true;
        soundRow.ValueColor = Theme.Muted;
        try
        {
            soundsAvailable = Ts3Sounds.IsAvailable(Ts3Sounds.SettingsDb);
            if (!soundsAvailable)
            {
                soundRow.Value = "keine Einstellungen gefunden – TeamSpeak 3 einmal starten";
                return;
            }
            soundsDisabled = Ts3Sounds.AreDisabled(Ts3Sounds.SettingsDb);
        }
        catch (Exception ex)
        {
            soundsAvailable = false;
            soundRow.Value = "nicht lesbar: " + ex.Message;
            return;
        }
        if (soundsDisabled)
        {
            soundRow.Value = "deaktiviert  ✔";
            soundRow.ValueColor = Theme.Good;
            soundRow.Action.Text = "AKTIVIEREN";
            soundRow.Action.Primary = false;
        }
        else
        {
            soundRow.Value = "Sounds sind an";
            soundRow.ValueColor = Theme.Pink;
        }
    }

    async Task ToggleSoundsAsync()
    {
        bool disable = !soundsDisabled;
        if (!Confirm(disable
                ? "Alle TeamSpeak-Töne werden dauerhaft abgeschaltet, zum Beispiel beim Betreten eines Channels oder bei Anstupsern. " +
                  "Die Sprache hörst du weiter.\n\nFortfahren?"
                : "Die TeamSpeak-Töne werden wieder eingeschaltet.\n\nFortfahren?", "TeamSpeak-Sounds"))
            return;

        // TeamSpeak hält die Einstellung im Speicher und muss dafür geschlossen sein.
        bool wasRunning = Ts3.IsRunning();
        if (!await EnsureTs3ClosedAsync()) return;

        host.SetBusy(true);
        host.SetStatus("TeamSpeak-Einstellung wird geändert …", Theme.Text);
        try
        {
            string db = Ts3Sounds.SettingsDb;
            await Task.Run(() =>
            {
                if (disable) Ts3Sounds.Disable(db, settings);
                else Ts3Sounds.Enable(db, settings);
            });
        }
        finally
        {
            if (wasRunning)
            {
                try { Ts3.Start(); }
                catch (Exception) { /* dann startet man TeamSpeak eben selbst */ }
            }
        }
        host.SetStatus(disable ? "Fertig!  TeamSpeak-Töne sind abgeschaltet." : "Fertig!  TeamSpeak-Töne sind wieder an.", Theme.Orange);
    }

    void ShowModState()
    {
        modReport = null;
        modRow.ValueColor = Theme.Muted;
        string app = host.FiveMAppDir;
        if (app == null)
        {
            modRow.Value = "FiveM nicht gefunden – im Cache Cleaner auswählen";
            return;
        }
        try
        {
            string ini = CleanTargets.IniPath(app);
            modReport = ModCheck.Scan(app, File.Exists(ini) ? FiveMIni.GetGamePath(FiveMIni.ReadText(ini)) : null);
        }
        catch (Exception ex)
        {
            modRow.Value = "nicht lesbar: " + ex.Message;
            return;
        }
        if (modReport.Total == 0)
        {
            modRow.Value = modReport.GtaDir == null ? "nichts gefunden  ·  GTA-Ordner unbekannt" : "nichts gefunden  ✔";
            modRow.ValueColor = modReport.GtaDir == null ? Theme.Muted : Theme.Good;
        }
        else
        {
            modRow.Value = modReport.Total + (modReport.Total == 1 ? " Datei" : " Dateien") + " gefunden";
            modRow.ValueColor = Theme.Orange;
        }
    }

    void ShowModDetails()
    {
        if (modReport == null) return;
        string app = host.FiveMAppDir;
        var text = new System.Text.StringBuilder();
        if (modReport.Total == 0)
        {
            text.Append("Es wurden keine ReShade-Dateien, ASI-Plugins, fremden DLLs oder Mods gefunden.\n");
        }
        else
        {
            text.Append("Diese Dateien klinken sich in das Spiel ein. Das ist nicht automatisch ein Fehler, sie können aber Abstürze und Grafikfehler auslösen.\n");
            AppendFiles(text, "GTA-Ordner", modReport.GtaDir, modReport.GtaFiles);
            AppendFiles(text, "FiveM-Plugins", Path.Combine(app, "plugins"), modReport.PluginFiles);
            if (modReport.ModFileCount > 0)
                text.Append("\nFiveM-Mods (" + Path.Combine(app, "mods") + ")\n•  " + modReport.ModFileCount +
                            (modReport.ModFileCount == 1 ? " Datei" : " Dateien") + "\n");
            text.Append("\nZum Testen: FiveM schließen, die Dateien in einen anderen Ordner verschieben und FiveM starten. " +
                        "Ist der Fehler weg, lag es daran. Das Tool selbst ändert daran nichts.\n");
        }
        if (modReport.GtaDir == null)
            text.Append("\nDer GTA-Ordner steht nicht in der CitizenFX.ini oder existiert nicht, deshalb wurde er nicht geprüft.\n");
        NeonDialog.Show(host, text.ToString(), "Grafik-Mods", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    static void AppendFiles(System.Text.StringBuilder text, string title, string dir, System.Collections.Generic.List<string> files)
    {
        const int MaxShown = 12;
        if (files.Count == 0) return;
        text.Append("\n" + title + " (" + dir + ")\n");
        for (int i = 0; i < files.Count && i < MaxShown; i++) text.Append("•  " + files[i] + "\n");
        if (files.Count > MaxShown) text.Append("•  … und " + (files.Count - MaxShown) + " weitere\n");
    }

    static void ShowVersionState(FeatureRow row, string installed, string latest, bool latestIsFallback)
    {
        string latestText = latest == null ? "aktuelle Version nicht abrufbar"
                          : "aktuell " + latest + (latestIsFallback ? " (zuletzt bekannt)" : "");
        if (installed == null)
        {
            row.Value = "nicht installiert  ·  " + latestText;
            row.ValueColor = Theme.Pink;
            row.Action.Text = "INSTALLIEREN";
            row.Action.Primary = true;
        }
        else if (installed == "")
        {
            row.Value = "installiert (Version unbekannt)  ·  " + latestText;
            row.ValueColor = Theme.Orange;
            row.Action.Text = "AKTUALISIEREN";
            row.Action.Primary = true;
        }
        else if (latest != null && Versions.Compare(installed, latest) < 0)
        {
            row.Value = "installiert " + installed + "  ·  Update auf " + latest + " verfügbar";
            row.ValueColor = Theme.Pink;
            row.Action.Text = "AKTUALISIEREN";
            row.Action.Primary = true;
        }
        else
        {
            row.Value = "installiert " + installed + "  ✔  aktuell";
            row.ValueColor = Theme.Good;
            row.Action.Text = "NEU INSTALLIEREN";
            row.Action.Primary = false;
        }
    }

    // ------------------------------------------------------------ Aktionen

    async Task Guard(Func<Task> action)
    {
        if (host.Busy || refreshing) return;
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            if (Elevation.IsUacCancelled(ex))
            {
                host.SetStatus("Abgebrochen – die Windows-Admin-Abfrage wurde nicht bestätigt.", Theme.Pink);
            }
            else
            {
                host.SetStatus("Fehler: " + ex.Message, Theme.Pink);
                NeonDialog.Show(FindForm(), ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        finally
        {
            if (host.Busy) host.SetBusy(false);
        }
        await RefreshAsync();
    }

    bool Confirm(string text, string caption)
    {
        return NeonDialog.Show(FindForm(), text, caption, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
    }

    // TeamSpeak muss für Installationen geschlossen sein.
    async Task<bool> EnsureTs3ClosedAsync()
    {
        if (!Ts3.IsRunning()) return true;
        if (!Confirm("TeamSpeak 3 läuft gerade und muss dafür geschlossen werden.\n\nJetzt schließen?", "TeamSpeak läuft"))
            return false;
        host.SetStatus("TeamSpeak 3 wird geschlossen …", Theme.Text);
        await Task.Run(() => Ts3.Close());
        return true;
    }

    // TeamSpeak 3 x64 und SaltyChat gibt es nur für 64-Bit-Windows.
    static void Require64Bit()
    {
        if (!Environment.Is64BitOperatingSystem)
            throw new NotSupportedException("Dieses Windows ist 32-Bit. TeamSpeak 3 (64-Bit) und SaltyChat " +
                                            "brauchen ein 64-Bit-Windows.");
    }

    async Task DownloadAsync(string what, string url, string destination)
    {
        host.SetStatus("Lade " + what + " …", Theme.Text);
        host.SetProgress(0);
        var progress = new Progress<int>(p =>
        {
            host.SetProgress(p < 0 ? 0 : p);
            if (p >= 0) host.SetStatus("Lade " + what + " …  " + (p / 10) + " %", Theme.Text);
        });
        IProgress<int> report = progress;
        await Task.Run(() => Downloader.DownloadFile(url, destination, p => report.Report(p)));
        host.SetProgress(1000);
    }

    async Task InstallTs3Async()
    {
        Require64Bit();
        if (ts3Latest == null)
            throw new InvalidOperationException("Die aktuelle TeamSpeak-Version konnte nicht abgerufen werden. Internetverbindung prüfen.");
        Ts3Release rel = ts3Latest;
        if (!Confirm("TeamSpeak 3 " + rel.Version + " wird heruntergeladen und installiert.\n" +
                     "Windows fragt dabei nach Administratorrechten.\n\nFortfahren?", "TeamSpeak 3 installieren"))
            return;
        if (!await EnsureTs3ClosedAsync()) return;

        host.SetBusy(true);
        string file = Path.Combine(Downloader.WorkDir, "TeamSpeak3-Client-win64-" + rel.Version + ".exe");
        try
        {
            await DownloadAsync("TeamSpeak 3 " + rel.Version, rel.Url, file);

            host.SetStatus("Prüfe Download …", Theme.Text);
            string hash = await Task.Run(() => Downloader.Sha256OfFile(file));
            if (!string.Equals(hash, rel.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Die Prüfsumme des TeamSpeak-Downloads stimmt nicht. Die Datei wurde verworfen.");

            host.SetStatus("Installiere TeamSpeak 3 " + rel.Version + " … (Admin-Abfrage bestätigen)", Theme.Text);
            int code = await Task.Run(() => Ts3.RunSilentInstaller(file));
            if (code != 0) throw new Exception("Der TeamSpeak-Installer meldet Fehlercode " + code + ".");
            host.SetStatus("Fertig!  TeamSpeak 3 " + rel.Version + " ist installiert.", Theme.Orange);
        }
        finally
        {
            Downloader.TryDelete(file);
        }
    }

    async Task InstallSaltyAsync()
    {
        Require64Bit();
        string version = saltyLatest ?? SaltyChat.FallbackVersion;
        if (Ts3.InstalledVersion() == null &&
            !Confirm("TeamSpeak 3 scheint nicht installiert zu sein. SaltyChat trotzdem installieren?", "SaltyChat"))
            return;
        if (!Confirm("SaltyChat " + version + " wird heruntergeladen und nach\n" + SaltyChat.PluginsDir +
                     "\ninstalliert.\n\nFortfahren?", "SaltyChat installieren"))
            return;
        if (!await EnsureTs3ClosedAsync()) return;

        host.SetBusy(true);
        string vcFile = Path.Combine(Downloader.WorkDir, "vc_redist.x64.exe");
        string pluginFile = Path.Combine(Downloader.WorkDir, "SaltyChat_" + version + ".ts3_plugin");
        string note = "";
        try
        {
            if (!VcRedist.IsInstalled())
            {
                await DownloadAsync("Visual C++ Runtime", VcRedist.Url, vcFile);
                host.SetStatus("Installiere Visual C++ Runtime … (Admin-Abfrage bestätigen)", Theme.Text);
                int code = await Task.Run(() => VcRedist.RunSilentInstaller(vcFile));
                if (code == 3010) note = "  ·  Windows-Neustart empfohlen";
                else if (code != 0 && code != 1638)
                    note = "  ·  Visual C++ Runtime meldet Code " + code;
            }

            await DownloadAsync("SaltyChat " + version, SaltyChat.DownloadUrl(version), pluginFile);
            host.SetStatus("Installiere SaltyChat " + version + " …", Theme.Text);
            await Task.Run(() => SaltyChat.InstallPlugin(pluginFile, SaltyChat.PluginsDir));
            host.SetStatus("Fertig!  SaltyChat " + version + " ist installiert" + note +
                           "\nTeamSpeak starten und unter Extras → Optionen → Addons prüfen, ob es aktiv ist.", Theme.Orange);
        }
        finally
        {
            Downloader.TryDelete(vcFile);
            Downloader.TryDelete(pluginFile);
        }
    }

    async Task ToggleSteamAsync()
    {
        if (steamConfigDir == null) return;
        bool remove = steamReady;
        string restart = "Steam überschreibt diese Einstellungen beim Beenden. Deshalb wird Steam kurz geschlossen und " +
                         "danach wieder gestartet. Laufende Downloads pausieren, ein laufendes Steam-Spiel bitte vorher beenden.";
        string text = remove
            ? "FiveM wird aus der Steam-Bibliothek entfernt, die Controller-Einstellung dafür ebenfalls.\n\n" + restart + "\n\nFortfahren?"
            : "FiveM wird als Nicht-Steam-Spiel in die Steam-Bibliothek eingetragen und Steam Input dafür aktiviert.\n\n" +
              SteamLibrary.StartViaSteamHint + "\n\n" +
              restart + "\n\nDer aktuelle Stand wird vorher gespeichert. Fortfahren?";
        if (!Confirm(text, "FiveM in Steam")) return;

        string shortcuts = Path.Combine(steamConfigDir, "shortcuts.vdf");
        string local = Path.Combine(steamConfigDir, "localconfig.vdf");
        string dir = steamDir, appDir = host.FiveMAppDir;

        host.SetBusy(true);
        bool wasRunning = SteamLibrary.IsRunning();
        if (wasRunning)
        {
            host.SetStatus("Steam wird geschlossen …", Theme.Text);
            if (!await Task.Run(() => SteamLibrary.Shutdown(dir, 60000)))
                throw new TimeoutException("Steam hat sich nicht schließen lassen. Es wurde nichts geändert. " +
                                           "Läuft noch ein Steam-Spiel? Bitte beenden und noch einmal versuchen.");
        }
        try
        {
            host.SetStatus("Steam-Einstellungen werden geändert …", Theme.Text);
            await Task.Run(() =>
            {
                GameConfigFiles.Snapshot(SteamLibrary.BackupDir, shortcuts, local);
                if (remove) SteamLibrary.RemoveFiveM(shortcuts, local);
                else SteamLibrary.AddFiveM(shortcuts, local, appDir);
            });
        }
        finally
        {
            if (wasRunning) SteamLibrary.Start(dir);
        }

        if (remove)
        {
            host.SetStatus("Fertig!  FiveM wurde aus der Steam-Bibliothek entfernt.", Theme.Orange);
            return;
        }
        host.SetStatus("Fertig!  Wichtig: FiveM ab jetzt immer über Steam starten,\nsonst funktioniert der Controller nicht.", Theme.Orange);
        NeonDialog.Show(FindForm(), SteamLibrary.SetupDoneText, SteamLibrary.SetupDoneTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    async Task ToggleHostsAsync()
    {
        bool enable = !hostsActive;
        string text = enable
            ? "Der TS-Moved-nicht-Bugfix trägt folgende Zeile in die Windows-hosts-Datei ein:\n\n    " + HostsFix.Entry +
              "\n\nWindows fragt dabei nach Administratorrechten.\n\nFortfahren?"
            : "Der TS-Moved-nicht-Bugfix-Eintrag wird wieder aus der hosts-Datei entfernt.\n\nFortfahren?";
        if (!Confirm(text, "TS-Moved-nicht-Bugfix")) return;

        host.SetBusy(true);
        host.SetStatus("Bitte die Windows-Admin-Abfrage bestätigen …", Theme.Text);
        int code = await RunElevatedAsync("--hosts-fix " + (enable ? "on" : "off"));
        if (code != 0) throw new Exception("Die hosts-Datei konnte nicht geändert werden (Code " + code + ").");
        host.SetStatus(enable ? "Fertig!  TS-Moved-nicht-Bugfix ist aktiv." : "Fertig!  TS-Moved-nicht-Bugfix wurde entfernt.", Theme.Orange);
    }
}
