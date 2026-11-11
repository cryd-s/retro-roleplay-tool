using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

static class Program
{
    [DllImport("user32.dll")]
    static extern bool SetProcessDPIAware();

    [STAThread]
    static int Main(string[] args)
    {
        // Admin-Hilfsmodus für den TS-Moved-nicht-Bugfix: "--hosts-fix on|off" (wird mit Adminrechten gestartet)
        if (args.Length == 2 && args[0] == "--hosts-fix")
        {
            try
            {
                HostsFix.Apply(HostsFix.HostsPath, args[1] == "on");
                HostsFix.FlushDns();
                return 0;
            }
            catch (Exception)
            {
                return 1;
            }
        }

        // Admin-Hilfsmodus für den Rockstar Launcher Fix (wird mit Adminrechten gestartet)
        if (args.Length > 0 && args[0] == RockstarLauncher.ElevatedArgument)
        {
            var rest = new string[args.Length - 1];
            Array.Copy(args, 1, rest, 0, rest.Length);
            try { return RockstarLauncher.RunElevated(rest); }
            catch (Exception) { return RockstarLauncher.MaxFlags; }
        }

        // Admin-Hilfsmodus für den SaltyChat DNS Fix (wird mit Adminrechten gestartet)
        if (args.Length > 0 && args[0] == SaltyDns.ElevatedArgument)
        {
            var rest = new string[args.Length - 1];
            Array.Copy(args, 1, rest, 0, rest.Length);
            try { return SaltyDns.RunElevated(rest); }
            catch (Exception) { return 1; }
        }

        // Admin-Hilfsmodus: TeamSpeak-Installer ohne Overwolf (wird mit Adminrechten gestartet)
        if (args.Length > 0 && args[0] == Ts3.ElevatedArgument)
        {
            var rest = new string[args.Length - 1];
            Array.Copy(args, 1, rest, 0, rest.Length);
            try { return Ts3.RunElevated(rest); }
            catch (Exception) { return 1; }
        }

        // Admin-Hilfsmodus für den Netzwerk Fix (wird mit Adminrechten gestartet)
        if (args.Length == 1 && args[0] == NetworkFix.ElevatedArgument)
        {
            try { return SystemCommands.RunAll(NetworkFix.Commands()); }
            catch (Exception) { return 1; }
        }

        // Scharfe Darstellung bei 125 %/150 % Skalierung: selbst skalieren statt Windows-Bitmap-Zoom.
        try { SetProcessDPIAware(); } catch { }
        float dpiScale;
        using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) dpiScale = g.DpiX / 96f;
        Theme.Scale = dpiScale;
        Theme.FontFactor = 1f;

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // Passt das Fenster nicht auf den Bildschirm (z. B. Laptop mit 150 %), wird es kleiner aufgebaut.
        Rectangle work = Screen.FromPoint(Cursor.Position).WorkingArea;
        var form = new MainForm();
        for (int i = 0; i < 3; i++)
        {
            float fit = Theme.FitScale(Theme.Scale, form.Width, form.Height, work.Width, work.Height);
            if (fit >= Theme.Scale - 0.001f) break;
            form.Dispose();
            Theme.Scale = fit;
            Theme.FontFactor = fit / dpiScale;
            form = new MainForm();
        }
        Application.Run(form);
        return 0;
    }
}
