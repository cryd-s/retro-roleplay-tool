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

    // Steht mit etwas Abstand unter den Retro-Links
    public static readonly Project[] RetroExtras =
    {
        new Project { Name = "EzTunes", Description = "PC-Optimierung für FiveM und mehr", Url = "https://eztunes.online/" },
    };

    public static void Open(string url, IWin32Window owner)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception)
        {
            NeonDialog.Show(owner, "Der Browser konnte nicht geöffnet werden.\n\n" + url, "Link öffnen",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}

// Unterseite mit einer Liste von Links (Discord Bots, Links zu Retro)
public class LinkListPage : Panel
{
    readonly List<FeatureRow> rows = new List<FeatureRow>();

    // top = wo der Inhalt beginnt (darüber liegt der Seitenkopf des Hauptfensters)
    // extras = weitere Links, abgesetzt unter der Liste
    public LinkListPage(Project[] items, string actionText, int margin, int inner, int top, Project[] extras = null)
    {
        BackColor = Theme.Bg;
        int y = top;

        foreach (Project p in items) AddRow(p, actionText, margin, inner, ref y);
        if (extras != null)
        {
            y += Theme.S(28);
            foreach (Project p in extras) AddRow(p, actionText, margin, inner, ref y);
        }
        ContentHeight = y + Theme.S(12);
    }

    void AddRow(Project p, string actionText, int margin, int inner, ref int y)
    {
        int rowHeight = Theme.S(84);
        var row = new FeatureRow(Theme.S(230))
        {
            Title = p.Name,
            Detail = p.Description,
            Value = p.Url.Replace("https://", "").TrimEnd('/'),
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

    public int ContentHeight { get; private set; }

    public void SetButtonsEnabled(bool enabled)
    {
        foreach (FeatureRow r in rows) r.Action.Enabled = enabled;
    }
}
