using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

public class Project
{
    public string Name, Description, Url;
}

// Im Programm verlinkte Seiten
public static class Projects
{
    public static readonly Project[] Bots =
    {
        new Project { Name = "Retro RP", Description = "Bot für Familien-Discord", Url = "https://retrorp.de" },
        new Project { Name = "Retro Business", Description = "Bot für Business", Url = "https://retrobusiness.de" },
    };

    public static readonly Project[] Retro =
    {
        new Project { Name = "UCP", Description = "User Control Panel im Retro-Portal", Url = "https://retro-roleplay.ch/portal" },
        new Project { Name = "Regelwerk", Description = "Die Regeln von Retro Roleplay", Url = "https://retro-roleplay.ch/regelwerk" },
        new Project { Name = "Discord", Description = "Der Retro-Discord", Url = "https://discord.gg/retrorp" },
        new Project { Name = "Entbannungsdiscord", Description = "Discord für Entbannungen", Url = "https://discord.gg/9s3n4u5ckE" },
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

// Unterseite mit einer Liste von Links (Discord Bots, Links zu Retro)
public class LinkListPage : Panel
{
    readonly List<FeatureRow> rows = new List<FeatureRow>();

    // top = wo der Inhalt beginnt (darüber liegt der Seitenkopf des Hauptfensters)
    public LinkListPage(Project[] items, string actionText, int margin, int inner, int top)
    {
        BackColor = Theme.Bg;
        int y = top;

        int rowHeight = Theme.S(84);
        foreach (Project p in items)
        {
            var row = new FeatureRow(Theme.S(230))
            {
                Title = p.Name,
                Detail = p.Description,
                Value = p.Url.Replace("https://", ""),
                ValueColor = Theme.Orange,
                Bounds = new Rectangle(margin, y, inner, rowHeight),
            };
            row.Action.Text = actionText;
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
