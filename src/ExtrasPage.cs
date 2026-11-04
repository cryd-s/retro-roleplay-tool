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
        int y = Theme.S(10);
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

// Unterseite „Sonderfunktionen“: TeamSpeak 3, SaltyChat, TS-Moved-nicht-Bugfix.
public class ExtrasPage : Panel
{
    readonly MainForm host;
    readonly FeatureRow ts3Row, saltyRow, hostsRow;

    Ts3Release ts3Latest;
    string saltyLatest;
    bool hostsActive;
    bool refreshing;

    public ExtrasPage(MainForm host, int margin, int inner)
    {
        this.host = host;
        BackColor = Theme.Bg;
        int y = Theme.S(12);

        Controls.Add(new Label
        {
            Text = "SONDERFUNKTIONEN",
            ForeColor = Theme.Orange,
            Font = Theme.MakeFont("Segoe UI", 9f, FontStyle.Bold),
            AutoSize = false,
            Bounds = new Rectangle(margin, y, inner, Theme.S(20)),
        });
        y += Theme.S(24);

        int rowHeight = Theme.S(84), buttonWidth = Theme.S(230);
        ts3Row = AddRow(margin, inner, ref y, rowHeight, buttonWidth,
            "TeamSpeak 3", "Lädt die aktuelle Version von teamspeak.com, prüft sie und installiert still.");
        saltyRow = AddRow(margin, inner, ref y, rowHeight, buttonWidth,
            "SaltyChat", "Installiert das aktuelle Plugin von saltyhub.net in TeamSpeak 3.");
        hostsRow = AddRow(margin, inner, ref y, rowHeight, buttonWidth,
            "TS-Moved-nicht-Bugfix", "Leitet " + HostsFix.Host + " in der hosts-Datei auf 127.0.0.1 um.");

        ts3Row.Action.Click += async delegate { await Guard(InstallTs3Async); };
        saltyRow.Action.Click += async delegate { await Guard(InstallSaltyAsync); };
        hostsRow.Action.Click += async delegate { await Guard(ToggleHostsAsync); };
        ContentHeight = y + Theme.S(12);
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
        ts3Row.Action.Enabled = enabled && !refreshing;
        saltyRow.Action.Enabled = enabled && !refreshing;
        hostsRow.Action.Enabled = enabled && !refreshing;
    }

    // ------------------------------------------------------------ Status

    public async Task RefreshAsync()
    {
        if (refreshing) return;
        refreshing = true;
        SetButtonsEnabled(!host.Busy);
        foreach (FeatureRow r in new[] { ts3Row, saltyRow, hostsRow })
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
                MessageBox.Show(FindForm(), ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
        return MessageBox.Show(FindForm(), text, caption, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
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
        // Das Programm startet sich selbst kurz mit Adminrechten, nur für diesen Schritt.
        var psi = new ProcessStartInfo(Application.ExecutablePath, "--hosts-fix " + (enable ? "on" : "off"))
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        int code = await Task.Run(() =>
        {
            using (Process p = Process.Start(psi))
            {
                p.WaitForExit();
                return p.ExitCode;
            }
        });
        if (code != 0) throw new Exception("Die hosts-Datei konnte nicht geändert werden (Code " + code + ").");
        host.SetStatus(enable ? "Fertig!  TS-Moved-nicht-Bugfix ist aktiv." : "Fertig!  TS-Moved-nicht-Bugfix wurde entfernt.", Theme.Orange);
    }
}
