using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

public class MainForm : Form
{
    const int ContentWidth = 712;   // logische Pixel bei 100 %
    const int Margin_ = 24;

    const string NameHint = "FiveM-Name muss danach neu gesetzt werden";

    const string SettingKey = "FiveMAppDir";

    // Wo FiveM liegt (gesucht oder von Hand gewählt)
    readonly AppSettings settings = new AppSettings(AppSettings.DefaultPath);
    readonly string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    FiveMLocation fiveM;

    // Was geleert werden kann
    List<CleanTarget> normalTargets;     // Normaler Cache Clear
    List<CleanTarget> fullTargets;       // Gesamter Cache Clear (normal + game-storage, nui-storage)
    readonly CleanTarget tempTarget;
    string iniPath;                      // null, wenn FiveM nicht gefunden wurde
    FeatureRow installRow;

    // Messwerte je Pfad
    readonly Dictionary<string, long> sizes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, bool> exists = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

    bool fullMode;
    ModeCard cardNormal, cardFull;
    TargetRow tempRow, iniRow;
    NeonButton btnMeasure, btnClean;
    NeonProgressBar bar;
    Label status;
    Panel overview, mainPage;
    GtaSettingsPage gta;
    ExtrasPage extras;
    BotsPage bots;
    readonly List<NavTile> tiles = new List<NavTile>();
    readonly List<NeonButton> backButtons = new List<NeonButton>();
    Panel[] pages;                 // 0 = Übersicht, 1 = Cleaner, 2 = GTA-Einstellungen, 3 = Sonderfunktionen, 4 = Discord Bots
    bool measuredOnce;

    bool busy;
    long progressDone, progressTotal;
    readonly System.Windows.Forms.Timer uiTimer = new System.Windows.Forms.Timer();

    public MainForm()
    {
        tempTarget = CleanTargets.Temp(Path.GetTempPath());
        SetLocation(FiveMLocator.Locate(localAppData, settings.Get(SettingKey)));

        Text = "Retro Roleplay Tool · by RR | Manu";
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.MakeFont("Segoe UI", 9.5f);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;

        BuildLayout();

        uiTimer.Interval = 100;
        uiTimer.Tick += delegate { UpdateProgress(); };
    }

    Label Heading(string text, int x, int y, int w)
    {
        return new Label
        {
            Text = text,
            ForeColor = Theme.Orange,
            Font = Theme.MakeFont("Segoe UI", 9f, FontStyle.Bold),
            AutoSize = false,
            Bounds = new Rectangle(x, y, w, Theme.S(20)),
        };
    }

    void BuildLayout()
    {
        int width = Theme.S(ContentWidth + 2 * Margin_);
        int m = Theme.S(Margin_);
        int inner = width - 2 * m;

        Image banner = LoadBanner();
        int bannerHeight = banner != null ? (int)Math.Round((double)width * banner.Height / banner.Width) : Theme.S(80);
        Controls.Add(new BannerView(banner) { Bounds = new Rectangle(0, 0, width, bannerHeight) });

        // Die Seiten liegen übereinander; sichtbar ist immer nur eine.
        int pageTop = bannerHeight;
        int contentTop = AddPageHeaderHeight();

        // --- Übersicht (Startseite)
        overview = new Panel { BackColor = Theme.Bg, Location = new Point(0, pageTop) };
        Controls.Add(overview);
        int oy = Theme.S(20);
        overview.Controls.Add(Heading("ÜBERSICHT", m, oy, inner));
        oy += Theme.S(28);
        var tileDefs = new[]
        {
            new[] { "CLEANER", "FiveM-Cache leeren, %TEMP% aufräumen, Stable-Kanal setzen", "" },
            new[] { "GTA-EINSTELLUNGEN", "Texture Budget, FXAA, VSync, Dichte, Distanz · mit Zurück-Funktion", "" },
            new[] { "SONDERFUNKTIONEN", "TeamSpeak 3, SaltyChat und TS-Moved-nicht-Bugfix", "" },
            new[] { "DISCORD BOTS", "Retro RP und Retro Business", "" },
        };
        int tileH = Theme.S(100), tileGlow = Theme.S(6);
        for (int i = 0; i < tileDefs.Length; i++)
        {
            var tile = new NavTile
            {
                Text = tileDefs[i][0],
                Detail = tileDefs[i][1],
                Glyph = tileDefs[i][2],
                Bounds = new Rectangle(m - tileGlow, oy, inner + 2 * tileGlow, tileH),
            };
            int page = i + 1;
            tile.Click += delegate { ShowPage(page); };
            tiles.Add(tile);
            overview.Controls.Add(tile);
            oy += tileH + Theme.S(10);
        }

        // --- Cleaner
        mainPage = new Panel { BackColor = Theme.Bg, Location = new Point(0, pageTop), Visible = false };
        Controls.Add(mainPage);
        AddPageHeader(mainPage, "CLEANER", m, inner);
        int y = contentTop;

        // --- Wo liegt FiveM?
        installRow = new FeatureRow(Theme.S(180)) { Title = "FiveM-Installation", Bounds = new Rectangle(m, y, inner, Theme.S(82)) };
        installRow.Action.Click += async delegate { await ChooseFiveMAsync(); };
        mainPage.Controls.Add(installRow);
        y += Theme.S(82) + Theme.S(12);

        // --- FiveM: zwei Optionen
        mainPage.Controls.Add(Heading("FIVEM CACHE CLEAR", m, y, inner));
        y += Theme.S(24);
        int glow = Theme.S(5);
        int cardW = (inner - Theme.S(14)) / 2 + 2 * glow;
        int cardH = Theme.S(124);
        cardNormal = new ModeCard
        {
            Title = "Normaler Cache Clear",
            Detail = "cache · server-cache · server-cache-priv · logs · crashes",
            Value = "…",
            Selected = true,
            Bounds = new Rectangle(m - glow, y, cardW, cardH),
        };
        cardFull = new ModeCard
        {
            Title = "Gesamter Cache Clear",
            Detail = "wie Normal, zusätzlich game-storage · nui-storage\nNur nutzen, wenn der normale Clear nicht hilft.",
            Warning = "⚠  " + NameHint,
            Value = "…",
            Bounds = new Rectangle(m + inner + glow - cardW, y, cardW, cardH),
        };
        cardNormal.Picked += delegate { SetMode(false); };
        cardFull.Picked += delegate { SetMode(true); };
        mainPage.Controls.Add(cardNormal);
        mainPage.Controls.Add(cardFull);
        y += cardH + Theme.S(12);

        // --- Zusätzlich
        mainPage.Controls.Add(Heading("ZUSÄTZLICH", m, y, inner));
        y += Theme.S(22);
        int rowHeight = Theme.S(52);
        tempRow = AddRow(tempTarget.Name, tempTarget.Path, m, ref y, inner, rowHeight);
        iniRow = AddRow("FiveM-Kanal auf Stable setzen", iniPath ?? "", m, ref y, inner, rowHeight);
        y += Theme.S(16);

        // --- Buttons
        int btnH = Theme.S(56);
        btnMeasure = new NeonButton { Text = "GRÖSSE PRÜFEN", Bounds = new Rectangle(m - Theme.S(6), y, Theme.S(200), btnH) };
        btnClean = new NeonButton { Text = "BEREINIGEN", Primary = true, Bounds = new Rectangle(m + inner - Theme.S(254), y, Theme.S(260), btnH) };
        btnMeasure.Click += async delegate { await MeasureAllAsync(); };
        btnClean.Click += async delegate { await CleanAsync(); };
        mainPage.Controls.Add(btnMeasure);
        mainPage.Controls.Add(btnClean);
        y += btnH + Theme.S(12);

        // --- GTA-Einstellungen, Sonderfunktionen und Discord Bots
        gta = new GtaSettingsPage(this, m, inner, contentTop) { Visible = false, Location = new Point(0, pageTop) };
        AddPageHeader(gta, "GTA-EINSTELLUNGEN", m, inner);
        Controls.Add(gta);
        extras = new ExtrasPage(this, m, inner, contentTop) { Visible = false, Location = new Point(0, pageTop) };
        AddPageHeader(extras, "SONDERFUNKTIONEN", m, inner);
        bots = new BotsPage(m, inner, contentTop) { Visible = false, Location = new Point(0, pageTop) };
        AddPageHeader(bots, "DISCORD BOTS", m, inner);
        Controls.Add(extras);
        Controls.Add(bots);

        // Alle Seiten bekommen dieselbe Höhe – die der längsten.
        pages = new Panel[] { overview, mainPage, gta, extras, bots };
        int pageHeight = Math.Max(Math.Max(oy, y), Math.Max(gta.ContentHeight, Math.Max(extras.ContentHeight, bots.ContentHeight)));
        foreach (Panel p in pages) p.Size = new Size(width, pageHeight);
        y = pageTop + pageHeight;

        bar = new NeonProgressBar { Bounds = new Rectangle(m, y, inner, Theme.S(12)) };
        Controls.Add(bar);
        y += Theme.S(12) + Theme.S(10);

        status = new Label
        {
            ForeColor = Theme.Text,
            Font = Theme.MakeFont("Segoe UI Semibold", 10f),
            AutoSize = false,
            Bounds = new Rectangle(m, y, inner, Theme.S(44)),
        };
        Controls.Add(status);
        y += Theme.S(44) + Theme.S(4);

        var footer = new Label
        {
            Text = "Retro Roleplay Tool " + AppVersion() + "  ·  by RR | Manu",
            ForeColor = Theme.Muted,
            Font = Theme.MakeFont("Segoe UI", 8f),
            TextAlign = ContentAlignment.MiddleRight,
            AutoSize = false,
            Bounds = new Rectangle(m, y, inner, Theme.S(18)),
        };
        Controls.Add(footer);
        y += Theme.S(18) + Theme.S(12);

        ClientSize = new Size(width, y);
        ShowLocation();
    }

    // ------------------------------------------------------------ FiveM-Installation

    void SetLocation(FiveMLocation location)
    {
        fiveM = location;
        normalTargets = fiveM.Found ? CleanTargets.FiveM(fiveM.AppDir, false) : new List<CleanTarget>();
        fullTargets = fiveM.Found ? CleanTargets.FiveM(fiveM.AppDir, true) : new List<CleanTarget>();
        iniPath = fiveM.Found ? CleanTargets.IniPath(fiveM.AppDir) : null;
        sizes.Clear();
        exists.Clear();
    }

    void ShowLocation()
    {
        if (fiveM.Found)
        {
            installRow.Detail = fiveM.AppDir;
            installRow.Value = "gefunden  ✔";
            installRow.ValueColor = Theme.Good;
            installRow.Action.Text = "ÄNDERN";
            installRow.Action.Primary = false;
        }
        else
        {
            installRow.Detail = "Bitte den Ordner von FiveM auswählen (die Datei FiveM.exe).";
            installRow.Value = "nicht gefunden";
            installRow.ValueColor = Theme.Pink;
            installRow.Action.Text = "AUSWÄHLEN";
            installRow.Action.Primary = true;
        }
        iniRow.Subtitle = iniPath ?? "FiveM nicht gefunden";
        if (!fiveM.Found)
        {
            cardNormal.Value = cardFull.Value = "FiveM fehlt";
            iniRow.Checked = false;
        }
        ApplyEnabled();
    }

    async Task ChooseFiveMAsync()
    {
        if (busy) return;
        using (var dlg = new OpenFileDialog
        {
            Title = "FiveM.exe auswählen",
            Filter = "FiveM (FiveM.exe)|FiveM.exe|Alle Dateien (*.*)|*.*",
            InitialDirectory = fiveM.Found ? Path.GetDirectoryName(fiveM.AppDir) : localAppData,
            CheckFileExists = true,
        })
        {
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            string app = FiveMLocator.NormalizeAppDir(dlg.FileName);
            if (app == null)
            {
                MessageBox.Show(this, "Das ist keine FiveM-Installation.\n\n" +
                    "Bitte die Datei FiveM.exe im FiveM-Ordner auswählen (dort liegt auch der Ordner FiveM.app).",
                    "FiveM nicht erkannt", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try { settings.Set(SettingKey, app); } catch (Exception) { /* Speichern optional */ }
            SetLocation(new FiveMLocation { AppDir = app, Source = "von dir gewählt" });
        }
        iniRow.Checked = true;
        ShowLocation();
        await MeasureAllAsync();
    }

    // Seitenkopf jeder Unterseite: links „‹ ÜBERSICHT“, rechts der Seitentitel
    static int AddPageHeaderHeight() { return Theme.S(12) + Theme.S(48) + Theme.S(14); }

    void AddPageHeader(Panel page, string title, int m, int inner)
    {
        int top = Theme.S(12), h = Theme.S(48);
        var back = new NeonButton { Text = "‹  ÜBERSICHT", Bounds = new Rectangle(m - Theme.S(6), top, Theme.S(170), h) };
        back.Click += delegate { ShowPage(0); };
        backButtons.Add(back);
        page.Controls.Add(back);
        page.Controls.Add(new Label
        {
            Text = title,
            ForeColor = Theme.Orange,
            Font = Theme.MakeFont("Segoe UI", 13f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleRight,
            AutoSize = false,
            Bounds = new Rectangle(m + Theme.S(180), top, inner - Theme.S(180), h),
        });
        page.Controls.Add(new Panel { BackColor = Theme.Line, Bounds = new Rectangle(m, top + h + Theme.S(7), inner, 1) });
    }

    TargetRow AddRow(string title, string subtitle, int x, ref int y, int w, int h)
    {
        var row = new TargetRow { Title = title, Subtitle = subtitle, Value = "…", Bounds = new Rectangle(x, y, w, h) };
        row.CheckedChanged += delegate { ShowSelectionTotal(); };
        mainPage.Controls.Add(row);
        y += h;
        return row;
    }

    static string AppVersion()
    {
        Version v = Assembly.GetExecutingAssembly().GetName().Version;
        return "v" + v.Major + "." + v.Minor + "." + v.Build;
    }

    static Image LoadBanner()
    {
        Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("GTACleaner.banner.png");
        return s == null ? null : Image.FromStream(s);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ShowPage(0);
    }

    void SetMode(bool full)
    {
        if (busy) return;
        fullMode = full;
        cardNormal.Selected = !full;
        cardFull.Selected = full;
        ShowSelectionTotal();
    }

    // ------------------------------------------------------------ Messen

    IEnumerable<CleanTarget> AllTargets()
    {
        foreach (CleanTarget t in fullTargets) yield return t;   // enthält alle normalen
        yield return tempTarget;
    }

    async Task MeasureAllAsync()
    {
        if (busy) return;
        SetBusy(true);
        bar.Value = 0;
        SetStatus("Größen werden ermittelt …", Theme.Text);
        foreach (TargetRow r in Rows()) { r.Value = "wird gemessen …"; r.ValueColor = Theme.Muted; }
        if (fiveM.Found) cardNormal.Value = cardFull.Value = "…";

        foreach (CleanTarget t in AllTargets())
        {
            string path = t.Path;
            exists[path] = Directory.Exists(path);
            sizes[path] = exists[path] ? await Task.Run(() => FolderCleaner.MeasureSize(path)) : 0;
            ShowSizes();
        }
        UpdateIniRow();
        SetBusy(false);
        ShowSelectionTotal();
    }

    IEnumerable<TargetRow> Rows()
    {
        yield return tempRow;
    }

    long Sum(IEnumerable<CleanTarget> list)
    {
        long total = 0;
        foreach (CleanTarget t in list)
        {
            long s;
            if (sizes.TryGetValue(t.Path, out s)) total += s;
        }
        return total;
    }

    bool AnyExists(IEnumerable<CleanTarget> list)
    {
        foreach (CleanTarget t in list)
        {
            bool e;
            if (exists.TryGetValue(t.Path, out e) && e) return true;
        }
        return false;
    }

    bool Measured(IEnumerable<CleanTarget> list)
    {
        foreach (CleanTarget t in list) if (!sizes.ContainsKey(t.Path)) return false;
        return true;
    }

    void ShowSizes()
    {
        if (!fiveM.Found) cardNormal.Value = cardFull.Value = "FiveM fehlt";
        else if (Measured(normalTargets))
            cardNormal.Value = AnyExists(normalTargets) ? SizeFormat.Format(Sum(normalTargets)) : "nicht gefunden";
        if (fiveM.Found && Measured(fullTargets))
            cardFull.Value = AnyExists(fullTargets) ? SizeFormat.Format(Sum(fullTargets)) : "nicht gefunden";
        ShowRowSize(tempRow, new List<CleanTarget> { tempTarget });
    }

    void ShowRowSize(TargetRow row, List<CleanTarget> list)
    {
        if (!Measured(list)) return;
        bool found = AnyExists(list);
        row.Value = found ? SizeFormat.Format(Sum(list)) : "nicht vorhanden";
        row.ValueColor = found ? Theme.Orange : Theme.Muted;
    }

    void UpdateIniRow()
    {
        if (iniPath == null)
        {
            iniRow.Value = "FiveM fehlt";
            iniRow.ValueColor = Theme.Muted;
            return;
        }
        if (!File.Exists(iniPath))
        {
            iniRow.Value = "INI nicht gefunden";
            iniRow.ValueColor = Theme.Muted;
            return;
        }
        string channel = null;
        try { channel = FiveMIni.GetUpdateChannel(File.ReadAllText(iniPath)); } catch (IOException) { }
        bool stable = channel == null || channel.Equals(FiveMIni.StableChannel, StringComparison.OrdinalIgnoreCase);
        iniRow.Value = stable ? FiveMIni.DisplayName(channel) + "  ✔" : FiveMIni.DisplayName(channel) + "  → Stable";
        iniRow.ValueColor = stable ? Theme.Good : Theme.Pink;
    }

    // Alles, was beim Klick auf BEREINIGEN geleert würde (nur vorhandene Ordner)
    List<CleanTarget> SelectedTargets()
    {
        var list = new List<CleanTarget>(fullMode ? fullTargets : normalTargets);
        if (tempRow.Checked) list.Add(tempTarget);
        return list.FindAll(t => { bool e; return exists.TryGetValue(t.Path, out e) && e; });
    }

    void ShowSelectionTotal()
    {
        if (busy) return;
        if (!fiveM.Found)
        {
            SetStatus("FiveM wurde nicht gefunden – bitte oben auswählen.\n" +
                      "Bis dahin kann nur %TEMP% geleert werden (" + SizeFormat.Format(Sum(SelectedTargets())) + ").", Theme.Pink);
            return;
        }
        string mode = fullMode ? "Gesamter Cache Clear" : "Normaler Cache Clear";
        string ini = iniRow.Checked ? "  ·  FiveM-Kanal → Stable" : "";
        SetStatus(mode + ": " + SizeFormat.Format(Sum(SelectedTargets())) + " werden freigegeben" + ini +
                  (fullMode ? "\n⚠  " + NameHint + "." : ""), fullMode ? Theme.Text : Theme.Text);
    }

    // ------------------------------------------------------------ Bereinigen

    async Task CleanAsync()
    {
        if (busy) return;

        if (FiveMProcess.IsRunning())
        {
            DialogResult answer = MessageBox.Show(this,
                "FiveM läuft gerade.\n\n" +
                "Ja = FiveM jetzt beenden und dann bereinigen\n" +
                "Nein = trotzdem bereinigen (gesperrte Dateien werden übersprungen)\n" +
                "Abbrechen = nichts tun",
                "FiveM läuft", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
            if (answer == DialogResult.Cancel) return;
            if (answer == DialogResult.Yes)
            {
                SetBusy(true);
                SetStatus("FiveM wird beendet …", Theme.Text);
                await Task.Run(() => FiveMProcess.KillAll());
                await Task.Delay(1000); // Dateisperren kurz nachlaufen lassen
                SetBusy(false);
            }
        }

        List<CleanTarget> selected = SelectedTargets();
        long total = Sum(selected);
        bool doIni = iniRow.Checked && iniPath != null;
        bool full = fullMode && fiveM.Found;

        var msg = new StringBuilder((full ? "Gesamter" : "Normaler") + " Cache Clear – folgende Ordner werden geleert:\n\n");
        foreach (CleanTarget t in selected)
            msg.Append("•  " + t.Name + "  (" + SizeFormat.Format(sizes[t.Path]) + ")\n");
        if (selected.Count == 0) msg = new StringBuilder("Es wurde kein vorhandener Ordner gefunden.\n");
        if (doIni) msg.Append("\nFiveM wird auf den Stable-Kanal gesetzt.\n");
        if (full) msg.Append("\nDen gesamten Cache Clear nur nutzen, wenn der normale Clear nicht hilft.\n" +
                             "\n⚠  Hinweis: game-storage enthält dein FiveM-Profil.\n" +
                             "Nach dem Bereinigen musst du in FiveM deinen Namen wieder eintragen.\n");
        if (selected.Count == 0 && !doIni)
        {
            MessageBox.Show(this, msg.ToString(), "Nichts zu tun", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        msg.Append("\nInsgesamt ca. " + SizeFormat.Format(total) + ". Fortfahren?");

        if (MessageBox.Show(this, msg.ToString(), "Bereinigen bestätigen",
                MessageBoxButtons.YesNo, full ? MessageBoxIcon.Warning : MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        SetBusy(true);
        Interlocked.Exchange(ref progressDone, 0);
        progressTotal = total;
        bar.Value = 0;
        uiTimer.Start();

        long freed = 0;
        int deleted = 0, skipped = 0;
        var errors = new List<string>();

        foreach (CleanTarget t in selected)
        {
            string path = t.Path;
            try
            {
                CleanResult r = await Task.Run(() =>
                    FolderCleaner.CleanContents(path, b => Interlocked.Add(ref progressDone, b)));
                freed += r.BytesFreed;
                deleted += r.FilesDeleted;
                skipped += r.FilesSkipped;
            }
            catch (Exception ex)
            {
                errors.Add(t.Name + ": " + ex.Message);
            }
        }

        string iniText = "FiveM-Kanal nicht angefasst";
        if (doIni) try
        {
            IniResult ir = await Task.Run(() => FiveMIni.EnsureStable(iniPath));
            iniText = ir == IniResult.Changed ? "FiveM auf Stable gesetzt"
                    : ir == IniResult.AlreadyStable ? "FiveM war schon auf Stable"
                    : "CitizenFX.ini nicht gefunden";
        }
        catch (Exception ex)
        {
            iniText = "FiveM-Kanal nicht geändert";
            errors.Add("CitizenFX.ini: " + ex.Message);
        }

        uiTimer.Stop();
        bar.Value = 1000;
        SetBusy(false);
        SetMode(false);          // Standard ist immer der normale Cache Clear
        await MeasureAllAsync();

        SetStatus("Fertig!  " + SizeFormat.Format(freed) + " freigegeben  ·  " +
                  deleted.ToString("N0") + " Dateien gelöscht" +
                  (skipped > 0 ? "  ·  " + skipped.ToString("N0") + " übersprungen (in Benutzung oder geschützt)" : "") +
                  "\n" + iniText + (full ? "  ·  ⚠ FiveM-Name neu setzen!" : ""), Theme.Orange);

        if (errors.Count > 0)
            MessageBox.Show(this, string.Join("\n", errors), "Hinweise", MessageBoxButtons.OK, MessageBoxIcon.Information);
        if (full)
            MessageBox.Show(this, "Gesamter Cache Clear ist fertig.\n\n" +
                "Wichtig: Beim nächsten Start von FiveM musst du deinen FiveM-Namen wieder eintragen.",
                "FiveM-Name neu setzen", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    void UpdateProgress()
    {
        long done = Interlocked.Read(ref progressDone);
        if (progressTotal > 0)
            bar.Value = (int)(Math.Min(done, progressTotal) * 1000 / progressTotal);
        SetStatus("Wird bereinigt …  " + SizeFormat.Format(done) +
                  (progressTotal > 0 ? " von " + SizeFormat.Format(progressTotal) : ""), Theme.Text);
    }

    // ------------------------------------------------------------ Gemeinsam genutzt (auch von ExtrasPage)

    public bool Busy { get { return busy; } }

    public void SetBusy(bool value)
    {
        busy = value;
        ApplyEnabled();
        extras.SetButtonsEnabled(!value);
        gta.SetButtonsEnabled(!value);
    }

    // FiveM-Optionen sind nur bedienbar, wenn FiveM gefunden wurde.
    void ApplyEnabled()
    {
        bool idle = !busy;
        btnMeasure.Enabled = idle;
        btnClean.Enabled = idle;
        foreach (NavTile t in tiles) t.Enabled = idle;
        foreach (NeonButton b in backButtons) b.Enabled = idle;
        bots.SetButtonsEnabled(idle);
        installRow.Action.Enabled = idle;
        cardNormal.Enabled = idle && fiveM.Found;
        cardFull.Enabled = idle && fiveM.Found;
        foreach (TargetRow r in Rows()) r.Enabled = idle;
        iniRow.Enabled = idle && fiveM.Found;
    }

    public void SetStatus(string text, Color color)
    {
        status.ForeColor = color;
        status.Text = text;
    }

    // Promille 0..1000
    public void SetProgress(int permille)
    {
        bar.Value = permille;
    }

    // 0 = Übersicht, 1 = Cleaner, 2 = GTA-Einstellungen, 3 = Sonderfunktionen, 4 = Discord Bots
    void ShowPage(int index)
    {
        if (busy) return;
        for (int i = 0; i < pages.Length; i++) pages[i].Visible = i == index;
        bar.Value = 0;
        bar.Visible = index == 1 || index == 3;   // Fortschritt gibt es nur beim Bereinigen und Installieren
        SetStatus(index == 0 ? "Wähle einen Bereich aus." : "", Theme.Muted);

        if (index == 1)
        {
            SetMode(false);      // Standard ist immer der normale Cache Clear
            // Größen erst messen, wenn der Cleaner das erste Mal geöffnet wird
            if (!measuredOnce) { measuredOnce = true; var ignored = MeasureAllAsync(); }
            else ShowSelectionTotal();
        }
        if (index == 2) { var ignored = gta.RefreshAsync(); }
        if (index == 3) { var ignored = extras.RefreshAsync(); }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (busy &&
            MessageBox.Show(this, "Es läuft gerade noch ein Vorgang. Trotzdem schließen?", "Retro Roleplay Tool",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            e.Cancel = true;
        base.OnFormClosing(e);
    }
}
