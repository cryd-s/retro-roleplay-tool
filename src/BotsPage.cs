using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

public class Project
{
    public string Name, Description, Url;
}

// Discord-Bots von RR | Manu – im Programm verlinkt.
public static class Projects
{
    public static readonly Project[] All =
    {
        new Project { Name = "Retro RP", Description = "Bot für Familien-Discord", Url = "https://retrorp.de" },
        new Project { Name = "Retro Business", Description = "Bot für Business", Url = "https://retrobusiness.de" },
    };

    public static void Open(string url, IWin32Window owner)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception)
        {
            MessageBox.Show(owner, "Der Browser konnte nicht geöffnet werden.\n\n" + url, "Link öffnen",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}

// Unterseite „Discord Bots“
public class BotsPage : Panel
{
    readonly System.Collections.Generic.List<FeatureRow> rows = new System.Collections.Generic.List<FeatureRow>();

    // top = wo der Inhalt beginnt (darüber liegt der Seitenkopf des Hauptfensters)
    public BotsPage(int margin, int inner, int top)
    {
        BackColor = Theme.Bg;
        int y = top;

        int rowHeight = Theme.S(84);
        foreach (Project p in Projects.All)
        {
            var row = new FeatureRow(Theme.S(230))
            {
                Title = p.Name,
                Detail = p.Description,
                Value = p.Url.Replace("https://", ""),
                ValueColor = Theme.Orange,
                Bounds = new Rectangle(margin, y, inner, rowHeight),
            };
            row.Action.Text = "WEBSEITE ÖFFNEN";
            string url = p.Url;
            row.Action.Click += delegate { Projects.Open(url, FindForm()); };
            Controls.Add(row);
            rows.Add(row);
            y += rowHeight;
        }
        ContentHeight = y + Theme.S(12);
    }

    public int ContentHeight { get; private set; }

    public void SetButtonsEnabled(bool enabled)
    {
        foreach (FeatureRow r in rows) r.Action.Enabled = enabled;
    }
}
