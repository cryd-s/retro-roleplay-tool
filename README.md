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
- TS-Moved-nicht-Bugfix (hosts-Eintrag) aktivieren oder wieder entfernen
- FiveM als Nicht-Steam-Spiel in die Steam-Bibliothek eintragen und Steam Input dafür aktivieren, oder beides wieder entfernen
  - Steam wird dafür normal geschlossen und danach wieder gestartet; vorher wird der aktuelle Stand gespeichert
  - **Noch nicht mit echtem Steam getestet.** FiveM unterstützt Steam Input nicht offiziell, der Controller funktioniert also vielleicht nicht in jedem Fall

**Update-Hinweis**
- Fragt beim Start bei GitHub die neueste Version ab und zeigt bei einer neueren Version einen Hinweis mit direktem Download-Link zur ZIP
- Installiert nichts selbst; ohne Internet erscheint einfach kein Hinweis

**Discord Bots**
- [Retro RP](https://retrorp.de) – Bot für Familien-Discord
- [Retro Business](https://retrobusiness.de) – Bot für Business

---

by RR | Manu
