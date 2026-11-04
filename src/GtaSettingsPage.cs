using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

// Unterseite „GTA-Einstellungen“: ändert nur die Dateien, die FiveM selbst benutzt (siehe GtaSettings.cs).
public class GtaSettingsPage : Panel
{
    readonly MainForm host;
    readonly Label fileInfo;
    readonly List<ModeCard> cards = new List<ModeCard>();
    readonly FeatureRow budgetRow, backupRow;

    string graphicsPath;
    int currentBudget = -1, recommendedBudget = -1;
    bool hasBackup, refreshing;

    public GtaSettingsPage(MainForm host, int margin, int inner, int top)
    {
        this.host = host;
        BackColor = Theme.Bg;
        int y = top;

        Controls.Add(Heading("GRAFIK-PROFIL", margin, y, inner));
        y += Theme.S(22);
        fileInfo = new Label
        {
            ForeColor = Theme.Muted,
            Font = Theme.MakeFont("Segoe UI", 8.25f),
            AutoSize = false,
            AutoEllipsis = true,
            Bounds = new Rectangle(margin, y, inner, Theme.S(18)),
        };
        Controls.Add(fileInfo);
        y += Theme.S(24);

        int glow = Theme.S(5), gap = Theme.S(12);
        int cardW = (inner - 2 * gap) / 3;
        int cardH = Theme.S(104);
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
        y += cardH + Theme.S(14);

        Controls.Add(Heading("TEXTUREN UND SICHERUNG", margin, y, inner));
        y += Theme.S(20);
        int rowHeight = Theme.S(84), buttonWidth = Theme.S(230);
        budgetRow = new FeatureRow(buttonWidth)
        {
            Title = "Extended Texture Budget",
            Detail = "wird geprüft …",
            Bounds = new Rectangle(margin, y, inner, rowHeight),
        };
        budgetRow.Action.Text = "EMPFOHLEN SETZEN";
        budgetRow.Action.Click += async delegate { await Guard(SetRecommendedBudgetAsync); };
        Controls.Add(budgetRow);
        y += rowHeight;

        backupRow = new FeatureRow(buttonWidth)
        {
            Title = "Sicherung",
            Detail = "Vor der ersten Änderung sichert das Tool die Original-Einstellungen.",
            Bounds = new Rectangle(margin, y, inner, rowHeight),
        };
        backupRow.Action.Text = "WIEDERHERSTELLEN";
        backupRow.Action.Primary = false;
        backupRow.Action.Click += async delegate { await Guard(RestoreAsync); };
        Controls.Add(backupRow);
        y += rowHeight;

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

    public void SetButtonsEnabled(bool enabled)
    {
        bool on = enabled && !refreshing;
        foreach (ModeCard c in cards) c.Enabled = on && graphicsPath != null;
        budgetRow.Action.Enabled = on && recommendedBudget >= 0;
        backupRow.Action.Enabled = on && hasBackup;
    }

    // ------------------------------------------------------------ Status

    public async Task RefreshAsync()
    {
        if (refreshing) return;
        refreshing = true;
        SetButtonsEnabled(false);

        graphicsPath = GameConfigFiles.GraphicsPath;
        GtaGraphics.Preset active = null;
        if (graphicsPath == null)
        {
            fileInfo.Text = "Keine FiveM-Grafikeinstellungen gefunden – FiveM einmal starten und wieder schließen.";
        }
        else
        {
            try
            {
                Encoding enc;
                active = GtaGraphics.Matching(GameConfigFiles.ReadText(graphicsPath, out enc));
                fileInfo.Text = "Aktuell: " + (active != null ? active.Name : "eigene Einstellungen") +
                                "   ·   Auflösung und Bildschirm bleiben unverändert   ·   " + graphicsPath;
            }
            catch (Exception ex)
            {
                fileInfo.Text = "Grafikeinstellungen nicht lesbar: " + ex.Message;
                graphicsPath = null;
            }
        }
        foreach (ModeCard c in cards) c.Selected = active != null && c.Title == active.Name;

        // Texture Budget
        currentBudget = 0;
        try
        {
            Encoding enc;
            if (File.Exists(GameConfigFiles.CfgPath))
            {
                int v;
                if (int.TryParse(FiveMCfg.Get(GameConfigFiles.ReadText(GameConfigFiles.CfgPath, out enc), TextureBudget.Convar), out v))
                    currentBudget = v;
            }
        }
        catch (Exception) { }
        KeyValuePair<string, long> gpu = await Task.Run(() => TextureBudget.DetectGpu());
        recommendedBudget = TextureBudget.Recommended(gpu.Value);
        budgetRow.Detail = gpu.Value > 0
            ? "Grafikkarte: " + (gpu.Key ?? "unbekannt") + "  ·  " + TextureBudget.FormatGb(gpu.Value) + " Grafikspeicher"
            : "Grafikspeicher konnte nicht ermittelt werden – bitte im FiveM-Grafikmenü einstellen.";
        string now = "aktuell Stufe " + currentBudget + " (" + TextureBudget.FormatGb(TextureBudget.BudgetBytes(currentBudget)) + ")";
        if (recommendedBudget < 0)
        {
            budgetRow.Value = now;
            budgetRow.ValueColor = Theme.Muted;
        }
        else if (currentBudget == recommendedBudget)
        {
            budgetRow.Value = now + "  ✔  passt zur Grafikkarte";
            budgetRow.ValueColor = Theme.Good;
        }
        else
        {
            budgetRow.Value = now + "  ·  empfohlen Stufe " + recommendedBudget +
                              " (" + TextureBudget.FormatGb(TextureBudget.BudgetBytes(recommendedBudget)) + ")";
            budgetRow.ValueColor = currentBudget > recommendedBudget ? Theme.Pink : Theme.Orange;
        }
        budgetRow.Action.Primary = recommendedBudget >= 0 && currentBudget != recommendedBudget;

        // Sicherung
        hasBackup = GameConfigFiles.HasBackup(GameConfigFiles.DefaultBackupDir);
        backupRow.Value = hasBackup
            ? "Sicherung vom " + GameConfigFiles.BackupTime(GameConfigFiles.DefaultBackupDir).ToString("dd.MM.yyyy, HH:mm") + " Uhr"
            : "noch nichts geändert – keine Sicherung nötig";
        backupRow.ValueColor = hasBackup ? Theme.Orange : Theme.Muted;

        refreshing = false;
        SetButtonsEnabled(!host.Busy);
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

    static void Backup()
    {
        GameConfigFiles.BackupOnce(GameConfigFiles.DefaultBackupDir, GameConfigFiles.GraphicsPath, GameConfigFiles.CfgPath);
    }

    async Task ApplyPresetAsync(GtaGraphics.Preset preset)
    {
        if (graphicsPath == null) return;
        if (!Confirm("Grafik-Profil „" + preset.Name + "“ für FiveM setzen?\n\n" + preset.Detail + "\n\n" +
                     "Auflösung, Bildschirm und Texture Budget bleiben unverändert.\n" +
                     "Die Original-Einstellungen werden vorher gesichert.", "Grafik-Profil"))
            return;
        if (!await EnsureFiveMClosedAsync()) return;

        host.SetBusy(true);
        string path = graphicsPath;
        await Task.Run(() =>
        {
            Backup();
            Encoding enc;
            string xml = GameConfigFiles.ReadText(path, out enc);
            GameConfigFiles.WriteText(path, GtaGraphics.Apply(xml, preset.Values), enc);
        });
        host.SetStatus("Fertig!  Grafik-Profil „" + preset.Name + "“ ist gesetzt – gilt ab dem nächsten FiveM-Start.", Theme.Orange);
    }

    async Task SetRecommendedBudgetAsync()
    {
        int value = recommendedBudget;
        if (value < 0) return;
        if (!Confirm("Extended Texture Budget auf Stufe " + value + " setzen?\n\n" +
                     "Damit darf FiveM bis zu " + TextureBudget.FormatGb(TextureBudget.BudgetBytes(value)) +
                     " für Texturen nutzen – etwa die Hälfte deines Grafikspeichers.\n" +
                     "Das hilft gegen fehlende oder matschige Texturen, ohne die Grafikkarte zu überlasten.\n\n" +
                     "Die Original-Einstellungen werden vorher gesichert.", "Extended Texture Budget"))
            return;
        if (!await EnsureFiveMClosedAsync()) return;

        host.SetBusy(true);
        string path = GameConfigFiles.CfgPath;
        await Task.Run(() =>
        {
            Backup();
            if (File.Exists(path))
            {
                Encoding enc;
                string cfg = GameConfigFiles.ReadText(path, out enc);
                GameConfigFiles.WriteText(path, FiveMCfg.Set(cfg, TextureBudget.Convar, value.ToString()), enc);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, FiveMCfg.Set("", TextureBudget.Convar, value.ToString()), new UTF8Encoding(false));
            }
        });
        host.SetStatus("Fertig!  Extended Texture Budget steht auf Stufe " + value + " – gilt ab dem nächsten FiveM-Start.", Theme.Orange);
    }

    async Task RestoreAsync()
    {
        if (!hasBackup) return;
        if (!Confirm("Die gesicherten Original-Einstellungen vom " +
                     GameConfigFiles.BackupTime(GameConfigFiles.DefaultBackupDir).ToString("dd.MM.yyyy, HH:mm") +
                     " Uhr zurückspielen?\n\nAlle Änderungen, die seitdem an den FiveM-Einstellungen gemacht wurden " +
                     "(auch im Spiel), gehen dabei verloren.", "Wiederherstellen"))
            return;
        if (!await EnsureFiveMClosedAsync()) return;

        host.SetBusy(true);
        await Task.Run(() => GameConfigFiles.Restore(GameConfigFiles.DefaultBackupDir));
        host.SetStatus("Fertig!  Die Original-Einstellungen sind wiederhergestellt.", Theme.Orange);
    }
}
