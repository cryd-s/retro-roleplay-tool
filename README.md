# Retro Roleplay Tool

![Retro Roleplay Tool](assets/banner.png)

Kleines Windows-Programm für FiveM-Spieler: räumt den FiveM-Cache auf, setzt FiveM auf den Stable-Kanal und
installiert TeamSpeak 3 und SaltyChat mit einem Klick.

## ⬇️ Download

**[RetroRoleplayTool.exe herunterladen (aktuelle Version)](https://github.com/cryd-s/retro-roleplay-tool/releases/latest/download/RetroRoleplayTool.exe)**

Einfach herunterladen und starten. Keine Installation nötig, läuft auf Windows 10 und Windows 11.
Alle Versionen findest du unter [Releases](https://github.com/cryd-s/retro-roleplay-tool/releases).

> Windows zeigt beim ersten Start eventuell „Der Computer wurde durch Windows geschützt“ an, weil das Programm
> nicht digital signiert ist. Dann auf **Weitere Informationen → Trotzdem ausführen** klicken.

## Funktionen

**Cleaner**
- Findet die FiveM-Installation automatisch, auch bei eigenem Installationsort, sonst manuell auswählbar
- **Normaler Cache Clear:** `cache`, `server-cache`, `server-cache-priv`, `logs`, `crashes`
- **Gesamter Cache Clear:** zusätzlich `game-storage` und `nui-storage`.
  Nur nutzen, wenn der normale Clear nicht hilft – danach muss der FiveM-Name neu gesetzt werden.
- Optional: Windows-Temp (`%TEMP%`) leeren und FiveM auf den Stable-Kanal setzen (`CitizenFX.ini`, mit Sicherung)
- Zeigt vorher an, wie viel Platz frei wird; gesperrte Dateien werden übersprungen

**Sonderfunktionen**
- TeamSpeak 3 in der aktuellen Version installieren/aktualisieren (offizieller Download mit Prüfsummen-Check)
- SaltyChat in der aktuellen Version installieren
- TS-Moved-Bugfix (hosts-Eintrag) aktivieren oder wieder entfernen

**Discord Bots**
- [Retro RP](https://retrorp.de) – Bot für Familien-Discord
- [Retro Business](https://retrobusiness.de) – Bot für Business

## Selbst bauen

Es wird nichts extra benötigt – der C#-Compiler von Windows (.NET Framework 4.8) reicht.

```
build.cmd
```

Führt zuerst die Tests aus und erzeugt dann `RetroRoleplayTool.exe`.

## Neue Version veröffentlichen

```
powershell -ExecutionPolicy Bypass -File tools\release.ps1 -Version 1.1.0 -Notes "Was ist neu"
```

Setzt die Versionsnummer, baut und testet, committet, pusht und legt das GitHub-Release mit der exe an.

---

by RR | Manu
