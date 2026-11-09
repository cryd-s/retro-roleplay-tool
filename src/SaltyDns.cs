using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

// Eine aktive Netzwerkverbindung. V4/V6 = fest eingetragene DNS-Server, leer = automatisch (DHCP).
public class DnsAdapter
{
    public string Id, Name;
    public int Index;
    public bool HasIPv6;
    public string[] V4 = new string[0], V6 = new string[0];
}

public class DnsBackupEntry
{
    public string Id;
    public string[] V4 = new string[0], V6 = new string[0];
}

// SaltyChat DNS Fix: aktive Netzwerkverbindungen auf Google DNS stellen und wieder zurück.
public static class SaltyDns
{
    public const string ElevatedArgument = "--dns-fix";
    public const string ModeOn = "on", ModeOff = "off";
    const int InvalidArguments = 64;

    public static readonly string[] GoogleV4 = { "8.8.8.8", "8.8.4.4" };
    public static readonly string[] GoogleV6 = { "2001:4860:4860::8888", "2001:4860:4860::8844" };

    public static string BackupFile
    {
        get
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                "RetroRoleplayTool", "Sicherung DNS", "dns.txt");
        }
    }

    // ------------------------------------------------------------ Texte

    public static string ConfirmOnText(IEnumerable<string> adapterNames)
    {
        return "Diese Netzwerkverbindung wird auf Google DNS umgestellt:\n\n•  " + string.Join("\n•  ", adapterNames) +
               "\n\nIPv4: 8.8.8.8 und 8.8.4.4\nIPv6: 2001:4860:4860::8888 und 2001:4860:4860::8844\n\n" +
               "Ein eigener DNS-Filter wie Pi-hole oder AdGuard wird damit umgangen, und deine DNS-Anfragen gehen an Google. " +
               "Die bisherigen Einstellungen werden gespeichert, über denselben Knopf kommst du zurück.\n\n" +
               "Windows fragt nach Administratorrechten. Fortfahren?";
    }

    public const string ConfirmOffText =
        "Die DNS-Einstellungen werden wieder so gesetzt, wie sie vor dem Fix waren.\n\n" +
        "Windows fragt nach Administratorrechten. Fortfahren?";

    public const string DoneOnText =
        "Google DNS ist eingetragen.\n\nStarte TeamSpeak und FiveM neu, damit sie die neuen Einstellungen nutzen. Ein Neustart des PCs ist nicht nötig.";

    public const string DoneOffText = "Die bisherigen DNS-Einstellungen sind wiederhergestellt.";

    public static string FailedText(int exitCode)
    {
        return "Die DNS-Einstellungen wurden nicht vollständig übernommen" + (exitCode != 0 ? " (Code " + exitCode + ")" : "") + ".\n\n" +
               "Prüfe, ob ein VPN oder eine Firmen-Richtlinie die Einstellungen sperrt, und versuche es noch einmal.";
    }

    // ------------------------------------------------------------ Adapter

    // Nur echte Netzwerkkarten mit Internetweg. VPN- und virtuelle Adapter bleiben unberührt.
    public static bool IsCandidate(NetworkInterfaceType type, OperationalStatus status, bool hasGateway, string description)
    {
        if (status != OperationalStatus.Up || !hasGateway) return false;
        if (type != NetworkInterfaceType.Ethernet && type != NetworkInterfaceType.Wireless80211 && type != NetworkInterfaceType.GigabitEthernet &&
            type != NetworkInterfaceType.FastEthernetT && type != NetworkInterfaceType.FastEthernetFx)
            return false;
        return !Regex.IsMatch(description ?? "", "virtual|vmware|hyper-v|vethernet|tailscale|wireguard|nordlynx|openvpn|tap-|wintun|zerotier|hamachi|vpn",
                              RegexOptions.IgnoreCase);
    }

    public static List<DnsAdapter> ActiveAdapters()
    {
        var result = new List<DnsAdapter>();
        foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (!nic.Supports(NetworkInterfaceComponent.IPv4)) continue;
            bool hasGateway = nic.GetIPProperties().GatewayAddresses.Any(g => !g.Address.Equals(IPAddress.Any) && !g.Address.Equals(IPAddress.IPv6Any));
            if (IsCandidate(nic.NetworkInterfaceType, nic.OperationalStatus, hasGateway, nic.Description)) result.Add(Read(nic));
        }
        return result;
    }

    static DnsAdapter Read(NetworkInterface nic)
    {
        return new DnsAdapter
        {
            Id = nic.Id,
            Name = nic.Name,
            Index = nic.GetIPProperties().GetIPv4Properties().Index,
            HasIPv6 = nic.Supports(NetworkInterfaceComponent.IPv6),
            V4 = ParseServers(RegistryServers("Tcpip", nic.Id), AddressFamily.InterNetwork),
            V6 = ParseServers(RegistryServers("Tcpip6", nic.Id), AddressFamily.InterNetworkV6),
        };
    }

    static string RegistryServers(string service, string id)
    {
        using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + service + @"\Parameters\Interfaces\" + id))
            return key == null ? null : key.GetValue("NameServer") as string;
    }

    // Nur gültige Adressen der richtigen Art: Die Werte landen in einem Befehl mit Administratorrechten.
    public static string[] ParseServers(string text, AddressFamily family)
    {
        var servers = new List<string>();
        foreach (string part in (text ?? "").Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            IPAddress address;
            if (IPAddress.TryParse(part, out address) && address.AddressFamily == family && !servers.Contains(address.ToString()))
                servers.Add(address.ToString());
        }
        return servers.ToArray();
    }

    public static bool IsGoogle(DnsAdapter adapter)
    {
        return Same(adapter.V4, GoogleV4) && (!adapter.HasIPv6 || Same(adapter.V6, GoogleV6));
    }

    static bool Same(string[] actual, string[] expected)
    {
        if (actual.Length != expected.Length) return false;
        for (int i = 0; i < actual.Length; i++)
            if (!IPAddress.Parse(actual[i]).Equals(IPAddress.Parse(expected[i]))) return false;
        return true;
    }

    // ------------------------------------------------------------ Befehle

    static KeyValuePair<string, string> Netsh(string arguments)
    {
        return new KeyValuePair<string, string>(Path.Combine(Environment.SystemDirectory, "netsh.exe"), arguments);
    }

    static void AddServers(List<KeyValuePair<string, string>> commands, string family, int index, string[] servers)
    {
        if (servers.Length == 0)
        {
            commands.Add(Netsh("interface " + family + " set dnsservers name=" + index + " source=dhcp"));
            return;
        }
        commands.Add(Netsh("interface " + family + " set dnsservers name=" + index + " source=static address=" + servers[0] + " register=primary validate=no"));
        for (int i = 1; i < servers.Length; i++)
            commands.Add(Netsh("interface " + family + " add dnsservers name=" + index + " address=" + servers[i] + " index=" + (i + 1) + " validate=no"));
    }

    static KeyValuePair<string, string> FlushCommand()
    {
        return new KeyValuePair<string, string>(Path.Combine(Environment.SystemDirectory, "ipconfig.exe"), "/flushdns");
    }

    public static List<KeyValuePair<string, string>> CommandsOn(IEnumerable<DnsAdapter> adapters)
    {
        var commands = new List<KeyValuePair<string, string>>();
        foreach (DnsAdapter adapter in adapters)
        {
            AddServers(commands, "ipv4", adapter.Index, GoogleV4);
            if (adapter.HasIPv6) AddServers(commands, "ipv6", adapter.Index, GoogleV6);
        }
        commands.Add(FlushCommand());
        return commands;
    }

    // Ohne gespeicherten Eintrag zurück auf automatisch: So ist Windows ab Werk eingestellt.
    public static List<KeyValuePair<string, string>> CommandsOff(IEnumerable<DnsAdapter> adapters, List<DnsBackupEntry> backup)
    {
        var commands = new List<KeyValuePair<string, string>>();
        foreach (DnsAdapter adapter in adapters)
        {
            DnsBackupEntry entry = backup.Find(e => string.Equals(e.Id, adapter.Id, StringComparison.OrdinalIgnoreCase));
            AddServers(commands, "ipv4", adapter.Index, entry != null ? entry.V4 : new string[0]);
            if (adapter.HasIPv6) AddServers(commands, "ipv6", adapter.Index, entry != null ? entry.V6 : new string[0]);
        }
        commands.Add(FlushCommand());
        return commands;
    }

    // ------------------------------------------------------------ Sicherung

    // Steht ein Adapter schon auf Google DNS, bleibt der alte Eintrag: Sonst ginge der Weg zurück verloren.
    public static string BuildBackup(string existing, IEnumerable<DnsAdapter> adapters)
    {
        List<DnsBackupEntry> entries = ParseBackup(existing);
        foreach (DnsAdapter adapter in adapters)
        {
            int at = entries.FindIndex(e => string.Equals(e.Id, adapter.Id, StringComparison.OrdinalIgnoreCase));
            if (IsGoogle(adapter))
            {
                if (at < 0) entries.Add(new DnsBackupEntry { Id = adapter.Id });
                continue;
            }
            var entry = new DnsBackupEntry { Id = adapter.Id, V4 = adapter.V4, V6 = adapter.V6 };
            if (at >= 0) entries[at] = entry; else entries.Add(entry);
        }
        var text = new StringBuilder();
        foreach (DnsBackupEntry e in entries)
            text.Append(e.Id + "|" + string.Join(",", e.V4) + "|" + string.Join(",", e.V6) + "\r\n");
        return text.ToString();
    }

    public static List<DnsBackupEntry> ParseBackup(string text)
    {
        var entries = new List<DnsBackupEntry>();
        foreach (string line in (text ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = line.Split('|');
            Guid id;
            if (parts.Length != 3 || !Guid.TryParse(parts[0], out id)) continue;
            entries.Add(new DnsBackupEntry
            {
                Id = parts[0],
                V4 = ParseServers(parts[1], AddressFamily.InterNetwork),
                V6 = ParseServers(parts[2], AddressFamily.InterNetworkV6),
            });
        }
        return entries;
    }

    public static void SaveBackup(string file, IEnumerable<DnsAdapter> adapters)
    {
        string existing = File.Exists(file) ? File.ReadAllText(file) : "";
        Directory.CreateDirectory(Path.GetDirectoryName(file));
        File.WriteAllText(file, BuildBackup(existing, adapters));
    }

    // ------------------------------------------------------------ Admin-Teil

    // Nach dem Zurücksetzen darf Google nur dort noch stehen, wo es schon vor dem Fix fest eingetragen war.
    public static bool IsRestored(IEnumerable<DnsAdapter> adapters, string backupFile)
    {
        return IsRestored(adapters, File.Exists(backupFile) ? ParseBackup(File.ReadAllText(backupFile)) : new List<DnsBackupEntry>());
    }

    public static bool IsRestored(IEnumerable<DnsAdapter> adapters, List<DnsBackupEntry> backup)
    {
        return adapters.Where(IsGoogle).All(a => backup.Exists(e => string.Equals(e.Id, a.Id, StringComparison.OrdinalIgnoreCase) && Same(e.V4, GoogleV4)));
    }

    public static bool IsValidBackupPath(string file)
    {
        return !string.IsNullOrEmpty(file) && Path.IsPathRooted(file) && !file.StartsWith(@"\\") &&
               file.EndsWith(@"\RetroRoleplayTool\Sicherung DNS\dns.txt", StringComparison.OrdinalIgnoreCase);
    }

    public static int RunElevated(string[] args)
    {
        if (args.Length != 2 || (args[0] != ModeOn && args[0] != ModeOff) || !IsValidBackupPath(args[1])) return InvalidArguments;

        if (args[0] == ModeOn) return SystemCommands.RunAll(CommandsOn(ActiveAdapters()));

        List<DnsBackupEntry> backup = File.Exists(args[1]) ? ParseBackup(File.ReadAllText(args[1])) : new List<DnsBackupEntry>();
        return SystemCommands.RunAll(CommandsOff(AdaptersToRestore(backup), backup));
    }

    // Auch Adapter aus der Sicherung, die gerade nicht verbunden sind, etwa das WLAN am Laptop.
    static List<DnsAdapter> AdaptersToRestore(List<DnsBackupEntry> backup)
    {
        List<DnsAdapter> adapters = ActiveAdapters();
        foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (!nic.Supports(NetworkInterfaceComponent.IPv4)) continue;
            string id = nic.Id;
            if (backup.Exists(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase)) &&
                !adapters.Exists(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase)))
                adapters.Add(Read(nic));
        }
        return adapters;
    }
}
