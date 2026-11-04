using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

// Liste „Einstellung · aktuell · Ziel · ✔/✖“
public class CheckListView : NeonControl
{
    List<SettingCheck> items = new List<SettingCheck>();
    readonly Font nameFont = Theme.MakeFont("Segoe UI Semibold", 10f);
    readonly Font valueFont = Theme.MakeFont("Segoe UI", 9.5f);
    readonly Font markFont = Theme.MakeFont("Segoe UI Semibold", 11f);

    public static int RowHeight { get { return Theme.S(27); } }

    public List<SettingCheck> Items
    {
        get { return items; }
        set { items = value ?? new List<SettingCheck>(); Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        Theme.Prepare(g);
        int h = RowHeight;
        int colCurrent = (int)(Width * 0.42), colTarget = (int)(Width * 0.70), colMark = Width - Theme.S(30);
        const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
        using (var line = new Pen(Theme.Line))
        {
            for (int i = 0; i < items.Count; i++)
            {
                SettingCheck c = items[i];
                int y = i * h;
                TextRenderer.DrawText(g, c.Name, nameFont, new Rectangle(0, y, colCurrent - Theme.S(8), h), Theme.Text, flags);
                TextRenderer.DrawText(g, c.Current, valueFont, new Rectangle(colCurrent, y, colTarget - colCurrent - Theme.S(8), h),
                    c.Ok ? Theme.Good : Theme.Pink, flags);
                TextRenderer.DrawText(g, "Ziel: " + c.Target, valueFont, new Rectangle(colTarget, y, colMark - colTarget - Theme.S(6), h),
                    Theme.Muted, flags);
                TextRenderer.DrawText(g, c.Ok ? "✔" : "✖", markFont, new Rectangle(colMark, y, Width - colMark, h),
                    c.Ok ? Theme.Good : Theme.Pink, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                g.DrawLine(line, 0, y + h - 1, Width, y + h - 1);
            }
        }
    }
}

// Unterseite „GTA-Einstellungen“: ändert nur die Dateien, die FiveM selbst benutzt (siehe GtaSettings.cs, RpSettings.cs).
public class GtaSettingsPage : Panel
{
    readonly MainForm host;
    readonly Label fileInfo, summary;
    readonly CheckListView checkList;
    readonly NeonButton applyButton;
    readonly List<ModeCard> cards = new List<ModeCard>();
    readonly FeatureRow backupRow;

    string graphicsPath;
    int notOk;
    int snapshotCount;
    bool refreshing;

    public GtaSettingsPage(MainForm host, int margin, int inner, int top)
    {
        this.host = host;
        BackColor = Theme.Bg;
        int y = top;

        // --- Die wichtigsten Einstellungen
        Controls.Add(Heading("WICHTIGSTE FIVEM-EINSTELLUNGEN", margin, y, inner));
        y += Theme.S(20);
        fileInfo = SmallLabel(margin, y, inner);
        Controls.Add(fileInfo);
        y += Theme.S(22);
        checkList = new CheckListView { Bounds = new Rectangle(margin, y, inner, 7 * CheckListView.RowHeight) };
        Controls.Add(checkList);
        y += checkList.Height + Theme.S(10);

        int btnH = Theme.S(50), btnW = Theme.S(260);
        summary = new Label
        {
            ForeColor = Theme.Muted,
            Font = Theme.MakeFont("Segoe UI Semibold", 10f),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoSize = false,
            Bounds = new Rectangle(margin, y, inner - btnW - Theme.S(10), btnH),
        };
        Controls.Add(summary);
        applyButton = new NeonButton
        {
            Text = "ALLE ÜBERNEHMEN",
            Primary = true,
            Bounds = new Rectangle(margin + inner - btnW + Theme.S(6), y, btnW, btnH),
        };
        applyButton.Click += async delegate { await Guard(ApplyRpSettingsAsync); };
        Controls.Add(applyButton);
        y += btnH + Theme.S(14);

        // --- Grafik-Profile (optional)
        Controls.Add(Heading("GRAFIK-PROFIL  (OPTIONAL)", margin, y, inner));
        y += Theme.S(24);
        int glow = Theme.S(5), gap = Theme.S(12);
        int cardW = (inner - 2 * gap) / 3;
        int cardH = Theme.S(96);
        for (int i = 0; i < GtaGraphics.All.Length; i++)
        {
            GtaGraphics.Preset preset = GtaGraphics.All[i];
            var card = new ModeCard
            {
                Title = preset.Name,
                Detail = preset.Detail,
                Bounds = new Rectangle(margin + i * (cardW + gap) - glow, y, cardW + 2 * glow, cardH),
            };
            card.Picked += async delegate { await Guard(() => ApplyPresetAsync(preset)); };
            cards.Add(card);
            Controls.Add(card);
        }
        y += cardH + Theme.S(10);

        // --- Gespeicherte Stände
        backupRow = new FeatureRow(Theme.S(230))
        {
            Title = "Gespeicherte Stände",
            Detail = "Vor jeder Änderung wird der aktuelle Stand gespeichert.",
            Bounds = new Rectangle(margin, y, inner, Theme.S(84)),
        };
        backupRow.Action.Text = "EINEN SCHRITT ZURÜCK";
        backupRow.Action.Primary = false;
        backupRow.Action.Click += async delegate { await Guard(RestoreAsync); };
        Controls.Add(backupRow);
        y += backupRow.Height;

        ContentHeight = y + Theme.S(12);
    }

    public int ContentHeight { get; private set; }

    static Label Heading(string text, int x, int y, int w)
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

    static Label SmallLabel(int x, int y, int w)
    {
        return new Label
        {
            ForeColor = Theme.Muted,
            Font = Theme.MakeFont("Segoe UI", 8.25f),
            AutoSize = false,
            AutoEllipsis = true,
            Bounds = new Rectangle(x, y, w, Theme.S(18)),
        };
    }

    public void SetButtonsEnabled(bool enabled)
    {
        bool on = enabled && !refreshing && graphicsPath != null;
        foreach (ModeCard c in cards) c.Enabled = on;
        applyButton.Enabled = on && notOk > 0;
        backupRow.Action.Enabled = enabled && !refreshing && snapshotCount > 0;
    }

    // ------------------------------------------------------------ Status

    public async Task RefreshAsync()
    {
        if (refreshing) return;
        refreshing = true;
        SetButtonsEnabled(false);
        try
        {
            string xml = null, cfg = "";
            graphicsPath = GameConfigFiles.GraphicsPath;
            await Task.Run(() =>
            {
                Encoding enc;
                if (graphicsPath != null) xml = GameConfigFiles.ReadText(graphicsPath, out enc);
                if (File.Exists(GameConfigFiles.CfgPath)) cfg = GameConfigFiles.ReadText(GameConfigFiles.CfgPath, out enc);
            });

            if (xml == null)
            {
                fileInfo.Text = "Keine FiveM-Einstellungen gefunden – FiveM einmal starten und wieder schließen.";
                checkList.Items = null;
                summary.Text = "";
                notOk = 0;
            }
            else
            {
                fileInfo.Text = graphicsPath + "   ·   " + GameConfigFiles.CfgPath;
                List<SettingCheck> checks = RpSettings.Check(xml, cfg);
                checkList.Items = checks;
                notOk = 0;
                foreach (SettingCheck c in checks) if (!c.Ok) notOk++;
                summary.Text = notOk == 0 ? "✔  Alles passt." : notOk + " von " + checks.Count + " Einstellungen passen noch nicht.";
                summary.ForeColor = notOk == 0 ? Theme.Good : Theme.Pink;
            }

            GtaGraphics.Preset active = xml == null ? null : GtaGraphics.Matching(xml);
            foreach (ModeCard c in cards) c.Selected = active != null && c.Title == active.Name;

            List<string> snaps = GameConfigFiles.Snapshots(GameConfigFiles.DefaultBackupDir);
            snapshotCount = snaps.Count;
            backupRow.Value = snapshotCount == 0
                ? "noch nichts geändert"
                : snapshotCount + (snapshotCount == 1 ? " Stand" : " Stände") + " gespeichert  ·  letzter vom " +
                  GameConfigFiles.SnapshotTime(snaps[snapshotCount - 1]).ToString("dd.MM.yyyy, HH:mm") + " Uhr";
            backupRow.ValueColor = snapshotCount > 0 ? Theme.Orange : Theme.Muted;
        }
        catch (Exception ex)
        {
            fileInfo.Text = "Einstellungen nicht lesbar: " + ex.Message;
            graphicsPath = null;
        }
        finally
        {
            refreshing = false;
            SetButtonsEnabled(!host.Busy);
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
            host.SetStatus("Fehler: " + ex.Message, Theme.Pink);
            MessageBox.Show(FindForm(), ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
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

    // FiveM speichert seine Einstellungen beim Beenden und würde Änderungen sonst überschreiben.
    async Task<bool> EnsureFiveMClosedAsync()
    {
        if (!FiveMProcess.IsRunning()) return true;
        if (MessageBox.Show(FindForm(),
                "FiveM läuft gerade. FiveM speichert seine Einstellungen beim Beenden und würde die Änderung " +
                "wieder überschreiben.\n\nFiveM jetzt schließen?", "FiveM läuft",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return false;
        host.SetStatus("FiveM wird geschlossen …", Theme.Text);
        await Task.Run(() => FiveMProcess.KillAll());
        return true;
    }

    static void SaveCurrentState()
    {
        GameConfigFiles.Snapshot(GameConfigFiles.DefaultBackupDir, GameConfigFiles.GraphicsPath, GameConfigFiles.CfgPath);
    }

    async Task ApplyRpSettingsAsync()
    {
        if (graphicsPath == null || notOk == 0) return;
        var lines = new StringBuilder();
        foreach (SettingCheck c in checkList.Items)
            if (!c.Ok) lines.Append("•  ").Append(c.Name).Append(":  ").Append(c.Current).Append("  →  ").Append(c.Target).Append('\n');
        if (!Confirm("Folgende Einstellungen werden für FiveM angepasst:\n\n" + lines +
                     "\nAlles andere bleibt unverändert. Der aktuelle Stand wird vorher gespeichert, " +
                     "du kannst also jederzeit zurück.", "Einstellungen übernehmen"))
            return;
        if (!await EnsureFiveMClosedAsync()) return;

        host.SetBusy(true);
        string xmlPath = graphicsPath, cfgPath = GameConfigFiles.CfgPath;
        await Task.Run(() =>
        {
            SaveCurrentState();
            Encoding xmlEnc, cfgEnc = new UTF8Encoding(false);
            string xml = GameConfigFiles.ReadText(xmlPath, out xmlEnc);
            string cfg = File.Exists(cfgPath) ? GameConfigFiles.ReadText(cfgPath, out cfgEnc) : "";
            string oldCfg = cfg;
            RpSettings.Apply(ref xml, ref cfg);
            GameConfigFiles.WriteText(xmlPath, xml, xmlEnc);
            if (cfg != oldCfg)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(cfgPath));
                GameConfigFiles.WriteText(cfgPath, cfg, cfgEnc);
            }
        });
        host.SetStatus("Fertig!  Die Einstellungen sind übernommen – sie gelten ab dem nächsten FiveM-Start.", Theme.Orange);
    }

    async Task ApplyPresetAsync(GtaGraphics.Preset preset)
    {
        if (graphicsPath == null) return;
        if (!Confirm("Grafik-Profil „" + preset.Name + "“ für FiveM setzen?\n\n" + preset.Detail + "\n\n" +
                     "Auflösung, Bildschirm und die wichtigsten Einstellungen oben bleiben unverändert.\n" +
                     "Der aktuelle Stand wird vorher gespeichert.", "Grafik-Profil"))
            return;
        if (!await EnsureFiveMClosedAsync()) return;

        host.SetBusy(true);
        string path = graphicsPath;
        await Task.Run(() =>
        {
            SaveCurrentState();
            Encoding enc;
            string xml = GameConfigFiles.ReadText(path, out enc);
            GameConfigFiles.WriteText(path, GtaGraphics.Apply(xml, preset.Values), enc);
        });
        host.SetStatus("Fertig!  Grafik-Profil „" + preset.Name + "“ ist gesetzt – gilt ab dem nächsten FiveM-Start.", Theme.Orange);
    }

    async Task RestoreAsync()
    {
        List<string> snaps = GameConfigFiles.Snapshots(GameConfigFiles.DefaultBackupDir);
        if (snaps.Count == 0) return;
        string when = GameConfigFiles.SnapshotTime(snaps[snaps.Count - 1]).ToString("dd.MM.yyyy, HH:mm");
        if (!Confirm("Einen Schritt zurück: Die Einstellungen werden auf den Stand vom " + when + " Uhr " +
                     "(vor der letzten Änderung durch das Tool) zurückgesetzt.\n\n" +
                     "Was seitdem geändert wurde – auch im Spiel – geht dabei verloren.", "Einen Schritt zurück"))
            return;
        if (!await EnsureFiveMClosedAsync()) return;

        host.SetBusy(true);
        await Task.Run(() => GameConfigFiles.RestoreLatest(GameConfigFiles.DefaultBackupDir));
        host.SetStatus("Fertig!  Die Einstellungen stehen wieder auf dem Stand vom " + when + " Uhr.", Theme.Orange);
    }
}
