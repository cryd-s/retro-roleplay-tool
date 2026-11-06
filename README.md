# Retro Roleplay Tool

![Retro Roleplay Tool](assets/banner.png)

Kleines Windows-Programm für FiveM-Spieler: räumt den FiveM-Cache auf, setzt FiveM auf den Stable-Kanal,
stellt die GTA-Grafik für FiveM ein und installiert TeamSpeak 3 und SaltyChat mit einem Klick.

## ⬇️ Download

**[RetroRoleplayTool.zip herunterladen (aktuelle Version)](https://github.com/cryd-s/retro-roleplay-tool/releases/latest/download/RetroRoleplayTool.zip)**

ZIP entpacken und `RetroRoleplayTool.exe` starten. Keine Installation nötig, läuft auf Windows 10 und Windows 11.
Alle Versionen (auch die exe einzeln) findest du unter [Releases](https://github.com/cryd-s/retro-roleplay-tool/releases).

> Windows zeigt beim ersten Start eventuell „Der Computer wurde durch Windows geschützt“ an, weil das Programm
> nicht digital signiert ist. Dann auf **Weitere Informationen → Trotzdem ausführen** klicken.
> Die Schritte mit Bildern stehen in der `Anleitung.pdf` in der ZIP.

## Funktionen

**Cache Cleaner**
- Findet die FiveM-Installation automatisch, auch bei eigenem Installationsort, sonst manuell auswählbar
- **Normaler Cache Clear:** `cache`, `server-cache`, `server-cache-priv`, `logs`, `crashes`
- **Gesamter Cache Clear:** zusätzlich `game-storage` und `nui-storage`.
  Nur nutzen, wenn der normale Clear nicht hilft – danach muss der FiveM-Name neu gesetzt werden.
- Optional: Windows-Temp (`%TEMP%`) leeren und FiveM auf den Stable-Kanal setzen (`CitizenFX.ini`, mit Sicherung)
- Zeigt vorher an, wie viel Platz frei wird; gesperrte Dateien werden übersprungen

**GTA-Einstellungen** (gelten für FiveM – geändert werden FiveMs eigene Dateien in `%APPDATA%\CitizenFX`)
- Prüft die wichtigsten Einstellungen und setzt sie mit einem Klick:
  Extended Texture Budget 50–95 %, FXAA an, MSAA aus, VSync aus, Bevölkerungsdichte 0,
  Bevölkerungsvielfalt 0, Darstellungsdistanz höchstens 95 %
- Optionale Grafik-Profile **Leistung**, **Ausgewogen** und **Qualität** – Auflösung und Bildschirm bleiben unverändert
- Vor jeder Änderung wird der aktuelle Stand gespeichert – „Einen Schritt zurück“ jederzeit möglich
- Nur bei geschlossenem FiveM, sonst überschreibt FiveM die Werte beim Beenden

**Sonderfunktionen**
- TeamSpeak 3 in der aktuellen Version installieren/aktualisieren (offizieller Download mit Prüfsummen-Check)
- SaltyChat in der aktuellen Version installieren
- TeamSpeak-Sounds dauerhaft deaktivieren oder wieder aktivieren (Sound-Paket „Sounds deactivated“, Sprache bleibt)
- TS-Moved-nicht-Bugfix (hosts-Eintrag) aktivieren oder wieder entfernen
- Rockstar Launcher Fix: DNS-Cache leeren, Winsock zurücksetzen und `%LOCALAPPDATA%\Rockstar Games\Launcher` leeren
  - Danach den PC neu starten und im Launcher neu anmelden; VPN, Tailscale und eigene DNS-Filter vorher selbst ausschalten
- Netzwerk Fix: `netsh winsock reset`, `netsh int ip reset` und `ipconfig /flushdns`, danach den PC neu starten
  - Feste IP-Adressen und eigene DNS-Einstellungen können verloren gehen; die Netzwerkdaten werden vorher in eine Textdatei gespeichert
- FiveM als Nicht-Steam-Spiel in die Steam-Bibliothek eintragen und Steam Input dafür aktivieren, oder beides wieder entfernen
  - Steam wird dafür normal geschlossen und danach wieder gestartet; vorher wird der aktuelle Stand gespeichert
  - **Noch nicht mit echtem Steam getestet.** FiveM unterstützt Steam Input nicht offiziell, der Controller funktioniert also vielleicht nicht in jedem Fall

**Grafik-Shader-Cache (im Cache Cleaner, Option)**
- Leert bei Hängern und Grafikfehlern den Shader-Cache von NVIDIA, AMD, Intel und Windows
- Standardmäßig aus; gilt auch für andere Spiele, der nächste Start dauert einmalig länger

**Grafik-Mods prüfen (unter Sonderfunktionen)**
- Zeigt ReShade, ASI-Plugins, fremde DLLs und Mods im GTA-Ordner und bei FiveM
- Ändert nichts, hilft nur bei der Suche nach Absturzursachen

**Links zu Retro**
- [UCP](https://retro-roleplay.ch/portal)
- [Regelwerk](https://retro-roleplay.ch/regelwerk)
- [Discord](https://discord.gg/retrorp)
- [Entbannungsdiscord](https://discord.gg/9s3n4u5ckE)

**Update-Hinweis**
- Fragt beim Start bei GitHub die neueste Version ab und zeigt bei einer neueren Version einen Hinweis mit direktem Download-Link zur ZIP
- Installiert nichts selbst; ohne Internet erscheint einfach kein Hinweis

**Discord Bots**
- [Retro RP](https://retrorp.de) – Bot für Familien-Discord
- [Retro Business](https://retrobusiness.de) – Bot für Business

---

by RR | Manu
